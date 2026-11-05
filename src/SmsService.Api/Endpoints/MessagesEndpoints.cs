using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmsService.Api.Contracts;
using SmsService.Api.Data;
using SmsService.Api.Domain;
using SmsService.Api.Sending;
using SmsService.Api.Validation;

namespace SmsService.Api.Endpoints;

public static class MessagesEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int MaxIdempotencyKeyLength = 100;
    private const int MaxListSize = 100;
    public const string NotAnEmployeePhoneError = "המספר אינו שייך לאף עובד במערכת";

    public static void MapMessagesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/messages").WithTags("Messages");

        group.MapPost("/", SendMessage)
            .Produces<SendMessageResponse>(StatusCodes.Status202Accepted)
            .Produces<SendMessageResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id}", GetMessage)
            .Produces<MessageStatusResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListMessages)
            .Produces<MessageListResponse>()
            .ProducesValidationProblem();
    }

    private static async Task<IResult> SendMessage(
        SendMessageRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        AppDbContext db,
        SendWorkerSignal signal,
        TimeProvider clock,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // Validate everything and return all errors together.
        var errors = new Dictionary<string, string[]>();

        var phone = PhoneNumberNormalizer.Normalize(request.To);
        if (!phone.IsValid) errors["to"] = [phone.Error!];
        // The system sends SMS to employees only: the number must belong to at least one employee.
        // A number may be shared by several employees; it is still one message to one number.
        else if (!await db.EmployeePhones.AnyAsync(p => p.PhoneE164 == phone.E164, ct))
            errors["to"] = [NotAnEmployeePhoneError];

        var body = request.Body?.Trim() ?? "";
        if (MessageBodyRules.Validate(body) is { } bodyError) errors["body"] = [bodyError];

        if (idempotencyKey is not null && (idempotencyKey.Trim().Length == 0 || idempotencyKey.Length > MaxIdempotencyKeyLength))
            errors[IdempotencyKeyHeader] = [$"Idempotency-Key חייב להכיל 1–{MaxIdempotencyKeyLength} תווים"];

        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (idempotencyKey is not null &&
            await FindByIdempotencyKey(db, idempotencyKey, phone.E164!, body, ct) is { } existingResult)
            return existingResult;

        var message = Message.Create(phone.E164!, body, MessageBodyRules.CountSegments(body), idempotencyKey, clock.GetUtcNow());
        db.Messages.Add(message);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (idempotencyKey is not null && IsUniqueViolation(ex))
        {
            // Two concurrent requests with the same key: the other one won the race.
            logger.LogInformation("Idempotency key race resolved in favor of the existing message");
            db.ChangeTracker.Clear();
            return await FindByIdempotencyKey(db, idempotencyKey, phone.E164!, body, ct)
                   ?? throw new InvalidOperationException("Unique violation on idempotency key but no message found.", ex);
        }

        logger.LogInformation("Message {MessageId} created. Status={Status} Segments={Segments} ToSuffix={ToSuffix}",
            message.Id, message.Status, message.Segments, LogHelpers.PhoneSuffix(message.To));
        signal.Notify(message.Id);

        return Results.Accepted($"/messages/{message.Id}", new SendMessageResponse(message.Id, message.Status.ToString()));
    }

    /// <summary>Same key + same content -> 200 with the existing id. Same key + different content -> 409.</summary>
    private static async Task<IResult?> FindByIdempotencyKey(
        AppDbContext db, string key, string toE164, string body, CancellationToken ct)
    {
        var existing = await db.Messages.AsNoTracking()
            .Where(m => m.IdempotencyKey == key)
            .Select(m => new { m.Id, m.To, m.Body, m.Status })
            .SingleOrDefaultAsync(ct);

        if (existing is null) return null;

        if (existing.To != toE164 || existing.Body != body)
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency-Key conflict",
                detail: "Idempotency-Key כבר שימש להודעה עם נמען או תוכן שונים");

        return Results.Ok(new SendMessageResponse(existing.Id, existing.Status.ToString()));
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    private static async Task<IResult> GetMessage(string id, AppDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var messageId))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["id"] = ["מזהה הודעה לא תקין"] });

        var status = await db.Messages.AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(m => new { m.Id, m.Status, m.Attempts, m.LastError, m.UpdatedAt })
            .SingleOrDefaultAsync(ct);
        if (status is null)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Message not found");

        return Results.Ok(new MessageStatusResponse(
            status.Id, status.Status.ToString(), status.Attempts, status.LastError, status.UpdatedAt));
    }

    private static async Task<IResult> ListMessages(string? status, AppDbContext db, CancellationToken ct)
    {
        var query = db.Messages.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            // Match by name only (Enum.TryParse would also accept numbers like "1").
            var name = Enum.GetNames<MessageStatus>()
                .FirstOrDefault(n => string.Equals(n, status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["status"] = [$"סטטוס לא קיים. ערכים אפשריים: {string.Join(", ", Enum.GetNames<MessageStatus>())}"]
                });

            var parsed = Enum.Parse<MessageStatus>(name);
            query = query.Where(m => m.Status == parsed);
        }

        var messages = await query.OrderByDescending(m => m.CreatedAt).Take(MaxListSize).ToListAsync(ct);
        return Results.Ok(new MessageListResponse(await ToResponses(db, messages, ct)));
    }

    /// <summary>
    /// Maps messages to responses, adding the employees who own each number.
    /// One query for all messages; a number may belong to zero, one or many employees.
    /// </summary>
    private static async Task<List<MessageResponse>> ToResponses(AppDbContext db, List<Message> messages, CancellationToken ct)
    {
        var numbers = messages.Select(m => m.To).Distinct().ToList();

        var owners = await (
                from phone in db.EmployeePhones
                join employee in db.Employees on phone.EmployeeId equals employee.Id
                where numbers.Contains(phone.PhoneE164)
                orderby employee.LastName, employee.FirstName
                select new { phone.PhoneE164, employee.FirstName, employee.LastName })
            .ToListAsync(ct);

        var ownersByNumber = owners.ToLookup(o => o.PhoneE164, o => new EmployeeNameResponse(o.FirstName, o.LastName));

        return messages.Select(m => new MessageResponse(
                m.Id, m.To, m.Body, m.Segments, m.Status.ToString(), m.Attempts, m.LastError,
                m.CreatedAt, m.UpdatedAt, ownersByNumber[m.To].ToList()))
            .ToList();
    }
}

namespace SmsService.Api.Contracts;

public sealed record SendMessageRequest(string? To, string? Body);

public sealed record SendMessageResponse(Guid Id, string Status);

/// <summary>
/// Response of GET /messages/{id}: the status only, plus the fields that explain it
/// (attempts, last error, last update). Recipient and body are not exposed here.
/// </summary>
public sealed record MessageStatusResponse(
    Guid Id,
    string Status,
    int Attempts,
    string? LastError,
    DateTimeOffset UpdatedAt);

public sealed record EmployeeNameResponse(string FirstName, string LastName);

public sealed record MessageResponse(
    Guid Id,
    string To,
    string Body,
    int Segments,
    string Status,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<EmployeeNameResponse> Employees);

/// <summary>Wrapped in an object so pagination fields can be added later without breaking clients.</summary>
public sealed record MessageListResponse(IReadOnlyList<MessageResponse> Items);

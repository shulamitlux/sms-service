namespace SmsService.Api.Domain;

/// <summary>
/// An SMS message and its lifecycle. Status transitions happen only through the Mark* methods,
/// so the rules in SPEC section 7 live in a single place.
/// </summary>
public class Message
{
    public const int MaxAttempts = 3;
    public const int MaxErrorLength = 500;

    // Delay before the next attempt, indexed by the number of attempts already made.
    // After attempt 1 -> wait 10s, after attempt 2 -> wait 30s.
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public Guid Id { get; private set; }
    public string To { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public int Segments { get; private set; }
    public MessageStatus Status { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;

    private Message() { } // for EF Core

    public static Message Create(string toE164, string body, int segments, string? idempotencyKey, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            To = toE164,
            Body = body,
            Segments = segments,
            IdempotencyKey = idempotencyKey,
            Status = MessageStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

    /// <summary>The worker picked the message up. Counts as an attempt, so a crash mid-send is not "free".</summary>
    public void MarkSending(DateTimeOffset now)
    {
        EnsureStatus(MessageStatus.Pending, nameof(MarkSending));
        Status = MessageStatus.Sending;
        Attempts++;
        NextAttemptAt = null;
        UpdatedAt = now;
    }

    public void MarkSent(string providerMessageId, DateTimeOffset now)
    {
        EnsureStatus(MessageStatus.Sending, nameof(MarkSent));
        Status = MessageStatus.Sent;
        ProviderMessageId = providerMessageId;
        LastError = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Transient error with attempts left -> back to Pending with a future NextAttemptAt.
    /// Permanent error, or no attempts left -> Failed.
    /// </summary>
    public void MarkFailedAttempt(string error, bool isTransient, DateTimeOffset now)
    {
        EnsureStatus(MessageStatus.Sending, nameof(MarkFailedAttempt));
        LastError = Truncate(error);
        UpdatedAt = now;

        if (isTransient && Attempts < MaxAttempts)
        {
            Status = MessageStatus.Pending;
            NextAttemptAt = now + RetryDelays[Attempts - 1];
        }
        else
        {
            Status = MessageStatus.Failed;
            NextAttemptAt = null;
        }
    }

    /// <summary>
    /// Recovery after a crash: a message stuck in Sending goes back to Pending.
    /// If it already used all its attempts it is failed instead, otherwise a message that
    /// crashes the process every time would loop forever.
    /// </summary>
    public void RecoverStuckSending(DateTimeOffset now)
    {
        EnsureStatus(MessageStatus.Sending, nameof(RecoverStuckSending));
        LastError = "Sending was interrupted (service restart). The provider may or may not have received it.";
        UpdatedAt = now;
        NextAttemptAt = null;
        Status = Attempts < MaxAttempts ? MessageStatus.Pending : MessageStatus.Failed;
    }

    private void EnsureStatus(MessageStatus expected, string operation)
    {
        if (Status != expected)
            throw new InvalidOperationException(
                $"Cannot {operation} message {Id}: status is {Status}, expected {expected}.");
    }

    private static string Truncate(string error) =>
        error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
}

namespace SmsService.Api.Sending;

/// <summary>The only abstraction over the SMS provider. A second provider = a second implementation.</summary>
public interface ISmsProviderClient
{
    /// <summary>Sends the message. Throws <see cref="SmsProviderException"/> on failure.</summary>
    Task<ProviderSendResult> SendAsync(string toE164, string body, Guid messageId, CancellationToken ct);

    /// <summary>Optional: delivery status from the provider. Not used by the worker yet (see README).</summary>
    Task<ProviderStatusResult?> GetStatusAsync(string providerMessageId, CancellationToken ct);
}

public sealed record ProviderSendResult(string ProviderMessageId);

public sealed record ProviderStatusResult(string Status);

/// <summary>A provider failure, already classified as transient (retry) or permanent (fail now).</summary>
public class SmsProviderException(string message, bool isTransient, Exception? inner = null)
    : Exception(message, inner)
{
    public bool IsTransient { get; } = isTransient;
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Polly.Timeout;

namespace SmsService.Api.Sending;

/// <summary>
/// The ONLY code that knows the provider's HTTP API. The contract below is an assumption
/// (SPEC section 11) — adapting to the real simulator should touch this file only.
///   POST {BaseUrl}/send        { "to", "message", "reference" }  ->  { "id" }
///   GET  {BaseUrl}/status/{id}                                    ->  { "status" }
/// Retries are NOT done here: the worker owns them (Attempts in the DB), so there is one source of truth.
/// </summary>
public class SmsProviderClient(HttpClient http) : ISmsProviderClient
{
    // Server-to-server JSON (not HTML), so Hebrew and '+' are sent as-is instead of \uXXXX escapes.
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private sealed record SendRequest(string To, string Message, string Reference);
    private sealed record SendResponse(string? Id);
    private sealed record StatusResponse(string? Status);

    public async Task<ProviderSendResult> SendAsync(string toE164, string body, Guid messageId, CancellationToken ct)
    {
        // messageId is sent as "reference" so a provider that supports it can detect duplicates.
        using var response = await CallAsync(
            () => http.PostAsJsonAsync("send", new SendRequest(toE164, body, messageId.ToString()), JsonOptions, ct), ct);

        var content = await response.Content.ReadFromJsonAsync<SendResponse>(ct);
        if (string.IsNullOrWhiteSpace(content?.Id))
            // Not retried: the provider said OK, so it may have sent it already. Retrying risks a duplicate SMS.
            throw new SmsProviderException("Provider returned success without a message id", isTransient: false);

        return new ProviderSendResult(content.Id);
    }

    public async Task<ProviderStatusResult?> GetStatusAsync(string providerMessageId, CancellationToken ct)
    {
        using var response = await CallAsync(
            () => http.GetAsync($"status/{Uri.EscapeDataString(providerMessageId)}", ct), ct);

        var content = await response.Content.ReadFromJsonAsync<StatusResponse>(ct);
        return content?.Status is { } status ? new ProviderStatusResult(status) : null;
    }

    /// <summary>
    /// Runs the HTTP call and classifies failures (SPEC section 7):
    /// transient = timeout, network error, 5xx, 429; permanent = any other 4xx.
    /// </summary>
    private static async Task<HttpResponseMessage> CallAsync(Func<Task<HttpResponseMessage>> call, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await call();
        }
        catch (TimeoutRejectedException ex)
        {
            throw new SmsProviderException("Provider timeout", isTransient: true, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new SmsProviderException("Provider timeout", isTransient: true, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SmsProviderException($"Provider network error: {ex.Message}", isTransient: true, ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        var status = (int)response.StatusCode;
        var isTransient = status >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests;
        var detail = await ReadSnippetAsync(response, ct);
        response.Dispose();
        throw new SmsProviderException($"Provider returned HTTP {status}: {detail}", isTransient);
    }

    private static async Task<string> ReadSnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        return text.Length <= 200 ? text : text[..200];
    }
}

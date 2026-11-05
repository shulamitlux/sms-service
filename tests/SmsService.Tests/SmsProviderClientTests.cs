using System.Net;
using System.Text;
using FluentAssertions;
using SmsService.Api.Sending;

namespace SmsService.Tests;

/// <summary>Error classification of the provider client (transient vs permanent), with a fake HTTP handler.</summary>
public class SmsProviderClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request);
        }
    }

    private static SmsProviderClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://provider.test/") });

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Success_returns_provider_id_and_sends_expected_contract()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"id":"abc"}"""));
        var messageId = Guid.NewGuid();

        var result = await CreateClient(handler).SendAsync("+972501234567", "hi", messageId, CancellationToken.None);

        result.ProviderMessageId.Should().Be("abc");
        handler.LastRequest!.RequestUri!.ToString().Should().Be("http://provider.test/send");
        handler.LastBody.Should().Be($$"""{"to":"+972501234567","message":"hi","reference":"{{messageId}}"}""");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Http_errors_are_classified(HttpStatusCode code, bool expectedTransient)
    {
        var handler = new StubHandler(_ => Json(code, "{}"));

        var act = () => CreateClient(handler).SendAsync("+972501234567", "hi", Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<SmsProviderException>()).Which.IsTransient.Should().Be(expectedTransient);
    }

    [Fact]
    public async Task Network_error_is_transient()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var act = () => CreateClient(handler).SendAsync("+972501234567", "hi", Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<SmsProviderException>()).Which.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task Success_without_id_is_permanent_to_avoid_duplicates()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "{}"));

        var act = () => CreateClient(handler).SendAsync("+972501234567", "hi", Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<SmsProviderException>()).Which.IsTransient.Should().BeFalse();
    }
}

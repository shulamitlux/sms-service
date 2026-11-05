using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SmsService.Api.Data;
using SmsService.Api.Domain;
using SmsService.Api.Sending;
using SmsService.Tests.Infrastructure;

namespace SmsService.Tests;

/// <summary>Worker against a real SQL Server (rowversion matters here) with a fake provider.</summary>
[Collection(SqlServerCollection.Name)]
public class SendWorkerTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero));
    private readonly ISmsProviderClient _provider = Substitute.For<ISmsProviderClient>();
    private string _connectionString = null!;
    private ServiceProvider _services = null!;
    private SendWorker _worker = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await sql.CreateDatabaseAsync();
        _services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseSqlServer(_connectionString))
            .AddSingleton(_provider)
            .BuildServiceProvider();
        _worker = new SendWorker(_services.GetRequiredService<IServiceScopeFactory>(), new SendWorkerSignal(),
            _clock, NullLogger<SendWorker>.Instance);
    }

    public Task DisposeAsync() => _services.DisposeAsync().AsTask();

    private async Task<Message> AddMessageAsync(string to = "+972501234567", Action<Message>? prepare = null)
    {
        var message = Message.Create(to, "שלום", 1, null, _clock.GetUtcNow());
        prepare?.Invoke(message);
        await using var db = SqlServerFixture.CreateContext(_connectionString);
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    private async Task<Message> ReloadAsync(Guid id)
    {
        await using var db = SqlServerFixture.CreateContext(_connectionString);
        return await db.Messages.SingleAsync(m => m.Id == id);
    }

    [Fact]
    public async Task Pending_message_is_sent_and_gets_provider_id()
    {
        var message = await AddMessageAsync();
        _provider.SendAsync(message.To, message.Body, message.Id, Arg.Any<CancellationToken>())
            .Returns(new ProviderSendResult("prov-123"));

        await _worker.ProcessBatchAsync(CancellationToken.None);

        var saved = await ReloadAsync(message.Id);
        saved.Status.Should().Be(MessageStatus.Sent);
        saved.ProviderMessageId.Should().Be("prov-123");
        saved.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Provider_timeout_is_retried_with_backoff_and_fails_after_3_attempts()
    {
        var message = await AddMessageAsync();
        _provider.SendAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new SmsProviderException("Provider timeout", isTransient: true));

        await _worker.ProcessBatchAsync(CancellationToken.None);
        (await ReloadAsync(message.Id)).Status.Should().Be(MessageStatus.Pending);

        // Not due yet: backoff is respected.
        await _worker.ProcessBatchAsync(CancellationToken.None);
        (await ReloadAsync(message.Id)).Attempts.Should().Be(1);

        _clock.Advance(TimeSpan.FromSeconds(10));
        await _worker.ProcessBatchAsync(CancellationToken.None);
        (await ReloadAsync(message.Id)).Attempts.Should().Be(2);

        _clock.Advance(TimeSpan.FromSeconds(30));
        await _worker.ProcessBatchAsync(CancellationToken.None);

        var saved = await ReloadAsync(message.Id);
        saved.Status.Should().Be(MessageStatus.Failed);
        saved.Attempts.Should().Be(3);
        saved.LastError.Should().Be("Provider timeout");
        await _provider.ReceivedWithAnyArgs(3).SendAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Permanent_provider_error_fails_immediately()
    {
        var message = await AddMessageAsync();
        _provider.SendAsync(default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new SmsProviderException("Provider returned HTTP 400", isTransient: false));

        await _worker.ProcessBatchAsync(CancellationToken.None);

        var saved = await ReloadAsync(message.Id);
        saved.Status.Should().Be(MessageStatus.Failed);
        saved.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Stuck_Sending_message_returns_to_Pending_on_startup()
    {
        var stuck = await AddMessageAsync(prepare: m => m.MarkSending(_clock.GetUtcNow()));
        _clock.Advance(TimeSpan.FromMinutes(3));
        var recent = await AddMessageAsync(prepare: m => m.MarkSending(_clock.GetUtcNow()));

        var recovered = await _worker.RecoverStuckMessagesAsync(CancellationToken.None);

        recovered.Should().Be(1);
        (await ReloadAsync(stuck.Id)).Status.Should().Be(MessageStatus.Pending);
        (await ReloadAsync(recent.Id)).Status.Should().Be(MessageStatus.Sending); // may still be in flight
    }

    [Fact]
    public async Task Failure_of_one_message_does_not_block_the_next()
    {
        var bad = await AddMessageAsync(to: "+972501111111");
        var good = await AddMessageAsync(to: "+972502222222");
        _provider.SendAsync(bad.To, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        _provider.SendAsync(good.To, Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderSendResult("prov-good"));

        await _worker.ProcessBatchAsync(CancellationToken.None);

        (await ReloadAsync(good.Id)).Status.Should().Be(MessageStatus.Sent);
        var badSaved = await ReloadAsync(bad.Id);
        badSaved.Status.Should().Be(MessageStatus.Pending);
        badSaved.LastError.Should().Contain("boom");
    }

    [Fact]
    public async Task Message_updated_concurrently_is_skipped()
    {
        var message = await AddMessageAsync();
        // Simulates another worker that already took the message: its row is no longer Pending.
        await using (var db = SqlServerFixture.CreateContext(_connectionString))
        {
            var other = await db.Messages.SingleAsync(m => m.Id == message.Id);
            other.MarkSending(_clock.GetUtcNow());
            await db.SaveChangesAsync();
        }

        await _worker.ProcessBatchAsync(CancellationToken.None);

        await _provider.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default, default);
    }
}

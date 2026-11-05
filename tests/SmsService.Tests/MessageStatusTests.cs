using FluentAssertions;
using SmsService.Api.Domain;

namespace SmsService.Tests;

public class MessageStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(2));

    private static Message NewMessage() => Message.Create("+972501234567", "שלום", 1, null, Now);

    [Fact]
    public void Pending_to_Sending_to_Sent()
    {
        var message = NewMessage();
        message.Status.Should().Be(MessageStatus.Pending);

        message.MarkSending(Now);
        message.Status.Should().Be(MessageStatus.Sending);

        message.MarkSent("prov-1", Now.AddSeconds(1));
        message.Status.Should().Be(MessageStatus.Sent);
        message.ProviderMessageId.Should().Be("prov-1");
        message.Attempts.Should().Be(1);
        message.UpdatedAt.Should().Be(Now.AddSeconds(1));
    }

    [Fact]
    public void Transient_error_on_first_attempt_returns_to_Pending_with_future_retry()
    {
        var message = NewMessage();
        message.MarkSending(Now);

        message.MarkFailedAttempt("timeout", isTransient: true, Now);

        message.Status.Should().Be(MessageStatus.Pending);
        message.Attempts.Should().Be(1);
        message.NextAttemptAt.Should().Be(Now.AddSeconds(10));
        message.LastError.Should().Be("timeout");
    }

    [Fact]
    public void Second_transient_error_waits_30_seconds()
    {
        var message = NewMessage();
        message.MarkSending(Now);
        message.MarkFailedAttempt("timeout", true, Now);
        message.MarkSending(Now);

        message.MarkFailedAttempt("timeout", true, Now);

        message.Status.Should().Be(MessageStatus.Pending);
        message.NextAttemptAt.Should().Be(Now.AddSeconds(30));
    }

    [Fact]
    public void Transient_error_on_third_attempt_fails()
    {
        var message = NewMessage();
        for (var i = 0; i < 2; i++)
        {
            message.MarkSending(Now);
            message.MarkFailedAttempt("timeout", true, Now);
        }

        message.MarkSending(Now);
        message.MarkFailedAttempt("timeout", true, Now);

        message.Status.Should().Be(MessageStatus.Failed);
        message.Attempts.Should().Be(3);
        message.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public void Permanent_error_fails_immediately()
    {
        var message = NewMessage();
        message.MarkSending(Now);

        message.MarkFailedAttempt("HTTP 400", isTransient: false, Now);

        message.Status.Should().Be(MessageStatus.Failed);
        message.Attempts.Should().Be(1);
    }

    [Fact]
    public void MarkSent_when_not_Sending_throws()
    {
        var message = NewMessage();

        var act = () => message.MarkSent("prov-1", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkSending_when_already_Sent_throws()
    {
        var message = NewMessage();
        message.MarkSending(Now);
        message.MarkSent("prov-1", Now);

        var act = () => message.MarkSending(Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Long_error_is_truncated()
    {
        var message = NewMessage();
        message.MarkSending(Now);

        message.MarkFailedAttempt(new string('x', 1000), true, Now);

        message.LastError.Should().HaveLength(Message.MaxErrorLength);
    }

    [Fact]
    public void Stuck_Sending_recovers_to_Pending_unless_attempts_are_exhausted()
    {
        var message = NewMessage();
        message.MarkSending(Now);
        message.RecoverStuckSending(Now);
        message.Status.Should().Be(MessageStatus.Pending);

        message.MarkSending(Now);
        message.MarkFailedAttempt("timeout", true, Now);
        message.MarkSending(Now); // attempt 3
        message.RecoverStuckSending(Now);
        message.Status.Should().Be(MessageStatus.Failed);
    }
}

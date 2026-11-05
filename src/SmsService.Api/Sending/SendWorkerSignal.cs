using System.Threading.Channels;

namespace SmsService.Api.Sending;

/// <summary>
/// Wakes the SendWorker as soon as a message is created, instead of waiting for the next poll.
/// It is only a signal: the DB is the queue and the source of truth, so a lost signal costs at most one poll interval.
/// </summary>
public class SendWorkerSignal
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(capacity: 100) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify(Guid messageId) => _channel.Writer.TryWrite(messageId);

    /// <summary>Waits until a signal arrives or the timeout passes, then drains pending signals.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.WaitToReadAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout: time for a regular poll.
        }

        while (_channel.Reader.TryRead(out _)) { }
    }
}

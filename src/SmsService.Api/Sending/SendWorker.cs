using Microsoft.EntityFrameworkCore;
using SmsService.Api.Data;
using SmsService.Api.Domain;

namespace SmsService.Api.Sending;

/// <summary>
/// Sends Pending messages to the provider. The Messages table is the queue (polling every second,
/// woken early by <see cref="SendWorkerSignal"/>), so a message saved in the DB can never be "missing from the queue".
/// </summary>
public class SendWorker(
    IServiceScopeFactory scopeFactory,
    SendWorkerSignal signal,
    TimeProvider clock,
    ILogger<SendWorker> logger) : BackgroundService
{
    public const int BatchSize = 20;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan StuckSendingThreshold = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastRecovery = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                // Runs on startup (crash recovery) and then once a minute as a safety net.
                if (clock.GetUtcNow() - lastRecovery >= RecoveryInterval)
                {
                    await RecoverStuckMessagesAsync(stoppingToken);
                    lastRecovery = clock.GetUtcNow();
                }

                processed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // e.g. DB temporarily unavailable. Log and keep the loop alive.
                logger.LogError(ex, "Send worker iteration failed");
            }

            // A full batch means there is probably more work: continue immediately.
            if (processed < BatchSize)
                await signal.WaitAsync(PollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Messages stuck in Sending (the process died mid-send) go back to Pending.
    /// Known trade-off: the provider may already have sent them, so a duplicate SMS is possible.
    /// </summary>
    public async Task<int> RecoverStuckMessagesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = clock.GetUtcNow();
        var threshold = now - StuckSendingThreshold;

        var stuck = await db.Messages
            .Where(m => m.Status == MessageStatus.Sending && m.UpdatedAt < threshold)
            .ToListAsync(ct);

        foreach (var message in stuck)
        {
            message.RecoverStuckSending(now);
            logger.LogWarning("Recovered stuck message {MessageId}. Attempt={Attempt} Status={Status}",
                message.Id, message.Attempts, message.Status);
        }

        await db.SaveChangesAsync(ct);
        return stuck.Count;
    }

    /// <summary>Sends up to <see cref="BatchSize"/> due messages. Returns how many were picked up.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        List<Guid> ids;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = clock.GetUtcNow();
            ids = await db.Messages
                .Where(m => m.Status == MessageStatus.Pending && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
                .OrderBy(m => m.CreatedAt)
                .Select(m => m.Id)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        foreach (var id in ids)
        {
            try
            {
                await ProcessMessageAsync(id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // shutting down; a message left in Sending is recovered on next startup
            }
            catch (Exception ex)
            {
                // One bad message must not stop the batch.
                logger.LogError(ex, "Failed to process message {MessageId}", id);
            }
        }

        return ids.Count;
    }

    private async Task ProcessMessageAsync(Guid id, CancellationToken ct)
    {
        // A fresh DbContext per message keeps failures isolated.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<ISmsProviderClient>();

        var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == id, ct);
        if (message is not { Status: MessageStatus.Pending })
            return; // already handled by someone else

        message.MarkSending(clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // RowVersion changed: something else updated the message after we read it. Skip it.
            logger.LogInformation("Message {MessageId} was modified concurrently; skipping", id);
            return;
        }
        Log(message);

        try
        {
            var result = await provider.SendAsync(message.To, message.Body, message.Id, ct);
            message.MarkSent(result.ProviderMessageId, clock.GetUtcNow());
        }
        catch (SmsProviderException ex)
        {
            message.MarkFailedAttempt(ex.Message, ex.IsTransient, clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Unexpected error (a bug or an unclassified failure): retry, bounded by MaxAttempts.
            logger.LogError(ex, "Unexpected error sending message {MessageId}", id);
            message.MarkFailedAttempt($"Unexpected error: {ex.Message}", isTransient: true, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(ct);
        Log(message);
    }

    private void Log(Message message)
    {
        // Privacy: never log the body or the full phone number.
        if (message.Status == MessageStatus.Failed)
            logger.LogWarning("Message {MessageId} failed. Attempt={Attempt} Status={Status} ToSuffix={ToSuffix} Error={Error}",
                message.Id, message.Attempts, message.Status, LogHelpers.PhoneSuffix(message.To), message.LastError);
        else
            logger.LogInformation("Message {MessageId} status changed. Attempt={Attempt} Status={Status} ToSuffix={ToSuffix} NextAttemptAt={NextAttemptAt}",
                message.Id, message.Attempts, message.Status, LogHelpers.PhoneSuffix(message.To), message.NextAttemptAt);
    }
}

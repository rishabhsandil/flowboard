using Dapper;
using FlowBoard.Core.Data;
using FlowBoard.Core.Data.Queries;

namespace FlowBoard.Api.Auth;

/// <summary>
/// Periodically deletes used / expired rows from <c>password_reset_tokens</c>
/// so the table does not grow without bound (audit #9). Rows linger for a
/// short grace window so operators can still correlate "who redeemed this"
/// in logs immediately after a reset; default cutoff is 7 days.
///
/// Tuning knobs live under <c>Forgot:Cleanup</c>:
///   • <c>IntervalSeconds</c> — how often to sweep (default 6 h)
///   • <c>RetentionDays</c>   — minimum age of a row before deletion (default 7)
///   • <c>Enabled</c>         — set false in tests / non-prod if desired
/// </summary>
public sealed class PasswordResetCleanupService : BackgroundService
{
    private readonly DbConnectionFactory _db;
    private readonly IConfiguration _config;
    private readonly ILogger<PasswordResetCleanupService> _log;

    public PasswordResetCleanupService(
        DbConnectionFactory db,
        IConfiguration config,
        ILogger<PasswordResetCleanupService> log)
    {
        _db = db; _config = config; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("Forgot:Cleanup:Enabled", true))
        {
            _log.LogInformation("PasswordResetCleanupService disabled by config; exiting.");
            return;
        }

        var interval = TimeSpan.FromSeconds(
            _config.GetValue("Forgot:Cleanup:IntervalSeconds", 6 * 60 * 60));
        var retention = TimeSpan.FromDays(
            _config.GetValue("Forgot:Cleanup:RetentionDays", 7));

        // Small jitter on first run so multiple instances don't all hit the
        // DB at the same instant on startup.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 30)), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var c = _db.Create();
                var deleted = await c.ExecuteAsync(
                    PasswordResetTokenQueries.DeleteStaleRows,
                    new { Cutoff = DateTime.UtcNow - retention });
                if (deleted > 0)
                {
                    _log.LogInformation(
                        "PasswordResetCleanup deleted {Count} stale token rows", deleted);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "PasswordResetCleanup sweep failed");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}

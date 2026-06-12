using Npgsql;

namespace FlowBoard.Core.Data;

/// <summary>
/// Retries a database operation a few times when Postgres reports a transient
/// concurrency failure — deadlock detected (SQLSTATE <c>40P01</c>) or
/// serialization failure (<c>40001</c>). Both are safe to retry because the
/// failing transaction is fully rolled back by the server before the error
/// surfaces, so the work is re-applied cleanly on the next attempt.
///
/// Issue-number allocation is serialized on a single per-project counter row
/// (see <c>assign_issue_number()</c> in schema.sql) and therefore cannot
/// deadlock on its own. This helper is the defence-in-depth guard for inserts
/// that ALSO fan out into the points-rollup / status-sync triggers, where two
/// concurrent writers touching the same parent issue could still race.
/// </summary>
public static class PostgresRetry
{
    private static readonly string[] TransientStates = { "40P01", "40001" };

    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, int maxAttempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (PostgresException ex)
                when (TransientStates.Contains(ex.SqlState) && attempt < maxAttempts)
            {
                // Brief, growing back-off so the retry doesn't immediately
                // re-collide with the transaction that won the first race.
                await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt));
            }
        }
    }
}

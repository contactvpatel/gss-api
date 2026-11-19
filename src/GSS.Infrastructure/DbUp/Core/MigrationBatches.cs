namespace GSS.Infrastructure.DbUp.Core;

/// <summary>
/// Splits pending scripts, in order, into runs that share a transaction mode. A script named
/// <c>...-NoTransaction.sql</c> runs outside a transaction, which <c>CREATE INDEX CONCURRENTLY</c> needs; every other
/// script runs in its own transaction.
/// </summary>
public static class MigrationBatches
{
    /// <summary>File-name suffix of a script that must run outside a transaction.</summary>
    public const string NoTransactionSuffix = "-NoTransaction.sql";

    /// <summary>True for a script that must run outside a transaction.</summary>
    public static bool IsNoTransaction(string scriptName)
    {
        ArgumentNullException.ThrowIfNull(scriptName);
        return scriptName.EndsWith(NoTransactionSuffix, StringComparison.Ordinal);
    }

    /// <summary>Consecutive scripts with the same mode, in the given order.</summary>
    public static IReadOnlyList<(bool Transactional, IReadOnlyList<string> Scripts)> Plan(IEnumerable<string> pendingScripts)
    {
        ArgumentNullException.ThrowIfNull(pendingScripts);

        List<(bool Transactional, IReadOnlyList<string> Scripts)> batches = [];
        List<string>? current = null;
        bool currentTransactional = false;
        foreach (string script in pendingScripts)
        {
            bool transactional = !IsNoTransaction(script);
            if (current is null || transactional != currentTransactional)
            {
                current = [];
                currentTransactional = transactional;
                batches.Add((transactional, current));
            }

            current.Add(script);
        }

        return batches;
    }
}

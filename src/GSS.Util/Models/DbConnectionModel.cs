using System.ComponentModel.DataAnnotations;

namespace GSS.Util.Models;

/// <summary>Read and write database connection settings. Infrastructure builds the connection strings (<c>DatabaseConnectionStrings</c>).</summary>
public class DbConnectionModel
{
    /// <summary>Read-only connection.</summary>
    public ConnectionModel Read { get; set; } = new();

    /// <summary>Write connection.</summary>
    public ConnectionModel Write { get; set; } = new();

    /// <summary>
    /// Most pods of this service running at once: replicas plus rolling-update surge, or the autoscaler's maximum.
    /// Startup warns when that many pods' pools exceed the server's connection limit. Default 1.
    /// </summary>
    [Range(1, 10000, ErrorMessage = "MaxReplicas must be 1-10000.")]
    public int? MaxReplicas { get; set; }

    /// <summary>
    /// True when PgBouncer, RDS Proxy or another pooler sits in front of PostgreSQL: the pools then count against the
    /// pooler's client limit, not the server's, so the startup connection check is skipped.
    /// </summary>
    public bool BehindConnectionPooler { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace GSS.Util.Models;

/// <summary>
/// Individual database connection settings.
/// </summary>
public class ConnectionModel
{
    /// <summary>Host.</summary>
    [Required]
    public string Host { get; set; } = string.Empty;

    /// <summary>Port (1-65535).</summary>
    [RegularExpression("^([1-9][0-9]{0,3}|[1-5][0-9]{4}|6[0-4][0-9]{3}|65[0-4][0-9]{2}|655[0-2][0-9]|6553[0-5])$", ErrorMessage = "Port must be 1-65535.")]
    public string Port { get; set; } = "5432";

    /// <summary>Database name.</summary>
    [Required]
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>User ID.</summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>Password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Disable, Allow, Prefer, Require, VerifyCA or VerifyFull (any case). Blank means Prefer; any other value fails
    /// startup, so a typo never weakens TLS.
    /// </summary>
    [RegularExpression(@"^\s*$|^(?i:Disable|Allow|Prefer|Require|VerifyCA|VerifyFull)$",
        ErrorMessage = "SslMode must be Disable, Allow, Prefer, Require, VerifyCA or VerifyFull.")]
    public string? SslMode { get; set; }

    /// <summary>Max pool size per pod (at least 1, and not below MinPoolSize). Default 20; pods times this must fit the server's max_connections.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "MaxPoolSize must be at least 1.")]
    public int? MaxPoolSize { get; set; }

    /// <summary>Min pool size (0 or more, not above MaxPoolSize). Default 5.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "MinPoolSize must not be negative.")]
    public int? MinPoolSize { get; set; }

    /// <summary>Idle seconds before a pooled connection closes (at least Npgsql's 10 s pruning interval). Default 300.</summary>
    [Range(10, int.MaxValue, ErrorMessage = "ConnectionIdleLifetime must be at least 10 seconds.")]
    public int? ConnectionIdleLifetime { get; set; }

    /// <summary>Command timeout in seconds; 0 = no limit. Default 30.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "CommandTimeout must not be negative.")]
    public int? CommandTimeout { get; set; }

    /// <summary>Connect timeout in seconds (0-1024, Npgsql's limit; 0 = no limit). Default 15.</summary>
    [Range(0, 1024, ErrorMessage = "Timeout must be 0-1024 seconds.")]
    public int? Timeout { get; set; }

    /// <summary>
    /// Seconds of inactivity before Npgsql sends a keepalive query, idle pooled connections included; 0 = off.
    /// Default 30: below load balancer and NAT idle timeouts (Azure 4 min, AWS NLB 350 s), so the MinPoolSize
    /// connections, which never idle out, are not silently dropped, and a dead one (failover) leaves the pool before a
    /// request gets it.
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "KeepAlive must not be negative.")]
    public int? KeepAlive { get; set; }

    /// <summary>Connection lifetime in seconds; 0 = unlimited. Default 0.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "ConnectionLifetime must not be negative.")]
    public int? ConnectionLifetime { get; set; }

    /// <summary>Max auto-prepared statements; 0 disables auto-prepare. Default 100.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "MaxAutoPrepare must not be negative.")]
    public int? MaxAutoPrepare { get; set; }

    /// <summary>Uses before a statement is auto-prepared (at least 1). Default 2.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "AutoPrepareMinUsages must be at least 1.")]
    public int? AutoPrepareMinUsages { get; set; }
}

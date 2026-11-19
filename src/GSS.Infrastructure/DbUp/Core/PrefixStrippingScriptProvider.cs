using System.Reflection;
using DbUp.Engine;
using DbUp.Engine.Transactions;
using DbUp.ScriptProviders;

namespace GSS.Infrastructure.DbUp.Core;

/// <summary>
/// Strips the namespace prefix so SchemaVersions journals bare file names.
/// </summary>
internal sealed class PrefixStrippingScriptProvider(
    Assembly assembly,
    string namespacePrefix,
    Func<string, bool> filter) : IScriptProvider
{
    private readonly EmbeddedScriptProvider _inner = new(assembly, filter);
    private readonly string _prefix = namespacePrefix.EndsWith('.') ? namespacePrefix : namespacePrefix + ".";

    public IEnumerable<SqlScript> GetScripts(IConnectionManager connectionManager)
    {
        return _inner.GetScripts(connectionManager)
            .Select(s => new SqlScript(
                s.Name.StartsWith(_prefix) ? s.Name[_prefix.Length..] : s.Name,
                s.Contents,
                s.SqlScriptOptions));
    }
}

using FluentAssertions;
using GSS.Core.Entities;
using GSS.Core.Interfaces.Base;
using GSS.Infrastructure.Interfaces;
using GSS.Infrastructure.Repositories.Base;
using Microsoft.Extensions.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace GSS.ArchitectureTests;

/// <summary>
/// <c>DapperRepositoryBase</c> builds each repository type's SQL once, from whichever instance asks first, and shares
/// it (<c>RepositorySql</c>). So the members it is built from must be constants: they may not use <c>this</c>
/// (fields, primary-constructor parameters, a <c>BatchColumns { get; } = [...]</c> initializer rebuilt per instance),
/// read a mutable static field, or call anything but pure helpers. Otherwise one request's values would be served to
/// every later one.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class RepositorySqlTests
{
    /// <summary>The members <c>BuildSql</c> reads (getters by their IL names).</summary>
    private static readonly HashSet<string> SqlSources =
        ["get_TableName", "get_Schema", "get_SelectSql", "get_BatchColumns", "BuildInsertSql", "BuildUpdateSql"];

    [Fact]
    public void Repositories_BuildTheirSqlFromConstantsOnly()
    {
        (var repositories, var violations) = Inspect(typeof(DapperRepositoryBase<>).Assembly.Location);

        repositories.Should().ContainSingle("the scaffold currently contains only the repository base")
            .Which.Should().StartWith("DapperRepositoryBase");
        violations.Should().BeEmpty(
            "a repository's SQL is built once per type and shared by every instance, so it must come from constants");
    }

    /// <summary>The rule catches each way a SQL source could depend on an instance or on changing state.</summary>
    [Fact]
    public void Rule_FlagsInstanceAndMutableState()
    {
        (_, var violations) = Inspect(typeof(StatefulRepository).Assembly.Location);

        violations.Should().Contain(v => v.StartsWith("StatefulRepository.get_TableName: uses this", StringComparison.Ordinal));
        violations.Should().Contain(v => v.StartsWith("StatefulRepository.get_BatchColumns: uses this", StringComparison.Ordinal),
            "a { get; } = [...] initializer is rebuilt in every instance");
        violations.Should().Contain(v => v.StartsWith("StatefulRepository.get_Schema: reads mutable static", StringComparison.Ordinal));
        violations.Should().Contain(v => v.StartsWith("StatefulRepository.BuildInsertSql: calls System.Environment::GetEnvironmentVariable", StringComparison.Ordinal));
        violations.Should().NotContain(v => v.StartsWith("StatefulRepository.get_SelectSql", StringComparison.Ordinal),
            "a constant is fine");
        violations.Should().NotContain(v => v.StartsWith("StatefulRepository.BuildUpdateSql", StringComparison.Ordinal),
            "an expression of constants is fine");
    }

    private static (IReadOnlyList<string> Repositories, IReadOnlyList<string> Violations) Inspect(string assemblyPath)
    {
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        List<string> repositories = [];
        List<string> violations = [];
        foreach (var type in assembly.MainModule.GetTypes().Where(IsRepositoryOrBase))
        {
            repositories.Add(type.Name);
            foreach (var method in type.Methods.Where(m => m.HasBody && SqlSources.Contains(m.Name)))
            {
                violations.AddRange(method.Body.Instructions
                    .Select(Problem)
                    .OfType<string>()
                    .Distinct()
                    .Select(problem => $"{type.Name}.{method.Name}: {problem}"));
            }
        }

        return (repositories, violations);
    }

    private static string? Problem(Instruction instruction)
    {
        var code = instruction.OpCode;

        // The SQL sources take no parameters, so any argument load is `this`.
        if (code == OpCodes.Ldarg_0 || code == OpCodes.Ldarg || code == OpCodes.Ldarg_S || code == OpCodes.Ldarga || code == OpCodes.Ldarga_S)
        {
            return "uses this (instance state)";
        }

        if ((code == OpCodes.Ldsfld || code == OpCodes.Ldsflda) && instruction.Operand is FieldReference fieldReference)
        {
            var field = fieldReference.Resolve();
            bool constant = field is null || field.IsInitOnly || field.IsLiteral || IsCompilerGenerated(field.DeclaringType);
            return constant ? null : $"reads mutable static {fieldReference.DeclaringType.Name}.{fieldReference.Name}";
        }

        if ((code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj) && instruction.Operand is MethodReference called)
        {
            return IsPure(called) ? null : $"calls {called.DeclaringType.FullName}::{called.Name}";
        }

        return null;
    }

    /// <summary>
    /// What a constant SQL source may call: the base's <c>Column</c> helper, delegate and collection construction
    /// (the compiler's code for lambdas and collection expressions), and string building.
    /// </summary>
    private static bool IsPure(MethodReference method)
    {
        var declaring = method.DeclaringType;
        string ns = declaring.Namespace;
        string name = declaring.Name;
        return (name.StartsWith("DapperRepositoryBase", StringComparison.Ordinal) && method.Name == "Column")
            || IsCompilerGenerated(declaring.Resolve())
            || (ns == "System" && (name == "String" || name.StartsWith("Func", StringComparison.Ordinal) || name.StartsWith("ReadOnlySpan", StringComparison.Ordinal) || name.StartsWith("Span", StringComparison.Ordinal)))
            || ns == "System.Collections.Generic"
            || ns == "System.Runtime.CompilerServices"
            || ns == "System.Runtime.InteropServices";
    }

    private static bool IsCompilerGenerated(TypeDefinition? type)
    {
        for (var current = type; current != null; current = current.DeclaringType)
        {
            if (current.Name.StartsWith('<') || current.CustomAttributes.Any(a => a.AttributeType.Name == nameof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRepositoryOrBase(TypeDefinition type)
    {
        for (TypeReference? current = type; current != null; current = current.Resolve()?.BaseType)
        {
            if (current.Namespace == typeof(DapperRepositoryBase<>).Namespace && current.Name == typeof(DapperRepositoryBase<>).Name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Breaks the rule in each way <see cref="Rule_FlagsInstanceAndMutableState"/> checks. Never instantiated.</summary>
    private sealed class StatefulRepository(
        IDapperConnectionFactory connectionFactory,
        IDapperTransactionManager transactionManager,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        IUserContext userContext,
        string tenant)
        : DapperRepositoryBase<SqlProbeEntity>(connectionFactory, transactionManager, loggerFactory, timeProvider, userContext)
    {
        private const string Columns = @"""id"" AS Id";

        public static string MutableSchema = "gss";

        protected override string TableName => tenant + "_probes";

        protected override string Schema => MutableSchema;

        protected override string SelectSql => "SELECT " + Columns + @" FROM ""gss"".""probes""";

        protected override IReadOnlyList<BatchColumn> BatchColumns { get; } = [Column("name", e => e.Name)];

        protected override string BuildInsertSql()
        {
            return Environment.GetEnvironmentVariable("INSERT_SQL") ?? string.Empty;
        }

        protected override string BuildUpdateSql()
        {
            return string.Concat(@"UPDATE ""gss"".""probes"" SET ", Columns);
        }
    }

    private sealed class SqlProbeEntity : EntityBase
    {
        public string Name { get; set; } = string.Empty;
    }
}

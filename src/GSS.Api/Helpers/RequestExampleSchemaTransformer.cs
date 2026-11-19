using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GSS.Business.DTOs;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GSS.Api.Helpers;

/// <summary>
/// Adds request body examples to DTO schemas so Swagger UI pre-fills them. Built from typed DTOs, so renames break the build. Add an entry per feature request DTO.
/// </summary>
public sealed class RequestExampleSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly FrozenDictionary<Type, JsonNode> Examples = new Dictionary<Type, JsonNode>
    {
        [typeof(SsoRenewTokenRequest)] = ToNode(new SsoRenewTokenRequest
        {
            RefreshToken = "<refresh-token>"
        })
    }.ToFrozenDictionary();

    /// <inheritdoc />
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (Examples.TryGetValue(context.JsonTypeInfo.Type, out var example))
        {
            schema.Examples = [example.DeepClone()];
        }

        return Task.CompletedTask;
    }

    private static JsonNode ToNode<T>(T value)
    {
        return JsonSerializer.SerializeToNode(value, SerializerOptions)!;
    }
}

using Microsoft.OpenApi;

namespace GSS.Api.Helpers;

/// <summary>Turns a schema from <c>GetOrCreateSchemaAsync</c> into component references, as the framework does.</summary>
internal static class OpenApiSchemaReferences
{
    private const string SchemaIdKey = "x-schema-id";
    private const string NullablePropertyKey = "x-is-nullable-property";

    /// <summary>Replaces each named schema with a component reference, registering it once.</summary>
    public static IOpenApiSchema ToReferences(IOpenApiSchema schema, OpenApiDocument document)
    {
        if (schema is not OpenApiSchema inline)
        {
            return schema;
        }

        if (inline.Properties is { } properties)
        {
            foreach (string name in properties.Keys.ToList())
            {
                properties[name] = ToReferences(properties[name], document);
            }
        }

        if (inline.Items is { } items)
        {
            inline.Items = ToReferences(items, document);
        }

        if (inline.Metadata is not { } metadata || !metadata.TryGetValue(SchemaIdKey, out object? value) || value is not string { Length: > 0 } id)
        {
            return inline;
        }

        bool nullable = metadata.TryGetValue(NullablePropertyKey, out object? isNullable) && isNullable is true;
        if (inline.Type is { } type)
        {
            inline.Type = type & ~JsonSchemaType.Null;
        }

        document.AddComponent<IOpenApiSchema>(id, inline);
        OpenApiSchemaReference reference = new(id, document);
        return nullable ? new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, reference] } : reference;
    }
}

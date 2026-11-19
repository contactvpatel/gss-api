using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GSS.Api.Helpers;

/// <summary>
/// Documents an action with several 200 <see cref="ProducesResponseTypeAttribute"/>s (GetAll: list or page) as
/// <c>oneOf</c> of component references; the API Explorer keeps only one response type per status code.
/// </summary>
public sealed class MultipleOkResponsesOperationTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        Type[] okTypes = [.. context.Description.ActionDescriptor.EndpointMetadata
            .OfType<ProducesResponseTypeAttribute>()
            .Where(a => a.StatusCode == StatusCodes.Status200OK && a.Type != typeof(void))
            .Select(a => a.Type)
            .Distinct()];

        if (okTypes.Length < 2 || context.Document is not { } document
            || operation.Responses?.GetValueOrDefault("200") is not { Content: { } content })
        {
            return;
        }

        List<IOpenApiSchema> shapes = [];
        foreach (var type in okTypes)
        {
            shapes.Add(OpenApiSchemaReferences.ToReferences(await context.GetOrCreateSchemaAsync(type, cancellationToken: cancellationToken), document));
        }

        foreach (var mediaType in content.Values.OfType<OpenApiMediaType>())
        {
            mediaType.Schema = new OpenApiSchema { OneOf = shapes };
        }
    }

}

using GSS.Api.Authentication;
using GSS.Api.Authorization;
using GSS.Api.Filters;
using GSS.Api.Models;
using GSS.Core.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

namespace GSS.Api.Helpers;

/// <summary>
/// Documents what the pipeline adds around each action: on every authenticated action, the SSO bearer token as a
/// security scheme, plus the x-app-* caller headers only where they are checked (<see cref="RequiresCallerHeadersAttribute"/>,
/// <see cref="AsmAuthorizeAttribute"/>), and the 401/403/429/5xx error envelopes; 403/502/504 likewise only where the
/// headers are checked. The headers are entered once in Swagger UI's Authorize dialog, which sends them only to the
/// operations that list them; sending them elsewhere is harmless (ignored). Responses an action declares itself are kept.
/// </summary>
public sealed class ApiSecurityOperationTransformer : IOpenApiOperationTransformer
{
    /// <summary>Security scheme name in the document.</summary>
    public const string BearerSchemeName = "Bearer";

    /// <summary>The x-app-* caller headers; each is an API-key-in-header scheme named after its header.</summary>
    private static readonly (string Header, string Description)[] CallerHeaders =
    [
        (AsmPermissionHandler.PositionIdHeader, "Position the caller acts as (a number)."),
        (AsmPermissionHandler.RoleIdHeader, "Role the caller holds on that position (a number)."),
        (AsmPermissionHandler.DivisionIdHeader, "Division of that position (a number)."),
        (AsmPermissionHandler.DepartmentIdHeader, "Department ids of that position, comma-separated.")
    ];

    /// <inheritdoc />
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.Document is not { } document)
        {
            return;
        }

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        bool anonymous = metadata.OfType<IAllowAnonymous>().Any();
        bool callerHeaders = metadata.Any(m => m is RequiresCallerHeadersAttribute or AsmAuthorizeAttribute);
        bool rateLimited = !metadata.OfType<DisableRateLimitingAttribute>().Any();
        bool readsBearerToken = metadata.OfType<ReadsBearerTokenAttribute>().Any();
        var idempotent = metadata.OfType<IdempotentAttribute>().FirstOrDefault();

        var error = OpenApiSchemaReferences.ToReferences(
            await context.GetOrCreateSchemaAsync(typeof(Response<object>), cancellationToken: cancellationToken), document);
        operation.Responses ??= [];

        if (!anonymous)
        {
            // One requirement (AND): the bearer token, plus every caller header where they are checked. A second
            // requirement would mean OR (the headers alone would suffice), so never split them out.
            EnsureBearerScheme(document);
            OpenApiSecurityRequirement requirement = new() { [new OpenApiSecuritySchemeReference(BearerSchemeName, document)] = [] };
            if (callerHeaders)
            {
                foreach ((string header, string description) in CallerHeaders)
                {
                    EnsureScheme(document, header, new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.ApiKey,
                        In = ParameterLocation.Header,
                        Name = header,
                        Description = description
                    });
                    requirement[new OpenApiSecuritySchemeReference(header, document)] = [];
                }
            }

            operation.Security = [requirement];
            AddResponse(operation, StatusCodes.Status401Unauthorized, "Missing, invalid or expired bearer token.", error);
            AddResponse(operation, StatusCodes.Status503ServiceUnavailable, "A required service (SSO, MIS or ASM) is unavailable; see Retry-After when present.", error);
        }
        else if (readsBearerToken)
        {
            // Anonymous, but the action reads the bearer token itself (it may be expired): bearer only, no caller headers.
            EnsureBearerScheme(document);
            operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerSchemeName, document)] = [] }];
        }

        if (callerHeaders)
        {
            AddResponse(operation, StatusCodes.Status403Forbidden, "The x-app-* headers do not match a position the caller holds, or it lacks the permission.", error);
            AddResponse(operation, StatusCodes.Status502BadGateway, "An upstream service (MIS or ASM) failed.", error);
            AddResponse(operation, StatusCodes.Status504GatewayTimeout, "An upstream service (MIS or ASM) did not answer in time.", error);
        }

        if (idempotent is not null)
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = IdempotencyLimits.HeaderName,
                In = ParameterLocation.Header,
                Required = idempotent.Required,
                Description = "Client-chosen key (for example a GUID, at most 255 printable characters). A retry of the same request with the same key returns the first response (header Idempotent-Replayed: true) instead of creating the resource again; the same key with a different request is a 422. Keys are kept for 24 hours.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = IdempotencyLimits.MaxKeyLength }
            });
            AddResponse(operation, StatusCodes.Status422UnprocessableEntity, "The Idempotency-Key was already used with a different request.", error);
        }

        if (rateLimited)
        {
            AddResponse(operation, StatusCodes.Status429TooManyRequests, "Rate limit exceeded; retry after the Retry-After delay.", error);
        }

        AddResponse(operation, StatusCodes.Status500InternalServerError, "Unexpected error; errorId is the request's correlation id.", error);
    }

    private static void EnsureBearerScheme(OpenApiDocument document)
    {
        EnsureScheme(document, BearerSchemeName, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "SSO-issued JWT access token. Send as: Authorization: Bearer <token>"
        });
    }

    private static void EnsureScheme(OpenApiDocument document, string name, OpenApiSecurityScheme scheme)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes.TryAdd(name, scheme);
    }

    private static void AddResponse(OpenApiOperation operation, int statusCode, string description, IOpenApiSchema schema)
    {
        operation.Responses!.TryAdd(statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture), new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = schema }
            }
        });
    }
}

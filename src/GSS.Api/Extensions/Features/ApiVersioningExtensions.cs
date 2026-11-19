using Asp.Versioning;

namespace GSS.Api.Extensions.Features;

/// <summary>
/// API versioning.
/// </summary>
public static class ApiVersioningExtensions
{
    /// <summary>
    /// Major-version API versioning from the URL segment (required) and the <c>api-version</c> header; no default.
    /// Deprecation and sunset dates come from <c>ApiPipeline:VersionDeprecation</c> (ApiPipeline.NET.Versioning);
    /// Asp.Versioning writes the <c>Deprecation</c>, <c>Sunset</c> and <c>Link</c> headers, so reporting must stay on.
    /// </summary>
    public static void ConfigureApiVersioning(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApiPipelineVersionDeprecation(configuration);

        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1);
            options.ReportApiVersions = true;
            options.ApiVersionReader = ApiVersionReader.Combine(
                new UrlSegmentApiVersionReader(),
                new HeaderApiVersionReader("api-version")
            );
        })
        .AddMvc()
        .AddApiExplorer(options =>
        {
            // Group name "v1" matches the default OpenAPI document name
            options.GroupNameFormat = "'v'V";
            options.SubstituteApiVersionInUrl = true;
        });
    }
}

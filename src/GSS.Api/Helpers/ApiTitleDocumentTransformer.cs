using GSS.Util;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GSS.Api.Helpers;

/// <summary>
/// Sets the OpenAPI document title from configuration: "App | vVersion | Build # n". Swagger UI uses the same title.
/// </summary>
public sealed class ApiTitleDocumentTransformer(IConfiguration configuration) : IOpenApiDocumentTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        string title = GetApiTitle(configuration);
        if (!string.IsNullOrEmpty(title))
        {
            document.Info ??= new OpenApiInfo();
            document.Info.Title = title;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the API title from configuration, or an empty string when no part is configured.
    /// </summary>
    public static string GetApiTitle(IConfiguration configuration)
    {
        string? appName = configuration.GetValue<string>(ConfigurationKeys.AppSettingsApplicationName);
        string? version = configuration.GetValue<string>(ConfigurationKeys.AppSettingsApplicationVersion);
        string? buildNumber = configuration.GetValue<string>(ConfigurationKeys.AppBuildNumber);

        List<string> parts = new List<string>();
        if (!string.IsNullOrEmpty(appName))
        {
            parts.Add(appName);
        }

        if (!string.IsNullOrEmpty(version))
        {
            parts.Add(version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : $"v{version}");
        }

        if (!string.IsNullOrEmpty(buildNumber))
        {
            parts.Add($"Build # {buildNumber}");
        }

        return string.Join(" | ", parts);
    }
}

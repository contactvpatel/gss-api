using System.Net;
using System.Text.Json;
using FluentAssertions;
using GSS.IntegrationTests.Fixtures;
using Xunit;

namespace GSS.IntegrationTests.API;

[Collection("IntegrationDatabase")]
[Trait("Category", TestCategories.Integration)]
public class OpenApiIntegrationTests(WebAppFactory factory) : ApiIntegrationTestBase(factory)
{
    [Fact]
    public async Task GetOpenApiDocument_V1_ListsVersionedEndpoints()
    {
        HttpResponseMessage response = await Client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync();
        using JsonDocument json = JsonDocument.Parse(content);
        List<string> paths = json.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        // API Explorer groups endpoints as "v1"; a mismatch with the document name silently empties the document
        paths.Should().Contain("/api/v1/sso/renew-token");
        paths.Should().NotContain(p => p.Contains("{version}"), because: "the version must be substituted into route templates");
    }

    [Fact]
    public async Task GetOpenApiDocument_Info_TitleComesFromAppSettings()
    {
        HttpResponseMessage response = await Client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        json.RootElement.GetProperty("info").GetProperty("title").GetString()
            .Should().StartWith("gss-api-dev | v1.0.0", because: "the title is \"<ApplicationName> | v<ApplicationVersion>[ | Build # <n>]\"");
    }

    [Fact]
    public async Task GetOpenApiDocument_Operations_HaveNoVersionPathParameter()
    {
        HttpResponseMessage response = await Client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        List<string> versionParameters = json.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => (Path: path.Name, Operation: operation)))
            .Where(entry => entry.Operation.Value.ValueKind == JsonValueKind.Object
                && entry.Operation.Value.TryGetProperty("parameters", out _))
            .SelectMany(entry => entry.Operation.Value.GetProperty("parameters").EnumerateArray()
                .Where(parameter => parameter.TryGetProperty("name", out JsonElement name) && name.GetString() == "version"
                    && parameter.TryGetProperty("in", out JsonElement location) && location.GetString() == "path")
                .Select(_ => $"{entry.Operation.Name.ToUpperInvariant()} {entry.Path}"))
            .ToList();

        versionParameters.Should().BeEmpty(because: "SubstituteApiVersionInUrl puts the version in the path, so Swagger UI has nothing to fill in");
    }

    [Theory]
    [InlineData("SsoRenewTokenRequest", "refreshToken", "<refresh-token>")]
    public async Task GetOpenApiDocument_RequestDtoSchemas_HaveExamples(string schemaName, string property, string expected)
    {
        HttpResponseMessage response = await Client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement example = json.RootElement
            .GetProperty("components").GetProperty("schemas").GetProperty(schemaName).GetProperty("examples")[0];

        example.GetProperty(property).ToString().Should().Be(expected);
    }

    /// <summary>The 200 response documents list, numbered-page and cursor-page shapes.</summary>
    [Fact]
    public async Task GetOpenApiDocument_PagedOrAllEndpoint_DocumentsEveryShape()
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));

        JsonElement schema = json.RootElement.GetProperty("paths").GetProperty(PagedProbeController.Path).GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");

        schema.GetProperty("oneOf").EnumerateArray().Select(s => s.GetProperty("$ref").GetString()).Should().BeEquivalentTo(
            "#/components/schemas/ResponseOfIReadOnlyListOfProbeItemDto",
            "#/components/schemas/ResponseOfPagedResultOfProbeItemDto",
            "#/components/schemas/ResponseOfCursorPagedResultOfProbeItemDto");
        json.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("ResponseOfPagedResultOfProbeItemDto", out _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task CursorPaging_UsesReturnedCursorToReachTheNextPage()
    {
        using JsonDocument first = JsonDocument.Parse(await Client.GetStringAsync(PagedProbeController.Path + "?pageSize=1"));
        JsonElement firstPage = first.RootElement.GetProperty("data");
        firstPage.GetProperty("items")[0].GetProperty("id").GetInt32().Should().Be(1);
        string next = firstPage.GetProperty("nextCursor").GetString()!;

        using JsonDocument second = JsonDocument.Parse(await Client.GetStringAsync(
            PagedProbeController.Path + "?pageSize=1&includeTotal=true&cursor=" + Uri.EscapeDataString(next)));
        JsonElement secondPage = second.RootElement.GetProperty("data");
        secondPage.GetProperty("items")[0].GetProperty("id").GetInt32().Should().Be(3);
        secondPage.GetProperty("totalCount").GetInt32().Should().Be(2);
        secondPage.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>XML comments reach the document (GenerateDocumentationFile on every src/ project).</summary>
    [Fact]
    public async Task GetOpenApiDocument_XmlComments_DescribeOperationsParametersAndSchemas()
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));
        JsonElement getById = json.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/mis/departments/{departmentId}").GetProperty("get");

        getById.GetProperty("summary").GetString()
            .Should().Be("Gets a department in the caller's division by identifier.", "the controller's comment");
        getById.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == "departmentId")
            .GetProperty("description").GetString().Should().Be("Department identifier.");
        json.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("DepartmentDto")
            .GetProperty("properties").GetProperty("departmentName").GetProperty("description").GetString()
            .Should().Be("Department name.", "comments from referenced projects (Business DTOs) are included too");
    }

    [Fact]
    public async Task GetOpenApiDocument_EveryOperation_HasASummary()
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));

        string[] withoutSummary = [.. json.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(op => op.Value.ValueKind == JsonValueKind.Object && op.Value.TryGetProperty("responses", out _))
                .Where(op => !op.Value.TryGetProperty("summary", out JsonElement summary) || string.IsNullOrWhiteSpace(summary.GetString()))
                .Select(op => $"{op.Name.ToUpperInvariant()} {path.Name}"))];

        withoutSummary.Should().BeEmpty(because: "every action needs a /// <summary> written for API clients");
    }

    /// <summary>Controller and DTO comments are public: developer notes belong in // comments.</summary>
    [Fact]
    public async Task GetOpenApiDocument_Text_HasNoDeveloperNotes()
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));
        List<string> texts = [];
        CollectTexts(json.RootElement, texts);

        texts.Should().NotBeEmpty();
        texts.Where(t => t.Contains(".docs", StringComparison.Ordinal) || t.Contains(".md", StringComparison.Ordinal)
                || t.Contains('<', StringComparison.Ordinal) || t.Contains("&lt;", StringComparison.Ordinal))
            .Should().BeEmpty(because: "doc links and C# signatures mean nothing to API clients");
    }

    private static void CollectTexts(JsonElement element, List<string> texts)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name is "summary" or "description" && property.Value.ValueKind == JsonValueKind.String)
                {
                    texts.Add(property.Value.GetString()!);
                }
                else
                {
                    CollectTexts(property.Value, texts);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                CollectTexts(item, texts);
            }
        }
    }

    [Fact]
    public async Task GetOpenApiDocument_DeclaresTheSsoBearerScheme()
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));

        JsonElement scheme = json.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

        scheme.GetProperty("type").GetString().Should().Be("http");
        scheme.GetProperty("scheme").GetString().Should().Be("bearer");
    }

    /// <summary>
    /// Authenticated actions carry the bearer requirement and pipeline errors; x-app-* headers appear only where they
    /// are checked.
    /// </summary>
    [Fact]
    public async Task GetOpenApiDocument_AuthenticatedOperation_HasSecurityAndErrorResponses()
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));

        JsonElement operation = json.RootElement.GetProperty("paths").GetProperty(PagedProbeController.Path).GetProperty("get");

        operation.GetProperty("security").GetArrayLength().Should().Be(1);
        operation.GetProperty("security")[0].EnumerateObject().Select(p => p.Name).Should().Equal("Bearer");
        JsonElement responses = operation.GetProperty("responses");
        foreach (string status in new[] { "401", "429", "500", "503" })
        {
            string? reference = responses.GetProperty(status).GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").GetProperty("$ref").GetString();
            reference.Should().StartWith("#/components/schemas/Response");
            json.RootElement.GetProperty("components").GetProperty("schemas")
                .TryGetProperty(reference!["#/components/schemas/".Length..], out _).Should().BeTrue();
        }

        responses.TryGetProperty("403", out _).Should().BeFalse(because: "the paged probe does not check the x-app-* headers");
    }

    /// <summary>
    /// Anonymous renew still reads the bearer token itself (it may be expired): bearer only, no x-app-* headers; a
    /// response the action declares (renew's own 401) is kept.
    /// </summary>
    [Fact]
    public async Task GetOpenApiDocument_AnonymousOperationReadingTheBearerToken_HasBearerOnly_AndKeepsDeclaredResponses()
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));

        JsonElement operation = json.RootElement.GetProperty("paths").GetProperty("/api/v1/sso/renew-token").GetProperty("post");

        JsonElement requirement = operation.GetProperty("security").EnumerateArray().Should().ContainSingle().Subject;
        requirement.EnumerateObject().Select(p => p.Name).Should().Equal("Bearer");
        operation.GetProperty("responses").GetProperty("401").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString().Should().EndWith("ResponseOfSsoAuthResponse");
        operation.GetProperty("responses").TryGetProperty("503", out _).Should().BeFalse(because: "no SSO validation runs");
        operation.GetProperty("responses").TryGetProperty("429", out _).Should().BeTrue();
    }

    /// <summary>Same routes as InputBoundaryIntegrationTests.MissingCallerHeaders_Returns403.</summary>
    [Theory]
    [InlineData("/api/v1/mis/departments")]
    [InlineData("/api/v1/mis/roletypes")]
    [InlineData("/api/v1/mis/roles")]
    [InlineData("/api/v1/mis/departments/{departmentId}")]
    [InlineData("/api/v1/mis/roles/{id}")]
    [InlineData("/api/v1/mis/roles/departments/{departmentId}")]
    [InlineData("/api/v1/mis/positions/roles/{roleId}")]
    public async Task GetOpenApiDocument_CallerHeaderOperation_RequiresBearerAndEveryXAppHeader(string path)
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));

        JsonElement operation = json.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get");

        // One requirement (AND): Swagger UI sends the token and every header entered in the Authorize dialog.
        operation.GetProperty("security").GetArrayLength().Should().Be(1);
        operation.GetProperty("security")[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "Bearer", "x-app-position-id", "x-app-role-id", "x-app-division-id", "x-app-department-id");
        operation.GetProperty("responses").TryGetProperty("403", out _).Should().BeTrue();
    }

    /// <summary>Endpoints used to discover caller context require only the bearer token.</summary>
    [Theory]
    [InlineData("/api/v1/mis/person-positions")]
    [InlineData("/api/v1/mis/divisions")]
    [InlineData("/api/v1/asm")]
    public async Task GetOpenApiDocument_OperationWithoutCallerHeaders_RequiresBearerOnly(string path)
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));

        JsonElement operation = json.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get");

        JsonElement requirement = operation.GetProperty("security").EnumerateArray().Should().ContainSingle().Subject;
        requirement.EnumerateObject().Select(p => p.Name).Should().Equal("Bearer");
        operation.GetProperty("responses").TryGetProperty("403", out _).Should().BeFalse();
    }

    /// <summary>Caller headers and the 403 they can cause always appear together in one AND requirement.</summary>
    [Fact]
    public async Task GetOpenApiDocument_CallerHeadersAndTheir403_AlwaysGoTogether()
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));
        List<string> mismatched = [];
        int withHeaders = 0;

        foreach (JsonProperty path in json.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (JsonProperty method in path.Value.EnumerateObject())
            {
                if (!method.Value.TryGetProperty("security", out JsonElement security))
                {
                    continue;
                }

                security.GetArrayLength().Should().Be(1, $"{method.Name} {path.Name} has one requirement");
                bool headers = security[0].EnumerateObject().Any(s => s.Name.StartsWith("x-app-", StringComparison.Ordinal));
                bool forbidden = method.Value.GetProperty("responses").TryGetProperty("403", out _);
                withHeaders += headers ? 1 : 0;
                if (headers != forbidden)
                {
                    mismatched.Add($"{method.Name} {path.Name}");
                }
            }
        }

        mismatched.Should().BeEmpty();
        withHeaders.Should().Be(7, "only the [RequiresCallerHeaders] MIS operations check the headers today");
    }

    [Theory]
    [InlineData("x-app-position-id")]
    [InlineData("x-app-role-id")]
    [InlineData("x-app-division-id")]
    [InlineData("x-app-department-id")]
    public async Task GetOpenApiDocument_DeclaresEachCallerHeaderAsAnApiKeyScheme(string header)
    {
        using JsonDocument json = JsonDocument.Parse(await Client.GetStringAsync("/openapi/v1.json"));

        JsonElement scheme = json.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty(header);

        scheme.GetProperty("type").GetString().Should().Be("apiKey");
        scheme.GetProperty("in").GetString().Should().Be("header");
        scheme.GetProperty("name").GetString().Should().Be(header);
    }

    [Fact]
    public async Task GetSwaggerUi_Anonymous_ServesPageWithItsOwnCsp_AndPointsAtOpenApiDocument()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        HttpResponseMessage page = await Client.GetAsync("/swagger/index.html");
        HttpResponseMessage config = await Client.GetAsync("/swagger/index.js");

        page.StatusCode.Should().Be(HttpStatusCode.OK, because: "Swagger UI runs before authorization, outside Production");
        string csp = string.Join(";", page.Headers.TryGetValues("Content-Security-Policy", out IEnumerable<string>? values)
            ? values
            : page.Content.Headers.TryGetValues("Content-Security-Policy", out IEnumerable<string>? contentValues) ? contentValues : []);
        csp.Should().Contain("default-src 'self'", because: "the API-wide default-src 'none' would blank the page");

        config.StatusCode.Should().Be(HttpStatusCode.OK);
        (await config.Content.ReadAsStringAsync()).Should().Contain("/openapi/v1.json");
    }
}

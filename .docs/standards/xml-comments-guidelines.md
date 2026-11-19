# XML Documentation Comments

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Public API Text](#public-api-text)
- [Code Documentation](#code-documentation)
- [inheritdoc](#inheritdoc)
- [Examples](#examples)
- [Verification](#verification)
- [References](#references)

---

## Overview

Every source project generates an XML documentation file. .NET 10 OpenAPI copies controller/action and DTO XML comments into the public API document, so comments on HTTP-facing types are client-facing contract text, not an internal design notebook.

Microsoft's XML documentation guidance takes precedence if this standard conflicts with the compiler/tag semantics. GSS adds the public-OpenAPI restrictions below.

## Public API Text

Every controller action MUST have a concise `<summary>`. Use `<remarks>` only for client-relevant behavior that does not fit the summary. Document parameters whose meaning is not obvious and keep response/status metadata in `ProducesResponseType` attributes.

Public OpenAPI text MUST NOT contain:

- repository `.docs` or Markdown links;
- C# signatures, generic syntax, or raw XML/HTML markup intended only for developers;
- implementation class names, database details, cache strategy, or internal call flow;
- secrets, example production identifiers, or exception text.

Write what the caller sends/receives and the behavior they can rely on. Developer-only rationale belongs in `//` comments or repository docs.

## Code Documentation

Document public abstractions and non-obvious behavior, especially:

- ownership, lifetime, or side effects;
- null/empty/not-found semantics;
- transaction and concurrency requirements;
- thrown exceptions that are part of the programming contract;
- generic type parameters where the name is insufficient.

Do not restate the member name word for word. Keep comments synchronized with behavior; a stale authoritative-sounding comment is worse than a missing comment.

## inheritdoc

Use `/// <inheritdoc />` when an implementation follows the interface/base contract without adding caller-visible behavior. Add a local `<remarks>` section when the implementation has an important constraint, while retaining inherited summary/parameter documentation.

Do not use `<inheritdoc />` to hide a mismatch between interface and implementation. Update the shared contract when behavior is common, or document the implementation difference explicitly.

## Examples

Actual GSS controller style (`SsoController.RenewToken`, attributes abridged):

```csharp
/// <summary>
/// Renews the access token with a refresh token. Send the current access token as the bearer token; it may be expired.
/// </summary>
/// <param name="request">Refresh token.</param>
/// <returns>New access and refresh tokens, or 401.</returns>
[HttpPost("renew-token")]
public async Task<ActionResult<Response<SsoAuthResponse>>> RenewToken(
    [FromBody] SsoRenewTokenRequest request,
    CancellationToken cancellationToken)
```

Actual shared-contract style (`IRepository<T>`):

```csharp
/// <summary>The page after <paramref name="afterId"/> by ID (keyset: cost independent of depth; 0 for the first page).</summary>
Task<PageSlice<T>> GetPagedAfterAsync(
    int afterId,
    int pageSize,
    bool includeTotal = false,
    CancellationToken cancellationToken = default);
```

These snippets describe real GSS shapes. Feature examples elsewhere must be explicitly labeled illustrative; nonexistent controllers are not “examples from the codebase.”

## Verification

- A build validates XML syntax and unresolved `cref` warnings under warnings-as-errors.
- `OpenApiIntegrationTests` requires an operation summary and rejects `.docs`, `.md`, `<` and `&lt;` in exported summary and description text.
- Review the generated `/openapi/v1.json` when changing controller or DTO comments.
- Keep `GenerateDocumentationFile` enabled for every source project.

## References

- [Recommended XML documentation tags](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags)
- [ASP.NET Core OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview)
- [Swagger UI guide](../guides/swagger-ui.md)

# Local Secrets Management

How to manage sensitive credentials (database passwords, API tokens, app secrets) during local development.

## Overview

Secrets are sensitive values that vary per environment and must never be committed to the repository. GSS uses environment variables loaded from a `.env.local` file in debug mode, keeping secrets out of version control.

**Pattern:**

- `.env.example` — Template (committed) showing required keys with placeholders
- `.env.local` — Your instance with actual values (gitignored, never committed)

## Setup

### 1. Create `.env.local` from `.env.example`

```bash
# Copy the template
cp .env.example .env.local
```

### 2. Fill in actual values

Edit `.env.local` and replace each `set-via-environment` placeholder:

```env
# .env.local
AppSettings__Environment=Development
AppSettings__ApplicationName=gss-api-dev

DbConnectionSettings__Read__Host=localhost
DbConnectionSettings__Read__Port=5432
DbConnectionSettings__Read__DatabaseName=gss
DbConnectionSettings__Read__UserId=postgres
DbConnectionSettings__Read__Password=YOUR_DB_PASSWORD_HERE

DbConnectionSettings__Write__Host=localhost
DbConnectionSettings__Write__Port=5432
DbConnectionSettings__Write__DatabaseName=gss
DbConnectionSettings__Write__UserId=postgres
DbConnectionSettings__Write__Password=YOUR_DB_PASSWORD_HERE

SsoService__Url=https://api.uat.bapsapps.org/sso/api/
MisService__Url=https://api.uat.bapsapps.org/myseva/api/v1/
AsmService__Url=https://api.uat.bapsapps.org/asm/api/v2/

MisService__Headers__AuthAppId=YOUR_MIS_AUTH_APP_ID
MisService__Headers__AuthAppSecret=YOUR_MIS_AUTH_APP_SECRET

AsmService__Headers__AuthAppId=YOUR_ASM_AUTH_APP_ID
AsmService__Headers__AuthAppSecret=YOUR_ASM_AUTH_APP_SECRET

CacheOptions__Redis__Configuration=cache.dev.bapsapps.org:6379
CacheOptions__EnvironmentPrefix=dev

OpenTelemetryOptions__CollectorUrl=http://localhost:4317
```

### 3. Run in Debug Mode

The app auto-loads `.env.local` only when running under a debugger:

**IDE (VSCode, Visual Studio, Rider):**

- Press F5 or click "Run & Debug"
- `.env.local` loads automatically
- Environment variables override `appsettings.json` values

**Command line (no debug):**

```bash
# Does NOT load .env.local (use shell env vars instead)
dotnet run --project src/GSS.Api
```

**Export to shell and run:**

```bash
# Load secrets into shell session, then run
export $(cat .env.local | xargs)
dotnet run --project src/GSS.Api
```

## How It Works

1. **At startup**, `Program.cs` checks `System.Diagnostics.Debugger.IsAttached`
2. **In debug mode** (IDE), it loads `.env.local` using DotEnv.Net
3. **Environment variables** override `appsettings.json` values (see [Configuration Hierarchy](#configuration-hierarchy))
4. **In non-prod and production**, `.env.local` is never loaded; secrets come from vault.

```csharp
// Program.cs
if (System.Diagnostics.Debugger.IsAttached)
{
    DotEnv.Load(options: new DotEnvOptions(envFilePaths: [".env.local"]));
}
```

## Configuration Hierarchy

Values are resolved in this order (later ones win):

1. `appsettings.json` defaults (usually `"set-via-environment"` placeholders)
2. Environment variables (from `.env.local` or shell)
3. Command-line arguments (highest priority)

**Example:**

```json
// appsettings.json
{
  "DbConnectionSettings": {
    "Read": {
      "Password": "set-via-environment"
    }
  }
}
```

```env
# .env.local
DbConnectionSettings__Read__Password=my-secret-password
```

Result: Password is `"my-secret-password"` (env var wins).

## Environment Variables Format

Use double underscores `__` to separate nested keys:

| Key | Format | Example |
| ----- | -------- | --------- |
| Flat | `KEY=value` | `AppSettings__ApplicationName=gss-api-dev` |
| Nested (1 level) | `SECTION__KEY=value` | `CacheOptions__Enabled=true` |
| Nested (2+ levels) | `SECTION__SUBSECTION__KEY=value` | `DbConnectionSettings__Read__Host=localhost` |

[Microsoft docs on configuration binding](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration)

## Common Keys

| Environment | Key | Example Value |
| ------------- | ----- | ---------------- |
| App | `AppSettings__Environment` | `Development` or `QA` |
| Database | `DbConnectionSettings__Read__Host` | `localhost` or `db.example.com` |
| Database | `DbConnectionSettings__Read__Port` | `5432` |
| Database | `DbConnectionSettings__Read__Password` | (actual password) |
| SSO | `SsoService__Url` | `https://api.uat.bapsapps.org/sso/api/` |
| Auth | `MisService__Headers__AuthAppId` | (actual app ID) |
| Cache | `CacheOptions__Redis__Configuration` | `localhost:6379` |
| Observability | `OpenTelemetryOptions__CollectorUrl` | `http://localhost:4317` |

See `.env.example` for the complete list.

## Best Practices

### Do

✅ Copy `.env.example` to `.env.local` to track what keys the app needs  
✅ Keep `.env.local` in `.gitignore` (never commit)  
✅ Replace placeholders with **your machine's actual values**  
✅ Use different values per developer if needed (dev DBs, test API keys)  
✅ Never store secrets in code, config files, or git history  

### Don't

❌ Commit `.env.local` or any `.env.*` file with real secrets  
❌ Share `.env.local` via email, Slack, or any unencrypted channel  
❌ Copy production secrets into `.env.local` (they're in vault)  
❌ Store vault passwords in `.env.local`  
❌ Modify `.env.example` with real secrets  

## Deployed Environments (Dev/QA/UAT/Production)

`.env.local` is **never** used in deployed environments.

**Secrets flow:**

1. Deploy pipeline reads from **vault**
2. Pipeline injects secrets as **environment variables** at runtime
3. App loads them normally (env var override in config binding)

No `.env.local` file exists on the server.

## Troubleshooting

### Secrets not loading

**Symptom:** Startup fails with "set-via-environment" value or validation error

**Solution:**

1. Confirm `.env.local` exists in repo root (same directory as `GSS.slnx`)
2. Run in debug mode (F5 from IDE, not `dotnet run`)
3. Check `.env.local` has the key (copy from `.env.example` if missing)
4. Verify no typos in key name (use `__` for nesting)

### Wrong database or service

**Symptom:** Connected to wrong database or calling wrong API

**Solution:**

1. Check `.env.local` has correct Host/Url/Port
2. Verify they match your local setup (e.g., Postgres on 5432, not 5433)
3. Test connection manually:

   ```bash
   psql -h localhost -p 5432 -U postgres -d gss
   ```

### Still using old values

**Symptom:** App loads old secret value after changing `.env.local`

**Solution:**

1. Restart the IDE or debugger (F5 again)
2. Or export to shell and run:

   ```bash
   export $(cat .env.local | xargs)
   dotnet run --project src/GSS.Api
   ```

## See Also

- [Configuration](../architecture/configuration.md) — How app reads settings
- [Docker Compose](../guides/docker-compose.md) — Running with database + Redis locally
- [Environment Switches](../architecture/environment-switches.md) — Development vs QA vs Production

---

**Last updated:** 2026-11-20

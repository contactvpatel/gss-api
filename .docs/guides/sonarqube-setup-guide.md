# SonarQube SAST Setup Guide

[← Back to README](../README.md)

## Table of Contents

- [Overview](#overview)
- [Architecture: SonarScanner + SonarCloud](#architecture-sonarscanner--sonarcloud)
- [SonarCloud Setup](#sonarcloud-setup)
- [GSS Project Configuration](#gss-project-configuration)
- [Understanding Results](#understanding-results)
- [Integration with CI/CD](#integration-with-cicd)
- [Troubleshooting](#troubleshooting)
- [Related Documentation](#related-documentation)

---

## Overview

This guide covers setting up **SonarQube SAST (Static Application Security Testing)** for local developer analysis using **SonarScanner for .NET (`dotnet-sonarscanner`) with SonarCloud**. Local SAST scanning identifies code quality issues, security vulnerabilities, and technical debt before pushing code to Git.

SonarCloud is a cloud-hosted solution (free tier available) that is recommended for local development—no server infrastructure to manage, quick setup, and works from anywhere.

`scripts/sonarqube-scan.ps1` runs the whole scan. It is not invoked by this repository's `Jenkinsfile`, which only calls the shared `useDotNetPipeline` (the shared library is not in this repository, so what it does beyond that is not documented here).

---

## Architecture: SonarScanner + SonarCloud

The workflow is simple:

```text
Developer Machine
├── Run: pwsh scripts/sonarqube-scan.ps1
├── Executes: dotnet-sonarscanner begin → dotnet build -c Release → dotnet-sonarscanner end
├── Scans: files under src/ (`source.inclusions`); the build compiles all of GSS.slnx, but the test projects are not analysed
└── Reports: Uploads to ↓

SonarCloud (Cloud-Hosted)
├── Stores: Project metadata
├── Stores: Analysis results
├── Provides: Web UI dashboard
└── URL: sonarcloud.io
```

**Key Points:**

- **SonarScanner for .NET** runs locally (a .NET global tool; the script installs it if missing)
- **Analysis** happens on your machine (uses your CPU)
- **Results** are uploaded to SonarCloud
- **Dashboard** available at sonarcloud.io

---

## SonarCloud Setup

**For local development:** SonarCloud provides a cloud-hosted solution with no server infrastructure needed. Free tier is available for open-source and public projects.

**Advantages:**

- ✅ No server to manage or maintain
- ✅ Free tier for open source and public projects
- ✅ Works from anywhere (cloud-hosted)
- ✅ Automatic updated rules and patterns

### Step 1: Create SonarCloud Account

1. Visit <https://sonarcloud.io>
2. Click "Sign up" or "Login with GitHub/GitLab/Bitbucket"
3. Authorize SonarCloud to access your account
4. Create or use existing organization

**First time?** Create a new organization:

- Organization key: `my-organization` (lowercase, no spaces)
- Display name: `My Organization`

### Step 2: Generate Authentication Token

Authentication tokens allow SonarScanner to upload results securely.

1. Visit: <https://sonarcloud.io/account/security>
   - Or: Profile (top-right) → Security
2. Click "Generate token"
3. Give it a name: `Local Development`
4. Set expiration: Optional (90 days recommended)
5. Click "Generate"
6. **Copy the token** (displayed once, can't be seen again)

**Example token:** `sqc_1234567890abcdef1234567890abcdef12345678`

### Step 3: Create Project on SonarCloud

1. Log in to <https://sonarcloud.io>
2. Click "+" → "Create project"
3. Fill in details:
   - **Organization:** Select your organization
   - **Project key:** must match `projectKey` in `sonar-config.json`
   - **Project name:** `GSS API`
   - **Visibility:** Public (free tier) or Private (paid/qualifying projects)
4. Click "Create"

### Step 4: Update sonar-config.json

Set `projectKey` and `sonarCloud.organizationKey` in `sonar-config.json` (project root) to your project and organization, or override them with `SONAR_PROJECT_KEY` / `SONAR_ORG_KEY` (see [GSS Project Configuration](#gss-project-configuration)).

### Step 5: Set Environment Variable

**Windows (PowerShell):**

```powershell
# Set for current session
$env:SONAR_TOKEN = "sqc_1234567890abcdef1234567890abcdef12345678"

# Verify it's set
echo $env:SONAR_TOKEN
```

**macOS/Linux:**

```bash
# Set for current session
export SONAR_TOKEN="sqc_1234567890abcdef1234567890abcdef12345678"

# Verify it's set
echo $SONAR_TOKEN

# Make permanent (add to ~/.bash_profile or ~/.zshrc)
echo 'export SONAR_TOKEN="sqc_1234567890abcdef1234567890abcdef12345678"' >> ~/.bash_profile
source ~/.bash_profile
```

**Important:** Store token securely. Treat it like a password - don't commit it to Git!

### Step 6: Run First Analysis

```powershell
pwsh scripts/sonarqube-scan.ps1
```

**Output will show (abridged):**

```text
[1/4] Checking SonarScanner installation...
✓ SonarScanner found: ...
[2/4] Collecting SonarQube configuration...
✓ Configuration file loaded: sonar-config.json
Server: https://sonarcloud.io (SonarCloud)
Project Key: ...
✓ Configuration valid
[3/4] Checking authentication...
✓ SONAR_TOKEN is set
[4/4] Running SonarQube analysis...
Initializing SonarScanner...
Command: dotnet-sonarscanner begin /k:... /d:sonar.host.url=https://sonarcloud.io ...  (token via SONARQUBE_SCANNER_PARAMS)
Building project...
Uploading analysis results...
✓ Analysis complete
```

### Step 7: View Results

1. Log in to <https://sonarcloud.io>
2. Open your organization
3. Click your project
4. Review:
   - **Issues:** Bugs, vulnerabilities, code smells
   - **Security Hotspots:** Code that needs security review
   - **Coverage:** empty: the script runs no tests and uploads no coverage report
   - **Duplicates:** Repeated code blocks
   - **Metrics:** Quality metrics and trends

---

## GSS Project Configuration

This project keeps the scan settings in `sonar-config.json`; environment variables override them:

**Configuration File:** `sonar-config.json` (project root)

**Current GSS Configuration** (the file also carries `$schema`, `$comment` and an informational `environment` block that the script ignores):

```json
{
  "projectKey": "gss-api",
  "projectName": "GSS API",
  "projectVersion": "1.0.0",
  "sonarCloud": {
    "enabled": true,
    "organizationKey": "86f3bb56-974d-4a8f-becd-6e4a030e5d4a",
    "hostUrl": "https://sonarcloud.io"
  },
  "selfHosted": {
    "enabled": false,
    "hostUrl": "http://localhost:9000"
  },
  "source": {
    "encoding": "utf-8",
    "inclusions": ["src/**"],
    "exclusions": [
      "**/bin/**",
      "**/obj/**",
      "**/test/**",
      "**/*.Test.cs",
      "**/*Tests.cs"
    ]
  },
  "analysis": {
    "beginParameters": {}
  }
}
```

**What the script reads (environment variable first, then the file, then a default or prompt):**

| Setting | Environment | `sonar-config.json` | Otherwise |
| --- | --- | --- | --- |
| Server URL | `SONAR_HOST_URL` | `selfHosted.hostUrl` when `selfHosted.enabled`, else `sonarCloud.hostUrl` | `https://sonarcloud.io` |
| Project key | `SONAR_PROJECT_KEY` | `projectKey` | prompt |
| Organization (SonarCloud only) | `SONAR_ORG_KEY` | `sonarCloud.organizationKey` | prompt |
| Name, version | | `projectName`, `projectVersion` (`/n:`, `/v:`) | |
| Sources | | `source.encoding`, `source.inclusions`, `source.exclusions` (`sonar.sourceEncoding`, `sonar.inclusions`, `sonar.exclusions`) | |
| Other begin-step properties | | `analysis.beginParameters`, each passed as `/d:key=value` | |

`SONAR_TOKEN` comes only from the environment; the script exits if it is not set and refuses `sonar.token`, `sonar.login` or `sonar.password` in the file. It passes the token through the scanner's `SONARQUBE_SCANNER_PARAMS` environment variable (read by both the begin and end steps), never as a `/d:sonar.token=` argument, so `ps` and shell history never show it.

For a self-hosted SonarQube, set `selfHosted.enabled` to `true` and `selfHosted.hostUrl` (or `SONAR_HOST_URL`); no organization is needed.

**For GSS development, you only need to set:**

```powershell
$env:SONAR_TOKEN = "your-sonarcloud-token-here"
```

The script will automatically use the project key and organization from `sonar-config.json`.

## Understanding Results

After analysis completes, SonarQube displays:

### Issue Types

| Type | Typical severity | Example | Action |
|------|----------|---------|--------|
| **Bug** | Critical | Null pointer dereference | Fix immediately |
| **Vulnerability** | Critical | SQL injection risk | Fix immediately |
| **Code Smell** | Low→Medium | Long method, duplicate code | Fix gradually |
| **Security Hotspot** | High | Cryptography, authentication | Manual review |

### Quality Metrics

| Metric | What It Means |
|--------|---------------|
| **Reliability** | Bug density |
| **Security** | Vulnerability density |
| **Maintainability** | Technical debt / code smells |
| **Coverage** | Test coverage % (not uploaded by the script) |
| **Duplications** | Duplicate lines % |

### Quality Gates

SonarCloud applies the organization's quality gate (pass/fail rules on new code). Nothing in this repository configures one, and the script does not fail on a failed gate.

---

## Integration with CI/CD

This repository's `Jenkinsfile` only calls the shared `useDotNetPipeline` and does not run `scripts/sonarqube-scan.ps1`. To add one in a pipeline you own, the steps match the script:

### Jenkins Pipeline Example

```groovy
pipeline {
    agent any

    stages {
        stage('SonarQube SAST') {
            steps {
                withCredentials([string(credentialsId: 'sonar-token', variable: 'SONAR_TOKEN')]) {
                    sh '''
                        # Token via the environment, not a /d: argument that ps would show.
                        export SONARQUBE_SCANNER_PARAMS="{\"sonar.token\":\"${SONAR_TOKEN}\"}"

                        dotnet-sonarscanner begin \
                            /k:"your-project-key" \
                            /o:"your-organization" \
                            /d:sonar.host.url="https://sonarcloud.io"

                        dotnet build --configuration Release

                        dotnet-sonarscanner end
                    '''
                }
            }
        }
    }
}
```

---

## Troubleshooting

### Issue: "SONAR_TOKEN environment variable not set"

**Cause:** `SONAR_TOKEN` is not set (the script never prompts for it)

**Fix:**

```powershell
# Set token
$env:SONAR_TOKEN = "your-token"

# Verify it's set
echo $env:SONAR_TOKEN

# Run again
pwsh scripts/sonarqube-scan.ps1
```

### Issue: "SonarScanner initialization failed"

**Cause:** Wrong project or organization key, or an invalid token

**Fix:** check `projectKey` and `sonarCloud.organizationKey` in `sonar-config.json` (or `SONAR_PROJECT_KEY` / `SONAR_ORG_KEY`) against <https://sonarcloud.io/organizations>, and that the project exists in that organization.

### Issue: "Build failed during analysis"

**Cause:** The solution doesn't compile (warnings are errors, `TreatWarningsAsErrors`), or restore failed (the private NuGet feed needs a `read:packages` token)

**Fix:**

```bash
# Test build locally first
dotnet build --configuration Release

# If build fails, fix it before running analysis
```

### Issue: "Analysis takes very long"

**Cause:** Slow network to SonarCloud, or a busy SonarCloud queue

**Solutions:**

- Check network connectivity to sonarcloud.io
- Check the project's background tasks in SonarCloud

### Issue: "Token expired or invalid"

**Cause:** SonarCloud token has expired

**Fix:**

```text
1. Visit https://sonarcloud.io/account/security
2. Generate new token
3. Update $env:SONAR_TOKEN
4. Run analysis again
```

---

## Related Documentation

### Related Docs in This Repository

- **[Security Scanning Guide](../standards/security-scanning.md)** - Local vulnerability scanning (dependency checks, credentials)
- **[Scripts README](../../scripts/README.md)** - All repository scripts

### Microsoft Learn References

- **[SonarScanner for .NET](https://docs.sonarqube.org/latest/analysis/scan/sonarscanner-for-msbuild/)** - Official SonarScanner documentation for .NET
- **[Code Analysis in .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)** - Microsoft's built-in .NET code analysis
- **[.NET Security Best Practices](https://learn.microsoft.com/en-us/dotnet/fundamentals/security/)** - Microsoft security guidelines

### Industry & Standards References

- **[SonarCloud](https://sonarcloud.io)** - Cloud-hosted SonarQube for open source and private projects
- **[CWE Top 25](https://cwe.mitre.org/top25/)** - Top security weaknesses SonarQube detects
- **[OWASP Top 10](https://owasp.org/www-project-top-ten/)** - Web application security risks

---

param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = "Stop"

$serversRoot = Join-Path $RepoRoot "Servers/MCPhappey.Servers.JSON/Servers"
$readmePath = Join-Path $RepoRoot "README.md"

if (-not (Test-Path $serversRoot)) {
    throw "Servers folder not found: $serversRoot"
}

$serverFiles = Get-ChildItem -Path $serversRoot -Recurse -Filter "Server.json"

$totalServerCount = 0
$byDomain = @{}

foreach ($file in $serverFiles) {
    try {
        $json = Get-Content -Path $file.FullName -Raw | ConvertFrom-Json
    }
    catch {
        continue
    }

    if (-not $json.serverInfo) {
        continue
    }

    $totalServerCount++

    $websiteUrl = [string]$json.serverInfo.websiteUrl
    $icons = $json.serverInfo.icons

    if ([string]::IsNullOrWhiteSpace($websiteUrl) -or -not $icons) {
        continue
    }

    $iconSrc = $null

    if ($icons -is [System.Array]) {
        $dark = $icons | Where-Object { $_.theme -eq "dark" -and -not [string]::IsNullOrWhiteSpace([string]$_.src) } | Select-Object -First 1
        $light = $icons | Where-Object { $_.theme -eq "light" -and -not [string]::IsNullOrWhiteSpace([string]$_.src) } | Select-Object -First 1
        $fallback = $icons | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.src) } | Select-Object -First 1

        if ($dark -and -not [string]::IsNullOrWhiteSpace([string]$dark.src)) {
            $iconSrc = [string]$dark.src
        }
        elseif ($light -and -not [string]::IsNullOrWhiteSpace([string]$light.src)) {
            $iconSrc = [string]$light.src
        }
        elseif ($fallback -and -not [string]::IsNullOrWhiteSpace([string]$fallback.src)) {
            $iconSrc = [string]$fallback.src
        }
    }
    else {
        $singleSrc = [string]$icons.src
        if (-not [string]::IsNullOrWhiteSpace($singleSrc)) {
            $iconSrc = $singleSrc
        }
    }

    if ([string]::IsNullOrWhiteSpace($iconSrc)) {
        continue
    }

    $domainKey = $websiteUrl.Trim().ToLowerInvariant()

    try {
        $uri = [System.Uri]$websiteUrl
        $domainKey = $uri.Host.ToLowerInvariant()
    }
    catch {
        # Keep raw URL as key when URI parsing fails
    }

    if (-not $byDomain.ContainsKey($domainKey)) {
        $title = [string]($json.serverInfo.title)
        if ([string]::IsNullOrWhiteSpace($title)) {
            $title = [string]($json.serverInfo.name)
        }

        $byDomain[$domainKey] = [pscustomobject]@{
            Title = $title
            WebsiteUrl = $websiteUrl
            IconSrc = $iconSrc
        }
    }
}

$logoEntries = $byDomain.Values | Sort-Object -Property Title
$logoCount = @($logoEntries).Count

$logoLinks = @()
foreach ($entry in $logoEntries) {
    $titleEscaped = [System.Net.WebUtility]::HtmlEncode($entry.Title)
    $urlEscaped = [System.Net.WebUtility]::HtmlEncode($entry.WebsiteUrl)
    $iconEscaped = [System.Net.WebUtility]::HtmlEncode($entry.IconSrc)
    $logoLinks += ('<a href="{0}" title="{1}" target="_blank" rel="noopener noreferrer"><img src="{2}" alt="{1}" width="28" height="28" /></a>' -f $urlEscaped, $titleEscaped, $iconEscaped)
}

$accessibilityLinks = @()
foreach ($entry in $logoEntries) {
    $titleEscaped = [System.Net.WebUtility]::HtmlEncode($entry.Title)
    $urlEscaped = [System.Net.WebUtility]::HtmlEncode($entry.WebsiteUrl)
    $accessibilityLinks += "[$titleEscaped]($urlEscaped)"
}

$logoWall = @"
<!-- PROVIDER_LOGO_GRID_START -->
<p>
$(($logoLinks -join "`n"))
</p>

<details>
<summary><strong>Accessibility fallback (alphabetical provider links)</strong></summary>

$(($accessibilityLinks -join " | "))

</details>
<!-- PROVIDER_LOGO_GRID_END -->
"@

$readme = @'
# aihappey-mcp

Open-source **MCP backend** that hosts and routes a large catalog of AI and business integrations as Model Context Protocol servers.

---

## What this project is

`aihappey-mcp` is the backend layer in the AIHappey ecosystem for MCP.
It provides a single place to expose many MCP servers, from AI providers to Microsoft 365, public datasets, and operational tools.

## What you can do with it

- Connect one MCP endpoint and discover many available servers.
- Use server-based tools for models, search, files, media, and workflows.
- Combine static JSON-defined servers with SQL-backed dynamic servers.
- Run with either header-based auth or Azure-authenticated hosting samples.
- Reuse the same backend across different clients and agent experiences.

## MCP server catalog at a glance

This repository includes **{0} MCP server definitions** from [`Servers/MCPhappey.Servers.JSON/Servers`](Servers/MCPhappey.Servers.JSON/Servers).

The logo wall below is **generated from those `Server.json` files**, deduplicated by `serverInfo.websiteUrl`.

- **Unique logo domains shown:** {1}
- **Total server definitions:** {0}
- Servers without `websiteUrl` or icon are not shown in the wall, but are included in the total count.

{2}

## Connect to the MCP backend

Default hosted endpoint:

- `https://mcp.aihappey.net`

Typical connection flow:

1. Point your MCP client to the base endpoint.
2. Discover available servers from the registry endpoint.
3. Select the servers relevant to your use case.
4. Authenticate based on your deployment profile (header auth or Azure auth).

Example registry discovery URL:

- `GET https://mcp.aihappey.net/v0.1/servers`

## Repository structure

- [`Abstractions`](Abstractions): authentication, tools, decoders, telemetry, scrapers
- [`Core`](Core): shared MCP hosting/services and core runtime logic
- [`Servers/MCPhappey.Servers.JSON`](Servers/MCPhappey.Servers.JSON): static JSON-defined MCP servers
- [`Servers/MCPhappey.Servers.SQL`](Servers/MCPhappey.Servers.SQL): SQL-backed dynamic MCP servers
- [`Samples/MCPhappey.HeaderAuth`](Samples/MCPhappey.HeaderAuth): sample host with header-based auth
- [`Samples/MCPhappey.AzureAuth`](Samples/MCPhappey.AzureAuth): sample host with Azure auth

## Run locally

Prerequisite:

- **.NET 9 SDK**

Run HeaderAuth sample:

```bash
dotnet run --project Samples/MCPhappey.HeaderAuth/MCPhappey.HeaderAuth.csproj
```

Run AzureAuth sample:

```bash
dotnet run --project Samples/MCPhappey.AzureAuth/MCPhappey.AzureAuth.csproj
```

## Maintenance

Regenerate this README (including the logo wall and counts) with:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/generate-readme.ps1
```
'@

$readme = $readme -f $totalServerCount, $logoCount, $logoWall

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($readmePath, $readme, $utf8NoBom)
Write-Host "README generated at $readmePath"
Write-Host "Total servers: $totalServerCount"
Write-Host "Unique logo domains: $logoCount"

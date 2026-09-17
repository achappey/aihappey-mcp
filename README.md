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
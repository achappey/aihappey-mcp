# MCPhappey.Servers.SQL

Implements dynamic MCP servers with configuration and storage backed by SQL Server.

## Architecture

```mermaid
flowchart TD
    Context
    Models
    Repositories
    Migrations
    Extensions
    Tools

    Context --> Models
    Context --> Repositories
    Context --> Migrations
    Context --> Extensions
    Context --> Tools
```

## Key Features
- Dynamic server configuration and management
- SQL Server-backed storage
- Entity Framework Core integration

## Dependencies
- MCPhappey.Core
- MCPhappey.Common
- MCPhappey.Auth
- Microsoft.EntityFrameworkCore

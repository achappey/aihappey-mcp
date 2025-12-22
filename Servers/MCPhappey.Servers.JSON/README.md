# MCPhappey.Servers.JSON

Implements MCP servers based on static JSON configurations.

## Architecture

```mermaid
flowchart TD
    StaticContentLoader
    Servers[Servers/ (JSON server definitions)]

    StaticContentLoader --> Servers
```

## Key Features
- Static server configuration via JSON files
- Easy extension with new server definitions
- Used for serving preconfigured MCP servers

## Dependencies
- MCPhappey.Common

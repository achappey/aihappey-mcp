# MCPhappey.Common

Shared models and utilities for MCPhappey projects, built on Model Context Protocol.

## Architecture

```mermaid
flowchart TD
    HeaderProvider
    IContentScraper
    subgraph Constants
        Hosts
        ServerMetadata
    end
    subgraph Extensions
        FileItemExtensions
        PromptExtensions
    end
    subgraph Models
        FileItem
        Prompts
        Server
    end

    HeaderProvider --> Constants
    HeaderProvider --> Extensions
    HeaderProvider --> Models
    IContentScraper --> Models
```

## Key Features
- Common data models
- Utility functions and constants
- Used across all MCPhappey packages

## Dependencies
- ModelContextProtocol
- ModelContextProtocol.AspNetCore

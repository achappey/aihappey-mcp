# MCPhappey.Core

Core library implementing dynamic MCP server logic, AI/memory integration, and shared abstractions for the MCPhappey ecosystem.

## Architecture

```mermaid
flowchart TD
    subgraph Services
        DownloadService
        PromptService
        ResourceService
        SamplingService
        TransformService
        UploadService
    end
    subgraph Extensions
        AspNetCoreExtensions
        ModelContextToolExtensions
        ModelContextServerExtensions
        ModelContextResourceExtensions
        ModelContextPromptExtensions
        ServiceExtensions
        GraphClientExtensions
        KernelMemoryExtensions
        SharePointNewsExtensions
        StaticTokenAuthProvider
        StringExtensions
    end

    Services --> Extensions
```

## Key Features
- Dynamic server hosting and configuration
- Integration with AI (Semantic Kernel, KernelMemory)
- Shared abstractions for all MCPhappey projects

## Dependencies
- MCPhappey.Auth
- MCPhappey.Common
- Microsoft.SemanticKernel
- Microsoft.KernelMemory
- ModelContextProtocol

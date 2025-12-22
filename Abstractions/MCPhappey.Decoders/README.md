# MCPhappey.Decoders

Provides decoders for various content formats (e.g., EPUB), with integration for AI/memory and Microsoft Graph.

## Architecture

```mermaid
flowchart TD
    EpubDecoder
    subgraph Extensions
        AspNetCoreExtensions
    end
    EpubDecoder --> Extensions
    EpubDecoder -->|Uses| MicrosoftGraph
    EpubDecoder -->|Uses| KernelMemory
    EpubDecoder -->|Uses| VersOneEpub
```

## Key Features
- Decoding of EPUB and other formats
- Integration with Microsoft Graph
- AI/memory content extraction

## Usage

Integrate as a library in your MCP server or Web API host. Use `EpubDecoder` and extension methods for content extraction and decoding.

## Dependencies
- VersOne.Epub
- Microsoft.Graph
- Microsoft.KernelMemory

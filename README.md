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

- **.NET 10 SDK**

Run HeaderAuth sample:

```bash
dotnet run --project Samples/MCPhappey.HeaderAuth/MCPhappey.HeaderAuth.csproj
```

Run AzureAuth sample:

```bash
dotnet run --project Samples/MCPhappey.AzureAuth/MCPhappey.AzureAuth.csproj
```

## Office formatting and safe editing

The OpenXML PowerPoint, Word and Excel servers edit documents in memory; they do
not render Office documents. The separate ClosedXML Excel server also exposes
cell formatting through its existing cell tools. Existing tool names and required
arguments remain available.

### Editing and inspection

- **PowerPoint:** `openxml_powerpoint_add_content` keeps its legacy behavior when
  `formattingMode` is omitted (`default`). Set `formattingMode: "preserve"` to
  retain body, paragraph and run properties. Use `shapeIndex`, optional
  `paragraphIndex`/`runIndex`, and paired `start`/`length` offsets to limit an edit.
  `operation: "formatOnly"` applies the typed `formatting` patch without changing
  text; `replaceContent` explicitly replaces the selection. `get_shapes` reports
  body/paragraph/run properties and run boundaries alongside the original fields.
- **Word:** `openxml_word_replace_text_with_track_changes` preserves formatting
  during tracked replacement. Optional `paragraphIndex` and paired `start`/`length`
  select a body paragraph (including table paragraphs) and text range instead of
  searching `originalText`. `operation: "formatOnly"` patches direct run formatting
  without inserting/deleting text or enabling tracked changes.
  `openxml_word_get_paragraphs` returns indexed paragraphs, run boundaries and
  direct properties. `includeSupportingXml: true` includes related document
  formatting resources.
- **Excel:** `openxml_excel_set_cell` accepts a cell or rectangular range in
  `address`, with `type: "formula"` for explicit formula replacement.
  `operation: "formatOnly"` ignores the still-required `value` and `type`
  arguments and patches only `formatting`. Font, fill, border, alignment and number
  format patches clone shared styles so unselected cells keep their formatting.
  `openxml_excel_get_cells` inspects cells and ranges, including separate values
  and formulas, cell/style references, dimensions, merges and conditional rules.
  Ranges are limited to 10,000 cells.
- **ClosedXML:** `closedxml_set_cell` adds `operation: "formatOnly"` (or `formula`
  for explicit formula updates), `endCellAddress` for a rectangular range, and a
  typed `formatting` patch. `closedxml_get_cell` accepts the same range endpoint
  and adds values, formulas, formatting, dimensions and rule summaries while
  retaining the original `callValuye` response field.

Only supplied formatting properties are changed; null leaves existing direct or
inherited properties alone, and `false` explicitly disables a boolean property.
Text offsets use zero-based UTF-16 positions, not rendered glyph positions.
Text range editing is paragraph-local; unsupported complex fields/revisions or
invalid boundaries are rejected rather than flattened.

PowerPoint preserved replacement distributes characters over the selected
original run lengths, with overflow inheriting the last selected run's properties.
It maps lines to existing paragraphs; additional paragraphs inherit the final
paragraph/run formatting. A zero-length PowerPoint insertion at a run boundary
inherits the preceding run's formatting. Word's inserted text inherits the first selected run's
properties; tracked deletions retain each original run's properties, and paragraph
and named-style references remain intact. Excel retains cell styles when replacing
values/formulas; only an
explicit content replacement removes the selected cell's old formula/cached value.
Use explicit ranges for precise formatting boundaries. `copyFromTarget` and HTML
embedded in Markdown are not rich-text editing APIs.
OpenXML Excel rejects content replacements inside shared, array or data-table
formula regions rather than breaking formulas in unselected cells; formatting-only
edits remain supported there.

PowerPoint's Markdown support is limited to simple bullet-line parsing; it does
not interpret inline HTML as run formatting. Preserved plain text is literal.
Word's existing create/append import tools retain their Markdown-to-HTML/AltChunk
behavior (rendered by Word); the range editor treats replacement text literally.
Excel string values are literal, not Markdown/HTML.

For example, add these optional fields to the existing PowerPoint arguments to
color five characters in the first paragraph without replacing their text:

```json
{
  "formattingMode": "preserve",
  "operation": "formatOnly",
  "shapeIndex": 0,
  "paragraphIndex": 0,
  "start": 0,
  "length": 5,
  "formatting": { "color": "FF0000" }
}
```

For the OpenXML Excel editor, select `address: "B2:C4"` and supply:

```json
{
  "operation": "formatOnly",
  "formatting": {
    "font": { "bold": true },
    "fill": { "color": "FFFF00" },
    "numberFormat": "0.00"
  }
}
```

Formatting inspection includes direct OpenXML properties and style/theme
references rather than claiming to reproduce Office's rendered appearance.
Absent direct properties mean inherited formatting, not necessarily `false` or a
default font. Conditional-formatting rules are reported, not evaluated; Excel
formula results are cached values, not recalculated by the OpenXML tools.

Existing-document writes resolve the OneDrive/SharePoint drive item and capture
its ETag **before** downloading content. Uploads address that same item and send
`If-Match` with the captured ETag. Missing identity/version metadata fails closed.
Graph lock (423) and concurrent-edit (412) failures are returned without an
unconditional retry: inspect/download the latest document and reapply the intended
edit. New-file creation continues to use the existing upload path. Authentication
and other MCP servers' upload behavior are unchanged.

The source changes must be deployed to the relevant MCP host; they do not update
an independently deployed endpoint such as `mcp.fakton.com`. Live lock/coauthoring
behavior and permission enforcement remain Microsoft Graph/SharePoint service
responsibilities.

## Maintenance

Regenerate this README (including the logo wall and counts) with:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/generate-readme.ps1
```
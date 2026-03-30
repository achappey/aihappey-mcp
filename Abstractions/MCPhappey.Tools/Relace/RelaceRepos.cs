using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.DumplingAI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Relace;

public static class RelaceRepos
{
    private const string ProviderName = "relace";
    [Description("Create a Relace repository from a single fileUrl, a Git repository, or an existing Relace repo template. Uses elicitation before execution and returns structured result data.")]
    [McpServerTool(
        Title = "Relace create repo",
        Name = "relace_repos_create",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Relace_Repos_Create(
        [Description("Optional repo display name stored inside metadata.name.")] string? name,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Create source type: files, git, or relace. Default: files.")] string sourceType = "files",
        [Description("Optional repo description stored inside metadata.description.")] string? description = null,
        [Description("Source file URL for sourceType=files. Uses the default SharePoint/OneDrive/HTTP download flow.")] string? fileUrl = null,
        [Description("Optional target filename/path when sourceType=files. Defaults to the source file name.")] string? filename = null,
        [Description("Git repository URL for sourceType=git.")] string? gitUrl = null,
        [Description("Optional Git branch for sourceType=git.")] string? branch = null,
        [Description("Optional shallow clone flag for sourceType=git. Default: true.")] bool? shallow = null,
        [Description("Template Relace repo id for sourceType=relace.")] string? templateRepoId = null,
        [Description("Optional flag to keep template metadata when sourceType=relace.")] bool? copyMetadata = null,
        [Description("Optional flag to keep original Git remote when sourceType=relace.")] bool? copyRemote = null,
        [Description("Optional extra metadata JSON object. Primitive-only MCP input: pass JSON as string.")] string? metadataJson = null,
        [Description("Enable automatic indexing after create.")] bool autoIndex = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<RelaceToolResult>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new RelaceCreateRepoInput
                    {
                        Name = name,
                        Description = description,
                        SourceType = sourceType,
                        FileUrl = fileUrl,
                        Filename = filename,
                        GitUrl = gitUrl,
                        Branch = branch,
                        Shallow = shallow,
                        TemplateRepoId = templateRepoId,
                        CopyMetadata = copyMetadata,
                        CopyRemote = copyRemote,
                        MetadataJson = metadataJson,
                        AutoIndex = autoIndex
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);

                var payload = await BuildCreateRepoPayloadAsync(typed, serviceProvider, requestContext, cancellationToken);
                var client = serviceProvider.GetRequiredService<RelaceClient>();
                var response = await client.SendJsonAsync(HttpMethod.Post, "v1/repo", payload, cancellationToken);

                return CreateToolResult(
                    method: "POST",
                    endpoint: "/v1/repo",
                    request: payload,
                    response: response,
                    resourceType: "repo",
                    resourceId: response?["repo_id"]?.GetValue<string>());
            }));

    [Description("Update a Relace repo using a single Git sync or a single diff operation. Complex bulk updates are intentionally reduced to one operation per tool call. Uses elicitation before execution and returns structured result data.")]
    [McpServerTool(
        Title = "Relace update repo",
        Name = "relace_repos_update",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Relace_Repos_Update(
        [Description("Repo id to update.")] string repoId,
        [Description("Update mode: diff, git, or metadata.")] string updateMode,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional single diff operation type for updateMode=diff: write, rename, or delete.")] string? operationType = null,
        [Description("Filename/path for write, rename old filename, or delete operations.")] string? filename = null,
        [Description("New filename/path for rename operations.")] string? newFilename = null,
        [Description("Optional raw text content for diff write operations.")] string? content = null,
        [Description("Optional source file URL for diff write operations. Uses the default SharePoint/OneDrive/HTTP download flow.")] string? fileUrl = null,
        [Description("Git repository URL for updateMode=git.")] string? gitUrl = null,
        [Description("Optional Git branch for updateMode=git.")] string? branch = null,
        [Description("Optional metadata.name value.")] string? name = null,
        [Description("Optional metadata.description value.")] string? description = null,
        [Description("Optional metadata JSON object string. Primitive-only MCP input.")] string? metadataJson = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<RelaceToolResult>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new RelaceUpdateRepoInput
                    {
                        RepoId = repoId,
                        UpdateMode = updateMode,
                        OperationType = operationType,
                        Filename = filename,
                        NewFilename = newFilename,
                        Content = content,
                        FileUrl = fileUrl,
                        GitUrl = gitUrl,
                        Branch = branch,
                        Name = name,
                        Description = description,
                        MetadataJson = metadataJson
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                ValidateRequired(typed.RepoId, nameof(repoId));

                var payload = await BuildUpdateRepoPayloadAsync(typed, serviceProvider, requestContext, cancellationToken);
                var client = serviceProvider.GetRequiredService<RelaceClient>();
                var response = await client.SendJsonAsync(HttpMethod.Post, $"v1/repo/{EscapePath(typed.RepoId!)}/update", payload, cancellationToken);

                return CreateToolResult(
                    method: "POST",
                    endpoint: "/v1/repo/{repo_id}/update",
                    request: new JsonObject
                    {
                        ["repo_id"] = typed.RepoId,
                        ["body"] = payload
                    },
                    response: response,
                    resourceType: "repo",
                    resourceId: response?["repo_id"]?.GetValue<string>() ?? typed.RepoId);
            }));

    [Description("Upload or replace a single file in a Relace repo from fileUrl input. Uses elicitation before execution and returns structured result data.")]
    [McpServerTool(
        Title = "Relace upload repo file",
        Name = "relace_repos_upload_file",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Relace_Repos_UploadFile(
        [Description("Repo id to update.")] string repoId,
        [Description("Source file URL to upload into the repo. Uses the default SharePoint/OneDrive/HTTP download flow.")] string fileUrl,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional path inside the repo. Defaults to the source file name.")] string? filePath = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<RelaceToolResult>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new RelaceUploadFileInput
                    {
                        RepoId = repoId,
                        FileUrl = fileUrl,
                        FilePath = filePath
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                ValidateRequired(typed.RepoId, nameof(repoId));
                ValidateRequired(typed.FileUrl, nameof(fileUrl));

                var sourceFile = await DownloadSingleFileAsync(serviceProvider, requestContext, typed.FileUrl!, cancellationToken);
                var targetPath = ResolveFilePath(sourceFile, typed.FilePath);

                var client = serviceProvider.GetRequiredService<RelaceClient>();
                var response = await client.SendBinaryAsync(
                    HttpMethod.Put,
                    $"v1/repo/{EscapePath(typed.RepoId!)}/file/{EscapePath(targetPath)}",
                    sourceFile.Contents.ToArray(),
                    sourceFile.MimeType,
                    cancellationToken);

                return CreateToolResult(
                    method: "PUT",
                    endpoint: "/v1/repo/{repo_id}/file/{file_path}",
                    request: new JsonObject
                    {
                        ["repo_id"] = typed.RepoId,
                        ["fileUrl"] = typed.FileUrl,
                        ["file_path"] = targetPath,
                        ["source_filename"] = sourceFile.Filename,
                        ["source_mime_type"] = sourceFile.MimeType
                    },
                    response: response,
                    resourceType: "repo-file",
                    resourceId: targetPath);
            }));

    [Description("Delete a Relace repo after explicit confirmation of the repo id. Returns structured result data.")]
    [McpServerTool(
        Title = "Relace delete repo",
        Name = "relace_repos_delete",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = true)]
    public static async Task<CallToolResult?> Relace_Repos_Delete(
        [Description("Repo id to delete.")] string repoId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<RelaceToolResult>(async () =>
            {
                ValidateRequired(repoId, nameof(repoId));
                await ConfirmExactValueAsync(requestContext, repoId, cancellationToken);

                var client = serviceProvider.GetRequiredService<RelaceClient>();
                await client.SendNoContentAsync(HttpMethod.Delete, $"v1/repo/{EscapePath(repoId)}", cancellationToken);

                return CreateToolResult(
                    method: "DELETE",
                    endpoint: "/v1/repo/{repo_id}",
                    request: new JsonObject { ["repo_id"] = repoId },
                    response: new JsonObject
                    {
                        ["deleted"] = true,
                        ["repo_id"] = repoId
                    },
                    resourceType: "repo",
                    resourceId: repoId);
            }));

    [Description("Run Relace semantic retrieve over a repo. This is a read-only POST workflow and returns structured retrieval results.")]
    [McpServerTool(
        Title = "Relace retrieve over repo",
        Name = "relace_repos_retrieve",
        ReadOnly = true,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Relace_Repos_Retrieve(
        [Description("Repo id to retrieve over.")] string repoId,
        [Description("Natural language retrieval query.")] string query,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional branch name.")] string? branch = null,
        [Description("Optional specific commit hash.")] string? hash = null,
        [Description("Optional minimum relevance score between 0.0 and 1.0. Default: 0.3.")] double? scoreThreshold = 0.3,
        [Description("Optional token limit. Default: 30000.")] int? tokenLimit = 30000,
        [Description("Include matching file content in results.")] bool includeContent = false,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<RelaceToolResult>(async () =>
            {
                ValidateRequired(repoId, nameof(repoId));
                ValidateRequired(query, nameof(query));

                var payload = new JsonObject
                {
                    ["query"] = query.Trim(),
                    ["branch"] = NullIfWhiteSpace(branch),
                    ["hash"] = NullIfWhiteSpace(hash),
                    ["score_threshold"] = scoreThreshold,
                    ["token_limit"] = tokenLimit,
                    ["include_content"] = includeContent
                }.WithoutNulls();

                var client = serviceProvider.GetRequiredService<RelaceClient>();
                var response = await client.SendJsonAsync(HttpMethod.Post, $"v1/repo/{EscapePath(repoId)}/retrieve", payload, cancellationToken);

                return CreateToolResult(
                    method: "POST",
                    endpoint: "/v1/repo/{repo_id}/retrieve",
                    request: new JsonObject
                    {
                        ["repo_id"] = repoId,
                        ["body"] = payload
                    },
                    response: response,
                    resourceType: "retrieve-result",
                    resourceId: repoId);
            }));

    [Description("Create a Relace repo token scoped to one or more repo ids. Uses elicitation before execution and returns structured result data.")]
    [McpServerTool(
        Title = "Relace create repo token",
        Name = "relace_repo_tokens_create",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = false)]
    public static async Task<CallToolResult?> Relace_RepoTokens_Create(
        [Description("Repo token display name.")] string name,
        [Description("Comma separated repo ids the token should access.")] string repoIdsCsv,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        [Description("Optional token lifetime in seconds.")] int? ttlSeconds = null,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<RelaceToolResult>(async () =>
            {
                var (typed, _, _) = await requestContext.Server.TryElicit(
                    new RelaceCreateRepoTokenInput
                    {
                        Name = name,
                        RepoIdsCsv = repoIdsCsv,
                        TtlSeconds = ttlSeconds
                    },
                    cancellationToken);

                ArgumentNullException.ThrowIfNull(typed);
                ValidateRequired(typed.Name, nameof(name));
                ValidateRequired(typed.RepoIdsCsv, nameof(repoIdsCsv));

                var repoIds = SplitCsv(typed.RepoIdsCsv);
                if (repoIds.Count == 0)
                    throw new ValidationException("At least one repo id is required.");

                var payload = new JsonObject
                {
                    ["name"] = typed.Name.Trim(),
                    ["repo_ids"] = new JsonArray(repoIds.Select(id => (JsonNode?)id).ToArray()),
                    ["ttl_seconds"] = typed.TtlSeconds
                }.WithoutNulls();

                var client = serviceProvider.GetRequiredService<RelaceClient>();
                var response = await client.SendJsonAsync(HttpMethod.Post, "v1/repo_token", payload, cancellationToken);

                return CreateToolResult(
                    method: "POST",
                    endpoint: "/v1/repo_token",
                    request: payload,
                    response: response,
                    resourceType: "repo-token",
                    resourceId: response?["token"]?.GetValue<string>());
            }));

    [Description("Delete a Relace repo token after explicit confirmation of the token value. Returns structured result data.")]
    [McpServerTool(
        Title = "Relace delete repo token",
        Name = "relace_repo_tokens_delete",
        ReadOnly = false,
        OpenWorld = true,
        Destructive = true)]
    public static async Task<CallToolResult?> Relace_RepoTokens_Delete(
        [Description("Repo token to delete.")] string token,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
            await requestContext.WithStructuredContent<RelaceToolResult>(async () =>
            {
                ValidateRequired(token, nameof(token));
                await ConfirmExactValueAsync(requestContext, token, cancellationToken);

                var client = serviceProvider.GetRequiredService<RelaceClient>();
                await client.SendNoContentAsync(HttpMethod.Delete, $"v1/repo_token/{EscapePath(token)}", cancellationToken);

                return CreateToolResult(
                    method: "DELETE",
                    endpoint: "/v1/repo_token/{token}",
                    request: new JsonObject { ["token"] = token },
                    response: new JsonObject
                    {
                        ["deleted"] = true,
                        ["token"] = token
                    },
                    resourceType: "repo-token",
                    resourceId: token);
            }));

    private static async Task<JsonObject> BuildCreateRepoPayloadAsync(
        RelaceCreateRepoInput input,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var sourceType = NormalizeRequired(input.SourceType, nameof(input.SourceType));
        JsonObject? source = sourceType switch
        {
            "files" => await BuildCreateFilesSourceAsync(input.FileUrl, input.Filename, serviceProvider, requestContext, cancellationToken),
            "git" => BuildCreateGitSource(input.GitUrl, input.Branch, input.Shallow),
            "relace" => BuildCreateRelaceSource(input.TemplateRepoId, input.CopyMetadata, input.CopyRemote),
            _ => throw new ValidationException("sourceType must be one of: files, git, relace.")
        };

        var metadata = BuildMetadata(input.Name, input.Description, input.MetadataJson);

        return new JsonObject
        {
            ["source"] = source,
            ["metadata"] = metadata,
            ["auto_index"] = input.AutoIndex
        }.WithoutNulls();
    }

    private static async Task<JsonObject> BuildUpdateRepoPayloadAsync(
        RelaceUpdateRepoInput input,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var updateMode = NormalizeRequired(input.UpdateMode, nameof(input.UpdateMode));
        JsonObject? source = updateMode switch
        {
            "diff" => await BuildSingleDiffSourceAsync(input, serviceProvider, requestContext, cancellationToken),
            "git" => BuildUpdateGitSource(input.GitUrl, input.Branch),
            "metadata" => null,
            _ => throw new ValidationException("updateMode must be one of: diff, git, metadata.")
        };

        var metadata = BuildMetadata(input.Name, input.Description, input.MetadataJson);
        if (source is null && metadata is null)
            throw new ValidationException("At least one update is required: metadata or a single diff/git source update.");

        return new JsonObject
        {
            ["source"] = source,
            ["metadata"] = metadata
        }.WithoutNulls();
    }

    private static async Task<JsonObject> BuildCreateFilesSourceAsync(
        string? fileUrl,
        string? filename,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        ValidateRequired(fileUrl, nameof(fileUrl));
        var sourceFile = await DownloadSingleFileAsync(serviceProvider, requestContext, fileUrl!, cancellationToken);
        var repoFilename = ResolveFilePath(sourceFile, filename);
        var content = ExtractTextContent(sourceFile, nameof(fileUrl));

        return new JsonObject
        {
            ["type"] = "files",
            ["files"] = new JsonArray
            {
                new JsonObject
                {
                    ["filename"] = repoFilename,
                    ["content"] = content
                }
            }
        };
    }

    private static JsonObject BuildCreateGitSource(string? gitUrl, string? branch, bool? shallow)
    {
        ValidateRequired(gitUrl, nameof(gitUrl));
        return new JsonObject
        {
            ["type"] = "git",
            ["url"] = gitUrl!.Trim(),
            ["branch"] = NullIfWhiteSpace(branch),
            ["shallow"] = shallow ?? true
        }.WithoutNulls();
    }

    private static JsonObject BuildCreateRelaceSource(string? templateRepoId, bool? copyMetadata, bool? copyRemote)
    {
        ValidateRequired(templateRepoId, nameof(templateRepoId));
        return new JsonObject
        {
            ["type"] = "relace",
            ["repo_id"] = templateRepoId!.Trim(),
            ["copy_metadata"] = copyMetadata,
            ["copy_remote"] = copyRemote
        }.WithoutNulls();
    }

    private static JsonObject BuildUpdateGitSource(string? gitUrl, string? branch)
    {
        ValidateRequired(gitUrl, nameof(gitUrl));
        return new JsonObject
        {
            ["type"] = "git",
            ["url"] = gitUrl!.Trim(),
            ["branch"] = NullIfWhiteSpace(branch)
        }.WithoutNulls();
    }

    private static async Task<JsonObject> BuildSingleDiffSourceAsync(
        RelaceUpdateRepoInput input,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var operationType = NormalizeRequired(input.OperationType, nameof(input.OperationType));
        JsonObject operation = operationType switch
        {
            "write" => await BuildWriteOperationAsync(input, serviceProvider, requestContext, cancellationToken),
            "rename" => BuildRenameOperation(input.Filename, input.NewFilename),
            "delete" => BuildDeleteOperation(input.Filename),
            _ => throw new ValidationException("operationType must be one of: write, rename, delete.")
        };

        return new JsonObject
        {
            ["type"] = "diff",
            ["operations"] = new JsonArray { operation }
        };
    }

    private static async Task<JsonObject> BuildWriteOperationAsync(
        RelaceUpdateRepoInput input,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        ValidateRequired(input.Filename, nameof(input.Filename));

        var hasContent = !string.IsNullOrWhiteSpace(input.Content);
        var hasFileUrl = !string.IsNullOrWhiteSpace(input.FileUrl);
        if (hasContent == hasFileUrl)
            throw new ValidationException("Provide exactly one of content or fileUrl for a diff write operation.");

        string resolvedContent;
        if (hasFileUrl)
        {
            var file = await DownloadSingleFileAsync(serviceProvider, requestContext, input.FileUrl!, cancellationToken);
            resolvedContent = ExtractTextContent(file, nameof(input.FileUrl));
        }
        else
        {
            resolvedContent = input.Content!.Trim();
        }

        return new JsonObject
        {
            ["type"] = "write",
            ["filename"] = input.Filename!.Trim(),
            ["content"] = resolvedContent
        };
    }

    private static JsonObject BuildRenameOperation(string? filename, string? newFilename)
    {
        ValidateRequired(filename, nameof(filename));
        ValidateRequired(newFilename, nameof(newFilename));

        return new JsonObject
        {
            ["type"] = "rename",
            ["old_filename"] = filename!.Trim(),
            ["new_filename"] = newFilename!.Trim()
        };
    }

    private static JsonObject BuildDeleteOperation(string? filename)
    {
        ValidateRequired(filename, nameof(filename));
        return new JsonObject
        {
            ["type"] = "delete",
            ["filename"] = filename!.Trim()
        };
    }

    private static JsonObject? BuildMetadata(string? name, string? description, string? metadataJson)
    {
        var metadata = ParseJsonObject(metadataJson, nameof(metadataJson)) ?? new JsonObject();

        if (!string.IsNullOrWhiteSpace(name))
            metadata["name"] = name.Trim();

        if (!string.IsNullOrWhiteSpace(description))
            metadata["description"] = description.Trim();

        return metadata.Count == 0 ? null : metadata;
    }

    private static async Task<FileItem> DownloadSingleFileAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        string fileUrl,
        CancellationToken cancellationToken)
    {
        var downloadService = serviceProvider.GetRequiredService<DownloadService>();
        var files = await downloadService.DownloadContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
        return files.FirstOrDefault()
            ?? throw new InvalidOperationException("No downloadable file content found for fileUrl.");
    }

    private static string ExtractTextContent(FileItem file, string parameterName)
    {
        var value = file.Contents.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{parameterName} must resolve to a readable text file.");

        return value;
    }

    private static string ResolveFilePath(FileItem file, string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath.Trim();

        if (!string.IsNullOrWhiteSpace(file.Filename))
            return file.Filename.Trim();

        if (Uri.TryCreate(file.Uri, UriKind.Absolute, out var absolute))
        {
            var filename = absolute.Segments.LastOrDefault();
            if (!string.IsNullOrWhiteSpace(filename))
                return Uri.UnescapeDataString(filename.Trim('/'));
        }

        throw new ValidationException("filePath is required when the source file does not expose a filename.");
    }

    private static async Task ConfirmExactValueAsync(
        RequestContext<CallToolRequestParams> requestContext,
        string expectedValue,
        CancellationToken cancellationToken)
    {
        var result = await requestContext.Server.GetElicitResponse<RelaceDeleteConfirmation>(expectedValue, cancellationToken);
        if (result?.Action != "accept")
            throw new ValidationException($"Deletion confirmation was not accepted for '{expectedValue}'.");

        var typed = result.GetTypedResult<RelaceDeleteConfirmation>()
            ?? throw new ValidationException("Deletion confirmation could not be parsed.");

        if (!string.Equals(typed.Name?.Trim(), expectedValue.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"Confirmation does not match '{expectedValue}'.");
    }

    private static RelaceToolResult CreateToolResult(
        string method,
        string endpoint,
        object? request,
        JsonNode? response,
        string? resourceType = null,
        string? resourceId = null)
        => new()
        {
            Method = method,
            Endpoint = endpoint,
            Request = request,
            Response = response,
            ResourceType = resourceType,
            ResourceId = resourceId
        };

    private static string NormalizeRequired(string? value, string parameterName)
    {
        ValidateRequired(value, parameterName);
        return value!.Trim().ToLowerInvariant();
    }

    private static JsonObject? ParseJsonObject(string? raw, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonNode.Parse(raw) as JsonObject
                ?? throw new ValidationException($"{parameterName} must contain a JSON object.");
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ValidationException($"{parameterName} must contain valid JSON. {ex.Message}");
        }
    }

    private static List<string> SplitCsv(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? []
            : raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static void ValidateRequired([System.Diagnostics.CodeAnalysis.NotNull] string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{parameterName} is required.");
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapePath(string value)
        => Uri.EscapeDataString(value);
}

public sealed class RelaceCreateRepoInput
{
    [JsonPropertyName("name")]
    [Description("Optional repo display name stored inside metadata.name.")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    [Description("Optional repo description stored inside metadata.description.")]
    public string? Description { get; set; }

    [JsonPropertyName("sourceType")]
    [Required]
    [Description("Create source type: files, git, or relace.")]
    public string SourceType { get; set; } = "files";

    [JsonPropertyName("fileUrl")]
    [Description("Source file URL for sourceType=files.")]
    public string? FileUrl { get; set; }

    [JsonPropertyName("filename")]
    [Description("Optional target filename/path for sourceType=files.")]
    public string? Filename { get; set; }

    [JsonPropertyName("gitUrl")]
    [Description("Git repository URL for sourceType=git.")]
    public string? GitUrl { get; set; }

    [JsonPropertyName("branch")]
    [Description("Optional Git branch.")]
    public string? Branch { get; set; }

    [JsonPropertyName("shallow")]
    [Description("Optional shallow clone flag.")]
    public bool? Shallow { get; set; }

    [JsonPropertyName("templateRepoId")]
    [Description("Template Relace repo id for sourceType=relace.")]
    public string? TemplateRepoId { get; set; }

    [JsonPropertyName("copyMetadata")]
    [Description("Keep template metadata when sourceType=relace.")]
    public bool? CopyMetadata { get; set; }

    [JsonPropertyName("copyRemote")]
    [Description("Keep original Git remote when sourceType=relace.")]
    public bool? CopyRemote { get; set; }

    [JsonPropertyName("metadataJson")]
    [Description("Optional extra metadata JSON object string.")]
    public string? MetadataJson { get; set; }

    [JsonPropertyName("autoIndex")]
    [Description("Enable automatic indexing after create.")]
    public bool AutoIndex { get; set; }
}

public sealed class RelaceUpdateRepoInput
{
    [JsonPropertyName("repoId")]
    [Required]
    [Description("Repo id to update.")]
    public string? RepoId { get; set; }

    [JsonPropertyName("updateMode")]
    [Required]
    [Description("Update mode: diff, git, or metadata.")]
    public string? UpdateMode { get; set; }

    [JsonPropertyName("operationType")]
    [Description("Single diff operation type for updateMode=diff: write, rename, or delete.")]
    public string? OperationType { get; set; }

    [JsonPropertyName("filename")]
    [Description("Filename/path for write, rename old filename, or delete operations.")]
    public string? Filename { get; set; }

    [JsonPropertyName("newFilename")]
    [Description("New filename/path for rename operations.")]
    public string? NewFilename { get; set; }

    [JsonPropertyName("content")]
    [Description("Raw text content for write operations.")]
    public string? Content { get; set; }

    [JsonPropertyName("fileUrl")]
    [Description("Source file URL for write operations.")]
    public string? FileUrl { get; set; }

    [JsonPropertyName("gitUrl")]
    [Description("Git repository URL for updateMode=git.")]
    public string? GitUrl { get; set; }

    [JsonPropertyName("branch")]
    [Description("Optional Git branch.")]
    public string? Branch { get; set; }

    [JsonPropertyName("name")]
    [Description("Optional metadata.name value.")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    [Description("Optional metadata.description value.")]
    public string? Description { get; set; }

    [JsonPropertyName("metadataJson")]
    [Description("Optional metadata JSON object string.")]
    public string? MetadataJson { get; set; }
}

public sealed class RelaceUploadFileInput
{
    [JsonPropertyName("repoId")]
    [Required]
    [Description("Repo id to update.")]
    public string? RepoId { get; set; }

    [JsonPropertyName("fileUrl")]
    [Required]
    [Description("Source file URL to upload into the repo.")]
    public string? FileUrl { get; set; }

    [JsonPropertyName("filePath")]
    [Description("Optional path inside the repo.")]
    public string? FilePath { get; set; }
}

public sealed class RelaceCreateRepoTokenInput
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Repo token display name.")]
    public string? Name { get; set; }

    [JsonPropertyName("repoIdsCsv")]
    [Required]
    [Description("Comma separated repo ids the token should access.")]
    public string? RepoIdsCsv { get; set; }

    [JsonPropertyName("ttlSeconds")]
    [Description("Optional token lifetime in seconds.")]
    public int? TtlSeconds { get; set; }
}

public sealed class RelaceDeleteConfirmation
{
    [JsonPropertyName("name")]
    [Required]
    [Description("Repeat the exact id or token to confirm deletion.")]
    public string? Name { get; set; }
}

internal sealed class RelaceToolResult
{
    public string Provider { get; set; } = "relace";

    public string Method { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public string? ResourceType { get; set; }

    public string? ResourceId { get; set; }

    public object? Request { get; set; }

    public JsonNode? Response { get; set; }
}

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common.Extensions;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.AI;

public static class KnowledgeGraph
{
    [Description("Extract a verified knowledge graph from a large text document using entity, relation, and verification prompts.")]
    [McpServerTool(
          Title = "Knowledge Graph extraction",
          Name = "knowledge_graph_extract",
          ReadOnly = true)]
    public static async Task<CallToolResult?> KnowledgeGraph_Extract(
          [Description("The full document url to analyze. Supports protected SharePoint and OneDrive links")] string fileUrl,
          IServiceProvider serviceProvider,
          RequestContext<CallToolRequestParams> requestContext,
          [Description("Optional model name override. Defaults to a balanced reasoning model.")] string? modelName = null,
          CancellationToken cancellationToken = default) =>
          await requestContext.WithExceptionCheck(async () =>
          await requestContext.WithStructuredContent(async () =>
      {
          var samplingService = serviceProvider.GetRequiredService<SamplingService>();
          var downloadService = serviceProvider.GetRequiredService<DownloadService>();
          var mcpServer = requestContext.Server;
          var model = modelName ?? "gpt-5-mini";

          var files = await downloadService.ScrapeContentAsync(serviceProvider, requestContext.Server, fileUrl, cancellationToken);
          var content = files.GetTextFiles()
            .Select(a => a.Contents.ToString());
          var documentContent = string.Join("\n\n", content);

          await mcpServer.SendMessageNotificationAsync("Starting typed knowledge graph extraction...", LoggingLevel.Debug, cancellationToken);

          var extractEntitiesArgs = new Dictionary<string, JsonElement>
          {
              ["documentContent"] = JsonSerializer.SerializeToElement(documentContent)
          };

          // STEP 1: Extract Entities
          var entitiesResult = await samplingService.GetPromptSample<List<Entity>>(
              serviceProvider,
              mcpServer,
              "extract-entities",
              extractEntitiesArgs,
              model,
              metadata: new Dictionary<string, object>
                        {
                            { "openai", new {
                                reasoning = new {
                                    effort = "low"
                                }
                            } },
                        },
              maxTokens: 16384,
              cancellationToken: cancellationToken);

          var extractRelationsArgs = new Dictionary<string, JsonElement>
          {
              ["documentContent"] = JsonSerializer.SerializeToElement(documentContent),
              ["entities"] = JsonSerializer.SerializeToElement(entitiesResult)
          };

          // STEP 2: Extract Relations
          var relationsResult = await samplingService.GetPromptSample<List<Relation>>(
              serviceProvider,
              mcpServer,
              "extract-relations",
              extractRelationsArgs,
              model,
              metadata: new Dictionary<string, object>
                        {
                            { "openai", new {
                                reasoning = new {
                                    effort = "medium"
                                }
                            } },
                        },
              maxTokens: 16384,
              cancellationToken: cancellationToken);

          //   var relations = relationsResult.Content ?? new();
          var verifyArg = new Dictionary<string, JsonElement>
          {
              ["relations"] = JsonSerializer.SerializeToElement(relationsResult),
              ["entities"] = JsonSerializer.SerializeToElement(entitiesResult)
          };
          // STEP 3: Verify Entities & Relations
          var verifyResult = await samplingService.GetPromptSample<VerifiedGraph>(
              serviceProvider,
              mcpServer,
              "verify-graph-data",
              verifyArg,
              model,
              metadata: new Dictionary<string, object>
                        {
                            { "openai", new {
                                reasoning = new {
                                    effort = "low"
                                }
                            } },
                        },
              maxTokens: 16384,
              cancellationToken: cancellationToken);

          var graphResultArgs = new Dictionary<string, JsonElement>
          {
              ["verifiedEntities"] = JsonSerializer.SerializeToElement(verifyResult?.Entities),
              ["verifiedRelations"] = JsonSerializer.SerializeToElement(verifyResult?.Relations)
          };
          
          // STEP 4: Compose Final Graph
          var graphResult = await samplingService.GetPromptSample<ComposedGraph>(
              serviceProvider,
              mcpServer,
              "compose-final-graph",
              graphResultArgs,
              model,
              metadata: new Dictionary<string, object>
                        {
                            { "openai", new {
                                reasoning = new {
                                    effort = "low"
                                }
                            } },
                        },
              maxTokens: 16384,
              cancellationToken: cancellationToken);

          await mcpServer.SendMessageNotificationAsync("Knowledge graph extraction completed (typed).", LoggingLevel.Info, cancellationToken);

          return graphResult;
      }));
}


// ------------------------------------------------------------
// MODELS
// ------------------------------------------------------------
public record Entity(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string? Description = null
);

public record Relation(
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("predicate")] string Predicate,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("confidenceScore")] double? ConfidenceScore = null,
    [property: JsonPropertyName("rejected")] bool? Rejected = null
);

public record VerifiedGraph(
    [property: JsonPropertyName("entities")] List<Entity> Entities,
    [property: JsonPropertyName("relations")] List<Relation> Relations
);

public record ComposedGraph(
    [property: JsonPropertyName("nodes")] List<GraphNode> Nodes,
    [property: JsonPropertyName("edges")] List<GraphEdge> Edges
);

public record GraphNode(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("description")] string? Description = null
);

public record GraphEdge(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("relation")] string Relation
);
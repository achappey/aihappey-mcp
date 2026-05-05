using System.ComponentModel;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.Brave;

public static class BraveAnswersAPI
{

    [Description("AI-generated answers backed by real-time web search and verifiable sources.")]
    [McpServerTool(
       Title = "Brave Sampling",
       Name = "brave_sampling",
       OpenWorld = false,
       ReadOnly = true)]
    public static async Task<CallToolResult?> Brave_Sampling(
       RequestContext<CallToolRequestParams> requestContext,
       [Description("The prompt to use for sampling")] string prompt,
       [Description("AI model to use (brave or brave-pro)")] string model = "brave",
       [Description("Search context size (low, medium or high).")] string contextSize = "medium",
       CancellationToken cancellationToken = default)
       => await requestContext.WithExceptionCheck(async () =>
       await requestContext.WithStructuredContent(async () =>
       {
           var options = new JsonObject
           {
               ["web_search_options"] = new JsonObject()
               {
                   ["search_context_size"] = contextSize
               }
           };

           var response = await requestContext.Server.SampleAsync(
           new CreateMessageRequestParams()
           {
               Metadata = new JsonObject
               {
                   ["brave"] = options
               },
               MaxTokens = 4096 * 4,
               ModelPreferences = model.ToModelPreferences(),
               Messages = [
                    prompt.ToUserSamplingMessage()
               ]
           },
           cancellationToken);

           return response;
       }));

    [Description("AI-generated research answers backed by real-time web search and verifiable sources.")]
    [McpServerTool(
           Title = "Brave Research",
           Name = "brave_research",
           OpenWorld = false,
           ReadOnly = true)]
    public static async Task<CallToolResult?> Brave_Research(
           RequestContext<CallToolRequestParams> requestContext,
           [Description("The prompt to use for research")] string prompt,
           [Description("AI model to use (brave or brave-pro)")] string model = "brave",
           [Description("Search context size (low, medium or high).")] string contextSize = "medium",
           CancellationToken cancellationToken = default)
           => await requestContext.WithExceptionCheck(async () =>
           await requestContext.WithStructuredContent(async () =>
           {
               var options = new JsonObject
               {
                   ["web_search_options"] = new JsonObject()
                   {
                       ["search_context_size"] = contextSize
                   },
                   ["enable_research"] = true
               };

               var response = await requestContext.Server.SampleAsync(
               new CreateMessageRequestParams()
               {
                   Metadata = new JsonObject
                   {
                       ["brave"] = options
                   },
                   MaxTokens = 4096 * 4,
                   ModelPreferences = model.ToModelPreferences(),
                   Messages = [
                        prompt.ToUserSamplingMessage()
                   ]
               },
               cancellationToken);

               return response;
           }));

}
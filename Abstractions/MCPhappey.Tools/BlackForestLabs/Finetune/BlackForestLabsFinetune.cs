using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPhappey.Common;
using MCPhappey.Common.Extensions;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.KernelMemory.Pipeline;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.BlackForestLabs.Finetune;

public static class BlackForestLabsFinetune
{
    private const string BaseUrl = "https://api.us1.bfl.ai";

    [Description("Please confirm deletion of the finetune ID: {0}")]
    public sealed class ConfirmDeleteFinetune : IHasName
    {
        [JsonPropertyName("name")]
        [Description("ID of the finetune to delete.")]
        public string Name { get; set; } = default!;
    }

    [Description("Delete a Black Forest Labs finetune by ID.")]
    [McpServerTool(
        Title = "BFL delete finetune",
        Name = "bfl_finetune_delete",
        ReadOnly = false,
        OpenWorld = false,
        Destructive = true)]
    public static async Task<CallToolResult?> BflFinetune_Delete(
        [Description("ID of the fine-tuned model you want to delete.")] string finetuneId,
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken = default)
        => await requestContext.WithExceptionCheck(async () =>
        await requestContext.WithStructuredContent(async () =>
        {
            ValidateRequired(finetuneId, nameof(finetuneId));

            return await requestContext.ConfirmAndDeleteAsync<ConfirmDeleteFinetune>(
                expectedName: finetuneId,
                deleteAction: async _ =>
                {
                    var http = await CreateClientAsync(serviceProvider, requestContext, cancellationToken);
                    var payload = new { finetune_id = finetuneId };
                    var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MimeTypes.Json);
                    using var response = await http.PostAsync("/v1/delete_finetune", body, cancellationToken);
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                        throw new Exception($"{response.StatusCode}: {json}");
                },
                successText: $"Finetune '{finetuneId}' deleted successfully.",
                ct: cancellationToken);
        }));

    private static async Task<HttpClient> CreateClientAsync(
        IServiceProvider serviceProvider,
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken cancellationToken)
    {
        var tokenService = serviceProvider.GetService<HeaderProvider>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var http = httpClientFactory.CreateClient();

        http.BaseAddress = new Uri(BaseUrl);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MimeTypes.Json));

        if (tokenService?.Headers?.TryGetValue("x-key", out var apiKey) == true && !string.IsNullOrWhiteSpace(apiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("x-key", apiKey);
            return http;
        }

        var serverConfig = serviceProvider.GetServerConfig(requestContext.Server);
        var configuredHeaders = serverConfig?.Server?.Headers;
        if (configuredHeaders != null && configuredHeaders.TryGetValue("x-key", out var configuredKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("x-key", configuredKey);
        }

        return http;
    }

    private static void ValidateRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{name} is required.");
    }
}

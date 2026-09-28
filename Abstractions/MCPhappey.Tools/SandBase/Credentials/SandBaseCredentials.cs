using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.SandBase.Credentials;

public static class SandBaseCredentials
{
    [Description("Update only non-secret SandBase credential selection metadata. No secret create/rotation tools are provided. Disable a credential to stop selection.")]
    [McpServerTool(Title = "Update SandBase credential metadata", Name = "sandbase_credentials_update", ReadOnly = false, Destructive = false, OpenWorld = false)]
    public static async Task<CallToolResult?> Update(string credentialId, IServiceProvider services,
        RequestContext<CallToolRequestParams> context, string? status = null, string? strategy = null,
        int? weight = null, CancellationToken cancellationToken = default)
        => await ModelContextToolExtensions.WithExceptionCheck(async () => await context.WithStructuredContent(async () =>
        {
            var body = new JsonObject();
            if (status is not null)
            {
                if (status is not ("active" or "disabled")) throw new ValidationException("status must be active or disabled.");
                body["status"] = status;
            }
            if (strategy is not null)
            {
                if (strategy != "round_robin") throw new ValidationException("strategy must be round_robin.");
                body["strategy"] = strategy;
            }
            if (weight is not null) body["weight"] = weight > 0 ? weight : throw new ValidationException("weight must be positive.");
            if (body.Count == 0) throw new ValidationException("Provide at least one credential metadata field.");
            return await SandBaseClient.SendAsync(services, HttpMethod.Patch,
                $"v1/credentials/{SandBaseClient.CredentialId(credentialId)}", body, cancellationToken);
        }));
}

using MCPhappey.Telemetry.Context;
using MCPHappey.Telemetry.Models;
using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Telemetry;

public class McpTelemetryService(MCPhappeyyTelemetryDatabaseContext db) : IMcpTelemetryService
{
    private readonly MCPhappeyyTelemetryDatabaseContext _db = db;

    private async Task<int> EnsureToolAsync(string toolName, CancellationToken ct)
    {
        // normalise for unique index
        var normalized = toolName.Trim();

        var tool = await _db.Tools
            .FirstOrDefaultAsync(t => t.ToolName == normalized, ct);

        if (tool == null)
        {
            tool = new Tool { ToolName = normalized };
            _db.Tools.Add(tool);
            await _db.SaveChangesAsync(ct);
        }

        return tool.Id;
    }

    private async Task<int> EnsureResourceAsync(string uri, CancellationToken ct)
    {
        // normalise for unique index
        var normalized = uri.Trim().ToLowerInvariant();
        normalized = normalized[..Math.Min(normalized.Length, 850)];


        var resource = await _db.Resources
            .FirstOrDefaultAsync(r => r.Uri == normalized, ct);

        if (resource == null)
        {
            resource = new Resource { Uri = normalized };
            _db.Resources.Add(resource);
            await _db.SaveChangesAsync(ct);
        }

        return resource.Id;
    }


    private async Task<(int? userId, int serverId, int clientId)>
        EnsureUserServerAndClientAsync(
            string serverUrl,
            string? userId,
            string? username,
            string clientName,
            string clientVersion,
            CancellationToken ct)
    {
        int? userPk = null;

        // USER
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(username))
        {
            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.UserId == userId, ct);

            if (user == null)
            {
                user = new User { UserId = userId!, Username = username! };
                _db.Users.Add(user);
                await _db.SaveChangesAsync(ct);
            }
            else if (!string.Equals(user.Username, username, StringComparison.Ordinal))
            {
                user.Username = username!;
                await _db.SaveChangesAsync(ct);
            }

            userPk = user.Id;
        }

        // SERVER
        //    var normalizedUrl = serverUrl.ToLowerInvariant();
        var server = await _db.Servers
            .FirstOrDefaultAsync(s => s.Name == serverUrl, ct);

        if (server == null)
        {
            server = new Server { Name = serverUrl };
            _db.Servers.Add(server);
            await _db.SaveChangesAsync(ct);
        }

        // CLIENT
        // normalise name so uniqueness index works consistently
        var normalizedClientName = clientName.Trim();
        var client = await _db.Clients
            .FirstOrDefaultAsync(c => c.ClientName == normalizedClientName, ct);

        if (client == null)
        {
            client = new Client { ClientName = normalizedClientName };
            _db.Clients.Add(client);
            await _db.SaveChangesAsync(ct);
        }

        // CLIENT VERSION
        var version = await _db.ClientVersions
            .FirstOrDefaultAsync(v => v.ClientId == client.Id && v.Version == clientVersion, ct);

        if (version == null)
        {
            version = new ClientVersion
            {
                Version = clientVersion,
                ClientId = client.Id
            };
            _db.ClientVersions.Add(version);
            await _db.SaveChangesAsync(ct);
        }

        return (userPk, server.Id, client.Id);
    }

    public async Task TrackPromptRequestAsync(
        string server,
        string sessionId,
        string clientName,
        string clientVersion,
        int outputSize,
        DateTime started,
        DateTime ended,
        string? userId,
        string? username,
        CancellationToken cancellationToken = default)
    {
        var (userPk, serverPk, clientId) = await EnsureUserServerAndClientAsync(server, userId, username, clientName, clientVersion, cancellationToken);

        _db.PromptRequests.Add(new PromptRequest
        {
            StartedAt = started,
            OutputSize = outputSize,
            ClientId = clientId,
            EndedAt = ended,
            SessionId = sessionId,
            ServerId = serverPk,
            UserId = userPk
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task TrackResourceRequestAsync(
        string server,
        string sessionId,
        string clientName,
        string clientVersion,
        string resourceUrl,
        int outputSize,
        DateTime started,
        DateTime ended,
        string? userId,
        string? username,
        CancellationToken cancellationToken = default)
    {
        var (userPk, serverPk, clientId) = await EnsureUserServerAndClientAsync(server, userId, username, clientName, clientVersion, cancellationToken);

        var resourceId = await EnsureResourceAsync(resourceUrl, cancellationToken);

        _db.ResourceRequests.Add(new ResourceRequest
        {
            StartedAt = started,
            ResourceId = resourceId,
            ClientId = clientId,
            EndedAt = ended,
            OutputSize = outputSize,
            ServerId = serverPk,
            SessionId = sessionId,
            UserId = userPk
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task TrackToolRequestAsync(
        string serverUrl,
        string sessionId,
        string clientName,
        string clientVersion,
        string toolName,
        int outputSize,
        DateTime started,
        DateTime ended,
        string? userId,
        string? username,
        CancellationToken cancellationToken = default)
    {
        var (userPk, serverPk, clientId) = await EnsureUserServerAndClientAsync(serverUrl, userId, username, clientName, clientVersion, cancellationToken);

        var toolId = await EnsureToolAsync(toolName, cancellationToken);

        _db.ToolRequests.Add(new ToolRequest
        {
            StartedAt = started,
            SessionId = sessionId,
            ClientId = clientId,
            ToolId = toolId,
            OutputSize = outputSize,
            EndedAt = ended,
            ServerId = serverPk,
            UserId = userPk
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}

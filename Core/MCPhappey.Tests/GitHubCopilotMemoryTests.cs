using System.Net;
using System.Text;
using System.Text.Json;
using MCPhappey.Core.Extensions;
using MCPhappey.Servers.JSON;
using MCPhappey.Tools.Memory.GitHubCopilot;
using Microsoft.Graph.Beta;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.SemanticKernel;

namespace MCPhappey.Tests;

public sealed class GitHubCopilotMemoryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_persists_cited_memory_and_new_instances_can_recall_it_without_writing()
    {
        using var drive = new FakeOneDrive();
        var clock = new Clock(Start);
        var entry = await Store(drive, clock).StoreAsync(" Build ", " Use dotnet ", [" docs/build:12 "], " Reusable ");

        Assert.True(Guid.TryParseExact(entry.Id, "D", out _));
        Assert.Equal("Build", entry.Subject);
        Assert.Equal("Use dotnet", entry.Fact);
        Assert.Equal(["docs/build:12"], entry.Citations);
        Assert.Equal("Reusable", entry.Reason);
        Assert.Equal(Start, entry.CreatedAt);
        Assert.Equal(Start, entry.UpdatedAt);
        Assert.Equal(Start.AddDays(28), entry.ExpiresAt);
        Assert.Single(drive.Files);
        Assert.Equal("GitHubCopilotMemory", drive.CreatedFolderName);
        var writes = drive.Writes;

        clock.Now = Start.AddDays(10);
        var recalled = Assert.Single(await Store(drive, clock).RecallAsync());
        Assert.Equal(entry.Id, recalled.Id);
        Assert.Equal(entry.ExpiresAt, recalled.ExpiresAt);
        Assert.Equal(entry.UpdatedAt, recalled.UpdatedAt);
        Assert.Equal(writes, drive.Writes);
        Assert.All(drive.Paths, p => Assert.DoesNotContain("memories", p, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Explicit_update_preserves_identity_and_creation_and_renews_for_28_days()
    {
        using var drive = new FakeOneDrive();
        var clock = new Clock(Start);
        var store = Store(drive, clock);
        var original = await Create(store);
        clock.Now = Start.AddDays(20);
        var updated = await store.StoreAsync("New subject", "Corrected fact", ["current-source:42"],
            "Checked the current source", original.Id);

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.Equal(clock.Now, updated.UpdatedAt);
        Assert.Equal(clock.Now.AddDays(28), updated.ExpiresAt);
        Assert.Equal("Corrected fact", updated.Fact);
        Assert.Equal(["current-source:42"], updated.Citations);
        Assert.Single(drive.Files);
        Assert.Equal("\"v1\"", drive.LastIfMatch);
        var persisted = Assert.Single(await Store(drive, clock).RecallAsync());
        Assert.Equal(updated.Fact, persisted.Fact);
        Assert.Equal(updated.ExpiresAt, persisted.ExpiresAt);
    }

    [Fact]
    public async Task Expiration_is_exclusive_and_recall_never_renews_or_deletes()
    {
        using var drive = new FakeOneDrive();
        var clock = new Clock(Start);
        var store = Store(drive, clock);
        var entry = await Create(store);
        var writes = drive.Writes;

        clock.Now = entry.ExpiresAt.AddTicks(-1);
        Assert.Single(await store.RecallAsync());
        clock.Now = entry.ExpiresAt;
        Assert.Empty(await store.RecallAsync());
        clock.Now = entry.ExpiresAt.AddDays(1);
        Assert.Empty(await store.RecallAsync());
        Assert.Equal(writes, drive.Writes);
        Assert.Single(drive.Files);

        // An agent can explicitly renew an expired memory after revalidating its evidence.
        var renewed = await store.StoreAsync(entry.Subject, entry.Fact, entry.Citations,
            "Revalidated against the current source", entry.Id);
        Assert.Equal(clock.Now.AddDays(28), renewed.ExpiresAt);
        Assert.Equal(entry.CreatedAt, renewed.CreatedAt);
        Assert.Single(await store.RecallAsync());
    }

    [Fact]
    public async Task Recall_reads_all_pages_before_sorting_and_applying_default_and_explicit_limits()
    {
        using var drive = new FakeOneDrive { PageSize = 7 };
        var clock = new Clock(Start);
        var store = Store(drive, clock);
        var entries = new List<GitHubCopilotMemoryEntry>();
        for (var i = 0; i < 23; i++)
        {
            clock.Now = Start.AddMinutes(i);
            entries.Add(await Create(store));
        }
        var writes = drive.Writes;
        var recalled = await store.RecallAsync();
        Assert.Equal(20, recalled.Count);
        Assert.Equal(entries.AsEnumerable().Reverse().Take(20).Select(e => e.Id), recalled.Select(e => e.Id));
        Assert.Equal(entries[^1].Id, Assert.Single(await store.RecallAsync(1)).Id);
        Assert.Equal(23, (await store.RecallAsync(100)).Count);
        Assert.True(drive.PageRequests > 3);
        Assert.Equal(writes, drive.Writes);

        clock.Now = clock.Now.AddMinutes(1);
        var first = entries[0];
        await store.StoreAsync(first.Subject, first.Fact, first.Citations, "Rechecked evidence", first.Id);
        Assert.Equal(first.Id, Assert.Single(await store.RecallAsync(1)).Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Invalid_limits_fail_before_contacting_OneDrive(int limit)
    {
        using var drive = new FakeOneDrive();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Store(drive).RecallAsync(limit));
        Assert.Empty(drive.Paths);
    }

    [Fact]
    public async Task Missing_folder_recall_and_delete_are_read_only_and_unknown_update_does_not_create()
    {
        using var drive = new FakeOneDrive();
        var store = Store(drive);
        var id = Guid.NewGuid().ToString("D");
        Assert.Empty(await store.RecallAsync());
        Assert.False(await store.DeleteAsync(id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.StoreAsync("Subject", "Fact", ["source"], "Validated", id));
        Assert.False(drive.FolderExists);
        Assert.Equal(0, drive.Writes);

        await Create(store);
        var writes = drive.Writes;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.StoreAsync("Subject", "Fact", ["source"], "Validated", id));
        Assert.Equal(writes, drive.Writes);
        Assert.Single(drive.Files);
    }

    [Fact]
    public async Task Delete_is_persistent_idempotent_and_can_delete_expired_memories()
    {
        using var drive = new FakeOneDrive();
        var clock = new Clock(Start);
        var entry = await Create(Store(drive, clock));
        clock.Now = entry.ExpiresAt;
        Assert.True(await Store(drive, clock).DeleteAsync(entry.Id));
        Assert.False(await Store(drive, clock).DeleteAsync(entry.Id));
        Assert.Empty(drive.Files);
        Assert.Empty(await Store(drive, clock).RecallAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../memories/other")]
    [InlineData("%2e%2e%2fother")]
    [InlineData("arbitrary-graph-item-id")]
    [InlineData("11111111-1111-1111-1111-111111111111/other")]
    public async Task Unsafe_ids_cannot_reach_Graph(string id)
    {
        using var drive = new FakeOneDrive();
        var store = Store(drive);
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(id));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreAsync("Subject", "Fact", ["source"], "Reason", id));
        Assert.Empty(drive.Paths);
    }

    [Fact]
    public async Task Store_requires_all_fields_and_nonempty_citation_references()
    {
        using var drive = new FakeOneDrive();
        var store = Store(drive);
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreAsync(" ", "Fact", ["source"], "Reason"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreAsync("Subject", " ", ["source"], "Reason"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreAsync("Subject", "Fact", ["source"], " "));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreAsync("Subject", "Fact", [], "Reason"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreAsync("Subject", "Fact", null!, "Reason"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StoreAsync("Subject", "Fact", ["source", " "], "Reason"));
        Assert.Empty(drive.Paths);
    }

    [Fact]
    public async Task Delegated_drives_are_isolated_for_recall_update_and_delete()
    {
        using var alice = new FakeOneDrive("alice");
        using var bob = new FakeOneDrive("bob");
        var a = Store(alice);
        var b = Store(bob);
        var entry = await Create(a);
        await Create(b); // Bob's dedicated folder exists, but does not contain Alice's memory.
        Assert.DoesNotContain(await b.RecallAsync(), m => m.Id == entry.Id);
        Assert.False(await b.DeleteAsync(entry.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => b.StoreAsync("Subject", "Fact", ["source"], "Reason", entry.Id));
        Assert.Equal(entry.Id, Assert.Single(await a.RecallAsync()).Id);
        Assert.All(alice.Paths.Where(p => p.StartsWith("/beta/drives/", StringComparison.Ordinal)),
            p => Assert.StartsWith("/beta/drives/alice/", p));
        Assert.All(bob.Paths.Where(p => p.StartsWith("/beta/drives/", StringComparison.Ordinal)),
            p => Assert.StartsWith("/beta/drives/bob/", p));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Upstream_errors_are_not_treated_as_missing_data(int status)
    {
        using var drive = new FakeOneDrive { ErrorStatus = status };
        var store = Store(drive);
        var id = Guid.NewGuid().ToString("D");
        await Assert.ThrowsAnyAsync<ApiException>(() => store.RecallAsync());
        await Assert.ThrowsAnyAsync<ApiException>(() => Create(store));
        await Assert.ThrowsAnyAsync<ApiException>(() => store.DeleteAsync(id));
        Assert.Equal(0, drive.Writes);
    }

    [Fact]
    public async Task Failed_update_does_not_renew_persisted_memory()
    {
        using var drive = new FakeOneDrive();
        var clock = new Clock(Start);
        var store = Store(drive, clock);
        var entry = await Create(store);
        clock.Now = Start.AddDays(5);
        drive.RejectWrites = true;
        await Assert.ThrowsAnyAsync<ApiException>(() => store.StoreAsync("Updated", "Fact", ["source"], "Revalidated", entry.Id));
        drive.RejectWrites = false;
        var recalled = Assert.Single(await store.RecallAsync());
        Assert.Equal(entry.ExpiresAt, recalled.ExpiresAt);
        Assert.Equal(entry.UpdatedAt, recalled.UpdatedAt);
    }

    [Fact]
    public async Task Cancellation_is_propagated_without_writing()
    {
        using var drive = new FakeOneDrive();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store(drive).RecallAsync(cancellationToken: cts.Token));
        Assert.Equal(0, drive.Writes);
    }

    [Fact]
    public async Task Malformed_memory_is_reported_and_never_silently_replaced()
    {
        using var drive = new FakeOneDrive();
        var store = Store(drive);
        var entry = await Create(store);
        drive.Files[entry.Id] = "{}";
        var writes = drive.Writes;
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RecallAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StoreAsync("Subject", "Fact", ["source"], "Reason", entry.Id));
        Assert.Equal(writes, drive.Writes);
        Assert.True(await store.DeleteAsync(entry.Id));
    }

    [Fact]
    public async Task Json_configuration_is_discovered_and_registers_exactly_the_requested_tools()
    {
        var servers = Path.Combine(AppContext.BaseDirectory, "Servers").GetServers().ToList();
        var config = Assert.Single(servers, c => c.Server.ServerInfo.Name == "GitHubCopilotMemory");
        Assert.Equal("1.0.0", config.Server.ServerInfo.Version);
        var plugin = Assert.Single(config.Server.Plugins!);
        Assert.Equal(typeof(GitHubCopilotMemory), Type.GetType(plugin));
        Assert.Contains("https://graph.microsoft.com/Files.ReadWrite", config.Server.OBO!["graph.microsoft.com"]);
        Assert.NotNull(config.Server.Capabilities.Tools);

        var tools = (await config.Server.ToToolsList(Kernel.CreateBuilder().Build(), []))!.Tools;
        Assert.Equal(["copilot_memory_delete", "copilot_memory_recall", "copilot_memory_store"],
            tools.Select(t => t.Name).Order().ToArray());
        var recall = Assert.Single(tools, t => t.Name == "copilot_memory_recall");
        Assert.True(recall.Annotations!.ReadOnlyHint);
        Assert.False(recall.Annotations.DestructiveHint);
        var store = Assert.Single(tools, t => t.Name == "copilot_memory_store");
        Assert.Equal(["subject", "fact", "citations", "reason"],
            store.InputSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.DoesNotContain("context", store.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("cancellationToken", store.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.Single(servers, c => c.Server.ServerInfo.Name == "OneDrive-Memory");
    }

    private static GitHubCopilotMemoryStore Store(FakeOneDrive drive, Clock? clock = null)
        => new(drive.Client, clock ?? new Clock(Start));

    private static Task<GitHubCopilotMemoryEntry> Create(GitHubCopilotMemoryStore store)
        => store.StoreAsync("Build", "Use dotnet", ["docs/build:12"], "Speeds up future builds");

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // Exercise the real Graph SDK and JSON persistence without credentials or network access.
    private sealed class FakeOneDrive : HttpMessageHandler
    {
        private readonly string driveId;
        private readonly HttpClient http;
        public GraphServiceClient Client { get; }
        public Dictionary<string, string> Files { get; } = [];
        public List<string> Paths { get; } = [];
        public bool FolderExists { get; private set; }
        public string? CreatedFolderName { get; private set; }
        public int Writes { get; private set; }
        public int PageSize { get; init; } = 100;
        public int PageRequests { get; private set; }
        public int? ErrorStatus { get; init; }
        public bool RejectWrites { get; set; }
        public string? LastIfMatch { get; private set; }

        public FakeOneDrive(string driveId = "user-drive")
        {
            this.driveId = driveId;
            http = new HttpClient(this, disposeHandler: false);
            Client = new GraphServiceClient(http, new AnonymousAuthenticationProvider(), "https://graph.microsoft.com/beta");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            Paths.Add(path);
            if (ErrorStatus is { } error) return Error(error);
            var method = request.Method;
            if (path == "/beta/me/drive" && method == HttpMethod.Get)
                return Json(new { id = driveId });

            var prefix = $"/beta/drives/{driveId}/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected or cross-user Graph request: {method} {path}");
            var relative = path[prefix.Length..];
            if (method == HttpMethod.Get && relative == "root") return Json(new { id = "root-id" });
            if (method == HttpMethod.Get && relative == "root:/GitHubCopilotMemory:")
                return FolderExists ? Json(new { id = "folder-id", folder = new { } }) : Error(404);

            if (method == HttpMethod.Post && relative == "items/root-id/children")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                CreatedFolderName = body.RootElement.GetProperty("name").GetString();
                Assert.Equal("GitHubCopilotMemory", CreatedFolderName);
                Assert.Equal("fail", body.RootElement.GetProperty("@microsoft.graph.conflictBehavior").GetString());
                FolderExists = true;
                Writes++;
                return Json(new { id = "folder-id", folder = new { } });
            }

            if (method == HttpMethod.Get && relative == "items/folder-id/children")
            {
                PageRequests++;
                var query = request.RequestUri.Query.TrimStart('?');
                var offset = query.StartsWith("offset=", StringComparison.Ordinal) ? int.Parse(query[7..]) : 0;
                var ids = Files.Keys.Skip(offset).Take(PageSize).ToArray();
                return Json(new Dictionary<string, object?>
                {
                    ["value"] = ids.Select(Item).ToArray(),
                    ["@odata.nextLink"] = offset + PageSize < Files.Count
                        ? $"https://graph.microsoft.com/beta/drives/{driveId}/items/folder-id/children?offset={offset + PageSize}"
                        : null
                });
            }

            const string pathPrefix = "items/folder-id:/";
            if (relative.StartsWith(pathPrefix, StringComparison.Ordinal))
            {
                var fileName = relative[pathPrefix.Length..].Split(':')[0];
                Assert.EndsWith(".json", fileName);
                var id = fileName[..^5];
                Assert.True(Guid.TryParseExact(id, "D", out _));
                if (method == HttpMethod.Get && relative.EndsWith(':'))
                    return Files.ContainsKey(id) ? Json(Item(id)) : Error(404);
                if (method == HttpMethod.Put && relative.EndsWith(":/content", StringComparison.Ordinal))
                {
                    if (RejectWrites) return Error(412);
                    Files[id] = await request.Content!.ReadAsStringAsync(cancellationToken);
                    Writes++;
                    return Json(Item(id));
                }
            }

            const string itemPrefix = "items/memory-";
            if (relative.StartsWith(itemPrefix, StringComparison.Ordinal))
            {
                var id = relative[itemPrefix.Length..].Split('/')[0];
                if (!Files.ContainsKey(id)) return Error(404);
                if (method == HttpMethod.Get && relative.EndsWith("/content", StringComparison.Ordinal))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Files[id], Encoding.UTF8, "application/json") };
                if (method == HttpMethod.Put && relative.EndsWith("/content", StringComparison.Ordinal))
                {
                    LastIfMatch = request.Headers.TryGetValues("If-Match", out var values) ? Assert.Single(values) : null;
                    if (RejectWrites) return Error(412);
                    Files[id] = await request.Content!.ReadAsStringAsync(cancellationToken);
                    Writes++;
                    return Json(Item(id));
                }
                if (method == HttpMethod.Delete)
                {
                    Files.Remove(id);
                    Writes++;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
            }

            throw new InvalidOperationException($"Unexpected Graph request: {method} {path}");
        }

        private static object Item(string id)
            => new { id = "memory-" + id, name = id + ".json", file = new { }, eTag = "\"v1\"" };

        private static HttpResponseMessage Json(object body)
            => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Error(int status)
            => new((HttpStatusCode)status)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { error = new { code = "testError", message = "Simulated Graph error" } }), Encoding.UTF8, "application/json")
            };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Client.Dispose();
                http.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

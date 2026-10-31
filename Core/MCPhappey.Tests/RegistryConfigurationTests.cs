using System.Text.Json;
using MCPhappey.Auth;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;

namespace MCPhappey.Tests;

public sealed class RegistryConfigurationTests
{
    [Fact]
    public void Retired_Nebius_image_tool_and_server_are_not_shipped()
    {
        Assert.Null(typeof(MCPhappey.Tools.OpenAI.Image.OpenAIImages).Assembly.GetType("MCPhappey.Tools.Nebius.NebiusImages"));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MCPhappey.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        Assert.False(File.Exists(Path.Combine(root.FullName, "Servers", "MCPhappey.Servers.JSON", "Servers", "Nebius-Images", "Server.json")));
    }

    [Theory]
    [InlineData("OpenAI-Images", "openai_api_key", false)]
    [InlineData("OpenAI-Images", "openai_api_key", true)]
    public void Real_image_definitions_publish_the_effective_input_contract(string name, string key, bool delegated)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MCPhappey.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var path = Path.Combine(root.FullName, "Servers", "MCPhappey.Servers.JSON", "Servers", name, "Server.json");
        var server = JsonSerializer.Deserialize<Common.Models.Server>(File.ReadAllText(path))!;
        var config = new ServerConfig { Server = server };
        var policy = new ServerAccessPolicy(delegated ? ServerHeaderAuthenticationTests.OAuth() : null);
        var registry = config.ToRegistryServer("https://mcp.test", policy);
        var json = JsonSerializer.Serialize(registry);
        var header = Assert.Single(Assert.Single(registry.Remotes!).Headers!);
        Assert.NotNull(server.OBO);
        Assert.Equal("Authorization", header.Name);
        Assert.Contains(delegated ? "access_token" : key, header.Value);
        Assert.True(registry.Description!.Length is > 0 and <= 100);
        Assert.DoesNotContain("directAccess", json);
        Assert.DoesNotContain("operator-secret", json);
        // Optional local export for validating these exact serialized entries against the official schema.
        if (Environment.GetEnvironmentVariable("MCP_REGISTRY_VALIDATION_OUTPUT") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, $"{name}-{(delegated ? "delegated" : "headers")}.json"), json);
        }
    }
}

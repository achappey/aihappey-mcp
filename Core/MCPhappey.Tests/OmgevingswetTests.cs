using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCPhappey.Auth;
using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Core.Extensions;
using MCPhappey.Core.Services;
using MCPhappey.Tools.Rijksoverheid;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tests;

public sealed class OmgevingswetTests
{
    private const string Plan = "NL.IMRO.0200.bp1253-vas1";
    private static readonly string[] Collections = ["bestemmingsvlakken", "bouwvlakken", "functieaanduidingen",
        "bouwaanduidingen", "lettertekenaanduidingen", "maatvoeringen", "figuren", "gebiedsaanduidingen",
        "structuurvisiegebieden", "structuurvisiecomplexen", "structuurvisieverklaringen", "besluitvlakken", "besluitsubvlakken"];

    [Theory]
    [InlineData(OmgevingswetClient.InformationServer, 1, 5, 3)]
    [InlineData(OmgevingswetClient.PlansServer, 19, 8, 37)]
    public async Task Definitions_publish_resources_and_only_read_only_POST_tools(string name, int toolCount, int resourceCount, int templateCount)
    {
        var config = Definition(name);
        Assert.Null(config.Server.OBO);
        Assert.NotEmpty(config.Server.Instructions!);
        foreach (var policy in new[] { new ServerAccessPolicy(), new ServerAccessPolicy(ServerHeaderAuthenticationTests.OAuth()) })
        {
            Assert.Equal(ServerAccessMode.Headers, policy.GetMode(config.Server));
            var header = Assert.Single(policy.GetPublicHeaders(config.Server)!, h => h.Name == "X-Api-Key");
            Assert.True(header.IsRequired);
            Assert.True(header.IsSecret);
            var input = header.Variables![OmgevingswetClient.CredentialVariable];
            Assert.True(input.IsSecret);
            Assert.True(input.IsRequired);
            Assert.NotEmpty(input.Placeholder!);
            Assert.Equal("caller-key", policy.GetCredential(config.Server,
                new Dictionary<string, string> { ["x-api-key"] = "caller-key" }, OmgevingswetClient.CredentialVariable));
            var registry = config.ToMCPServerRegistryItem("https://mcp.test", [], policy);
            Assert.DoesNotContain("access_token", JsonSerializer.Serialize(registry));
        }
        var tools = (await config.Server.ToToolsList(Kernel.CreateBuilder().Build(), []))!.Tools;
        Assert.Equal(toolCount, tools.Count);
        Assert.Equal(toolCount, tools.Select(t => t.Name).Distinct().Count());
        foreach (var tool in tools)
        {
            Assert.True(tool.Annotations!.ReadOnlyHint);
            Assert.DoesNotContain("api_key", tool.InputSchema.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("authorization", tool.InputSchema.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(resourceCount, config.ResourceList!.Resources.Count);
        Assert.Equal(templateCount, config.ResourceTemplateList!.ResourceTemplates.Count);
        foreach (var resource in config.ResourceList.Resources)
            OmgevingswetClient.ValidateUri(config, resource.Uri);
        foreach (var template in config.ResourceTemplateList.ResourceTemplates)
        {
            Assert.StartsWith(OmgevingswetClient.BaseUrl(config) + "/", template.UriTemplate);
            Assert.DoesNotContain("_zoek", template.UriTemplate);
            Assert.DoesNotContain("�", template.Description!);
        }
        if (name == OmgevingswetClient.PlansServer)
        {
            foreach (var collection in Collections.Concat(["teksten", "cartografiesummaries"]))
            {
                Assert.Contains(config.ResourceTemplateList.ResourceTemplates, t => t.UriTemplate.Contains($"/plannen/{{planId}}/{collection}{{?"));
                Assert.Contains(config.ResourceTemplateList.ResourceTemplates, t => t.UriTemplate.Contains($"/plannen/{{planId}}/{collection}/{{id}}"));
            }
            var plans = Assert.Single(config.ResourceTemplateList.ResourceTemplates, t => t.Name == "plannen");
            Assert.Contains("expand*", plans.UriTemplate);
            Assert.Contains("status,", plans.UriTemplate);
            Assert.DoesNotContain("status*", plans.UriTemplate);
        }
    }

    [Theory]
    [InlineData(OmgevingswetClient.InformationServer, false)]
    [InlineData(OmgevingswetClient.InformationServer, true)]
    [InlineData(OmgevingswetClient.PlansServer, false)]
    [InlineData(OmgevingswetClient.PlansServer, true)]
    public async Task Transport_uses_header_auth_even_with_OAuth_hosting(string name, bool oauth)
    {
        var config = Definition(name);
        using var services = new ServiceCollection().BuildServiceProvider(); // No JWT validator registered or needed.
        foreach (var key in new[] { "first-key", "second-key", "" })
        {
            var context = new DefaultHttpContext { RequestServices = services };
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("mcp.test");
            context.Request.Path = "/" + name.ToLowerInvariant();
            context.Request.Headers["x-api-key"] = key;
            context.Response.Body = new MemoryStream();
            var reached = false;
            await new ServerAccessMiddleware(_ => { reached = true; return Task.CompletedTask; },
                new ServerAccessPolicy(oauth ? ServerHeaderAuthenticationTests.OAuth() : null)).InvokeAsync(context, [config]);
            Assert.Equal(key.Length > 0, reached);
            Assert.False(context.User.Identity?.IsAuthenticated == true);
            Assert.Empty(context.Response.Headers.WWWAuthenticate.ToArray());
            if (reached)
            {
                var access = (ServerRequestAccess)context.Items[typeof(ServerRequestAccess)]!;
                Assert.Equal(ServerAccessMode.Headers, access.Mode);
                Assert.Equal(key, access.Headers["x-api-key"]);
            }
            else Assert.Equal(401, context.Response.StatusCode);
        }
    }

    [Theory]
    [InlineData(OmgevingswetClient.InformationServer, "/crss", "application/json", null)]
    [InlineData(OmgevingswetClient.InformationServer, "/documenten/_akn_nl_act_gm0362_2020_omgevingsplan?geldigOp=2024-01-01", "application/hal+json", null)]
    [InlineData(OmgevingswetClient.InformationServer, "/openapi.yaml", "application/yaml", null)]
    [InlineData(OmgevingswetClient.PlansServer, "/plannen?page=1", "application/hal+json", "epsg:4258")]
    [InlineData(OmgevingswetClient.PlansServer, "/app-info", "application/json", "epsg:4258")]
    public async Task Resource_reads_use_only_the_current_caller_key(string name, string path, string accept, string? crs)
    {
        var config = Definition(name);
        var calls = 0;
        using var handler = new CaptureHandler(request =>
        {
            calls++;
            Assert.Equal("caller-key", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("X-Unrelated-Key"));
            Assert.Equal(accept, Assert.Single(request.Headers.Accept).MediaType);
            Assert.Equal(crs, request.Headers.TryGetValues("Accept-Crs", out var values) ? Assert.Single(values) : null);
            return JsonResponse("{\"_links\":{\"self\":{\"href\":\"unchanged\"}}}", accept);
        });
        using var services = Services(config, handler);
        var files = await new OmgevingswetContentDownloader().DownloadAsync(services, config, OmgevingswetClient.BaseUrl(config) + path);
        Assert.Contains("unchanged", Assert.Single(files!).Contents.ToString());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Resource_CRS_is_optional_but_validated_and_keys_are_not_shared_between_requests()
    {
        var config = Definition(OmgevingswetClient.PlansServer);
        var seen = new List<string>();
        using var handler = new CaptureHandler(request =>
        {
            seen.Add(Assert.Single(request.Headers.GetValues("X-Api-Key")));
            Assert.Equal("epsg:28992", Assert.Single(request.Headers.GetValues("Accept-Crs")));
            return JsonResponse("{}");
        });
        foreach (var key in new[] { "first-key", "second-key" })
        {
            using var services = Services(config, handler, key, "epsg:28992");
            await services.GetRequiredService<OmgevingswetClient>().GetAsync(services, config, OmgevingswetClient.PlansBaseUrl + "/plannen");
        }
        using (var services = Services(config, handler, crs: "epsg:4326"))
            await Assert.ThrowsAsync<ArgumentException>(() => services.GetRequiredService<OmgevingswetClient>()
                .GetAsync(services, config, OmgevingswetClient.PlansBaseUrl + "/plannen"));
        Assert.Equal(new[] { "first-key", "second-key" }, seen);
    }

    [Fact]
    public async Task Resource_service_returns_HAL_as_readable_text_without_operator_scrapers()
    {
        var config = Definition(OmgevingswetClient.PlansServer);
        const string json = "{\"_embedded\":{\"plannen\":[]},\"_links\":{\"next\":{\"href\":\"preserved\"}}}";
        using var handler = new CaptureHandler(_ => JsonResponse(json));
        using var services = Services(config, handler);
        await using var server = Server(config);
        var downloader = new DownloadService(null!, new TransformService([]), [new ForbiddenScraper()],
            services.GetServices<IRequestContentDownloader>());
        var result = await new ResourceService(downloader).GetServerResource(services, server,
            OmgevingswetClient.PlansBaseUrl + "/plannen", sessionConfig: config);
        var text = Assert.IsType<TextResourceContents>(Assert.Single(result.Contents));
        Assert.Equal("application/hal+json", text.MimeType);
        Assert.Equal(json, text.Text);
    }

    [Theory]
    [InlineData("https://evil.test/plannen")]
    [InlineData("http://ruimte.omgevingswet.overheid.nl/ruimtelijke-plannen/api/opvragen/v4/plannen")]
    [InlineData("https://ruimte.omgevingswet.overheid.nl/ruimtelijke-plannen/api/opvragen/v40/plannen")]
    [InlineData("https://ruimte.omgevingswet.overheid.nl/another-api/plannen")]
    [InlineData("https://ruimte.omgevingswet.overheid.nl:444/ruimtelijke-plannen/api/opvragen/v4/plannen")]
    [InlineData("https://user@ruimte.omgevingswet.overheid.nl/ruimtelijke-plannen/api/opvragen/v4/plannen")]
    [InlineData("https://ruimte.omgevingswet.overheid.nl/ruimtelijke-plannen/api/opvragen/v4/plannen#fragment")]
    [InlineData("https://ruimte.omgevingswet.overheid.nl/ruimtelijke-plannen/api/opvragen/v4/../secret")]
    [InlineData("https://ruimte.omgevingswet.overheid.nl/ruimtelijke-plannen/api/opvragen/v4/%2fsecret")]
    [InlineData("https://ruimte.omgevingswet.overheid.nl/ruimtelijke-plannen/api/opvragen/v4/%252fsecret")]
    [InlineData(OmgevingswetClient.InformationBaseUrl + "/crss")]
    public async Task Untrusted_resource_urls_never_reach_HTTP_or_operator_scrapers(string url)
    {
        var config = Definition(OmgevingswetClient.PlansServer);
        using var handler = new CaptureHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        using var services = Services(config, handler);
        var downloader = new DownloadService(null!, null!, [new ForbiddenScraper()], services.GetServices<IRequestContentDownloader>());
        await Assert.ThrowsAsync<ArgumentException>(() => downloader.DownloadContentAsync(services, config, null, url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{omgevingswet_api_key}")]
    [InlineData("bad key")]
    public async Task Missing_and_unexpanded_keys_never_fall_back_to_other_credentials(string? key)
    {
        var config = Definition(OmgevingswetClient.InformationServer);
        using var handler = new CaptureHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        using var services = Services(config, handler, key);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<OmgevingswetClient>()
            .GetAsync(services, config, OmgevingswetClient.InformationBaseUrl + "/crss"));
    }

    [Fact]
    public async Task Credentials_from_another_server_request_are_rejected()
    {
        var config = Definition(OmgevingswetClient.InformationServer);
        using var handler = new CaptureHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        using var services = Services(config, handler);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<OmgevingswetClient>()
            .GetAsync(services, Definition(OmgevingswetClient.PlansServer), OmgevingswetClient.PlansBaseUrl + "/plannen"));
    }

    [Theory]
    [InlineData("https://evil.test/steal")]
    [InlineData("/outside-api")]
    [InlineData(OmgevingswetClient.InformationBaseUrl + "/crss")]
    public async Task GET_redirects_cannot_export_credentials(string destination)
    {
        var config = Definition(OmgevingswetClient.PlansServer);
        var calls = 0;
        using var handler = new CaptureHandler(_ =>
        {
            calls++;
            return new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri(destination, UriKind.RelativeOrAbsolute) } };
        });
        using var services = Services(config, handler);
        await Assert.ThrowsAsync<ArgumentException>(() => services.GetRequiredService<OmgevingswetClient>()
            .GetAsync(services, config, OmgevingswetClient.PlansBaseUrl + "/"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GET_can_follow_same_API_redirect_but_redirect_loops_are_bounded()
    {
        var config = Definition(OmgevingswetClient.PlansServer);
        var calls = 0;
        using var handler = new CaptureHandler(request =>
        {
            calls++;
            return request.RequestUri!.AbsolutePath.EndsWith("/openapi.yaml") ? JsonResponse("openapi: 3.0.3", "application/yaml")
                : new(HttpStatusCode.PermanentRedirect) { Headers = { Location = new Uri("openapi.yaml", UriKind.Relative) } };
        });
        using var services = Services(config, handler);
        var file = await services.GetRequiredService<OmgevingswetClient>().GetAsync(services, config, OmgevingswetClient.PlansBaseUrl + "/");
        Assert.Equal("application/yaml", file.MimeType);
        Assert.Equal(2, calls);
        using var loopHandler = new CaptureHandler(_ => new(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("/ruimtelijke-plannen/api/opvragen/v4/", UriKind.Relative) } });
        using var loopServices = Services(config, loopHandler);
        await Assert.ThrowsAsync<HttpRequestException>(() => loopServices.GetRequiredService<OmgevingswetClient>()
            .GetAsync(loopServices, config, OmgevingswetClient.PlansBaseUrl + "/"));
    }

    public static IEnumerable<object[]> PostEndpoints()
    {
        yield return [nameof(OmgevingsinformatieTools.SearchDocuments), "/documenten/_zoek", 20];
        yield return [nameof(RuimtelijkePlannenTools.SearchPlans), "/plannen/_zoek", 10];
        yield return [nameof(RuimtelijkePlannenTools.SearchPlanObjects), $"/plannen/{Plan}/planobjecten/_zoek", 0];
        yield return [nameof(RuimtelijkePlannenTools.SearchSubjects), "/onderwerpen/_zoek", 0];
        yield return [nameof(RuimtelijkePlannenTools.SearchArticles), $"/plannen/{Plan}/artikelen/_zoek", 10];
        yield return [nameof(RuimtelijkePlannenTools.GetZoningAreasByIds), $"/plannen/{Plan}/bestemmingsvlakken", 50];
        yield return [nameof(RuimtelijkePlannenTools.GetCartographySummariesByIds), $"/plannen/{Plan}/cartografiesummaries", 50];
        var methods = new[] { "SearchZoningAreas", "SearchBuildingEnvelopes", "SearchFunctionDesignations", "SearchBuildingDesignations",
            "SearchLetterDesignations", "SearchDimensions", "SearchFigures", "SearchAreaDesignations", "SearchStructureVisionAreas",
            "SearchStructureVisionComplexes", "SearchStructureVisionExplanations", "SearchDecisionAreas", "SearchDecisionSubareas" };
        for (var i = 0; i < methods.Length; i++) yield return [methods[i], $"/plannen/{Plan}/{Collections[i]}/_zoek", i == 0 ? 50 : 10];
    }

    [Theory]
    [MemberData(nameof(PostEndpoints))]
    public async Task Every_POST_tool_binds_arguments_and_preserves_endpoint_specific_contract(string method, string path, int size)
    {
        var info = method == nameof(OmgevingsinformatieTools.SearchDocuments);
        var config = Definition(info ? OmgevingswetClient.InformationServer : OmgevingswetClient.PlansServer);
        var calls = 0;
        using var handler = new CaptureHandler(request =>
        {
            calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(OmgevingswetClient.BaseUrl(config) + path).AbsolutePath, request.RequestUri!.AbsolutePath);
            Assert.Equal("caller-key", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            var body = JsonNode.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult())!;
            if (method.Contains("ByIds"))
            {
                Assert.Equal("NL.IMRO.object1", body["ids"]![0]!.GetValue<string>());
                Assert.Null(body["_geo"]);
                Assert.False(request.Headers.Contains("Content-Crs"));
            }
            else
            {
                var geometry = info ? body["geometrie"] : body["_geo"]!["contains"];
                Assert.Equal("Point", geometry!["type"]!.GetValue<string>());
                Assert.Equal(info ? OmgevingswetClient.RdNew : "epsg:4258", Assert.Single(request.Headers.GetValues("Content-Crs")));
            }
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
            if (size > 0) Assert.Equal(size.ToString(), query[info ? "size" : "pageSize"].ToString());
            else Assert.Empty(query);
            return JsonResponse("{\"_embedded\":{},\"_links\":{\"next\":{\"href\":\"preserved\"}}}");
        });
        using var services = Services(config, handler);
        await using var server = Server(config);
        var arguments = new Dictionary<string, JsonElement>();
        if (info) arguments["geometrie"] = JsonSerializer.SerializeToElement(new { type = "Point", coordinates = new[] { 155000, 463000 } });
        else if (method.Contains("ByIds")) arguments["ids"] = JsonSerializer.SerializeToElement(new[] { "NL.IMRO.object1" });
        else arguments["geometry"] = JsonSerializer.SerializeToElement(new { type = "Point", coordinates = new[] { 5.960233, 52.179515 } });
        if (path.Contains(Plan)) arguments["planId"] = JsonSerializer.SerializeToElement(Plan);
        var result = await Invoke(method, services, server, arguments);
        Assert.False(result.IsError == true, string.Join('\n', result.Content.OfType<TextContentBlock>().Select(b => b.Text)));
        Assert.Contains("preserved", JsonSerializer.Serialize(result.StructuredContent));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Filters_and_expansions_keep_CSV_versus_repeated_encoding_and_timezone_offsets()
    {
        var config = Definition(OmgevingswetClient.PlansServer);
        using var handler = new CaptureHandler(request =>
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal("vastgesteld,ontwerp", query["status"].ToString());
            Assert.Equal(new[] { "geometrie", "teksten" }, query["expand"].ToArray());
            Assert.Equal("gemeente A & B", query["beleidsmatigVerantwoordelijkeOverheid.naam"].ToString());
            Assert.Equal("2024-01-01T12:34:56+01:00", query["beschikbaarOp"].ToString());
            Assert.Equal("false", query["isTamPlan"].ToString());
            Assert.Equal("true", query["ponsOmgevingsplan"].ToString());
            Assert.Equal("epsg:28992", Assert.Single(request.Headers.GetValues("Content-Crs")));
            Assert.Equal("epsg:4258", Assert.Single(request.Headers.GetValues("Accept-Crs")));
            return JsonResponse("{}");
        });
        using var services = Services(config, handler);
        await using var server = Server(config);
        var result = await Invoke(nameof(RuimtelijkePlannenTools.SearchPlans), services, server, new()
        {
            ["geometry"] = JsonSerializer.SerializeToElement(new { type = "Point", coordinates = new[] { 155000, 463000 } }),
            ["options"] = JsonSerializer.SerializeToElement(new { contentCrs = "epsg:28992", expand = new[] { "geometrie", "teksten" } }),
            ["filters"] = JsonSerializer.SerializeToElement(new { status = new[] { "vastgesteld", "ontwerp" },
                beleidsmatigVerantwoordelijkeOverheidNaam = "gemeente A & B", beschikbaarOp = "2024-01-01T12:34:56+01:00",
                isTamPlan = false, ponsOmgevingsplan = true })
        });
        Assert.False(result.IsError == true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Document_search_preserves_all_body_filters_temporal_parameters_and_conditional_CRS(bool withGeometry)
    {
        var config = Definition(OmgevingswetClient.InformationServer);
        using var handler = new CaptureHandler(request =>
        {
            var body = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!;
            Assert.Equal("NL.IMRO", body["_find"]!.GetValue<string>());
            Assert.Equal("GEMEENTE", body["bestuurslaag"]!.GetValue<string>());
            Assert.Equal("REGELGEVING", body["regelgevingOfOverig"]!.GetValue<string>());
            Assert.Equal("/akn/nl/act/gm0362/2020/omgevingsplan", body["identificaties"]![0]!.GetValue<string>());
            Assert.Equal(withGeometry, request.Headers.Contains("Content-Crs"));
            Assert.Equal(withGeometry, body["geometrie"] is not null);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal("2024-01-01", query["geldigOp"].ToString());
            Assert.Equal("2024-01-01T12:34:56+01:00", query["beschikbaarOp"].ToString());
            Assert.Equal("Volledig", query["synchroniseerMetTileset"].ToString());
            Assert.Equal("true", query["inclusiefToekomstigGeldig"].ToString());
            Assert.Equal("2", query["page"].ToString());
            Assert.Equal("200", query["size"].ToString());
            Assert.Equal("DESC", query["_sort"].ToString());
            return JsonResponse("{}");
        });
        using var services = Services(config, handler);
        await using var server = Server(config);
        var arguments = new Dictionary<string, JsonElement>
        {
            ["find"] = JsonSerializer.SerializeToElement("NL.IMRO"),
            ["bestuurslaag"] = JsonSerializer.SerializeToElement("GEMEENTE"),
            ["regelgevingOfOverig"] = JsonSerializer.SerializeToElement("REGELGEVING"),
            ["identificaties"] = JsonSerializer.SerializeToElement(new[] { "/akn/nl/act/gm0362/2020/omgevingsplan" }),
            ["geldigOp"] = JsonSerializer.SerializeToElement("2024-01-01"),
            ["beschikbaarOp"] = JsonSerializer.SerializeToElement("2024-01-01T12:34:56+01:00"),
            ["synchroniseerMetTileset"] = JsonSerializer.SerializeToElement("Volledig"),
            ["inclusiefToekomstigGeldig"] = JsonSerializer.SerializeToElement(true),
            ["page"] = JsonSerializer.SerializeToElement(2),
            ["size"] = JsonSerializer.SerializeToElement(200),
            ["sort"] = JsonSerializer.SerializeToElement("DESC")
        };
        if (withGeometry) arguments["geometrie"] = JsonSerializer.SerializeToElement(new { type = "Point", coordinates = new[] { 155000, 463000 } });
        var result = await Invoke(nameof(OmgevingsinformatieTools.SearchDocuments), services, server, arguments);
        Assert.False(result.IsError == true);
    }

    [Theory]
    [InlineData("{\"page\":-1}")]
    [InlineData("{\"pageSize\":30}")]
    [InlineData("{\"contentCrs\":\"epsg:4326\"}")]
    [InlineData("{\"acceptCrs\":\"epsg:4326\"}")]
    public async Task Invalid_spatial_options_return_tool_errors_without_contacting_the_API(string options)
    {
        var config = Definition(OmgevingswetClient.PlansServer);
        using var handler = new CaptureHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        using var services = Services(config, handler);
        await using var server = Server(config);
        var result = await Invoke(nameof(RuimtelijkePlannenTools.SearchPlans), services, server, new()
        {
            ["geometry"] = JsonSerializer.SerializeToElement(new { type = "Point", coordinates = new[] { 5.9, 52.1 } }),
            ["options"] = JsonSerializer.Deserialize<JsonElement>(options)
        });
        Assert.True(result.IsError);
        Assert.DoesNotContain("HTTP must not run", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("bad-relation", "Point")]
    [InlineData("contains", "Feature")]
    public async Task Invalid_relations_and_non_geometry_GeoJSON_are_rejected(string relation, string geometryType)
    {
        var config = Definition(OmgevingswetClient.PlansServer);
        using var handler = new CaptureHandler(_ => throw new InvalidOperationException("HTTP must not run."));
        using var services = Services(config, handler);
        await using var server = Server(config);
        var result = await Invoke(nameof(RuimtelijkePlannenTools.SearchPlans), services, server, new()
        {
            ["geometry"] = JsonSerializer.SerializeToElement(new { type = geometryType, coordinates = new[] { 5.9, 52.1 } }),
            ["relation"] = JsonSerializer.SerializeToElement(relation)
        });
        Assert.True(result.IsError);
        Assert.DoesNotContain("HTTP must not run", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(422)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(307)]
    public async Task Upstream_errors_are_tool_errors_with_details_but_never_echo_the_key_or_replay_POST(int status)
    {
        var config = Definition(OmgevingswetClient.PlansServer);
        var calls = 0;
        using var handler = new CaptureHandler(_ =>
        {
            calls++;
            return new((HttpStatusCode)status) { Content = new StringContent("{\"detail\":\"upstream detail caller-key\"}"),
                Headers = { Location = new Uri("https://evil.test/steal") } };
        });
        using var services = Services(config, handler);
        await using var server = Server(config);
        var result = await Invoke(nameof(RuimtelijkePlannenTools.SearchSubjects), services, server, new()
        {
            ["geometry"] = JsonSerializer.SerializeToElement(new { type = "Point", coordinates = new[] { 5.9, 52.1 } })
        });
        Assert.True(result.IsError);
        var text = string.Join('\n', result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        Assert.Contains(status.ToString(), text);
        Assert.Contains("upstream detail", text);
        Assert.DoesNotContain("caller-key", text);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Registration_is_idempotent_and_the_named_HTTP_handler_disables_automatic_redirects_and_cookies()
    {
        using var services = new ServiceCollection().AddOmgevingswet().AddOmgevingswet().BuildServiceProvider();
        Assert.Single(services.GetServices<IRequestContentDownloader>());
        var handler = services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(OmgevingswetClient.ClientName);
        while (handler is DelegatingHandler delegating) handler = delegating.InnerHandler!;
        var primary = Assert.IsType<HttpClientHandler>(handler);
        Assert.False(primary.AllowAutoRedirect);
        Assert.False(primary.UseCookies);
    }

    private static async Task<CallToolResult> Invoke(string method, IServiceProvider services, McpServer server, Dictionary<string, JsonElement> arguments)
    {
        var type = method == nameof(OmgevingsinformatieTools.SearchDocuments) ? typeof(OmgevingsinformatieTools) : typeof(RuimtelijkePlannenTools);
        var tool = McpServerTool.Create(type.GetMethod(method)!);
        var parameters = new CallToolRequestParams { Name = tool.ProtocolTool.Name, Arguments = arguments };
        var context = new RequestContext<CallToolRequestParams>(server, new JsonRpcRequest { Id = new RequestId(1), Method = "tools/call" }, parameters)
        { Services = services };
        return await tool.InvokeAsync(context);
    }

    private static ServerConfig Definition(string name)
    {
        var config = AgentDualModeTests.Definition(name);
        var folder = Path.Combine(RepositoryRoot(), "Servers", "MCPhappey.Servers.JSON", "Servers", name);
        config.ResourceList = JsonSerializer.Deserialize<ListResourcesResult>(File.ReadAllText(Path.Combine(folder, "Resources.json")))!;
        config.ResourceTemplateList = JsonSerializer.Deserialize<ListResourceTemplatesResult>(File.ReadAllText(Path.Combine(folder, "ResourceTemplates.json")))!;
        return config;
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "MCPhappey.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Cannot locate repository root.");
    }

    private static ServiceProvider Services(ServerConfig config, HttpMessageHandler handler, string? key = "caller-key", string? crs = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["Authorization"] = "Bearer unrelated-provider-token", ["X-Unrelated-Key"] = "must-not-forward" };
        if (key is not null) headers["X-Api-Key"] = key;
        if (crs is not null) headers["Accept-Crs"] = crs;
        var context = new DefaultHttpContext();
        context.Items[typeof(ServerRequestAccess)] = new ServerRequestAccess(config, ServerAccessMode.Headers, headers);
        var registrations = new ServiceCollection().AddOmgevingswet()
            .AddSingleton<IReadOnlyList<ServerConfig>>(new[] { config })
            .AddSingleton(new ServerAccessPolicy(ServerHeaderAuthenticationTests.OAuth()))
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });
        registrations.AddHttpClient(OmgevingswetClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return registrations.BuildServiceProvider();
    }

    private static McpServer Server(ServerConfig config) => McpServer.Create(new StreamServerTransport(Stream.Null, Stream.Null), new()
    { ServerInfo = new() { Name = config.Server.ServerInfo.Name, Version = "1.0.0" } });

    private static HttpResponseMessage JsonResponse(string text, string mediaType = "application/hal+json")
        => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, mediaType) };

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ForbiddenScraper : IContentScraper
    {
        public bool SupportsHost(ServerConfig serverConfig, string url) => throw new InvalidOperationException("Operator scraper must not run.");
        public Task<IEnumerable<FileItem>?> GetContentAsync(McpServer server, IServiceProvider services, string url, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Operator scraper must not run.");
    }
}

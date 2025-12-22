using System.ComponentModel;
using System.Text.Json;
using Bogus;
using MCPhappey.Core.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPhappey.Tools.GitHub.Bogus;

public static class BogusService
{
    private static Faker CreateFaker(string? locale, int? seed)
    {
        var f = new Faker(locale ?? "en");
        if (seed.HasValue)
        {
            f.Random = new Randomizer(seed.Value);
            //    f.DateTimeReference = new DateTime(2000, 1, 1);
        }
        return f;
    }

    [Description("Generate a realistic person profile (name, username, email, phone, address).")]
    [McpServerTool(
     Title = "Generate person",
     Name = "github_bogus_generate_person",
     ReadOnly = true,
     OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubBogus_GeneratePerson(
     RequestContext<CallToolRequestParams> requestContext,
     [Description("Locale code (e.g. 'en', 'nl', 'de_CH')")] string? locale = "en",
     [Description("Optional deterministic seed")] int? seed = null)
        => await requestContext.WithStructuredContent(async () =>
    {
        var f = CreateFaker(locale, seed);
        var json = new
        {
            Person = new
            {
                FirstName = f.Name.FirstName(),
                LastName = f.Name.LastName(),
                UserName = f.Internet.UserName(),
                Email = f.Internet.Email(),
                Phone = f.Phone.PhoneNumber(),
                Address = new
                {
                    Street = f.Address.StreetAddress(),
                    City = f.Address.City(),
                    ZipCode = f.Address.ZipCode(),
                    Country = f.Address.Country()
                }
            }
        }
        ;
        return await Task.FromResult(json);
    });


    [Description("Generate lorem text (sentences).")]
    [McpServerTool(
        Title = "Generate lorem",
        Name = "github_bogus_generate_lorem",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GenerateLorem(
        [Description("Number of sentences")] int sentences = 3,
        [Description("Locale code (e.g. 'en', 'nl')")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null) =>
        await Task.FromResult(CreateFaker(locale, seed).Lorem.Sentences(sentences));

    [Description("Generate a company (name, catch phrase, BS).")]
    [McpServerTool(
        Title = "Generate company",
        Name = "github_bogus_generate_company",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GenerateCompany(
        [Description("Locale code (e.g. 'en', 'nl')")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null) =>
        await Task.FromResult(JsonSerializer.Serialize(new
        {
            Company = new
            {
                Name = CreateFaker(locale, seed).Company.CompanyName(),
                CatchPhrase = CreateFaker(locale, seed).Company.CatchPhrase(),
                Bs = CreateFaker(locale, seed).Company.Bs()
            }
        }));

    [Description("Generate a list of simple companies.")]
    [McpServerTool(
           Title = "Generate company list",
           Name = "github_bogus_generate_company_list",
           ReadOnly = true,
           OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubBogus_GenerateCompanyList(
           RequestContext<CallToolRequestParams> requestContext,
           [Description("How many people")] int count = 10,
           [Description("Locale code")] string? locale = "en",
           [Description("Optional deterministic seed")] int? seed = null)
           => await requestContext.WithStructuredContent(async () =>
        {
            var f = CreateFaker(locale, seed);
            var data = Enumerable.Range(0, Math.Max(0, count))
                .Select(i =>
                {
                    // Minor per-item variation by nudging seed via IndexGlobal if provided
                    return new
                    {
                        Name = f.Company.CompanyName(),
                        Suffix = f.Company.CompanySuffix(),
                        Logo = f.Image.PicsumUrl(128, 128),
                        Address = f.Address.FullAddress(),
                        CatchPhrase = f.Company.CatchPhrase(),
                        Bs = f.Company.Bs()
                    };
                });

            return await Task.FromResult(data);
        });

    [Description("Generate a random integer within [min, max].")]
    [McpServerTool(
        Title = "Random int",
        Name = "github_bogus_random_int",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<int> GitHubBogus_RandomInt(
        [Description("Minimum value (inclusive)")] int min = 0,
        [Description("Maximum value (inclusive)")] int max = 100,
        [Description("Optional deterministic seed")] int? seed = null) =>
        await Task.FromResult((seed.HasValue ? new Randomizer(seed.Value) : new Randomizer()).Int(min, max));

    [Description("Generate a future/past date relative to a reference.")]
    [McpServerTool(
        Title = "Random date",
        Name = "github_bogus_random_date",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_RandomDate(
        [Description("'past' or 'future'")] string direction = "future",
        [Description("Days range from reference")] int days = 30,
        [Description("Optional deterministic seed")] int? seed = null) =>
        await Task.FromResult(direction?.ToLowerInvariant() == "past"
            ? CreateFaker("en", seed).Date.Past(days).ToString("O")
            : CreateFaker("en", seed).Date.Future(days).ToString("O"));

    [Description("Generate a realistic postal address (street, city, zip, country, lat/lng).")]
    [McpServerTool(
        Title = "Generate address",
        Name = "github_bogus_generate_address",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GenerateAddress(
        [Description("Locale code (e.g. 'en', 'nl')")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker(locale, seed);
        var json = JsonSerializer.Serialize(new
        {
            Address = new
            {
                Street = f.Address.StreetAddress(),
                City = f.Address.City(),
                ZipCode = f.Address.ZipCode(),
                Country = f.Address.Country(),
                Latitude = f.Address.Latitude(),
                Longitude = f.Address.Longitude()
            }
        });
        return await Task.FromResult(json);
    }

    // 2) Internet account (username/email/url/password)
    [Description("Generate an internet account payload (username, email, domain, url, password).")]
    [McpServerTool(
        Title = "Generate internet account",
        Name = "github_bogus_generate_internet_account",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GenerateInternetAccount(
        [Description("Locale code")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker(locale, seed);
        var first = f.Name.FirstName();
        var last = f.Name.LastName();
        var json = JsonSerializer.Serialize(new
        {
            Internet = new
            {
                UserName = f.Internet.UserName(first, last),
                Email = f.Internet.Email(first, last),
                Domain = f.Internet.DomainName(),
                Url = f.Internet.Url(),
                Password = f.Internet.Password()
            }
        });
        return await Task.FromResult(json);
    }

    // 3) Payment card (for testing)
    [Description("Generate fake payment card details (name, number, cvv, expiry, iban/bic).")]
    [McpServerTool(
        Title = "Generate payment card",
        Name = "github_bogus_generate_payment_card",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GeneratePaymentCard(
        [Description("Locale code")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker(locale, seed);
        var json = JsonSerializer.Serialize(new
        {
            Card = new
            {
                Cardholder = $"{f.Name.FirstName()} {f.Name.LastName()}",
                Number = f.Finance.CreditCardNumber(),
                Cvv = f.Finance.CreditCardCvv(),
                ExpiryMonth = f.Random.Int(1, 12).ToString("00"),
                ExpiryYear = f.Date.Future(5).Year,
                Iban = f.Finance.Iban(),
                Bic = f.Finance.Bic()
            }
        });
        return await Task.FromResult(json);
    }

    // 4) Product (commerce)
    [Description("Generate a product payload (name, material, adjective, category, price, ean13).")]
    [McpServerTool(
        Title = "Generate product",
        Name = "github_bogus_generate_product",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GenerateProduct(
        [Description("Locale code")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker(locale, seed);
        var json = JsonSerializer.Serialize(new
        {
            Product = new
            {
                Name = f.Commerce.ProductName(),
                Material = f.Commerce.ProductMaterial(),
                Adjective = f.Commerce.ProductAdjective(),
                Category = f.Commerce.Categories(1).First(),
                Price = f.Commerce.Price(5, 500, 2),
                Ean13 = f.Commerce.Ean13()
            }
        });
        return await Task.FromResult(json);
    }

    // 5) Vehicle
    [Description("Generate vehicle data (VIN, manufacturer, model, type, fuel).")]
    [McpServerTool(
        Title = "Generate vehicle",
        Name = "github_bogus_generate_vehicle",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GenerateVehicle(
        [Description("Locale code")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker(locale, seed);
        var json = JsonSerializer.Serialize(new
        {
            Vehicle = new
            {
                Vin = f.Vehicle.Vin(),
                Manufacturer = f.Vehicle.Manufacturer(),
                Model = f.Vehicle.Model(),
                Type = f.Vehicle.Type(),
                Fuel = f.Vehicle.Fuel()
            }
        });
        return await Task.FromResult(json);
    }

    // 6) Geo point within radius (location-ish data)
    [Description("Generate a random geo point (lat/lng) and an approximate address.")]
    [McpServerTool(
        Title = "Generate geo point",
        Name = "github_bogus_generate_geo_point",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GenerateGeoPoint(
        [Description("Locale code")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker(locale, seed);
        var json = JsonSerializer.Serialize(new
        {
            Geo = new
            {
                Latitude = f.Address.Latitude(),
                Longitude = f.Address.Longitude(),
                NearbyStreet = f.Address.StreetAddress(),
                NearbyCity = f.Address.City(),
                NearbyCountry = f.Address.Country()
            }
        });
        return await Task.FromResult(json);
    }

    // 7) User agent + device-ish identities
    [Description("Generate a realistic user agent and network identifiers.")]
    [McpServerTool(
        Title = "Generate user agent",
        Name = "github_bogus_generate_user_agent",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GenerateUserAgent(
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker("en", seed);
        var json = JsonSerializer.Serialize(new
        {
            Client = new
            {
                UserAgent = f.Internet.UserAgent(),
                IPv4 = f.Internet.Ip(),
                IPv6 = f.Internet.Ipv6(),
                Mac = f.Internet.Mac()
            }
        });
        return await Task.FromResult(json);
    }

    // 8) Avatar / image placeholders
    [Description("Generate an avatar URL and placeholder image URLs.")]
    [McpServerTool(
        Title = "Generate images",
        Name = "github_bogus_generate_images",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GenerateImages(
        [Description("Width in pixels")] int width = 128,
        [Description("Height in pixels")] int height = 128,
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker("en", seed);
        var json = JsonSerializer.Serialize(new
        {
            Images = new
            {
                Avatar = f.Internet.Avatar(),
                Picsum = f.Image.PicsumUrl(width, height),
                Placeholder = f.Image.PlaceholderUrl(width, height),
                DataUriSvg = f.Image.DataUri(width, height)
            }
        });
        return await Task.FromResult(json);
    }

    // 9) Review (Rant dataset)
    [Description("Generate a short fake review (stars, title, body).")]
    [McpServerTool(
        Title = "Generate review",
        Name = "github_bogus_generate_review",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string?> GitHubBogus_GenerateReview(
        [Description("Locale code")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker(locale, seed);
        var stars = f.Random.Int(1, 5);
        var json = JsonSerializer.Serialize(new
        {
            Review = new
            {
                Stars = stars,
                Title = f.Commerce.ProductAdjective(),
                Body = f.Rant.Review()
            }
        });
        return await Task.FromResult(json);
    }

    // 10) UUID/GUID batch
    [Description("Generate a list of GUIDs/UUIDs.")]
    [McpServerTool(
        Title = "Generate UUIDs",
        Name = "github_bogus_generate_uuids",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<IEnumerable<string>> GitHubBogus_GenerateUuids(
        [Description("How many UUIDs to generate")] int count = 5,
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var rand = seed.HasValue ? new Randomizer(seed.Value) : new Randomizer();
        var list = Enumerable.Range(0, Math.Max(0, count))
            .Select(_ => rand.Guid().ToString())
            .ToList();
        return await Task.FromResult(list);
    }

    // 11) Bulk: simple people list (minimal fields) for seeding tables
    [Description("Generate a list of simple people (first/last/email).")]
    [McpServerTool(
        Title = "Generate people list",
        Name = "github_bogus_generate_people_list",
        ReadOnly = true,
        OpenWorld = false)]
    public static async Task<CallToolResult?> GitHubBogus_GeneratePeopleList(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("How many people")] int count = 10,
        [Description("Locale code")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null)
        => await requestContext.WithStructuredContent(async () =>
    {
        var f = CreateFaker(locale, seed);
        var data = Enumerable.Range(0, Math.Max(0, count))
            .Select(i =>
            {
                // Minor per-item variation by nudging seed via IndexGlobal if provided
                var first = f.Name.FirstName();
                var last = f.Name.LastName();
                return new
                {
                    FirstName = first,
                    LastName = last,
                    Email = f.Internet.Email(first, last)
                };
            });

        return await Task.FromResult(data);
    });

    // 12) Bulk: simple orders list
    [Description("Generate a list of simple orders (id, item, quantity, price).")]
    [McpServerTool(
        Title = "Generate orders list",
        Name = "github_bogus_generate_orders_list",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<CallToolResult?> GitHubBogus_GenerateOrdersList(
        RequestContext<CallToolRequestParams> requestContext,
        [Description("How many orders")] int count = 10,
        [Description("Locale code")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null)
        => await requestContext.WithStructuredContent(async () =>
    {
        var f = CreateFaker(locale, seed);
        var orderId = 0;
        var data = Enumerable.Range(0, Math.Max(0, count))
            .Select(_ => new
            {
                OrderId = orderId++,
                Item = f.Commerce.Product(),
                Quantity = f.Random.Int(1, 10),
                Price = f.Commerce.Price(1, 200, 2)
            });

        return await Task.FromResult(JsonSerializer.Serialize(data));
    });

    // 1) Hacker phrase (great for demo data / placeholders)
    [Description("Generate a hacker phrase (adjective+noun/verb-ing phrase).")]
    [McpServerTool(
        Title = "Generate hacker phrase",
        Name = "bogus_generate_hacker_phrase",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string> Bogus_GenerateHackerPhrase(
        [Description("Optional deterministic seed")] int? seed = null) =>
        await Task.FromResult(CreateFaker("en", seed).Hacker.Phrase());

    // 2) Database column/type/engine (fake schema bits)
    [Description("Generate database artifacts: column, type, collation, engine.")]
    [McpServerTool(
        Title = "Generate database artifact",
        Name = "bogus_generate_database_artifact",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string> Bogus_GenerateDatabaseArtifact(
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker("en", seed);
        var json = JsonSerializer.Serialize(new
        {
            Column = f.Database.Column(),
            Type = f.Database.Type(),
            Collation = f.Database.Collation(),
            Engine = f.Database.Engine()
        });
        return await Task.FromResult(json);
    }

    // 3) System file/meta (filename, ext, mime, semver, version)
    [Description("Generate system file metadata and version info.")]
    [McpServerTool(
        Title = "Generate system file",
        Name = "bogus_generate_system_file",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string> Bogus_GenerateSystemFile(
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker("en", seed);
        var json = JsonSerializer.Serialize(new
        {
            FileName = f.System.FileName(),
            CommonFileExt = f.System.CommonFileExt(),
            CommonFileType = f.System.CommonFileType(),
            MimeType = f.System.MimeType(),
            Semver = f.System.Semver(),
            Version = f.System.Version().ToString()
        });
        return await Task.FromResult(json);
    }

    // 4) Finance transaction (amount, currency, type, routing numbers)
    [Description("Generate a fake finance transaction for testing.")]
    [McpServerTool(
        Title = "Generate finance transaction",
        Name = "bogus_generate_finance_transaction",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string> Bogus_GenerateFinanceTransaction(
        [Description("Minimum amount")] decimal min = 1,
        [Description("Maximum amount")] decimal max = 1000,
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker("en", seed);
        var amount = f.Finance.Amount(min, max);
        var json = JsonSerializer.Serialize(new
        {
            Transaction = new
            {
                Type = f.Finance.TransactionType(),
                Amount = amount,
                Currency = f.Finance.Currency().Code,
                Account = f.Finance.Account(),
                RoutingNumber = f.Finance.RoutingNumber(),
                IBAN = f.Finance.Iban(),
                BIC = f.Finance.Bic()
            }
        });
        return await Task.FromResult(json);
    }

    // 5) Handlebars-like string parse via Faker.Parse
    [Description("Parse a template using Bogus datasets, e.g. '{{name.lastName}}, {{name.firstName}}'.")]
    [McpServerTool(
        Title = "Parse template",
        Name = "bogus_parse_template",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string> Bogus_ParseTemplate(
        [Description("Template string with dataset handles")] string template,
        [Description("Locale code")] string? locale = "en",
        [Description("Optional deterministic seed")] int? seed = null) =>
        await Task.FromResult(CreateFaker(locale, seed).Parse(template));

    // 6) Name + job profile (with gender input)

    // 7) Recent/soon date window (useful for timelines)
    [Description("Generate a window of dates: recent/past and soon/future.")]
    [McpServerTool(
        Title = "Generate date window",
        Name = "bogus_generate_date_window",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string> Bogus_GenerateDateWindow(
        [Description("Days back for 'recent'")] int recentDays = 7,
        [Description("Days forward for 'soon'")] int soonDays = 7,
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker("en", seed);
        var json = JsonSerializer.Serialize(new
        {
            Recent = f.Date.Recent(recentDays).ToString("O"),
            Soon = f.Date.Soon(soonDays).ToString("O"),
            Between = new
            {
                Start = f.Date.Between(DateTime.UtcNow.AddDays(-recentDays), DateTime.UtcNow.AddDays(soonDays)).ToString("O"),
                End = f.Date.Between(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(soonDays + 1)).ToString("O")
            }
        });
        return await Task.FromResult(json);
    }

    // 8) Network endpoint (domain, port, URL w/ path, IPv4+port, IPv6+port)
    [Description("Generate a network endpoint: domain, ports, URLs, and IP endpoints.")]
    [McpServerTool(
        Title = "Generate network endpoint",
        Name = "bogus_generate_network_endpoint",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string> Bogus_GenerateNetworkEndpoint(
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker("en", seed);
        var port = f.Internet.Port();
        var ipv4 = f.Internet.Ip();
        var ipv6 = f.Internet.Ipv6();
        var json = JsonSerializer.Serialize(new
        {
            Domain = f.Internet.DomainName(),
            Port = port,
            Url = f.Internet.UrlWithPath(),
            IPv4Endpoint = $"{ipv4}:{port}",
            IPv6Endpoint = $"[{ipv6}]:{port}"
        });
        return await Task.FromResult(json);
    }

    // 9) Color set (hex + friendly)
    [Description("Generate a friendly color set (name and hex).")]
    [McpServerTool(
        Title = "Generate color",
        Name = "bogus_generate_color",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string> Bogus_GenerateColor(
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker("en", seed);
        var colorName = f.Commerce.Color();
        var hex = f.Random.Hexadecimal(6, string.Empty); // 6 hex chars
        var json = JsonSerializer.Serialize(new
        {
            Name = colorName,
            Hex = $"#{hex}"
        });
        return await Task.FromResult(json);
    }

    // 11) System exception with fake stacktrace (for log pipelines)
    [Description("Generate a fake Exception payload (message, stack-like text).")]
    [McpServerTool(
        Title = "Generate exception",
        Name = "bogus_generate_exception",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string> Bogus_GenerateException(
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker("en", seed);
        var ex = f.System.Exception();
        var json = JsonSerializer.Serialize(new
        {
            Type = ex.GetType().FullName,
            Message = ex.Message,
            Stack = ex.StackTrace
        });
        return await Task.FromResult(json);
    }

    // 12) Directions (cardinal/ordinal)
    [Description("Generate directions (cardinal/ordinal) useful for geo UIs.")]
    [McpServerTool(
        Title = "Generate directions",
        Name = "bogus_generate_directions",
        ReadOnly = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    public static async Task<string> Bogus_GenerateDirections(
        [Description("Optional deterministic seed")] int? seed = null)
    {
        var f = CreateFaker("en", seed);
        var json = JsonSerializer.Serialize(new
        {
            Direction = f.Address.Direction(),
            Cardinal = f.Address.CardinalDirection(),
            Ordinal = f.Address.OrdinalDirection()
        });
        return await Task.FromResult(json);
    }

}


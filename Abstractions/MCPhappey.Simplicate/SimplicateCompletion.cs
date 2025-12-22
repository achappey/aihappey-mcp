using MCPhappey.Common;
using MCPhappey.Common.Models;
using MCPhappey.Simplicate.Options;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using MCPhappey.Core.Services;
using MCPhappey.Simplicate.Extensions;
using System.Text.Json.Serialization;

namespace MCPhappey.Simplicate;

public class SimplicateCompletion(
    SimplicateOptions simplicateOptions,
    DownloadService downloadService) : IAutoCompletion
{
    public bool SupportsHost(ServerConfig serverConfig)
        => serverConfig.Server.ServerInfo.Name.StartsWith("Simplicate-");

    public async Task<Completion> GetCompletion(
     McpServer mcpServer,
     IServiceProvider serviceProvider,
     CompleteRequestParams? completeRequestParams,
     CancellationToken cancellationToken = default)
    {
        if (completeRequestParams?.Argument?.Name is not string argName || completeRequestParams.Argument.Value is not string argValue)
            return new();

        if (!completionSources.TryGetValue(argName, out var source))
            return new();

        // Use reflection to invoke the generic helper
        var sourceType = source.GetType();
        var tType = sourceType.GenericTypeArguments[0];
        var method = GetType().GetMethod(nameof(CompleteAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.MakeGenericMethod(tType);

        if (method == null)
            return new();

        var urlFactory = sourceType.GetProperty(nameof(CompletionSource<object>.UrlFactory))?.GetValue(source);
        var selector = sourceType.GetProperty(nameof(CompletionSource<object>.Selector))?.GetValue(source);

        if (method == null || urlFactory == null || selector == null)
            return new();

        var objArray = new object[]
        {
                urlFactory,
                selector,
                argValue,
                completeRequestParams!.Context?.Arguments ?? new Dictionary<string, string>(),
                mcpServer,
                serviceProvider,
                cancellationToken
        };

        var result = method.Invoke(this, objArray);

        if (result is not Task<List<string>> task)
            return new();

        var values = await task;

        return new()
        {
            Values = values
        };

    }

    // Signature now takes context:
    private async Task<List<string>> CompleteAsync<T>(
        Func<string, Dictionary<string, string>?, string> urlFactory,
        Func<T, Dictionary<string, string>?, string> selector,
        string argValue,
        Dictionary<string, string>? context, // <--- new param
        McpServer mcpServer,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var url = simplicateOptions.GetApiUrl($"/{urlFactory(argValue, context)}");
        var items = await downloadService.GetSimplicatePageAsync<T>(serviceProvider, mcpServer, url, cancellationToken);
        return items?.Data?.Take(100).Select(item => selector(item, context)).Where(a => !string.IsNullOrEmpty(a)).ToList() ?? [];
    }

    public IEnumerable<string> GetArguments(IServiceProvider serviceProvider)
    {
        return completionSources.Keys;
    }

    private readonly Dictionary<string, object> completionSources = new()
    {
        ["teamNaam"] = new CompletionSource<SimplicateNameItem>(
            (value, _) => $"hrm/team?q[name]=*{value}*&sort=name&select=name",
            (item, _) => item.Name),

        ["relatieSoort"] = new CompletionSource<SimplicateLabelItem>(
            (value, _) => $"crm/relationtype?q[label]=*{value}*&sort=label&select=label",
            (item, _) => item.Label),

        ["salesNaam"] = new CompletionSource<SimplicateSales>(
            (value, _) => $"sales/sales?q[subject]=*{value}*&sort=subject&select=subject",
            (item, _) => item.Subject),

        ["offerteOnderwerp"] = new CompletionSource<SimplicateQuote>(
            (value, _) => $"sales/quote?q[quote_subject]=*{value}*&sort=quote_subject&select=quote_subject",
            (item, _) => item.QuoteSubject),

        ["offertenummer"] = new CompletionSource<SimplicateQuote>(
            (value, _) => $"sales/quote?q[quote_number]=*{value}*&sort=quote_number&select=quote_number",
            (item, _) => item.QuoteNumber),

        ["salesBron"] = new CompletionSource<SimplicateNameItem>(
            (value, _) => $"sales/salessource?q[name]=*{value}*&sort=name&select=name",
            (item, _) => item.Name),

        ["salesReden"] = new CompletionSource<SimplicateNameItem>(
            (value, _) => $"sales/salesreason?q[name]=*{value}*&sort=name&select=name",
            (item, _) => item.Name),

        ["salesVoortgang"] = new CompletionSource<SimplicateLabelItem>(
            (value, _) => $"sales/salesprogress?q[label]=*{value}*&sort=label&select=label",
            (item, _) => item.Label),

        ["projectNaam"] = new CompletionSource<SimplicateNameItem>(
            (value, _) => $"projects/project?q[name]=*{value}*&sort=name&select=name",
            (item, _) => item.Name),

        ["projectdienstNaam"] = new CompletionSource<SimplicateNameItem>(
            (value, _) => $"projects/service?q[name]=*{value}*&sort=name&select=name",
            (item, _) => item.Name),

        ["urenType"] = new CompletionSource<SimplicateLabelItem>(
            (value, _) => $"hours/hourstype?q[label]=*{value}*&sort=label&select=label",
            (item, _) => item.Label),

        ["medewerkerNaam"] = new CompletionSource<SimplicateNameItem>(
            (value, _) => $"hrm/employee?q[name]=*{value}*&sort=name&select=name&q[is_user]=true",
            (item, _) => item.Name),

        ["brancheNaam"] = new CompletionSource<SimplicateNameItem>(
            (value, _) => $"crm/industry?q[name]=*{value}*&sort=name&select=name",
            (item, _) => item.Name),

        ["naamBedrijf"] = new CompletionSource<SimplicateNameItem>(
            (value, _) => $"crm/organization?q[name]=*{value}*&sort=name&select=name",
            (item, _) => item.Name),

        ["factuurnummer"] = new CompletionSource<SimplicateInvoiceItem>(
            (value, _) => $"invoices/invoice?q[invoice_number]={value}*&sort=invoice_number&select=invoice_number",
            (item, _) => item.InvoiceNumber),

        ["factuurStatus"] = new CompletionSource<SimplicateNameItem>(
            (value, _) => $"invoices/invoicestatus?q[name]=*{value}*&sort=name&select=name",
            (item, _) => item.Name.Replace("label_", string.Empty)),

    };

    public class SimplicateDebtorItem
    {
        [JsonPropertyName("organization")]
        public SimplicateNameItem? Organization { get; set; }

        [JsonPropertyName("person")]
        public SimplicateFullNameItem? Person { get; set; } = default!;
    }

    public class SimplicateInvoiceItem
    {
        [JsonPropertyName("invoice_number")]
        public string InvoiceNumber { get; set; } = string.Empty;
    }

    public class SimplicateFullNameItem
    {
        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;
    }

    public class SimplicateNameItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class SimplicateSales
    {
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;
    }

    public class SimplicateQuote
    {
        [JsonPropertyName("quote_subject")]
        public string QuoteSubject { get; set; } = string.Empty;

        [JsonPropertyName("quote_number")]
        public string QuoteNumber { get; set; } = string.Empty;
    }

    public class SimplicateLabelItem
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;
    }

    // Change to:
    public class CompletionSource<T>(
        Func<string, Dictionary<string, string>?, string> urlFactory,   // takes (value, context)
        Func<T, Dictionary<string, string>?, string> selector)         // takes (item, context)
    {
        public Func<string, Dictionary<string, string>?, string> UrlFactory { get; set; } = urlFactory;
        public Func<T, Dictionary<string, string>?, string> Selector { get; set; } = selector;
    }

}

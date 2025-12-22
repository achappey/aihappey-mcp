using System.Linq.Dynamic.Core;
using ModelContextProtocol.Protocol;
using MCPhappey.Common.Extensions;
using System.Text.RegularExpressions;

namespace MCPhappey.Tools.DataAnalysis;

public static partial class DataAnalysisExtensions
{
    public static CallToolResult ToDataSample(this List<IDictionary<string, object?>>? data, string url,
              int? limit = 5,
              int? skip = 0)
    {
        return new
        {
            Columns = data?.FirstOrDefault()?.Keys,
            Rows = data?
                   .Skip(skip ?? 0)
                   .Take(limit ?? 5)
                   .ToList(),
            RowCount = data?.Count()
        }.ToJsonContentBlock(url)
           .ToCallToolResult();
    }


    private static readonly Regex UnsafeCol = UnsafeColRegex();

    private static string RewriteColumnRefs(string expr) =>
        UnsafeCol.Replace(expr,
            m => $"it[\"{m.Value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"]");

    private static string Accessor(string col) =>
        AccessorRegex().IsMatch(col)
            ? col
            : $"it[\"{col.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"]";

    private static string Alias(string col)
    {
        var id = Regex.Replace(col, @"[^A-Za-z0-9_]", "_");
        return char.IsDigit(id[0]) ? "_" + id : id;
    }

    // ── main entry ───────────────────────────────────────────────────────────
    public static IReadOnlyList<dynamic> ExecuteDataQuery(
        this IEnumerable<IDictionary<string, object?>> rows,
        IDictionary<string, string>? compute = null,
        string? filter = null,
        IList<string>? groupBy = null,
        IDictionary<string, string>? aggregate = null,
        string? sort = null,
        int? limit = null,
        IList<string>? select = null)
    {
        var list = rows?.ToList() ?? throw new ArgumentNullException(nameof(rows));
        if (list.Count == 0) return [];
        string Safe(string raw) => RewriteColumnRefs(raw ?? string.Empty);

        // 1️⃣  compute
        if (compute is { Count: > 0 })
        {
            foreach (var row in list)
                foreach (var (dest, rawExpr) in compute)
                {
                    try
                    {
                        var val = new[] { row }.AsQueryable()
                                   .Select($"new({Safe(rawExpr)} as _v)").First()._v;
                        row[dest] = val;
                    }
                    catch (Exception ex) { row[dest] = $"#ERROR: {ex.Message}"; }
                }
        }

        // 2️⃣  base query
        IQueryable query = list.AsQueryable();

        // 3️⃣  filter
        if (!string.IsNullOrWhiteSpace(filter))
            query = query.Where(Safe(filter));

        // 4️⃣  group / aggregate
        if (groupBy is { Count: > 0 })
        {
            var aliasMap = groupBy.ToDictionary(c => c, Alias, StringComparer.OrdinalIgnoreCase);
            var autoAgg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (select is { Count: > 0 })
            {
                var plain = select
                    .Where(s => !s.Contains(" as ", StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var col in plain)
                    if (!groupBy.Contains(col, StringComparer.OrdinalIgnoreCase) &&
                        !(aggregate?.ContainsKey(col) ?? false))
                        autoAgg[col] = $"Max(Convert.ToDecimal({Accessor(col)}))";
            }
            if (autoAgg.Count > 0)
                aggregate = (aggregate ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                            .Concat(autoAgg)
                            .ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

            var keyExpr = "new(" + string.Join(",", groupBy.Select(c => $"{Accessor(c)} as {aliasMap[c]}")) + ")";
            var keySelect = groupBy.Select(c => $"Key.{aliasMap[c]} as {c.Split('.').Last()}");

            if (aggregate is { Count: > 0 })
            {
                var aggParts = aggregate.Select(kvp => $"{Safe(kvp.Value)} as {kvp.Key}");
                query = query.GroupBy(keyExpr)
                             .Select($"new({string.Join(",", keySelect.Concat(aggParts))})");
            }
            else
                query = query.GroupBy(keyExpr)
                             .Select($"new({string.Join(",", keySelect)})");
        }
        else if (aggregate is { Count: > 0 })
        {
            var agg = string.Join(",", aggregate.Select(kvp => $"{Safe(kvp.Value)} as {kvp.Key}"));
            query = query.Select($"new({agg})");
        }

        // 5️⃣  projection
        if (select is { Count: > 0 })
        {
            var proj = string.Join(",", select.Select(s =>
            {
                if (s.Contains(" as ", StringComparison.OrdinalIgnoreCase)) return s;

                // if the column was grouped, use its alias instead of a new dictionary lookup
                // if the column was grouped, select the *existing* key property (last segment)
                if (groupBy?.Contains(s, StringComparer.OrdinalIgnoreCase) == true)
                    return $"{s.Split('.').Last()}";

                // otherwise fall back to the original Accessor logic
                return $"{Accessor(s)} as {s.Split('.').Last()}";
            }));

            query = query.Select($"new({proj})");
        }

        // 6️⃣  sort & limit
        if (!string.IsNullOrWhiteSpace(sort))
        {
            if (groupBy is { Count: > 0 })
                foreach (var (orig, al) in groupBy.ToDictionary(c => c, Alias))
                    sort = Regex.Replace(sort, $@"\b{Regex.Escape(orig)}\b", al);
            query = query.OrderBy(sort);
        }
        if (limit is > 0) query = query.Take(limit.Value);

        // 7️⃣  materialise
        return query.ToDynamicList();
    }

    [GeneratedRegex(@"(?<!\[\"")\b(?!(Convert|Sum|Average|Count|Min|Max)\b)
          ([A-Za-z_]\w*(?:[ .-][A-Za-z0-9_]+)+)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace, "nl-NL")]
    private static partial Regex UnsafeColRegex();
    [GeneratedRegex(@"^[A-Za-z_]\w*$")]
    private static partial Regex AccessorRegex();
}

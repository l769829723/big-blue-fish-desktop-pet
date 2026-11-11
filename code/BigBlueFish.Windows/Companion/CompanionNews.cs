using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Text.Json;

namespace BigBlueFish.Windows;

internal static class CompanionNews
{
    internal const double Probability = .03;
    internal static bool ShouldSelect(double roll, bool chatty = false) =>
        roll >= 0 && roll < (chatty ? 1 : Probability);

    internal sealed record Item(string Title, string Summary, DateTimeOffset Published)
    {
        internal string Context => $"来源：科技新闻\n发布时间：{Published.ToLocalTime():yyyy-MM-dd HH:mm}\n标题：{Title}\n新闻摘要：{Summary}";
    }

    internal static Item[] ReadFeed(string xml, DateTimeOffset now)
    {
        static string? Field(XElement item, params string[] names) => item.Elements()
            .FirstOrDefault(e => names.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))?.Value;
        return XDocument.Parse(xml).Descendants()
            .Where(i => string.Equals(i.Name.LocalName, "item", StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase))
            .Select(i =>
            {
                string? published = Field(i, "pubDate", "published", "updated");
                return DateTimeOffset.TryParse(published, out var date)
                    ? new Item(PlainText(Field(i, "title"), 240),
                        PlainText(Field(i, "description", "summary", "content"), 1600), date)
                    : null;
            })
            .Where(i => i != null && i.Published <= now && now - i.Published < TimeSpan.FromDays(2))
            .Select(i => i!)
            .Where(i => i.Title.Length > 0)
            .DistinctBy(i => i.Title).Take(30).ToArray();
    }

    internal static Item[] Read(string content, DateTimeOffset now)
    {
        if (!content.TrimStart().StartsWith("{", StringComparison.Ordinal) && !content.TrimStart().StartsWith("[", StringComparison.Ordinal))
            return ReadFeed(content, now);
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)) root = data;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("articles", out var articles)) root = articles;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items)) root = items;
        if (root.ValueKind != JsonValueKind.Array) return [];
        return root.EnumerateArray().Select(x => new Item(
            PlainText(Get(x, "title"), 240), PlainText(Get(x, "summary") ?? Get(x, "description") ?? Get(x, "content"), 1600),
            DateTimeOffset.TryParse(Get(x, "published") ?? Get(x, "publishedAt") ?? Get(x, "pubDate"), out var date) ? date : now))
            .Where(x => x.Title.Length > 0 && x.Published <= now && now - x.Published < TimeSpan.FromDays(2))
            .DistinctBy(x => x.Title).Take(30).ToArray();
    }

    private static string? Get(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in item.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value.ToString();
        return null;
    }

    private static string PlainText(string? html, int limit)
    {
        var text = Regex.Replace(html ?? "", @"<\s*(script|style)\b[^>]*>[\s\S]*?</\s*\1\s*>", " ", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", " "));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= limit ? text : text[..limit];
    }
}

using System.Globalization;
using System.Text.Json;
using Sms.Modules.Academics.Contracts;

namespace Sms.Application.Services.Academics;

/// Parses CRM class-test snapshots so we can notify only newly added tests.
public static class ClassTestScheduleNotices
{
    public sealed record Item(string Key, string Title, string? Subject, DateTime? Date, string? ClassName);

    public static IReadOnlyList<Item> NewTests(PublishSnapshotResponse? previous, PublishSnapshotResponse saved)
    {
        var before = ParseUnion(previous?.DraftJson, previous?.PublishedJson)
            .Select(t => t.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ParseUnion(saved.DraftJson, saved.PublishedJson)
            .Where(t => !before.Contains(t.Key))
            .ToList();
    }

    public static IReadOnlyList<Item> ParseUnion(string? draftJson, string? publishedJson)
    {
        var map = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Parse(publishedJson).Concat(Parse(draftJson)))
            map.TryAdd(item.Key, item);
        return map.Values.ToList();
    }

    private static IReadOnlyList<Item> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var items = new List<Item>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var title = Str(el, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;

                var className = Str(el, "cls", "className", "class_name");
                var subject = Str(el, "subject");
                var date = Date(el, "date");
                var id = Str(el, "id");
                var key = string.IsNullOrWhiteSpace(id)
                    ? $"{title}|{className}|{date:yyyy-MM-dd}"
                    : id;
                items.Add(new Item(key, title, subject, date, className));
            }
            return items;
        }
    }

    private static string? Str(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var p)) continue;
            switch (p.ValueKind)
            {
                case JsonValueKind.String:
                    var s = p.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                    break;
                case JsonValueKind.Number:
                    return p.GetRawText();
            }
        }
        return null;
    }

    private static DateTime? Date(JsonElement el, string name)
    {
        var raw = Str(el, name);
        if (raw is null) return null;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d))
            return d.Date;
        return null;
    }
}

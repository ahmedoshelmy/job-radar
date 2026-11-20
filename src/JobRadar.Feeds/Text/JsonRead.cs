using System.Globalization;
using System.Text.Json;

namespace JobRadar.Feeds.Text;

/// <summary>
/// Forgiving reads over feed JSON: a missing or oddly typed field is "" / null / empty, never an exception,
/// because third-party feeds change shape without notice.
/// </summary>
internal static class JsonRead
{
    public static JsonElement Prop(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static string Str(this JsonElement e, string name) => e.Prop(name).AsString();

    public static string AsString(this JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        _ => "",
    };

    public static bool? Bool(this JsonElement e, string name) => e.Prop(name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public static decimal? Num(this JsonElement e, string name)
    {
        var v = e.Prop(name);
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d))
        {
            return d;
        }

        return v.ValueKind == JsonValueKind.String
            && decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : null;
    }

    public static IEnumerable<JsonElement> Items(this JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : [];

    public static IEnumerable<JsonElement> Items(this JsonElement e, string name) => e.Prop(name).Items();

    public static IReadOnlyList<string> Strings(this JsonElement e, string name, int max = 30) =>
        e.Items(name).Select(x => FeedText.Clean(x.AsString(), 60)).Where(x => x.Length > 0).Take(max).ToArray();

    public static IReadOnlyList<string> NonEmpty(params string?[] values) =>
        values.Select(v => FeedText.Clean(v, 60)).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

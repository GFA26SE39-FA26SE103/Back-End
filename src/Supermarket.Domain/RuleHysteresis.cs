using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supermarket.Domain;

/// <summary>
/// Dual (hysteresis) thresholds for a MonitoringRule. A level is entered at its threshold (value ≥ enter) and held until the value
/// drops below its exit threshold (value &lt; exit), so noise around one number cannot flip the level or clear/re-create the event.
/// Exits live in MonitoringRule.parameters_json (no schema change); a missing exit equals the enter threshold (single-threshold behaviour).
/// </summary>
public static class RuleHysteresis
{
    public const string WarningKey = "warningExitThreshold", CriticalKey = "criticalExitThreshold";

    /// <summary>Lenient read for runtime: anything malformed is treated as "no exit configured".</summary>
    public static (decimal? Warning, decimal? Critical) Read(string? json)
    {
        try { return Strict(json); }
        catch (DomainException) { return (null, null); }
    }

    /// <summary>Strict read for validation: a present key must be a JSON number.</summary>
    public static (decimal? Warning, decimal? Critical) Strict(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return (null, null);
            return (Number(document.RootElement, WarningKey), Number(document.RootElement, CriticalKey));
        }
        catch (JsonException) { return (null, null); }
    }

    /// <summary>Writes the explicit exits into parameters JSON; null removes the key. Unrelated parameters are preserved.</summary>
    public static string? Merge(string? json, decimal? warningExit, decimal? criticalExit)
    {
        var empty = string.IsNullOrWhiteSpace(json);
        if (warningExit is null && criticalExit is null && (empty || !HasKeys(json!))) return json;
        JsonObject root;
        try { root = empty ? new JsonObject() : JsonNode.Parse(json!) as JsonObject ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
        root.Remove(WarningKey);
        root.Remove(CriticalKey);
        if (warningExit is { } warning) root[WarningKey] = warning;
        if (criticalExit is { } critical) root[CriticalKey] = critical;
        return root.Count == 0 && empty ? null : root.ToJsonString();
    }

    /// <summary>0 ≤ warningExit ≤ warning; warning &lt; criticalExit ≤ critical; four decimals; whole numbers for people counts.</summary>
    public static void Validate(MonitoringRule rule)
    {
        var (warningExit, criticalExit) = Strict(rule.ParametersJson);
        var whole = rule.ThresholdUnit == "PEOPLE";
        if (warningExit is { } w)
            Rules.Require(w >= 0 && w <= rule.WarningThreshold && decimal.Round(w, 4) == w && (!whole || decimal.Truncate(w) == w),
                "INVALID_EXIT_THRESHOLDS", "Warning exit threshold must be between 0 and the warning threshold" + (whole ? " and a whole people count." : "."));
        if (criticalExit is { } c)
            Rules.Require(c > rule.WarningThreshold && c <= rule.CriticalThreshold && decimal.Round(c, 4) == c && (!whole || decimal.Truncate(c) == c),
                "INVALID_EXIT_THRESHOLDS", "Critical exit threshold must be above the warning threshold and not above the critical threshold"
                    + (whole ? ", as a whole people count." : "."));
    }

    private static decimal? Number(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)) throw Invalid();
        return number;
    }

    private static bool HasKeys(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && (document.RootElement.TryGetProperty(WarningKey, out _) || document.RootElement.TryGetProperty(CriticalKey, out _));
        }
        catch (JsonException) { return false; }
    }

    private static DomainException Invalid() => new("INVALID_RULE_PARAMETERS", "Exit thresholds must be numbers inside a JSON object.");
}

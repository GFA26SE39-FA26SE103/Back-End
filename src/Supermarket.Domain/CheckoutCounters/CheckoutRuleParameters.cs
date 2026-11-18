using System.Text.Json;

namespace Supermarket.Domain.CheckoutCounters;

public sealed record CheckoutRuleParameters(int MinQueue, string NoStaffedAlertLevel)
{
    public static CheckoutRuleParameters Parse(string? json)
    {
        const string code = "INVALID_CHECKOUT_PARAMETERS";
        const string message = "Checkout parameters require an object with minQueue as an integer >= 1 and noStaffedAlertLevel as WARNING or CRITICAL.";
        Rules.Require(!string.IsNullOrWhiteSpace(json) && json.Length <= 16000, code, message);
        try
        {
            using var document = JsonDocument.Parse(json!);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("minQueue", out var minimum)
                || minimum.ValueKind != JsonValueKind.Number || !minimum.TryGetInt32(out var minQueue) || minQueue < 1
                || !root.TryGetProperty("noStaffedAlertLevel", out var level)
                || level.ValueKind != JsonValueKind.String || level.GetString() is not ("WARNING" or "CRITICAL"))
                throw new DomainException(code, message);
            return new(minQueue, level.GetString()!);
        }
        catch (JsonException) { throw new DomainException(code, message); }
    }
}

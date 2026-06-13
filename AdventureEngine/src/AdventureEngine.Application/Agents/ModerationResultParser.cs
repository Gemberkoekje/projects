namespace AdventureEngine.Application.Agents;

using System.Text.Json;

public static class ModerationResultParser
{
    public static bool TryParse(string? response, out ModerationResult result)
    {
        result = ModerationResult.Safe;
        if (string.IsNullOrWhiteSpace(response))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<ModerationResponse>(
                response,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Result))
                return false;

            if (parsed.Result.Equals("SAFE", StringComparison.OrdinalIgnoreCase))
            {
                result = ModerationResult.Safe;
                return true;
            }

            if (parsed.Result.Equals("UNSAFE", StringComparison.OrdinalIgnoreCase))
            {
                var reason = string.IsNullOrWhiteSpace(parsed.Reason) ? null : parsed.Reason.Trim();
                result = new ModerationResult(false, reason);
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private sealed record ModerationResponse(string? Result, string? Reason);
}

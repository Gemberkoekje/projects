namespace AdventureEngine.Application.Agents;

public static class JsonResponseParser
{
    public static string ExtractJsonPayload(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return string.Empty;

        var trimmed = response.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOfAny(['\r', '\n']);
            if (firstNewLine >= 0)
            {
                trimmed = trimmed[(firstNewLine + 1)..].Trim();
            }

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3].Trim();
            }
        }

        if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
            return trimmed;

        var firstJsonStart = trimmed.IndexOfAny(['{', '[']);
        if (firstJsonStart < 0)
            return trimmed;

        var lastObjectEnd = trimmed.LastIndexOf('}');
        var lastArrayEnd = trimmed.LastIndexOf(']');
        var lastJsonEnd = Math.Max(lastObjectEnd, lastArrayEnd);
        if (lastJsonEnd <= firstJsonStart)
            return trimmed[firstJsonStart..];

        return trimmed[firstJsonStart..(lastJsonEnd + 1)].Trim();
    }
}

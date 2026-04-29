namespace SpaceTraders.App.Services;

public sealed class InternalApiOptions
{
    public const string SectionName = "InternalApi";

    public string BaseUrl { get; set; } = "http://localhost:5000/spacetraders/api/";

    public string ApiKey { get; set; } = string.Empty;
}

namespace FrontPage.core;

/// <summary>Whether a tool on the page is running.</summary>
public enum AppState
{
    /// <summary>Not known.</summary>
    None = 0,

    /// <summary>Every workload of the tool has at least one ready replica.</summary>
    Up = 1,

    /// <summary>At least one workload of the tool is missing or has no ready replica.</summary>
    Down = 2,
}

namespace FrontPage.WebApp;

/// <summary>Reads the cluster settings from the configuration, failing loudly when they're wrong.</summary>
internal static class ClusterSettings
{
    /// <summary>How often the cluster is read when <c>Cluster:RefreshSeconds</c> is not set.</summary>
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reads <c>Apps</c> (each page id with its <c>namespace/name</c> workloads) and
    /// <c>Cluster:SystemNamespaces</c>.
    /// </summary>
    /// <param name="configuration">The app configuration.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="InvalidOperationException">No apps are configured.</exception>
    /// <exception cref="FormatException">A workload is not of the form <c>namespace/name</c>.</exception>
    public static ClusterStatusSettings Read(IConfiguration configuration)
    {
        var apps = configuration.GetSection("Apps").GetChildren()
            .Select(app => new AppDefinition
            {
                Id = app.Key,
                Workloads = [.. app.GetChildren().Select(w => WorkloadRef.Parse(w.Value ?? string.Empty))],
            })
            .ToArray();

        if (apps.Length == 0)
        {
            throw new InvalidOperationException("Apps is empty: the page has no tools to show a status for.");
        }

        var systemNamespaces = configuration.GetSection("Cluster:SystemNamespaces").GetChildren()
            .Select(ns => ns.Value ?? string.Empty)
            .Where(ns => ns.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return new ClusterStatusSettings { Apps = apps, SystemNamespaces = systemNamespaces };
    }

    /// <summary>Reads <c>Cluster:RefreshSeconds</c>.</summary>
    /// <param name="configuration">The app configuration.</param>
    /// <returns>The refresh interval.</returns>
    public static TimeSpan RefreshInterval(IConfiguration configuration)
    {
        var seconds = configuration.GetValue("Cluster:RefreshSeconds", 0);
        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : DefaultRefreshInterval;
    }
}

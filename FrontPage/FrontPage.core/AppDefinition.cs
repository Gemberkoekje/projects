namespace FrontPage.core;

/// <summary>A tool on the page and the workloads that have to run for it to be up.</summary>
public sealed record AppDefinition
{
    /// <summary>The id the page uses (<c>data-app</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Its Deployments and StatefulSets.</summary>
    public required IReadOnlyList<WorkloadRef> Workloads { get; init; }
}

/// <summary>A Deployment or StatefulSet by namespace and name.</summary>
public sealed record WorkloadRef
{
    /// <summary>Its namespace.</summary>
    public required string Namespace { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Reads a <c>namespace/name</c> pair from the configuration.</summary>
    /// <param name="value">The pair.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="FormatException">The value is not of the form <c>namespace/name</c>.</exception>
    public static WorkloadRef Parse(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(p => p.Length > 0)
            ? new WorkloadRef { Namespace = parts[0], Name = parts[1] }
            : throw new FormatException($"'{value}' is not a workload: expected 'namespace/name'.");
    }

    /// <inheritdoc />
    public override string ToString() => $"{Namespace}/{Name}";
}

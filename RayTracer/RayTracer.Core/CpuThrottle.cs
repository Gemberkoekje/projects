namespace RayTracer;

/// <summary>
/// Controls how many worker threads the job system spawns and at what
/// OS thread priority they run.
/// </summary>
public enum CpuThrottle
{
    /// <summary>All logical cores, Normal thread priority.</summary>
    Normal,

    /// <summary>All logical cores, BelowNormal thread priority — the OS
    /// preempts workers instantly when any Normal-priority thread needs time.</summary>
    BelowNormal,

    /// <summary>All cores minus one at BelowNormal priority, leaving one
    /// core fully free for the OS and other applications.</summary>
    MinusOne,

    /// <summary>Half the logical cores at BelowNormal priority.</summary>
    Half,

    /// <summary>A single worker thread at BelowNormal priority.</summary>
    One
}

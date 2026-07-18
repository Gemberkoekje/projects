namespace RayTracer;

/// <summary>
/// The generic camera-driver seam the engine owns so the renderer never depends on
/// <em>how</em> the camera is animated (pinball-plan §7.1, Milestone E). A driver advances a
/// <see cref="Camera"/> in place each frame and flags <see cref="Dirty"/> whenever the pose
/// changed so the caller can reset accumulation / TAA history.
///
/// <para>The maze's wall-following walker (<see cref="CameraController"/>, which relocates to
/// <c>RayTracer.Maze</c> when the assembly split lands) is one implementation; pinball's
/// fixed / nudge camera is another. Both feed the same accumulate-when-still convergence loop,
/// so the driver contract is exactly "update the camera, tell me if it moved" — nothing about
/// mazes, cells or headings leaks across it.</para>
/// </summary>
public interface ICameraDriver
{
    /// <summary>
    /// Set by the driver whenever <see cref="Update"/> moves or rotates the camera, so the
    /// caller can reset the accumulation buffer. The caller clears it after handling.
    /// </summary>
    bool Dirty { get; set; }

    /// <summary>
    /// Advance the supplied <paramref name="camera"/> in place by
    /// <paramref name="deltaTime"/> seconds since the last frame.
    /// </summary>
    void Update(float deltaTime, Camera camera);
}

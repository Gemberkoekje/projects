using UnityEngine;

namespace IronFlag.Core
{
    /// <summary>
    /// A camera standing still and looking at something: where it is aimed, from which
    /// heading, at what tilt, and how far back.
    /// </summary>
    /// <remarks>
    /// The four numbers <see cref="TopDownCameraRig"/> already frames the battlefield with,
    /// except that three of them are fixed there and all four vary here - the select view's
    /// heading follows the bunker and its distance follows the shape of the viewport. Passing
    /// them as one value is what keeps <see cref="TopDownCameraRig.Park(CutawayPose)"/> from
    /// being a four-argument call whose arguments could be given in the wrong order.
    /// </remarks>
    public readonly struct CutawayPose
    {
        /// <summary>
        /// Creates a pose.
        /// </summary>
        /// <param name="focus">Point the camera is aimed at.</param>
        /// <param name="pitchDegrees">Downward tilt of the view.</param>
        /// <param name="yawDegrees">Compass heading of the view.</param>
        /// <param name="distance">Metres back along the view direction.</param>
        public CutawayPose(Vector3 focus, float pitchDegrees, float yawDegrees, float distance)
        {
            Focus = focus;
            PitchDegrees = pitchDegrees;
            YawDegrees = yawDegrees;
            Distance = distance;
        }

        /// <summary>Point the camera is aimed at.</summary>
        public Vector3 Focus { get; }

        /// <summary>Downward tilt of the view, in degrees.</summary>
        public float PitchDegrees { get; }

        /// <summary>Compass heading of the view, in degrees.</summary>
        public float YawDegrees { get; }

        /// <summary>Metres back along the view direction.</summary>
        public float Distance { get; }

        /// <summary>Where the camera itself stands, in world space.</summary>
        public Vector3 Position
            => TopDownCameraRig.SolveCameraPosition(Focus, PitchDegrees, YawDegrees, Distance);

        /// <summary>Which way the camera is turned.</summary>
        public Quaternion Rotation => TopDownCameraRig.SolveRotation(PitchDegrees, YawDegrees);
    }
}

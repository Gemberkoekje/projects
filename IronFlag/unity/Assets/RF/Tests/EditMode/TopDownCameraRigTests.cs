using NUnit.Framework;
using UnityEngine;
using IronFlag.Core;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Covers the camera placement maths: where the camera sits for a focus point, and how
    /// far ahead of a moving vehicle it looks.
    /// </summary>
    public sealed class TopDownCameraRigTests
    {
        private const float Pitch = 58.0f;
        private const float Yaw = 0.0f;
        private const float Distance = 34.0f;

        [Test]
        public void TheCameraLooksAtItsFocusPoint()
        {
            var focus = new Vector3(12.0f, 0.0f, -7.0f);

            Vector3 position = TopDownCameraRig.SolveCameraPosition(focus, Pitch, Yaw, Distance);
            Quaternion rotation = TopDownCameraRig.SolveRotation(Pitch, Yaw);

            Vector3 toFocus = (focus - position).normalized;
            Assert.That(Vector3.Dot(toFocus, rotation * Vector3.forward), Is.EqualTo(1.0f).Within(0.0001f));
        }

        [Test]
        public void TheCameraSitsAboveAndBehindTheFocus()
        {
            Vector3 focus = Vector3.zero;

            Vector3 position = TopDownCameraRig.SolveCameraPosition(focus, Pitch, Yaw, Distance);

            Assert.That(position.y, Is.GreaterThan(0.0f), "the view is from above");
            Assert.That(position.z, Is.LessThan(0.0f), "and from behind, for a yaw of zero");
            Assert.That(Vector3.Distance(position, focus), Is.EqualTo(Distance).Within(0.001f));
        }

        [Test]
        public void TheViewIsTheSameAngleWhereverItIs()
        {
            Quaternion here = TopDownCameraRig.SolveRotation(Pitch, Yaw);
            Quaternion there = TopDownCameraRig.SolveRotation(Pitch, Yaw);

            Assert.That(Quaternion.Angle(here, there), Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(here.eulerAngles.z, Is.EqualTo(0.0f).Within(0.0001f), "the horizon stays level");
        }

        [Test]
        public void TheFocusLeadsAMovingVehicle()
        {
            var position = new Vector3(0.0f, 0.0f, 0.0f);
            var velocity = new Vector3(0.0f, 0.0f, 10.0f);

            Vector3 focus = TopDownCameraRig.SolveFocus(position, velocity, 0.5f, 9.0f, 0.0f);

            Assert.That(focus.z, Is.EqualTo(5.0f).Within(0.001f));
        }

        [Test]
        public void TheLookAheadIsCapped()
        {
            var velocity = new Vector3(0.0f, 0.0f, 100.0f);

            Vector3 focus = TopDownCameraRig.SolveFocus(Vector3.zero, velocity, 0.5f, 9.0f, 0.0f);

            Assert.That(focus.z, Is.EqualTo(9.0f).Within(0.001f));
        }

        [Test]
        public void ClimbingDoesNotPushTheViewOffTheMap()
        {
            var climbing = new Vector3(0.0f, 20.0f, 0.0f);

            Vector3 focus = TopDownCameraRig.SolveFocus(Vector3.zero, climbing, 0.5f, 9.0f, 0.0f);

            Assert.That(focus.x, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(focus.z, Is.EqualTo(0.0f).Within(0.001f));
        }

        [Test]
        public void AFlyingTargetIsFollowedAtAFraction()
        {
            var flying = new Vector3(0.0f, 20.0f, 0.0f);

            Vector3 focus = TopDownCameraRig.SolveFocus(flying, Vector3.zero, 0.0f, 0.0f, 0.35f);

            Assert.That(focus.y, Is.EqualTo(7.0f).Within(0.001f));
        }
    }
}

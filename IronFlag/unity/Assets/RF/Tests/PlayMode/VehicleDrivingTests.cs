using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Core;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// Drives the vehicles for real: rigidbodies, gravity, fixed steps and all.
    /// </summary>
    /// <remarks>
    /// The movement maths is covered without a scene in the edit-mode tests. What these add
    /// is the wiring between that maths and the physics engine - that the velocity actually
    /// reaches the rigidbody, that the helicopter holds its altitude against gravity being
    /// off, and that the camera ends up looking at the thing it was told to follow.
    /// Vehicles are assembled here rather than loaded from the prefabs so that a failure
    /// means the code is wrong, not the asset; the prefabs have their own tests.
    /// </remarks>
    public sealed class VehicleDrivingTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject item in spawned)
            {
                if (item != null)
                {
                    Object.Destroy(item);
                }
            }

            spawned.Clear();
        }

        [UnityTest]
        public IEnumerator FullThrottleDrivesTheJeepForwards()
        {
            CreateGround();
            GroundVehicle jeep = CreateGroundVehicle(VehicleKind.Jeep, Vector3.zero);

            yield return Drive(jeep, new VehicleInput(Vector2.up, Vector2.zero, 0.0f), 1.5f);

            Assert.That(jeep.transform.position.z, Is.GreaterThan(10.0f), "the jeep did not move");
            Assert.That(Mathf.Abs(jeep.transform.position.x), Is.LessThan(0.5f), "it wandered sideways");
            Assert.That(jeep.ForwardSpeed, Is.GreaterThan(10.0f));
        }

        [UnityTest]
        public IEnumerator TheJeepCannotTurnOnTheSpot()
        {
            CreateGround();
            GroundVehicle jeep = CreateGroundVehicle(VehicleKind.Jeep, Vector3.zero);

            yield return Drive(jeep, new VehicleInput(Vector2.right, Vector2.zero, 0.0f), 1.0f);

            Assert.That(Mathf.DeltaAngle(0.0f, jeep.transform.eulerAngles.y), Is.EqualTo(0.0f).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator TheTankTurnsOnTheSpot()
        {
            CreateGround();
            GroundVehicle tank = CreateGroundVehicle(VehicleKind.Tank, Vector3.zero);
            Vector3 start = tank.transform.position;

            yield return Drive(tank, new VehicleInput(Vector2.right, Vector2.zero, 0.0f), 1.0f);

            float turned = Mathf.DeltaAngle(0.0f, tank.transform.eulerAngles.y);
            Assert.That(turned, Is.GreaterThan(40.0f), "the tank barely moved its hull");
            Assert.That(
                Vector3.Distance(HorizontalOnly(start), HorizontalOnly(tank.transform.position)),
                Is.LessThan(0.5f),
                "the tank drifted while pivoting");
        }

        [UnityTest]
        public IEnumerator ReleasingTheControlsBringsAVehicleToAStop()
        {
            CreateGround();
            GroundVehicle jeep = CreateGroundVehicle(VehicleKind.Jeep, Vector3.zero);

            yield return Drive(jeep, new VehicleInput(Vector2.up, Vector2.zero, 0.0f), 1.0f);
            jeep.ReleaseControls();
            yield return Wait(2.0f);

            Assert.That(jeep.ForwardSpeed, Is.EqualTo(0.0f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator TheHelicopterClimbsAndThenHoldsItsAltitude()
        {
            CreateGround();
            Helicopter helicopter = CreateHelicopter(new Vector3(0.0f, 4.0f, 0.0f));

            yield return Drive(helicopter, new VehicleInput(Vector2.zero, Vector2.zero, 1.0f), 1.0f);
            float climbed = helicopter.transform.position.y;

            helicopter.ReleaseControls();
            yield return Wait(1.5f);

            Assert.That(climbed, Is.GreaterThan(8.0f), "the helicopter did not climb");
            Assert.That(helicopter.transform.position.y, Is.EqualTo(helicopter.Altitude).Within(0.1f));
            Assert.That(helicopter.transform.position.y, Is.GreaterThan(climbed - 0.5f), "it sank");
        }

        [UnityTest]
        public IEnumerator TheHelicopterIgnoresTheGroundBelowIt()
        {
            CreateGround();
            Helicopter helicopter = CreateHelicopter(new Vector3(0.0f, 10.0f, 0.0f));

            yield return Drive(helicopter, new VehicleInput(Vector2.up, Vector2.zero, 0.0f), 1.5f);

            Assert.That(helicopter.transform.position.z, Is.GreaterThan(8.0f), "it did not fly forwards");
            Assert.That(helicopter.transform.position.y, Is.EqualTo(10.0f).Within(0.2f), "it did not stay level");
        }

        [UnityTest]
        public IEnumerator TheCameraEndsUpLookingAtWhatItFollows()
        {
            CreateGround();
            GroundVehicle jeep = CreateGroundVehicle(VehicleKind.Jeep, new Vector3(20.0f, 0.0f, -8.0f));
            TopDownCameraRig rig = CreateCameraRig();

            rig.SetTarget(jeep, true);
            yield return null;

            Vector3 toVehicle = (jeep.transform.position - rig.transform.position).normalized;
            Assert.That(
                Vector3.Dot(toVehicle, rig.transform.forward), Is.GreaterThan(0.98f), "it is looking away");
            Assert.That(rig.transform.position.y, Is.GreaterThan(jeep.transform.position.y));
        }

        [UnityTest]
        public IEnumerator TheCameraCatchesUpWithAMovingVehicle()
        {
            CreateGround();
            GroundVehicle jeep = CreateGroundVehicle(VehicleKind.Jeep, Vector3.zero);
            TopDownCameraRig rig = CreateCameraRig();
            rig.SetTarget(jeep, true);

            yield return Drive(jeep, new VehicleInput(Vector2.up, Vector2.zero, 0.0f), 2.0f);

            float behind = jeep.transform.position.z - rig.transform.position.z;
            Assert.That(jeep.transform.position.z, Is.GreaterThan(10.0f), "the jeep never left the line");
            Assert.That(behind, Is.GreaterThan(0.0f), "the camera overtook the jeep");
            Assert.That(behind, Is.LessThan(40.0f), "the camera fell behind");
        }

        private static Vector3 HorizontalOnly(Vector3 value) => new Vector3(value.x, 0.0f, value.z);

        private void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Test Ground";
            ground.transform.localScale = new Vector3(400.0f, 1.0f, 400.0f);
            ground.transform.position = new Vector3(0.0f, -0.5f, 0.0f);
            spawned.Add(ground);
        }

        private GroundVehicle CreateGroundVehicle(VehicleKind kind, Vector3 position)
            => Create<GroundVehicle>(kind, position + new Vector3(0.0f, 0.05f, 0.0f));

        private Helicopter CreateHelicopter(Vector3 position)
        {
            Helicopter helicopter = Create<Helicopter>(VehicleKind.Helicopter, position);

            // The cosmetic tilt goes on a child, exactly as the prefab builds it: tilting
            // the root would tilt the collider with it.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(helicopter.transform, false);
            helicopter.ConfigureFlight(new FlightTuning(), visual.transform);
            return helicopter;
        }

        /// <summary>
        /// Assembles the smallest thing that counts as a vehicle: a body, a box, a controller.
        /// </summary>
        /// <typeparam name="T">Controller to attach.</typeparam>
        /// <param name="kind">Vehicle whose tuning to drive with.</param>
        /// <param name="position">Where to place it.</param>
        /// <returns>The controller, already awake.</returns>
        /// <remarks>
        /// Built while inactive so the tuning is in place before the controller wakes and
        /// applies it to the rigidbody - the same ordering the prefab gets for free.
        /// </remarks>
        private T Create<T>(VehicleKind kind, Vector3 position)
            where T : VehicleController
        {
            var host = new GameObject(kind.ToString());
            host.SetActive(false);
            host.transform.position = position;

            var box = host.AddComponent<BoxCollider>();
            box.size = new Vector3(1.8f, 1.6f, 4.0f);
            box.center = new Vector3(0.0f, 0.8f, 0.0f);

            host.AddComponent<Rigidbody>();
            T controller = host.AddComponent<T>();
            controller.Configure(kind, VehicleTuning.For(kind));

            host.SetActive(true);
            spawned.Add(host);
            return controller;
        }

        private TopDownCameraRig CreateCameraRig()
        {
            var host = new GameObject("Test Camera", typeof(Camera));
            spawned.Add(host);
            return host.AddComponent<TopDownCameraRig>();
        }

        private static IEnumerator Drive(VehicleController vehicle, VehicleInput input, float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                vehicle.SetInput(input);
                yield return new WaitForFixedUpdate();
            }
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                yield return new WaitForFixedUpdate();
            }
        }
    }
}

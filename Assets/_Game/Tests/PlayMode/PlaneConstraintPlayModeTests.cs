using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using JetFighter.Player;
using JetFighter.Physics;

namespace JetFighter.Tests.PlayMode
{
    /// <summary>
    /// The cell's acceptance criterion: world-Z never changes under any input
    /// combination, X and Y keep full physics response, and there is no
    /// jitter over a soak.
    ///
    /// The jet from the previous branch is used unmodified. That is the point
    /// of having built it unconstrained first -- if the feel changes now, the
    /// constraint is the only thing that could have changed it.
    /// </summary>
    public class PlaneConstraintPlayModeTests
    {
        private const float Epsilon = 1e-4f;

        private GameObject jet;
        private JetController controller;
        private PlaneConstraint constraint;
        private Rigidbody body;
        private JetFlightConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<JetFlightConfig>();
            config.maxSpeed = 10f;
            config.acceleration = 40f;
            config.linearDrag = 2f;
            config.angularDrag = 4f;
            config.bankAngleMax = 30f;
            config.bankResponsiveness = 8f;

            jet = new GameObject("Jet");
            jet.transform.position = new Vector3(0f, 0f, 7f);
            body = jet.AddComponent<Rigidbody>();
            controller = jet.AddComponent<JetController>();
            controller.Config = config;
            controller.ApplyConfigToBody();
            constraint = jet.AddComponent<PlaneConstraint>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(jet);
            Object.DestroyImmediate(config);
        }

        private IEnumerator Tick(int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator ThePlaneIsCapturedFromTheSpawnPosition()
        {
            yield return null;
            Assert.AreEqual(7f, constraint.LockedValue, Epsilon,
                "the jet was pulled to the origin instead of holding where it spawned");
        }

        [UnityTest]
        public IEnumerator DiagonalInputNeverLeavesThePlane()
        {
            controller.inputVector = new Vector2(1f, 1f);
            yield return Tick(300);
            Assert.LessOrEqual(constraint.DeviationFrom(body.position), Epsilon);
        }

        [UnityTest]
        public IEnumerator AnExternalImpulseAlongTheLockedAxisIsAbsorbed()
        {
            // A collision or an explosion will do exactly this. The clamp has
            // to hold against forces the input system never produced.
            body.AddForce(new Vector3(0f, 0f, 500f), ForceMode.Impulse);
            yield return Tick(60);
            Assert.LessOrEqual(constraint.DeviationFrom(body.position), Epsilon);
            Assert.AreEqual(0f, body.linearVelocity.z, Epsilon,
                "velocity on the locked axis survived, so the body fights the clamp every tick");
        }

        [UnityTest]
        public IEnumerator TheFreeAxesKeepFullPhysicsResponse()
        {
            // The failure this guards is a constraint that "works" by killing
            // the flight model along with the locked axis.
            controller.inputVector = new Vector2(1f, 1f);
            yield return Tick(180);
            Assert.Greater(body.linearVelocity.x, 0.5f, "X lost its physics response");
            Assert.Greater(body.linearVelocity.y, 0.5f, "Y lost its physics response");
            Assert.Less(controller.BankAngle, -1f, "banking stopped working under the constraint");
        }

        [UnityTest]
        public IEnumerator ASoakUnderChangingInputStaysWithinEpsilon()
        {
            // 900 ticks is 18 seconds of fixed time. Drift from a per-tick
            // rounding error would accumulate; a one-shot clamp would not
            // catch it.
            float worst = 0f;
            for (int i = 0; i < 900; i++)
            {
                float phase = i * 0.05f;
                controller.inputVector = new Vector2(Mathf.Sin(phase), Mathf.Cos(phase * 0.7f));
                yield return new WaitForFixedUpdate();
                worst = Mathf.Max(worst, constraint.DeviationFrom(body.position));
            }
            Assert.LessOrEqual(worst, Epsilon, $"worst deviation over the soak was {worst}");
        }

        [UnityTest]
        public IEnumerator ThePlaneHoldsExactlyRatherThanOscillating()
        {
            // Jitter is the classic symptom of clamping position without
            // clearing velocity: the body is dragged back every tick and
            // pushes out again in between. Sampling the exact value each tick
            // is what distinguishes "held" from "corrected".
            controller.inputVector = new Vector2(1f, -1f);
            for (int i = 0; i < 240; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(constraint.LockedValue, body.position.z, Epsilon,
                    $"tick {i} left the plane");
            }
        }

        [UnityTest]
        public IEnumerator LockingADifferentAxisWorksToo()
        {
            // ADR-001 chose Z for Phase 1. The component is not built around
            // that choice, and Phase 2 may want another plane.
            //
            // Built inactive on purpose: AddComponent runs Awake immediately,
            // and Awake is where the plane is captured -- so the axis has to
            // be set before the object is enabled, not after.
            var other = new GameObject("XLockedJet");
            other.SetActive(false);
            other.transform.position = new Vector3(3f, 0f, 0f);
            var otherBody = other.AddComponent<Rigidbody>();
            var otherController = other.AddComponent<JetController>();
            otherController.Config = config;
            var xLock = other.AddComponent<PlaneConstraint>();
            xLock.LockedAxis = PlaneConstraint.Axis.X;
            other.SetActive(true);
            yield return null;

            Assert.AreEqual(3f, xLock.LockedValue, Epsilon, "the X plane was not captured");
            otherController.inputVector = new Vector2(1f, 1f);
            yield return Tick(120);
            Assert.LessOrEqual(xLock.DeviationFrom(otherBody.position), Epsilon);
            Assert.Greater(otherBody.linearVelocity.y, 0.5f, "Y should still be free");

            Object.DestroyImmediate(other);
        }
    }
}

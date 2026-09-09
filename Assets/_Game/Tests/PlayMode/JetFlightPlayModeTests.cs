using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using JetFighter.Player;

namespace JetFighter.Tests.PlayMode
{
    /// <summary>
    /// The flight model under a real physics tick, unconstrained.
    ///
    /// This is the branch's acceptance criterion: visible inertia, drag and
    /// banking in free 3D space, with no PlaneConstraint applied. Proving it
    /// here means that when the constraint lands in the next branch and the
    /// feel changes, the constraint is the only thing that could have caused
    /// it. Note the deliberate absence of any Z assertion -- nothing locks Z
    /// yet, and asserting it would pass for the wrong reason.
    /// </summary>
    public class JetFlightPlayModeTests
    {
        private GameObject jet;
        private JetController controller;
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
            body = jet.AddComponent<Rigidbody>();
            controller = jet.AddComponent<JetController>();
            controller.Config = config;
            controller.ApplyConfigToBody();
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
        public IEnumerator TheConfigDrivesTheRigidbody()
        {
            yield return null;
            Assert.AreEqual(config.linearDrag, body.linearDamping, 1e-3f);
            Assert.AreEqual(config.angularDrag, body.angularDamping, 1e-3f);
            Assert.IsFalse(body.useGravity, "a jet on rails must not sag under gravity");
        }

        [UnityTest]
        public IEnumerator HoldingRightBuildsSpeedUpToTheCap()
        {
            controller.inputVector = Vector2.right;
            yield return Tick(180);

            Assert.Greater(body.linearVelocity.x, 0f, "the jet never moved");
            Assert.LessOrEqual(body.linearVelocity.magnitude, config.maxSpeed * 1.05f,
                "speed overshot the configured cap");
        }

        [UnityTest]
        public IEnumerator ReleasingTheStickCoastsRatherThanStopping()
        {
            controller.inputVector = Vector2.right;
            yield return Tick(120);
            float cruising = body.linearVelocity.x;

            controller.inputVector = Vector2.zero;
            yield return Tick(1);
            float justAfter = body.linearVelocity.x;

            Assert.Greater(justAfter, 0f, "the jet stopped dead -- that is not inertia");
            Assert.Less(justAfter, cruising, "drag is not slowing the jet at all");

            yield return Tick(240);
            Assert.Less(body.linearVelocity.x, justAfter, "the jet never settles");
        }

        [UnityTest]
        public IEnumerator TheJetIsUnconstrainedOnEveryAxisInThisBranch()
        {
            // Phase 1 branch 2 runs before PlaneConstraint on purpose. A jet
            // that already could not leave the plane would make the next
            // branch untestable -- its own test would pass before it existed.
            controller.inputVector = Vector2.up;
            yield return Tick(120);
            Assert.Greater(body.linearVelocity.y, 0f);

            body.linearVelocity = new Vector3(0f, 0f, 5f);
            yield return Tick(1);
            Assert.Greater(body.linearVelocity.z, 0f,
                "something is already clamping Z; the constraint branch would prove nothing");
        }

        [UnityTest]
        public IEnumerator BankingFollowsLateralMotionAndReturnsToLevel()
        {
            controller.inputVector = Vector2.right;
            yield return Tick(120);
            Assert.Less(controller.BankAngle, -1f, "the jet is not banking into a right turn");

            controller.inputVector = Vector2.left;
            yield return Tick(240);
            Assert.Greater(controller.BankAngle, 1f, "the bank did not follow the turn back");
        }

        [UnityTest]
        public IEnumerator BankingRotatesTheVisualNotTheRigidbody()
        {
            // The physics body stays axis-aligned so collision response stays
            // predictable; only the model tilts.
            controller.inputVector = Vector2.right;
            yield return Tick(120);
            Assert.AreEqual(0f, Quaternion.Angle(jet.transform.rotation, Quaternion.identity), 0.5f,
                "the Rigidbody itself rolled");
        }

        [UnityTest]
        public IEnumerator AControllerWithNoConfigDoesNothingRatherThanThrow()
        {
            controller.Config = null;
            controller.inputVector = Vector2.right;
            yield return Tick(10);
            Assert.AreEqual(0f, body.linearVelocity.magnitude, 1e-3f);
        }
    }
}

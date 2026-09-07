using NUnit.Framework;
using UnityEngine;
using JetFighter.Player;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The flight model's arithmetic, pinned without a physics tick.
    ///
    /// These cover what a playtest cannot: that a diagonal is not faster than
    /// a cardinal push, that the bank leans into the turn rather than away
    /// from it, and that the same config behaves identically at 30fps and
    /// 60fps. Feel itself is judged on a device -- that half is a capture, not
    /// an assertion (docs/PHASE1_TECHNICAL_SPEC.md section 4).
    /// </summary>
    public class JetFlightModelTests
    {
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
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
        }

        [Test]
        public void NoInput_AtRest_ProducesNoAcceleration()
        {
            Assert.AreEqual(0f, JetController.ComputeAcceleration(
                Vector2.zero, Vector2.zero, config).magnitude, 1e-4f);
        }

        [Test]
        public void NoInput_WhileMoving_DoesNotBrake()
        {
            // Releasing the stick must coast on drag. Accelerating back toward
            // zero here would read as a handbrake and kill the inertia the
            // whole cycle exists to get right.
            Vector3 a = JetController.ComputeAcceleration(Vector2.zero, new Vector2(8f, 0f), config);
            Assert.LessOrEqual(a.x, 0f);
            Assert.AreEqual(config.acceleration, a.magnitude, 1e-3f,
                "the coast-back is capped at the configured acceleration");
        }

        [Test]
        public void FullInput_FromRest_AcceleratesTowardTheInput()
        {
            Vector3 a = JetController.ComputeAcceleration(Vector2.right, Vector2.zero, config);
            Assert.Greater(a.x, 0f);
            Assert.AreEqual(0f, a.y, 1e-4f);
            Assert.AreEqual(0f, a.z, 1e-4f, "the flight model never accelerates on the locked axis");
        }

        [Test]
        public void AtTargetVelocity_TheJetStopsAccelerating()
        {
            Vector3 a = JetController.ComputeAcceleration(
                Vector2.right, new Vector2(config.maxSpeed, 0f), config);
            Assert.AreEqual(0f, a.magnitude, 1e-3f);
        }

        [Test]
        public void ADiagonalIsNotFasterThanACardinalPush()
        {
            // Vector2.one has magnitude 1.41; unclamped, holding a diagonal
            // would be 41% faster than holding right. Classic and invisible
            // until someone speedruns the game sideways.
            Vector3 diagonal = JetController.ComputeAcceleration(Vector2.one, Vector2.zero, config);
            Vector3 cardinal = JetController.ComputeAcceleration(Vector2.right, Vector2.zero, config);
            Assert.AreEqual(cardinal.magnitude, diagonal.magnitude, 1e-3f);
        }

        [Test]
        public void AccelerationIsCappedByTheConfig()
        {
            Vector3 a = JetController.ComputeAcceleration(
                Vector2.right, new Vector2(-500f, 0f), config);
            Assert.LessOrEqual(a.magnitude, config.acceleration + 1e-3f);
        }

        [Test]
        public void ANullConfigIsInertRatherThanAnException()
        {
            Assert.AreEqual(Vector3.zero, JetController.ComputeAcceleration(Vector2.one, Vector2.zero, null));
            Assert.AreEqual(0f, JetController.ComputeTargetBankAngle(5f, null));
        }

        [Test]
        public void TheJetBanksIntoTheTurnNotAwayFromIt()
        {
            float rightward = JetController.ComputeTargetBankAngle(config.maxSpeed, config);
            float leftward = JetController.ComputeTargetBankAngle(-config.maxSpeed, config);
            Assert.AreEqual(-config.bankAngleMax, rightward, 1e-3f);
            Assert.AreEqual(config.bankAngleMax, leftward, 1e-3f);
        }

        [Test]
        public void BankAngleIsClampedBeyondMaxSpeed()
        {
            Assert.AreEqual(-config.bankAngleMax,
                JetController.ComputeTargetBankAngle(config.maxSpeed * 12f, config), 1e-3f);
        }

        [Test]
        public void LevelFlightBanksLevel()
        {
            Assert.AreEqual(0f, JetController.ComputeTargetBankAngle(0f, config), 1e-4f);
        }

        [Test]
        public void BankingConvergesOnItsTarget()
        {
            float angle = 0f;
            for (int i = 0; i < 600; i++)
            {
                angle = JetController.StepTowardBank(angle, -30f, config.bankResponsiveness, 1f / 60f);
            }
            Assert.AreEqual(-30f, angle, 0.01f);
        }

        [Test]
        public void BankingNeverOvershootsItsTarget()
        {
            float angle = JetController.StepTowardBank(0f, -30f, 1000f, 1f);
            Assert.GreaterOrEqual(angle, -30f);
        }

        [Test]
        public void BankingIsFrameRateIndependent()
        {
            // The reason StepTowardBank uses exponential decay rather than
            // Lerp(current, target, responsiveness * dt): this project ships a
            // 30fps low tier, and the naive form banks visibly differently
            // there. Same elapsed second, two tick rates, same angle.
            float at60 = 0f;
            for (int i = 0; i < 60; i++)
            {
                at60 = JetController.StepTowardBank(at60, -30f, config.bankResponsiveness, 1f / 60f);
            }
            float at30 = 0f;
            for (int i = 0; i < 30; i++)
            {
                at30 = JetController.StepTowardBank(at30, -30f, config.bankResponsiveness, 1f / 30f);
            }
            Assert.AreEqual(at60, at30, 0.25f);
        }

        [Test]
        public void ANonPositiveStepIsANoOp()
        {
            Assert.AreEqual(5f, JetController.StepTowardBank(5f, -30f, 8f, 0f));
            Assert.AreEqual(5f, JetController.StepTowardBank(5f, -30f, 0f, 1f / 60f));
        }
    }
}

using NUnit.Framework;
using UnityEngine;
using JetFighter.Player;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// Techniques JetFlightModelTests deliberately does not use.
    ///
    /// That file pins named cases -- the diagonal, the bank direction, the
    /// 30-vs-60fps equivalence -- chosen because a human worked out that each
    /// one could go wrong. This file covers the cases nobody worked out:
    ///
    ///   differential -- check the implementation against an independent
    ///                   closed form, not against a number someone recorded
    ///   combinatorial -- sweep the input space on a grid rather than at points
    ///   metamorphic   -- assert relations between two runs where no single
    ///                    correct answer can be written down
    ///   fuzz          -- seeded random input, asserting only invariants
    ///   boundary      -- the values where a clamp changes its mind
    ///
    /// Every randomised test here is seeded from a constant. An unseeded
    /// failure cannot be replayed, and a failure that cannot be replayed never
    /// becomes a regression test.
    /// </summary>
    public class JetFlightAdvancedTests
    {
        private const int Seed = 20260920;
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
            UnityEngine.Object.DestroyImmediate(config);
        }

        // --- differential: an independent oracle, not a recorded number ------

        /// <summary>
        /// Repeated exponential decay has a closed form. One step is
        /// a' = a + (target - a) * (1 - e^(-k*dt)), so after n steps from zero
        /// the angle is exactly target * (1 - e^(-k*dt*n)).
        ///
        /// Checking against that rather than against a golden value is what
        /// makes this a real test of the implementation: a recorded golden
        /// would have been produced by the same code and would agree with it
        /// even if the decay law itself were wrong.
        /// </summary>
        [Test]
        public void SteppedBankingMatchesTheClosedFormOfItsDecayLaw(
            [Values(1f / 120f, 1f / 60f, 1f / 30f)] float deltaTime,
            [Values(1f, 4f, 8f, 25f)] float responsiveness,
            [Values(10, 60, 300)] int steps)
        {
            const float target = -30f;

            float stepped = 0f;
            for (int i = 0; i < steps; i++)
            {
                stepped = JetController.StepTowardBank(stepped, target, responsiveness, deltaTime);
            }

            // Tolerance is 0.01 degrees, not 1e-3: the left side accumulates float
            // error over up to 300 steps, and pinning it tighter than the
            // arithmetic can hold would make this fail for reasons unrelated to
            // the decay law it is checking.
            float closedForm = target * (1f - Mathf.Exp(-responsiveness * deltaTime * steps));
            Assert.AreEqual(closedForm, stepped, 0.01f,
                $"dt={deltaTime} k={responsiveness} n={steps}: stepping diverged from the decay law");
        }

        /// <summary>
        /// The same property stated as an equivalence rather than a formula:
        /// the elapsed time is what determines the angle, not how it was
        /// sliced. This is the frame-rate independence guarantee generalised
        /// off the two rates JetFlightModelTests happens to name.
        /// </summary>
        [Test]
        public void TheBankAfterOneSecondIsTheSameAtEveryTickRate(
            [Values(15, 24, 30, 60, 90, 120, 240)] int ticksPerSecond)
        {
            float angle = 0f;
            for (int i = 0; i < ticksPerSecond; i++)
            {
                angle = JetController.StepTowardBank(angle, -30f, config.bankResponsiveness, 1f / ticksPerSecond);
            }

            float exact = -30f * (1f - Mathf.Exp(-config.bankResponsiveness));
            Assert.AreEqual(exact, angle, 0.05f, $"{ticksPerSecond}fps disagrees after one second");
        }

        // --- combinatorial: a grid, not a handful of points -------------------

        /// <summary>
        /// The clamp-to-unit-circle guarantee across the whole stick, not just
        /// at the diagonal. NUnit expands [Values] combinatorially, so this is
        /// 11 x 11 = 121 stick positions.
        /// </summary>
        [Test]
        public void AccelerationNeverExceedsTheConfiguredCapAnywhereOnTheStick(
            [Range(-1f, 1f, 0.2f)] float x,
            [Range(-1f, 1f, 0.2f)] float y)
        {
            Vector3 a = JetController.ComputeAcceleration(new Vector2(x, y), Vector2.zero, config);
            Assert.LessOrEqual(a.magnitude, config.acceleration + 1e-3f,
                $"input ({x}, {y}) produced {a.magnitude}");
            Assert.AreEqual(0f, a.z, 1e-6f, "the flight model never accelerates on the locked axis");
        }

        /// <summary>
        /// Two inputs of equal magnitude must produce equal acceleration
        /// magnitude regardless of direction. A stick that is faster at some
        /// angles than others is the diagonal bug in a subtler form.
        /// </summary>
        [Test]
        public void SpeedIsIsotropicAroundTheStick([Range(0f, 350f, 10f)] float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            Vector2 input = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector3 a = JetController.ComputeAcceleration(input, Vector2.zero, config);
            Assert.AreEqual(config.acceleration, a.magnitude, 1e-3f,
                $"a full push at {degrees} degrees is not the same strength as at 0");
        }

        // --- metamorphic: relations between runs ------------------------------

        /// <summary>
        /// Mirroring the input mirrors the output. No oracle needed: whatever
        /// the correct acceleration for some input is, the correct
        /// acceleration for its mirror is that value mirrored.
        /// </summary>
        [Test]
        public void MirroringTheInputMirrorsTheAcceleration(
            [Range(-1f, 1f, 0.25f)] float x,
            [Range(-1f, 1f, 0.25f)] float y)
        {
            Vector3 forward = JetController.ComputeAcceleration(new Vector2(x, y), Vector2.zero, config);
            Vector3 mirrored = JetController.ComputeAcceleration(new Vector2(-x, -y), Vector2.zero, config);
            Assert.AreEqual(-forward.x, mirrored.x, 1e-4f);
            Assert.AreEqual(-forward.y, mirrored.y, 1e-4f);
        }

        [Test]
        public void MirroringLateralMotionMirrorsTheBank([Range(0f, 20f, 2.5f)] float speed)
        {
            Assert.AreEqual(
                -JetController.ComputeTargetBankAngle(speed, config),
                JetController.ComputeTargetBankAngle(-speed, config),
                1e-4f);
        }

        /// <summary>
        /// Going faster sideways may never bank you less. Monotonicity is the
        /// relation; the actual angle at any given speed is a tuning decision
        /// no test should pin.
        /// </summary>
        [Test]
        public void BankMagnitudeNeverDecreasesAsLateralSpeedRises()
        {
            float previous = 0f;
            for (float speed = 0f; speed <= config.maxSpeed * 2f; speed += 0.25f)
            {
                float current = Mathf.Abs(JetController.ComputeTargetBankAngle(speed, config));
                Assert.GreaterOrEqual(current, previous - 1e-4f,
                    $"bank fell from {previous} to {current} when speed rose to {speed}");
                previous = current;
            }
        }

        /// <summary>
        /// Stepping toward a target must never move away from it, and never
        /// past it. Both are the classic failures of a hand-rolled smoothing
        /// term, and both are invisible in a single-step test.
        /// </summary>
        [Test]
        public void SteppingAlwaysClosesTheGapAndNeverOvershoots(
            [Values(-30f, -7.5f, 0f, 7.5f, 30f)] float start,
            [Values(-30f, 0f, 30f)] float target,
            [Values(1f / 120f, 1f / 30f, 0.5f)] float deltaTime)
        {
            float next = JetController.StepTowardBank(start, target, config.bankResponsiveness, deltaTime);
            Assert.LessOrEqual(Mathf.Abs(target - next), Mathf.Abs(target - start) + 1e-4f,
                "the step moved away from the target");
            Assert.GreaterOrEqual((target - next) * (target - start), -1e-4f,
                "the step crossed the target -- the gap changed sign");
        }

        // --- boundary: where a clamp changes its mind -------------------------

        [Test]
        public void TheSpeedClampEngagesExactlyAtMaxSpeed(
            [Values(0.5f, 0.9f, 0.999f, 1f, 1.001f, 1.5f, 40f)] float fractionOfMax)
        {
            float lateral = config.maxSpeed * fractionOfMax;
            float bank = Mathf.Abs(JetController.ComputeTargetBankAngle(lateral, config));
            if (fractionOfMax >= 1f)
            {
                Assert.AreEqual(config.bankAngleMax, bank, 1e-3f, "the clamp did not hold past max speed");
            }
            else
            {
                Assert.Less(bank, config.bankAngleMax, "the clamp engaged before max speed");
            }
        }

        [Test]
        public void ADegenerateStepIsAlwaysANoOp(
            [Values(0f, -0f, -1f / 60f, 1f / 60f)] float deltaTime,
            [Values(0f, -1f, 8f)] float responsiveness)
        {
            float result = JetController.StepTowardBank(5f, -30f, responsiveness, deltaTime);
            if (deltaTime <= 0f || responsiveness <= 0f)
            {
                Assert.AreEqual(5f, result, "a non-positive dt or responsiveness must not move the angle");
            }
            else
            {
                Assert.AreNotEqual(5f, result);
            }
        }

        // --- fuzz: seeded random input, invariants only -----------------------

        /// <summary>
        /// 20,000 random stick positions and velocities. Nothing here asserts a
        /// value -- only that the model stays inside the envelope it promises.
        /// The seed is a constant so a failure printed by CI can be reproduced
        /// exactly, which is the same reason TestOp.seed is required by the
        /// schema for randomised suites.
        /// </summary>
        [Test]
        public void TheModelStaysInsideItsEnvelopeUnderRandomInput()
        {
            var random = new System.Random(Seed);
            float Next(float min, float max) => (float)(random.NextDouble() * (max - min) + min);

            for (int i = 0; i < 20000; i++)
            {
                var input = new Vector2(Next(-5f, 5f), Next(-5f, 5f));
                var velocity = new Vector2(Next(-500f, 500f), Next(-500f, 500f));

                Vector3 a = JetController.ComputeAcceleration(input, velocity, config);

                Assert.IsFalse(float.IsNaN(a.x) || float.IsNaN(a.y) || float.IsNaN(a.z),
                    $"NaN at iteration {i}: input={input} velocity={velocity}");
                Assert.IsFalse(float.IsInfinity(a.magnitude),
                    $"infinite acceleration at iteration {i}: input={input} velocity={velocity}");
                Assert.LessOrEqual(a.magnitude, config.acceleration + 1e-2f,
                    $"cap breached at iteration {i}: input={input} velocity={velocity}");
                Assert.AreEqual(0f, a.z, 1e-6f, $"z drift at iteration {i}");
            }
        }

        [Test]
        public void BankingStaysBoundedUnderRandomTargetsAndTimeSteps()
        {
            var random = new System.Random(Seed);
            float angle = 0f;

            for (int i = 0; i < 20000; i++)
            {
                float target = (float)(random.NextDouble() * 2 - 1) * config.bankAngleMax;
                float deltaTime = (float)(random.NextDouble() * 0.5);
                angle = JetController.StepTowardBank(angle, target, config.bankResponsiveness, deltaTime);

                Assert.IsFalse(float.IsNaN(angle), $"bank went NaN at iteration {i}");
                Assert.LessOrEqual(Mathf.Abs(angle), config.bankAngleMax + 1e-3f,
                    $"bank left its range at iteration {i}: {angle}");
            }
        }

        // --- determinism ------------------------------------------------------

        /// <summary>
        /// Bit-identical, not approximately equal. Replay, netcode rollback and
        /// the golden-trajectory regressions that arrive with the plane
        /// constraint all need the model to be a pure function of its inputs;
        /// a tolerance here would hide the exact drift those features cannot
        /// tolerate.
        /// </summary>
        [Test]
        public void TheSameInputsProduceBitIdenticalOutputTwice()
        {
            var random = new System.Random(Seed);
            var inputs = new Vector2[500];
            var velocities = new Vector2[500];
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i] = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
                velocities[i] = new Vector2((float)random.NextDouble() * 20f, (float)random.NextDouble() * 20f);
            }

            for (int i = 0; i < inputs.Length; i++)
            {
                Vector3 first = JetController.ComputeAcceleration(inputs[i], velocities[i], config);
                Vector3 second = JetController.ComputeAcceleration(inputs[i], velocities[i], config);
                Assert.IsTrue(first.x.Equals(second.x), $"x differed on evaluation {i}");
                Assert.IsTrue(first.y.Equals(second.y), $"y differed on evaluation {i}");
            }
        }

        /// <summary>
        /// Reordering evaluations must not change any of them. A cached static
        /// or a leaked field inside a "pure" function shows up here and almost
        /// nowhere else.
        /// </summary>
        [Test]
        public void EvaluationOrderDoesNotAffectAnyResult()
        {
            var speeds = new[] { -20f, -10f, -1f, 0f, 1f, 10f, 20f };

            var forward = new float[speeds.Length];
            for (int i = 0; i < speeds.Length; i++)
            {
                forward[i] = JetController.ComputeTargetBankAngle(speeds[i], config);
            }

            var backward = new float[speeds.Length];
            for (int i = speeds.Length - 1; i >= 0; i--)
            {
                backward[i] = JetController.ComputeTargetBankAngle(speeds[i], config);
            }

            CollectionAssert.AreEqual(forward, backward);
        }

        // --- config robustness -------------------------------------------------

        /// <summary>
        /// The inspector's [Min] and [Range] attributes constrain what a human
        /// can type into the editor. They constrain nothing at all about a
        /// config built in code, loaded from an asset bundle, or mutated by a
        /// Phase 3 power-up -- all of which this project plans to do.
        /// </summary>
        [Test]
        public void AConfigWithDegenerateTuningDoesNotProduceNaN(
            [Values(0.01f, 1f, 10000f)] float maxSpeed,
            [Values(0.01f, 40f, 10000f)] float acceleration)
        {
            config.maxSpeed = maxSpeed;
            config.acceleration = acceleration;

            Vector3 a = JetController.ComputeAcceleration(Vector2.one, new Vector2(3f, -4f), config);
            Assert.IsFalse(float.IsNaN(a.magnitude), "degenerate tuning produced NaN acceleration");
            Assert.LessOrEqual(a.magnitude, acceleration + 1e-2f);

            float bank = JetController.ComputeTargetBankAngle(maxSpeed * 0.5f, config);
            Assert.IsFalse(float.IsNaN(bank), "degenerate tuning produced NaN bank");
            Assert.LessOrEqual(Mathf.Abs(bank), config.bankAngleMax + 1e-3f);
        }
    }
}

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using JetFighter.Player;

namespace JetFighter.Tests.PlayMode
{
    /// <summary>
    /// Long-run behaviour under a real physics tick.
    ///
    /// JetFlightPlayModeTests proves the model works for a couple of seconds.
    /// The failures this file is for only appear over time: a slow drift on an
    /// axis nothing is pushing, an integrator that accumulates energy until the
    /// jet is unrecoverable, a bank that creeps past its own limit because the
    /// clamp is applied to the target rather than the result.
    ///
    /// What is deliberately *not* here: a wall-clock assertion. A shared CI
    /// runner's timing is noise, and a test that fails on a noisy neighbour
    /// teaches everyone to re-run rather than to read. The timing budget lives
    /// in .github/workflows/qa.yml, where it is measured against a declared
    /// TestOp.budget_ms rather than guessed at inside the suite.
    /// </summary>
    public class JetFlightSoakTests
    {
        private const int Seed = 20260920;

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

            jet = new GameObject("SoakJet");
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

        /// <summary>
        /// Roughly 30 seconds of simulated flight on a randomised stick. The
        /// seed is constant so a failure can be replayed exactly.
        /// </summary>
        [UnityTest]
        public IEnumerator TheJetStaysInsideItsEnvelopeOverALongRandomisedRun()
        {
            var random = new System.Random(Seed);
            const int steps = 1500;

            for (int i = 0; i < steps; i++)
            {
                if (i % 25 == 0)
                {
                    controller.inputVector = new Vector2(
                        (float)(random.NextDouble() * 2 - 1),
                        (float)(random.NextDouble() * 2 - 1));
                }
                yield return new WaitForFixedUpdate();

                Assert.IsFalse(float.IsNaN(body.linearVelocity.sqrMagnitude),
                    $"velocity went NaN at step {i}");
                Assert.LessOrEqual(body.linearVelocity.magnitude, config.maxSpeed * 1.25f,
                    $"speed ran away at step {i}: {body.linearVelocity.magnitude}");
                Assert.LessOrEqual(Mathf.Abs(controller.BankAngle), config.bankAngleMax + 0.5f,
                    $"bank left its range at step {i}: {controller.BankAngle}");
            }
        }

        /// <summary>
        /// The flight model only ever writes X and Y. Z must stay exactly where
        /// it started, with no constraint component involved -- if Z drifts now,
        /// the PlaneConstraint branch will be built on top of a leak and its own
        /// test will mask it.
        /// </summary>
        [UnityTest]
        public IEnumerator NothingDriftsOnTheUntouchedAxisOverALongRun()
        {
            float startZ = jet.transform.position.z;
            controller.inputVector = new Vector2(1f, -1f);

            yield return Tick(1200);

            Assert.AreEqual(0f, body.linearVelocity.z, 1e-4f, "the jet gained velocity on Z");
            Assert.AreEqual(startZ, jet.transform.position.z, 1e-3f, "the jet drifted along Z");
        }

        /// <summary>
        /// Released at cruise, the jet must actually come to rest rather than
        /// coasting forever on a drag term that rounds to nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator ReleasedAtCruiseTheJetEventuallySettles()
        {
            controller.inputVector = Vector2.right;
            yield return Tick(180);
            Assert.Greater(body.linearVelocity.magnitude, 1f, "the jet never reached cruise");

            controller.inputVector = Vector2.zero;
            yield return Tick(900);

            Assert.Less(body.linearVelocity.magnitude, 0.1f,
                "18 seconds after release the jet is still moving");
            Assert.Less(Mathf.Abs(controller.BankAngle), 0.5f,
                "the jet settled but never levelled out");
        }

        /// <summary>
        /// Two runs, same seed, same result. Physics determinism is a
        /// precondition for the Phase 4 network work (ADR-005): a host and a
        /// peer that diverge from identical inputs cannot be reconciled by any
        /// amount of state sync.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSameInputSequenceProducesTheSameFlightTwice()
        {
            Vector3 first = default;

            for (int run = 0; run < 2; run++)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                jet.transform.position = Vector3.zero;

                var random = new System.Random(Seed);
                for (int i = 0; i < 400; i++)
                {
                    if (i % 20 == 0)
                    {
                        controller.inputVector = new Vector2(
                            (float)(random.NextDouble() * 2 - 1),
                            (float)(random.NextDouble() * 2 - 1));
                    }
                    yield return new WaitForFixedUpdate();
                }

                if (run == 0)
                {
                    first = body.linearVelocity;
                }
                else
                {
                    Assert.AreEqual(first.x, body.linearVelocity.x, 1e-3f, "run 2 diverged on X");
                    Assert.AreEqual(first.y, body.linearVelocity.y, 1e-3f, "run 2 diverged on Y");
                }
            }
        }
    }
}

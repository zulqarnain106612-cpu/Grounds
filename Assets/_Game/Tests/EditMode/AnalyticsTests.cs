using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using JetFighter.Analytics;
using JetFighter.Enemy;
using JetFighter.Player;
using JetFighter.PowerUp;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion is "every listed event appears in the Firebase dashboard
    /// on a real run", which is slow, manual, and only possible after a build
    /// ships.
    ///
    /// A recording backend makes the *wiring* assertable here, so the
    /// dashboard visit confirms the pipe rather than discovering a missing
    /// call. The events that never fire are the ones nobody notices, because
    /// a missing event looks exactly like a feature nobody used.
    /// </summary>
    public class AnalyticsTests
    {
        private sealed class Recorder : AnalyticsService.IAnalyticsBackend
        {
            public readonly List<(string Name, IReadOnlyDictionary<string, object> Parameters)> Events =
                new List<(string, IReadOnlyDictionary<string, object>)>();

            public bool ThrowOnLog;

            public void LogEvent(string name, IReadOnlyDictionary<string, object> parameters)
            {
                if (ThrowOnLog)
                {
                    throw new System.InvalidOperationException("SDK not initialised");
                }
                Events.Add((name, parameters));
            }

            public bool Saw(string name) => Events.Exists(e => e.Name == name);

            public int Count(string name) => Events.FindAll(e => e.Name == name).Count;
        }

        private Recorder recorder;

        [SetUp]
        public void SetUp()
        {
            AnalyticsService.Reset();
            recorder = new Recorder();
            AnalyticsService.Bind(recorder);
        }

        [TearDown]
        public void TearDown()
        {
            AnalyticsService.Reset();
        }

        // --- names ----------------------------------------------------------

        [Test]
        public void EveryDeclaredEventNameIsValid()
        {
            // Firebase drops an over-long or malformed name silently. The
            // dashboard then looks complete and is missing a funnel step, and
            // the cost is discovered weeks later.
            foreach (string name in AnalyticsEvents.All)
            {
                Assert.IsTrue(AnalyticsService.IsValidName(name), $"'{name}' would be dropped");
            }
        }

        [Test]
        public void TheSpecsSevenEventsAreAllDeclared()
        {
            CollectionAssert.AreEquivalent(
                new[] { "run_start", "run_end", "death_cause", "powerup_collected",
                        "iap_purchase", "ad_watched", "difficulty_tier_reached" },
                AnalyticsEvents.All);
        }

        [Test]
        public void MixedCaseNamesAreRejected()
        {
            // Not a Firebase rule but a consistency one: run_start and
            // Run_Start are two events on the dashboard, and nobody notices
            // until the numbers are half what they should be.
            Assert.IsFalse(AnalyticsService.IsValidName("Run_Start"));
        }

        [Test]
        public void MalformedNamesAreRejectedBeforeSending()
        {
            foreach (string bad in new[] { null, "", "  ", "1_starts_with_digit", "_leading", "has space", "has-dash" })
            {
                Assert.IsFalse(AnalyticsService.IsValidName(bad), $"'{bad}' should be rejected");
            }
            Assert.IsFalse(AnalyticsService.IsValidName(new string('a', 41)));
        }

        [Test]
        public void AnInvalidNameIsCountedRatherThanSilentlyDropped()
        {
            // Counting is the difference between a bug and a mystery.
            Assert.IsFalse(AnalyticsService.LogEvent("Bad Name"));
            Assert.AreEqual(1, AnalyticsService.DroppedEvents);
            Assert.IsFalse(recorder.Saw("Bad Name"));
        }

        [Test]
        public void TooManyParametersIsRefused()
        {
            var many = new Dictionary<string, object>();
            for (int i = 0; i < 30; i++)
            {
                many[$"p{i}"] = i;
            }
            Assert.IsFalse(AnalyticsService.LogEvent(AnalyticsEvents.RunStart, many));
            Assert.AreEqual(1, AnalyticsService.DroppedEvents);
        }

        // --- failure behaviour ----------------------------------------------

        [Test]
        public void ABackendThatThrowsDoesNotTakeTheRunDown()
        {
            // The failure mode that makes teams rip analytics out entirely.
            recorder.ThrowOnLog = true;
            Assert.DoesNotThrow(() => AnalyticsService.LogEvent(AnalyticsEvents.RunStart));
            Assert.AreEqual(1, AnalyticsService.DroppedEvents);
        }

        [Test]
        public void NoBackendIsNotAFailure()
        {
            // Analytics is absent in the editor and in a build with consent
            // withheld, and neither is a bug.
            AnalyticsService.Reset();
            Assert.IsFalse(AnalyticsService.LogEvent(AnalyticsEvents.RunStart));
            Assert.AreEqual(0, AnalyticsService.DroppedEvents);
        }

        // --- the wiring -----------------------------------------------------

        [Test]
        public void ThePowerUpControllerReportsCollection()
        {
            var root = new GameObject("Player");
            var stats = root.AddComponent<PlayerStatsRuntime>();
            var flight = ScriptableObject.CreateInstance<JetFlightConfig>();
            stats.FlightConfig = flight;
            var powerUps = root.AddComponent<PowerUpController>();
            powerUps.Stats = stats;

            var def = ScriptableObject.CreateInstance<PowerUpDef>();
            def.type = PowerUpDef.PowerUpType.FireRate;
            def.magnitude = 1.5f;
            powerUps.Apply(def);

            Assert.IsTrue(recorder.Saw(AnalyticsEvents.PowerUpCollected));
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(flight);
        }

        [Test]
        public void APowerUpEventCarriesThePostPickupPowerLevel()
        {
            // Logged with the pre-pickup level, the analytics and the
            // difficulty curve would disagree about the same moment.
            var root = new GameObject("Player");
            var stats = root.AddComponent<PlayerStatsRuntime>();
            stats.FlightConfig = ScriptableObject.CreateInstance<JetFlightConfig>();
            var powerUps = root.AddComponent<PowerUpController>();
            powerUps.Stats = stats;

            var def = ScriptableObject.CreateInstance<PowerUpDef>();
            def.magnitude = 2f;
            powerUps.Apply(def);

            var recorded = recorder.Events.Find(e => e.Name == AnalyticsEvents.PowerUpCollected);
            Assert.Greater((float)recorded.Parameters[AnalyticsEvents.ParamPowerLevel], 0f);

            Object.DestroyImmediate(root);
            Object.DestroyImmediate(def);
        }

        [Test]
        public void TheDifficultyManagerReportsEachTierOnce()
        {
            // An event per spawn would be tens of thousands per session --
            // past Firebase's daily limits and useless besides.
            var root = new GameObject("Difficulty");
            var manager = root.AddComponent<DifficultyManager>();
            var curve = ScriptableObject.CreateInstance<DifficultyCurve>();
            var basic = ScriptableObject.CreateInstance<EnemyDef>();
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = basic, powerLevelThreshold = 1f });
            manager.Curve = curve;

            for (int i = 0; i < 500; i++)
            {
                manager.ReportTierIfNew(2f);
            }
            Assert.AreEqual(1, recorder.Count(AnalyticsEvents.DifficultyTierReached));

            Object.DestroyImmediate(root);
            Object.DestroyImmediate(curve);
            Object.DestroyImmediate(basic);
        }

        [Test]
        public void ATierIsNotReReportedWhenPowerFallsAndRises()
        {
            // Otherwise "reached tier 3" means "was at tier 3 repeatedly",
            // which no funnel can use.
            var root = new GameObject("Difficulty");
            var manager = root.AddComponent<DifficultyManager>();
            var curve = ScriptableObject.CreateInstance<DifficultyCurve>();
            var basic = ScriptableObject.CreateInstance<EnemyDef>();
            curve.unlocks.Add(new DifficultyCurve.ArchetypeUnlock { archetype = basic, powerLevelThreshold = 1f });
            manager.Curve = curve;

            manager.ReportTierIfNew(2f);
            manager.ReportTierIfNew(0f);
            manager.ReportTierIfNew(2f);
            Assert.AreEqual(1, recorder.Count(AnalyticsEvents.DifficultyTierReached));

            Object.DestroyImmediate(root);
            Object.DestroyImmediate(curve);
            Object.DestroyImmediate(basic);
        }

        // --- run lifecycle --------------------------------------------------

        [Test]
        public void ARunReportsItsEndAndItsCauseSeparately()
        {
            // They answer different questions, and merging them makes every
            // retention query filter on a death reason it does not care about.
            var root = new GameObject("Run");
            var run = root.AddComponent<RunAnalytics>();
            run.BeginRun();
            run.Tick(42f);
            Assert.IsTrue(run.EndRun("collision", 1200));

            Assert.IsTrue(recorder.Saw(AnalyticsEvents.RunEnd));
            Assert.IsTrue(recorder.Saw(AnalyticsEvents.DeathCause));

            var ended = recorder.Events.Find(e => e.Name == AnalyticsEvents.RunEnd);
            Assert.AreEqual(42, ended.Parameters[AnalyticsEvents.ParamDurationSeconds]);
            Assert.AreEqual(1200, ended.Parameters[AnalyticsEvents.ParamScore]);

            Object.DestroyImmediate(root);
        }

        [Test]
        public void ARunEndsOnlyOnce()
        {
            // A game-over screen reachable twice -- which a rewarded continue
            // produces -- would double every run-length statistic, halving the
            // average silently.
            var root = new GameObject("Run");
            var run = root.AddComponent<RunAnalytics>();
            run.BeginRun();
            Assert.IsTrue(run.EndRun("collision", 10));
            Assert.IsFalse(run.EndRun("collision", 10));
            Assert.AreEqual(1, recorder.Count(AnalyticsEvents.RunEnd));

            Object.DestroyImmediate(root);
        }

        [Test]
        public void AContinuedRunKeepsItsClock()
        {
            // A continued run is one run to the player; splitting it would
            // make the continue look like it shortened sessions.
            var root = new GameObject("Run");
            var run = root.AddComponent<RunAnalytics>();
            run.BeginRun();
            run.Tick(30f);
            run.EndRun("collision", 5);
            run.ResumeAfterContinue();
            run.Tick(30f);
            run.EndRun("collision", 9);

            var last = recorder.Events.FindLast(e => e.Name == AnalyticsEvents.RunEnd);
            Assert.AreEqual(60, last.Parameters[AnalyticsEvents.ParamDurationSeconds]);

            Object.DestroyImmediate(root);
        }

        [Test]
        public void AnUnknownDeathCauseIsNamedRatherThanEmpty()
        {
            // An empty string on a dashboard is indistinguishable from a
            // missing parameter.
            var root = new GameObject("Run");
            var run = root.AddComponent<RunAnalytics>();
            run.BeginRun();
            run.EndRun("", 0);

            var cause = recorder.Events.Find(e => e.Name == AnalyticsEvents.DeathCause);
            Assert.AreEqual("unknown", cause.Parameters[AnalyticsEvents.ParamCause]);

            Object.DestroyImmediate(root);
        }

        [Test]
        public void AnUnstartedRunReportsNothing()
        {
            var root = new GameObject("Run");
            var run = root.AddComponent<RunAnalytics>();
            Assert.IsFalse(run.EndRun("collision", 0));
            Assert.IsFalse(recorder.Saw(AnalyticsEvents.RunEnd));
            Object.DestroyImmediate(root);
        }
    }
}

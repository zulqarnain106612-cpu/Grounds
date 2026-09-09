using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using JetFighter.Player;
using JetFighter.UI.Input;
using JetFighter.Weapon;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The criterion is negative and precise: nothing the player controls
    /// works until Go, and everything works at Go -- not before.
    ///
    /// "Not before" is the half that decays, so it is asserted at every step
    /// of the whole sequence rather than sampled once mid-countdown.
    /// </summary>
    public class IntroSequenceTests
    {
        private GameObject jetObject;
        private GameObject prefab;
        private IntroSequenceController intro;
        private Rigidbody body;
        private JetController jet;
        private PlayerInputRouter router;
        private PrimaryGunController gun;
        private WeaponBase weapon;

        [SetUp]
        public void SetUp()
        {
            prefab = new GameObject("BulletPrefab");
            prefab.AddComponent<Rigidbody>().isKinematic = true;
            prefab.AddComponent<Bullet>();
            prefab.SetActive(false);

            weapon = ScriptableObject.CreateInstance<WeaponBase>();
            weapon.projectilePrefab = prefab;
            weapon.poolCapacity = 4;

            jetObject = new GameObject("Jet");
            jetObject.transform.position = new Vector3(0f, 0f, 5f);
            body = jetObject.AddComponent<Rigidbody>();
            jet = jetObject.AddComponent<JetController>();
            router = jetObject.AddComponent<PlayerInputRouter>();
            router.Jet = jet;
            gun = jetObject.AddComponent<PrimaryGunController>();
            gun.WeaponDef = weapon;

            intro = jetObject.AddComponent<IntroSequenceController>();
            intro.JetBody = body;
            intro.InputRouter = router;
            intro.PrimaryGun = gun;
            intro.SpawnSeconds = 1f;
            intro.CountFrom = 5;
            intro.SecondsPerCount = 1f;
            intro.Begin();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(jetObject);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(weapon);
        }

        private void Run(float seconds, float step = 1f / 60f)
        {
            int steps = Mathf.RoundToInt(seconds / step);
            for (int i = 0; i < steps; i++)
            {
                intro.Tick(step);
            }
        }

        private void AssertLocked(string when)
        {
            Assert.IsTrue(body.isKinematic, $"{when}: the jet is physics-driven");
            Assert.IsFalse(router.enabled, $"{when}: the input router is live");
            Assert.IsFalse(gun.AutoFire, $"{when}: the gun is firing");
        }

        [Test]
        public void TheSequenceStartsInSpawn()
        {
            Assert.AreEqual(IntroSequenceController.State.Spawn, intro.Current);
        }

        [Test]
        public void TheJetStartsBelowItsRestPositionAndArrivesAtIt()
        {
            Assert.Less(jetObject.transform.position.y, 0f, "the jet did not enter from below");
            Run(1f);
            Assert.AreEqual(0f, jetObject.transform.position.y, 1e-3f);
        }

        [Test]
        public void TheSpawnTweenDoesNotUsePhysics()
        {
            // A jet flown in by forces arrives carrying velocity the plane
            // constraint has to fight on the first frame of player control.
            Run(0.5f);
            Assert.IsTrue(body.isKinematic);
            Assert.AreEqual(Vector3.zero, body.linearVelocity);
        }

        [Test]
        public void TheJetLandsExactlyOnItsRestPositionNotWhereTheCurveEnded()
        {
            // An overshooting ease would hand the player a jet slightly off
            // its plane, and the constraint would visibly yank it back.
            Run(2f);
            Assert.AreEqual(5f, jetObject.transform.position.z, 1e-4f);
            Assert.AreEqual(0f, jetObject.transform.position.y, 1e-4f);
        }

        [Test]
        public void NothingIsControllableAtAnyPointBeforeGo()
        {
            // The half of the criterion that decays: asserted continuously
            // rather than sampled once.
            for (int i = 0; i < 360; i++)
            {
                if (intro.Current == IntroSequenceController.State.Go
                    || intro.Current == IntroSequenceController.State.PlayerControl)
                {
                    break;
                }
                AssertLocked($"tick {i} in {intro.Current}");
                intro.Tick(1f / 60f);
            }
        }

        [Test]
        public void TheCountdownCountsDownFromFive()
        {
            var seen = new List<int>();
            intro.OnCountChanged += seen.Add;
            Run(1f);  // spawn
            Run(6f);  // countdown
            CollectionAssert.AreEqual(new[] { 5, 4, 3, 2, 1, 0 }, seen);
        }

        [Test]
        public void ControlArrivesExactlyAtGo()
        {
            Run(1f);
            AssertLocked("countdown start");
            Run(4.9f);
            AssertLocked("one tick before Go");

            Run(0.2f);
            Assert.IsFalse(body.isKinematic, "the jet is still kinematic after Go");
            Assert.IsTrue(router.enabled);
            Assert.IsTrue(gun.AutoFire);
        }

        [Test]
        public void TheSequenceEndsInPlayerControl()
        {
            Run(8f);
            Assert.AreEqual(IntroSequenceController.State.PlayerControl, intro.Current);
        }

        [Test]
        public void PlayerControlIsAnnouncedOnce()
        {
            int announcements = 0;
            intro.OnPlayerControl += () => announcements++;
            Run(20f);
            Assert.AreEqual(1, announcements);
        }

        [Test]
        public void AHeldStickDoesNotResumeWhenControlArrives()
        {
            // The router normally zeroes this. Disabled, it cannot -- so a
            // stick held during the countdown would fly the jet at Go.
            jet.inputVector = new Vector2(1f, 1f);
            intro.ApplyControl(false);
            Assert.AreEqual(Vector2.zero, jet.inputVector);
        }

        [Test]
        public void VelocityIsClearedAsTheGateCloses()
        {
            // Cleared on lock rather than on unlock: velocity banked before
            // the gate would be waiting to launch the jet the instant control
            // arrives.
            body.isKinematic = false;
            body.linearVelocity = new Vector3(50f, 50f, 0f);
            intro.ApplyControl(false);
            Assert.AreEqual(Vector3.zero, body.linearVelocity);
        }

        [Test]
        public void TheCountdownIsFrameRateIndependent()
        {
            var atThirty = new List<int>();
            intro.OnCountChanged += atThirty.Add;
            Run(7f, 1f / 30f);
            CollectionAssert.AreEqual(new[] { 5, 4, 3, 2, 1, 0 }, atThirty);
        }

        [Test]
        public void ALongHitchDuringTheCountdownDoesNotSkipNumbers()
        {
            // A single frame longer than a count must still emit every number
            // the HUD needs to render, not jump from five to one.
            var seen = new List<int>();
            intro.OnCountChanged += seen.Add;
            Run(1f);
            intro.Tick(3.5f);
            CollectionAssert.AreEqual(new[] { 5, 4, 3, 2 }, seen);
        }

        [Test]
        public void TheSequenceCanBeReplayed()
        {
            Run(10f);
            Assert.AreEqual(IntroSequenceController.State.PlayerControl, intro.Current);

            intro.Begin();
            Assert.AreEqual(IntroSequenceController.State.Spawn, intro.Current);
            AssertLocked("after a replay");
        }

        [Test]
        public void AMissingSystemDoesNotStopTheSequence()
        {
            // The launcher is optional in this scene. A null gate must not
            // strand the player in a countdown that never ends.
            intro.MissileLauncher = null;
            intro.PrimaryGun = null;
            Run(10f);
            Assert.AreEqual(IntroSequenceController.State.PlayerControl, intro.Current);
        }
    }
}

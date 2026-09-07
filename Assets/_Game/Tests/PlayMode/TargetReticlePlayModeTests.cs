using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using JetFighter.UI.Input;

namespace JetFighter.Tests.PlayMode
{
    /// <summary>
    /// The cell's criterion: right-half touches move the reticle, left-half
    /// touches have zero targeting effect.
    ///
    /// Driven through the real pointer handlers, and with a real camera and
    /// real colliders, because the raycast is half of what is being claimed --
    /// a reticle that moves but never locks anything would pass a
    /// field-poking test.
    /// </summary>
    public class TargetReticlePlayModeTests
    {
        private GameObject cameraObject;
        private GameObject reticleObject;
        private GameObject groundEnemy;
        private GameObject airEnemy;
        private TargetReticleInput reticle;
        private Camera cam;

        private const int GroundLayer = 8;
        private const int AirLayer = 9;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("Camera");
            cam = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.transform.rotation = Quaternion.identity;

            reticleObject = new GameObject("Reticle", typeof(RectTransform));
            reticle = reticleObject.AddComponent<TargetReticleInput>();
            reticle.TargetingCamera = cam;
            reticle.TargetableLayers = 1 << GroundLayer;

            groundEnemy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundEnemy.name = "GroundEnemy";
            groundEnemy.layer = GroundLayer;
            groundEnemy.transform.position = Vector3.zero;
            groundEnemy.transform.localScale = Vector3.one * 4f;

            airEnemy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            airEnemy.name = "AirEnemy";
            airEnemy.layer = AirLayer;
            airEnemy.transform.position = new Vector3(0f, 20f, 0f);
            airEnemy.transform.localScale = Vector3.one * 4f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(reticleObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(groundEnemy);
            Object.DestroyImmediate(airEnemy);
        }

        private static PointerEventData Pointer(Vector2 position, int id = 0)
        {
            return new PointerEventData(EventSystem.current)
            {
                pointerId = id, position = position, pressPosition = position,
            };
        }

        private void Press(Vector2 p, int id = 0) =>
            ((IPointerDownHandler)reticle).OnPointerDown(Pointer(p, id));

        private void Drag(Vector2 p, int id = 0) =>
            ((IDragHandler)reticle).OnDrag(Pointer(p, id));

        private void Release(Vector2 p, int id = 0) =>
            ((IPointerUpHandler)reticle).OnPointerUp(Pointer(p, id));

        private Vector2 OverGroundEnemy()
        {
            Vector3 screen = cam.WorldToScreenPoint(groundEnemy.transform.position);
            return new Vector2(Mathf.Max(screen.x, Screen.width * 0.75f), screen.y);
        }

        private static Vector2 LeftHalf => new Vector2(Screen.width * 0.25f, Screen.height * 0.5f);

        [UnityTest]
        public IEnumerator ARightHalfTouchLocksAGroundEnemy()
        {
            // Placed so the ray passes through the enemy regardless of the
            // runner's screen size.
            groundEnemy.transform.position = cam.ScreenToWorldPoint(
                new Vector3(Screen.width * 0.75f, Screen.height * 0.5f, 20f));
            yield return null;

            Press(new Vector2(Screen.width * 0.75f, Screen.height * 0.5f));
            yield return null;

            Assert.AreEqual(groundEnemy.transform, reticle.CurrentTarget);
            Assert.IsNotNull(reticle.ScreenTarget);
        }

        [UnityTest]
        public IEnumerator ALeftHalfTouchHasZeroTargetingEffect()
        {
            // The mirror of Phase 1's proof, and the cell's stated criterion.
            groundEnemy.transform.position = cam.ScreenToWorldPoint(
                new Vector3(Screen.width * 0.25f, Screen.height * 0.5f, 20f));
            yield return null;

            Press(LeftHalf);
            Drag(LeftHalf + new Vector2(10f, 10f));
            yield return null;

            Assert.IsFalse(reticle.IsHeld, "the left-half touch claimed the reticle");
            Assert.IsNull(reticle.CurrentTarget);
            Assert.IsNull(reticle.ScreenTarget);
        }

        [UnityTest]
        public IEnumerator ALeftHalfTouchCannotDragAnActiveLock()
        {
            groundEnemy.transform.position = cam.ScreenToWorldPoint(
                new Vector3(Screen.width * 0.75f, Screen.height * 0.5f, 20f));
            yield return null;

            Press(new Vector2(Screen.width * 0.75f, Screen.height * 0.5f), id: 0);
            Vector2? locked = reticle.ScreenTarget;

            Drag(LeftHalf, id: 1);
            yield return null;
            Assert.AreEqual(locked, reticle.ScreenTarget, "a second finger moved the reticle");
        }

        [UnityTest]
        public IEnumerator TheLockSurvivesTheFingerLifting()
        {
            // The player raises the thumb to press the missile button. A lock
            // that died with the touch would make the weapon unusable.
            groundEnemy.transform.position = cam.ScreenToWorldPoint(
                new Vector3(Screen.width * 0.75f, Screen.height * 0.5f, 20f));
            yield return null;

            Vector2 p = new Vector2(Screen.width * 0.75f, Screen.height * 0.5f);
            Press(p);
            Release(p);
            yield return null;

            Assert.IsFalse(reticle.IsHeld);
            Assert.AreEqual(groundEnemy.transform, reticle.CurrentTarget);
        }

        [UnityTest]
        public IEnumerator AirEnemiesAreNotLockable()
        {
            // ADR-002: missiles are ground-only, enforced by the layer mask.
            airEnemy.transform.position = cam.ScreenToWorldPoint(
                new Vector3(Screen.width * 0.75f, Screen.height * 0.5f, 20f));
            groundEnemy.transform.position = new Vector3(0f, -500f, 0f);
            yield return null;

            Press(new Vector2(Screen.width * 0.75f, Screen.height * 0.5f));
            yield return null;

            Assert.IsNull(reticle.CurrentTarget, "an air enemy was locked; the layer mask is not holding");
        }

        [UnityTest]
        public IEnumerator ADeadTargetClearsTheLock()
        {
            // Enemies are pooled, so a killed one is deactivated rather than
            // destroyed. A stale lock would have the launcher firing at it.
            groundEnemy.transform.position = cam.ScreenToWorldPoint(
                new Vector3(Screen.width * 0.75f, Screen.height * 0.5f, 20f));
            yield return null;
            Press(new Vector2(Screen.width * 0.75f, Screen.height * 0.5f));
            yield return null;
            Assert.IsNotNull(reticle.CurrentTarget);

            groundEnemy.SetActive(false);
            yield return null;
            Assert.IsNull(reticle.CurrentTarget);
            Assert.IsNull(reticle.ScreenTarget);
        }
    }
}

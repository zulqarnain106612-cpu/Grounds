using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using JetFighter.Player;
using JetFighter.UI.Input;

namespace JetFighter.Tests.PlayMode
{
    /// <summary>
    /// The criterion end to end: a left-half touch moves the jet, a right-half
    /// touch has zero effect on jet.inputVector.
    ///
    /// Driven through the real IPointerDownHandler/IDragHandler entry points
    /// rather than by poking fields, because the guarantee being tested lives
    /// in OnPointerDown. Setting `offset` directly would test nothing.
    /// </summary>
    public class PlayerInputRouterPlayModeTests
    {
        private GameObject jetObject;
        private GameObject uiObject;
        private JetController jet;
        private JoystickInput joystick;
        private PlayerInputRouter router;
        private JetFlightConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<JetFlightConfig>();
            config.maxSpeed = 10f;
            config.acceleration = 40f;

            jetObject = new GameObject("Jet");
            jetObject.AddComponent<Rigidbody>();
            jet = jetObject.AddComponent<JetController>();
            jet.Config = config;

            uiObject = new GameObject("Joystick", typeof(RectTransform));
            joystick = uiObject.AddComponent<JoystickInput>();
            joystick.Radius = 100f;

            router = uiObject.AddComponent<PlayerInputRouter>();
            router.Joystick = joystick;
            router.Jet = jet;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(uiObject);
            Object.DestroyImmediate(jetObject);
            Object.DestroyImmediate(config);
        }

        private static PointerEventData Pointer(Vector2 position, int id = 0)
        {
            return new PointerEventData(EventSystem.current)
            {
                pointerId = id,
                position = position,
                pressPosition = position,
            };
        }

        private void Press(Vector2 position, int id = 0)
        {
            ((IPointerDownHandler)joystick).OnPointerDown(Pointer(position, id));
        }

        private void Drag(Vector2 position, int id = 0)
        {
            ((IDragHandler)joystick).OnDrag(Pointer(position, id));
        }

        private void Release(Vector2 position, int id = 0)
        {
            ((IPointerUpHandler)joystick).OnPointerUp(Pointer(position, id));
        }

        private static Vector2 LeftHalf => new Vector2(Screen.width * 0.25f, Screen.height * 0.5f);
        private static Vector2 RightHalf => new Vector2(Screen.width * 0.75f, Screen.height * 0.5f);

        [UnityTest]
        public IEnumerator ALeftHalfTouchDrivesTheJet()
        {
            Press(LeftHalf);
            Drag(LeftHalf + new Vector2(100f, 0f));
            yield return null;

            Assert.Greater(jet.inputVector.x, 0.5f, "the left-hand stick did not reach the jet");
        }

        [UnityTest]
        public IEnumerator ARightHalfTouchHasZeroEffect()
        {
            // The structural guarantee, not the convention.
            Press(RightHalf);
            Drag(RightHalf + new Vector2(100f, 0f));
            yield return null;

            Assert.IsFalse(joystick.IsHeld, "the right-half touch claimed the stick");
            Assert.AreEqual(Vector2.zero, jet.inputVector);
        }

        [UnityTest]
        public IEnumerator ARightHalfTouchCannotStealAnActiveStick()
        {
            Press(LeftHalf, id: 0);
            Drag(LeftHalf + new Vector2(100f, 0f), id: 0);
            yield return null;
            Vector2 held = jet.inputVector;

            Drag(RightHalf, id: 1);
            Release(RightHalf, id: 1);
            yield return null;

            Assert.AreEqual(held, jet.inputVector, "a second finger overrode the one holding the stick");
        }

        [UnityTest]
        public IEnumerator ReleasingRecentresTheStick()
        {
            Press(LeftHalf);
            Drag(LeftHalf + new Vector2(100f, 0f));
            yield return null;
            Assert.Greater(jet.inputVector.magnitude, 0f);

            Release(LeftHalf);
            yield return null;
            Assert.AreEqual(Vector2.zero, jet.inputVector, "the jet keeps flying after the finger lifts");
        }

        [UnityTest]
        public IEnumerator AMissingJoystickReadsAsNoInputNotAsStaleInput()
        {
            Press(LeftHalf);
            Drag(LeftHalf + new Vector2(100f, 0f));
            yield return null;

            router.Joystick = null;
            yield return null;
            Assert.AreEqual(Vector2.zero, jet.inputVector);
        }

        [UnityTest]
        public IEnumerator TheRouterSurvivesAJetThatWentAway()
        {
            router.Jet = null;
            Press(LeftHalf);
            Drag(LeftHalf + new Vector2(100f, 0f));
            yield return null;
            Assert.Pass("no exception from a null jet during a scene teardown");
        }
    }
}

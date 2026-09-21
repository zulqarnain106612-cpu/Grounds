using NUnit.Framework;
using UnityEngine;
using JetFighter.Physics;

namespace JetFighter.Tests.EditMode
{
    /// <summary>
    /// The constraint's axis arithmetic, without a physics tick. The soak --
    /// deviation staying inside an epsilon over a long run under diagonal
    /// input -- is the PlayMode suite's job; this pins the pieces it is
    /// built from.
    /// </summary>
    public class PlaneConstraintTests
    {
        [Test]
        public void ComponentReadsTheAxisItIsAskedFor()
        {
            var v = new Vector3(1f, 2f, 3f);
            Assert.AreEqual(1f, PlaneConstraint.Component(v, PlaneConstraint.Axis.X));
            Assert.AreEqual(2f, PlaneConstraint.Component(v, PlaneConstraint.Axis.Y));
            Assert.AreEqual(3f, PlaneConstraint.Component(v, PlaneConstraint.Axis.Z));
        }

        [Test]
        public void WithComponentReplacesOnlyTheLockedAxis()
        {
            Vector3 result = PlaneConstraint.WithComponent(
                new Vector3(1f, 2f, 3f), PlaneConstraint.Axis.Z, 0f);
            Assert.AreEqual(new Vector3(1f, 2f, 0f), result);
        }

        [Test]
        public void WithComponentDoesNotMutateItsArgument()
        {
            // Vector3 is a struct, so this holds by language rule -- but the
            // signature takes it by value and returns a copy, and someone
            // switching it to `ref` for "efficiency" would break every caller
            // silently.
            var original = new Vector3(1f, 2f, 3f);
            PlaneConstraint.WithComponent(original, PlaneConstraint.Axis.Z, 99f);
            Assert.AreEqual(3f, original.z);
        }

        [Test]
        public void EveryAxisCanBeLocked()
        {
            // Z is the Phase 1 decision (ADR-001), not a hard-coded assumption.
            // Phase 2's ground enemies may want a different plane.
            foreach (PlaneConstraint.Axis axis in System.Enum.GetValues(typeof(PlaneConstraint.Axis)))
            {
                Vector3 clamped = PlaneConstraint.WithComponent(Vector3.one * 5f, axis, 0f);
                Assert.AreEqual(0f, PlaneConstraint.Component(clamped, axis));
                Assert.AreEqual(15f - 5f, clamped.x + clamped.y + clamped.z,
                    "clamping one axis changed another");
            }
        }
    }
}

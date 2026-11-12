using Bun3.Unity.Core.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Core.Tests
{
    internal interface IProbe
    {
    }

    internal sealed class Probe : MonoBehaviour, IProbe
    {
    }

    public class InterfaceRefTests
    {
        private GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
        }

        [Test]
        public void ValueReturnsAssignedComponent()
        {
            go = new GameObject("probe");
            var probe = go.AddComponent<Probe>();

            Assert.That(new InterfaceRef<IProbe>(probe).Value, Is.SameAs(probe));
        }

        [Test]
        public void ValueIsNullWhenDestroyed()
        {
            go = new GameObject("probe");
            var reference = new InterfaceRef<IProbe>(go.AddComponent<Probe>());

            Object.DestroyImmediate(go);

            Assert.That(reference.Value, Is.Null);
        }

        [Test]
        public void ResolvePicksImplementingComponentFromGameObject()
        {
            go = new GameObject("probe");
            Assert.That(InterfaceRef<IProbe>.Resolve(go), Is.Null);

            var probe = go.AddComponent<Probe>();

            Assert.That(InterfaceRef<IProbe>.Resolve(go), Is.SameAs(probe));
        }
    }
}

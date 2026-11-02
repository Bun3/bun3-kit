using NUnit.Framework;
using UnityEngine;

namespace Bun3.Unity.Telemetry.Tests
{
    public class UiPathTests
    {
        [Test]
        public void Of_JoinsHierarchyAndStripsClone()
        {
            var root = new GameObject("Canvas(Clone)");
            var child = new GameObject("Ok");
            child.transform.SetParent(root.transform);
            try { Assert.AreEqual("Canvas/Ok", UiPath.Of(child.transform)); }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Of_DropdownItem_DropsOptionText()
        {
            var root = new GameObject("Dropdown");
            var item = new GameObject("Item 2: secret-option");
            item.transform.SetParent(root.transform);
            try { Assert.AreEqual("Dropdown/Item 2", UiPath.Of(item.transform)); }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Of_Null_Empty() => Assert.AreEqual("", UiPath.Of(null));
    }
}

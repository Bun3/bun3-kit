using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Bun3.Unity.Diagnostics.Editor.Tests
{
    public class FrameDebuggerReflectionTests
    {
        [Test]
        public void AllFrameDebuggerBindingsResolve()
        {
            var missing = FrameDebuggerReflection.Bind();

            Assert.IsEmpty(missing, "Editor upgrade broke Frame Debugger reflection: " + string.Join(", ", missing));
            Assert.IsTrue(FrameDebuggerReflection.IsBound);
        }

        [Test]
        public void EventDataExposesMappedFields()
        {
            FrameDebuggerReflection.Bind();
            var type = FrameDebuggerReflection.GetEventDataType();
            Assert.IsNotNull(type);

            var names = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => f.Name.TrimStart('m', '_'))
                .ToArray();
            var required = new[]
            {
                "OriginalShaderName", "PassName", "VertexCount", "DrawCallCount",
                "InstanceCount", "BatchBreakCause", "ComponentInstanceID",
            };
            foreach (var name in required)
                Assert.Contains(name, names, $"FrameDebuggerEventData no longer exposes {name}");
        }
    }
}

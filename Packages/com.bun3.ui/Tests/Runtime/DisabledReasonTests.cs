using System;
using Bun3.UI.Buttons;
using NUnit.Framework;

namespace Bun3.UI.Tests
{
    public class DisabledReasonTests
    {
        [Test]
        public void Default_IsEmpty()
        {
            Assert.IsTrue(default(DisabledReason).IsEmpty);
        }

        [Test]
        public void MessageConstructor_CarriesMessageOnly()
        {
            var reason = new DisabledReason("not enough gold");

            Assert.IsFalse(reason.IsEmpty);
            Assert.AreEqual("not enough gold", reason.DisabledMessage);
            Assert.IsNull(reason.DisabledAction);
        }

        [Test]
        public void ActionConstructor_CarriesActionOnly()
        {
            Action action = () => { };
            var reason = new DisabledReason(action);

            Assert.IsFalse(reason.IsEmpty);
            Assert.AreSame(action, reason.DisabledAction);
            Assert.IsNull(reason.DisabledMessage);
        }

        [Test]
        public void NullMessage_IsEmpty()
        {
            Assert.IsTrue(new DisabledReason((string)null).IsEmpty);
        }

        [Test]
        public void NullAction_IsEmpty()
        {
            Assert.IsTrue(new DisabledReason((Action)null).IsEmpty);
        }
    }
}

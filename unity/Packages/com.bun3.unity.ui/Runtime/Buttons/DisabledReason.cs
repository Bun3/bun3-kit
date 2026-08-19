using System;

namespace Bun3.Unity.UI.Buttons
{
    /// <summary>
    /// Why a button is disabled. Holds exactly one of a message or an action.
    /// </summary>
    public readonly struct DisabledReason
    {
        /// <summary>Message to display. Null when <see cref="DisabledAction"/> is set.</summary>
        public string DisabledMessage { get; }

        /// <summary>Action to run. Null when <see cref="DisabledMessage"/> is set.</summary>
        public Action DisabledAction { get; }

        /// <summary>True when there is no reason to deliver; the handler is not called.</summary>
        public bool IsEmpty => DisabledMessage == null && DisabledAction == null;

        public DisabledReason(string disabledMessage)
        {
            DisabledMessage = disabledMessage;
            DisabledAction = null;
        }

        public DisabledReason(Action disabledAction)
        {
            DisabledMessage = null;
            DisabledAction = disabledAction;
        }
    }
}

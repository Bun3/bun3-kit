using System;
using System.Collections.Generic;

namespace Bun3.Server.Rpc
{
    /// <summary>Messaging schema/registration validation failure. Errors carries the full list of violations (for fail-fast startup failure).</summary>
    public sealed class RpcValidationException : Exception
    {
        /// <summary>Full list of validation violations.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>Creates the exception from the violation list.</summary>
        public RpcValidationException(IReadOnlyList<string> errors)
            : base("Messaging configuration validation failed:\n- " + string.Join("\n- ", errors))
        {
            Errors = errors;
        }
    }
}

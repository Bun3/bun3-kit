using System;
using Google.Protobuf;

namespace Bun3.Server.Rpc
{
    /// <summary>Result of request handling — success (response message) or failure (status code). Allocation-free readonly struct.</summary>
    public readonly struct Reply<TRes> where TRes : class, IMessage<TRes>
    {
        /// <summary>0 = OK. 1-99 framework-reserved, negative game-defined.</summary>
        public int Status { get; }

        /// <summary>Invariant: Status == 0 iff Value != null.</summary>
        public TRes? Value { get; }

        /// <summary>Whether Status is 0 (success).</summary>
        public bool IsOk => Status == 0;

        private Reply(int status, TRes? value)
        {
            Status = status;
            Value = value;
        }

        /// <summary>Creates a success reply.</summary>
        public static Reply<TRes> Ok(TRes value) =>
            new Reply<TRes>(0, value ?? throw new ArgumentNullException(nameof(value)));

        /// <summary>Creates a failure reply. status must not be 0.</summary>
        public static Reply<TRes> Fail(int status) =>
            status != 0
                ? new Reply<TRes>(status, null)
                : throw new ArgumentException("Failure status code must not be 0.", nameof(status));

        /// <summary>Implicit conversion from a response message to a success Reply.</summary>
        public static implicit operator Reply<TRes>(TRes value) => Ok(value);

        /// <summary>Implicit conversion from a ReplyFailure to a failure Reply.</summary>
        public static implicit operator Reply<TRes>(ReplyFailure failure) => Fail(failure.Status);
    }

    /// <summary>Intermediate value that lets Reply.Fail(code) be used without a generic argument.</summary>
    public readonly struct ReplyFailure
    {
        /// <summary>Failure status code.</summary>
        public int Status { get; }

        /// <summary>Creates a failure value with the given status code.</summary>
        public ReplyFailure(int status)
        {
            Status = status;
        }
    }

    /// <summary>Non-generic helper for Reply&lt;TRes&gt;.</summary>
    public static class Reply
    {
        /// <summary>Creates a ReplyFailure with the given status code.</summary>
        public static ReplyFailure Fail(int status) => new ReplyFailure(status);
    }
}

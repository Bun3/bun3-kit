using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Google.Protobuf;

namespace Bun3.Server.Rpc
{
    /// <summary>Server-level handler registration table. Built once at boot and fully validated by RpcSchema.Validate.</summary>
    public sealed class RpcConfig<TSession> where TSession : Session
    {
        internal sealed class Registration
        {
            public Type RequestType { get; }
            public Type ResponseType { get; }
            public Func<TSession, IMessage, ValueTask<(int Status, IMessage? Response)>> Invoke { get; }

            public Registration(
                Type requestType,
                Type responseType,
                Func<TSession, IMessage, ValueTask<(int Status, IMessage? Response)>> invoke)
            {
                RequestType = requestType;
                ResponseType = responseType;
                Invoke = invoke;
            }
        }

        internal Dictionary<Type, Registration> Registrations { get; } = new Dictionary<Type, Registration>();

        /// <summary>Registers the handler for one request type. Duplicate registration of the same TReq throws immediately.</summary>
        public void OnRequest<TReq, TRes>(Func<TSession, TReq, ValueTask<Reply<TRes>>> handler)
            where TReq : class, IMessage<TReq>
            where TRes : class, IMessage<TRes>
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (Registrations.ContainsKey(typeof(TReq)))
            {
                throw new RpcValidationException(new[] { $"Duplicate registration: {typeof(TReq).Name}" });
            }

            Registrations.Add(typeof(TReq), new Registration(
                typeof(TReq),
                typeof(TRes),
                async (session, message) =>
                {
                    var reply = await handler(session, (TReq)message).ConfigureAwait(false);
                    return (reply.Status, (IMessage?)reply.Value);
                }));
        }
    }
}

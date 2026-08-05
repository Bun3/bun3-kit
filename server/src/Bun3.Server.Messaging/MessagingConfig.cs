using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bun3.Server.Core;
using Google.Protobuf;

namespace Bun3.Server.Messaging
{
    /// <summary>서버 수준 핸들러 등록표. 부팅 시 1회 구성되고 MessagingSchema.Validate로 전수 검증된다.</summary>
    public sealed class MessagingConfig<TSession> where TSession : Session
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

        /// <summary>요청 타입 하나의 핸들러를 등록한다. 같은 TReq 중복 등록은 즉시 예외.</summary>
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
                throw new MessagingValidationException(new[] { $"중복 등록: {typeof(TReq).Name}" });
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

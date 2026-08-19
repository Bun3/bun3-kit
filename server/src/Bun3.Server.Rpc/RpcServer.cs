using System;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Rpc
{
    /// <summary>
    /// Server with the messaging layer assembled. Schema construction and full registration validation
    /// run at construction, so configuration errors fail at startup with the full list (fail-fast).
    /// </summary>
    public sealed class RpcServer<TSession, TRequest, TResponse, TUpdate> : ServerBase<TSession>
        where TSession : RpcSession
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
        where TUpdate : class, IMessage<TUpdate>, new()
    {
        private readonly Func<IConnection, TSession> _sessionFactory;
        private readonly RpcRuntime<TSession, TRequest, TResponse, TUpdate> _runtime;

        /// <summary>
        /// Configures the messaging server. Schema construction and registration validation run immediately,
        /// so configuration errors fail in this constructor with RpcValidationException.
        /// </summary>
        public RpcServer(
            ITransportListener transport,
            Func<IConnection, TSession> sessionFactory,
            RpcConfig<TSession> config,
            RpcServerOptions? options = null,
            ILogger? logger = null)
            // options is defaulted exactly once while evaluating the base(...) arguments,
            // and the body reuses the same (now non-null) value.
            : base(transport, logger, (options ??= new RpcServerOptions()).MaxQueuedPackets, options.SlowWorkWarning)
        {
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            _runtime = new RpcRuntime<TSession, TRequest, TResponse, TUpdate>(
                RpcSchema<TRequest, TResponse, TUpdate>.Create(),
                config,
                options,
                new SafeLogger(logger ?? NullLogger.Instance));
        }

        /// <summary>Creates a session and attaches the runtime.</summary>
        protected override TSession CreateSession(IConnection connection)
        {
            var session = _sessionFactory(connection);
            session.AttachRuntime(_runtime);
            return session;
        }
    }
}

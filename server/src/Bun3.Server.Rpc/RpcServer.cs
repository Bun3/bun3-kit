using System;
using Bun3.Server.Abstractions;
using Bun3.Server.Core;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Rpc
{
    /// <summary>
    /// 메시징 계층이 조립된 서버. 생성 시 스키마 구축과 등록표 전수 검증을 수행하므로
    /// 구성 오류는 기동 시점에 전체 목록과 함께 실패한다(fail-fast).
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
        /// 메시징 서버를 구성한다. 스키마 구축과 등록표 검증을 즉시 수행하므로,
        /// 구성 오류는 이 생성자에서 RpcValidationException으로 실패한다.
        /// </summary>
        public RpcServer(
            ITransportListener transport,
            Func<IConnection, TSession> sessionFactory,
            RpcConfig<TSession> config,
            RpcServerOptions? options = null,
            ILogger? logger = null)
            // options는 base(...) 인자 평가 시 한 번만 기본값으로 채워지고(단일 평가),
            // 본문에서도 같은(이제 non-null) 값을 재사용한다.
            : base(transport, logger, (options ??= new RpcServerOptions()).MaxQueuedPackets, options.SlowWorkWarning)
        {
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            _runtime = new RpcRuntime<TSession, TRequest, TResponse, TUpdate>(
                RpcSchema<TRequest, TResponse, TUpdate>.Create(),
                config,
                options,
                new SafeLogger(logger ?? NullLogger.Instance));
        }

        /// <summary>세션을 생성하고 런타임을 부착한다.</summary>
        protected override TSession CreateSession(IConnection connection)
        {
            var session = _sessionFactory(connection);
            session.AttachRuntime(_runtime);
            return session;
        }
    }
}

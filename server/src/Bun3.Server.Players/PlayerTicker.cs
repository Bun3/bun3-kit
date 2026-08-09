using System;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Bun3.Server.Rpc;
using Bun3.Server.Ticking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Players
{
    /// <summary>
    /// 접속 중 Player의 틱/주기 저장을 구동하는 TickLoop 잡. 순회(틱 루프 스레드)는
    /// 포스팅만 하고, 실행은 각 Player의 현재 세션 액터 안에서 일어난다(락 제로).
    /// 유예 중 Player는 건너뛴다 — detach 시 즉시 저장되므로 항상 저장된 상태다.
    /// 호스팅(AddPlayerServer)은 자동 배선하며, 비호스팅은 Register를 직접 호출한다.
    /// </summary>
    public sealed class PlayerTicker<TPlayer> where TPlayer : Player
    {
        private readonly PlayerRegistry<TPlayer> _registry;
        private readonly TimeSpan _tickInterval;
        private readonly TimeSpan _saveInterval;
        private readonly ILogger _logger;
        private readonly Action<TPlayer> _tickPlayer;   // 틱당 델리게이트 재할당 방지 캐시

        /// <summary>레지스트리와 옵션으로 티커를 구성한다. 옵션은 스냅샷된다.</summary>
        public PlayerTicker(PlayerRegistry<TPlayer> registry, PlayersOptions? options = null, ILogger? logger = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            var effective = options ?? new PlayersOptions();
            _tickInterval = effective.PlayerTickInterval;
            _saveInterval = effective.SaveInterval;
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
            _tickPlayer = TickPlayer;
        }

        /// <summary>틱 루프에 Player 틱 잡을 등록한다. loop.Start 전에 호출할 것.</summary>
        public void Register(TickLoop loop)
        {
            if (loop == null) throw new ArgumentNullException(nameof(loop));
            loop.Every(_tickInterval, TickAsync, "players");
        }

        internal ValueTask TickAsync(TimeSpan _)
        {
            _registry.ForEachPlayer(_tickPlayer);   // 무할당 순회 — 순회 중 추가/제거 안전
            return default;
        }

        private void TickPlayer(TPlayer player)
        {
            var session = player.CurrentSession;
            if (session == null)
            {
                return;   // 유예 중 — 틱 없음
            }

            // 틱 작업 클로저는 (player, session) 쌍 기준으로 캐시된다 — 재바인딩 시에만 재생성.
            // 캐시 필드는 틱 루프 스레드에서만 접근하므로 락 불필요.
            var work = player.TickWork;
            if (work == null || !ReferenceEquals(player.TickWorkSession, session))
            {
                work = CreateTickWork(player, session);
                player.TickWork = work;
                player.TickWorkSession = session;
            }

            if (!session.Post(work))
            {
                // 닫히는 중이거나 큐 포화 — 이번 틱 스킵, 다음 틱이 온다 (종료 경합은 정상 경로라 Debug)
                _logger.LogDebug("Player {AccountKey}: 세션 큐 포화/종료로 이번 틱 스킵", player.AccountKey);
            }
        }

        private Func<ValueTask> CreateTickWork(TPlayer player, RpcSession session) => async () =>
        {
            if (!ReferenceEquals(player.CurrentSession, session))
            {
                return;   // 실행 시점 재확인 — NewWins 이전/킥 경합 방어
            }

            var now = DateTime.UtcNow.Ticks;
            var delta = TimeSpan.FromTicks(Math.Max(0, now - player.LastTickAtTicksUtc));
            player.LastTickAtTicksUtc = now;
            try
            {
                await player.OnTickAsync(delta).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnTickAsync 예외 (Player {AccountKey})", player.AccountKey);
            }

            if (!ReferenceEquals(player.CurrentSession, session))
            {
                return;   // OnTickAsync 도중 소유권 이전(NewWins) — 저장은 새 세션의 스윕이 맡는다 (dirty 유지)
            }

            if (now >= player.NextSaveAtTicksUtc && player.IsDirty)
            {
                player.NextSaveAtTicksUtc = now + _saveInterval.Ticks;
                await player.TrySaveAsync(_logger).ConfigureAwait(false);
            }
        };
    }
}

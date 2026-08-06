using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bun3.Server.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bun3.Server.Players
{
    /// <summary>
    /// accountKey → Player 레지스트리. 프로세스 내 메모리 전제(다중 서버 스케일아웃은
    /// 별도 설계). 계정 키 단위 직렬화는 스트라이프 락 256개로 수행한다.
    /// </summary>
    public sealed class PlayerRegistry<TPlayer> where TPlayer : Player
    {
        private const int StripeCount = 256;

        private sealed class Entry
        {
            public readonly TPlayer Player;
            public PlayerSession<TPlayer>? Session;
            public long DetachedAtTicksUtc;   // 0 = 접속 중

            public Entry(TPlayer player) => Player = player;
        }

        private readonly Func<string, ValueTask<TPlayer>> _loader;
        private readonly TimeSpan _gracePeriod;
        private readonly DuplicateLoginPolicy _duplicatePolicy;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>();
        private readonly SemaphoreSlim[] _stripes;
        private readonly CancellationTokenSource _sweepCts = new CancellationTokenSource();

        /// <summary>
        /// 계정 키 로더, 옵션, 로거로 레지스트리를 생성한다. GracePeriod &gt; 0이면
        /// 백그라운드 유예 스윕 루프를 즉시 시작한다.
        /// </summary>
        public PlayerRegistry(
            Func<string, ValueTask<TPlayer>> loader,
            PlayersOptions? options = null,
            ILogger? logger = null)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            var effectiveOptions = options ?? new PlayersOptions();
            _gracePeriod = effectiveOptions.GracePeriod;   // 생성 시점 스냅샷 — 이후 옵션 변이는 무시
            _duplicatePolicy = effectiveOptions.DuplicatePolicy;
            _logger = new SafeLogger(logger ?? NullLogger.Instance);
            _stripes = new SemaphoreSlim[StripeCount];
            for (var i = 0; i < StripeCount; i++)
            {
                _stripes[i] = new SemaphoreSlim(1, 1);
            }

            if (_gracePeriod > TimeSpan.Zero)
            {
                _ = RunSweepAsync(_sweepCts.Token);
            }
        }

        /// <summary>현재 레지스트리의 Player 스냅샷 (브로드캐스트용).</summary>
        public IReadOnlyCollection<TPlayer> Players => _entries.Values.Select(e => e.Player).ToArray();

        /// <summary>accountKey로 조회. 없으면 null.</summary>
        public TPlayer? TryGet(string accountKey) =>
            _entries.TryGetValue(accountKey, out var entry) ? entry.Player : null;

        /// <summary>세션 팩토리를 감싸 레지스트리·허용 목록을 부착한다. Players 사용의 필수 경로.</summary>
        public Func<IConnection, TSession> Wrap<TSession>(
            PlayersConfig<TSession> config, Func<IConnection, TSession> factory)
            where TSession : PlayerSession<TPlayer>
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            var unauthenticatedTypes = new HashSet<Type>(config.UnauthenticatedTypes);   // 검증 시점 스냅샷 — 뒤늦은 등록이 게이트를 몰래 열지 못한다
            return connection =>
            {
                var session = factory(connection);
                session.AttachPlayers(this, unauthenticatedTypes);
                return session;
            };
        }

        internal async ValueTask<SignInResult<TPlayer>> SignInAsync(
            PlayerSession<TPlayer> session, string accountKey)
        {
            if (string.IsNullOrEmpty(accountKey))
            {
                throw new ArgumentException("accountKey가 비어 있다.", nameof(accountKey));
            }

            if (session.Player != null)
            {
                throw new InvalidOperationException("이미 인증된 세션에서 SignInAsync를 재호출했다.");
            }

            PlayerSession<TPlayer>? kickAfterRelease = null;
            var stripe = GetStripe(accountKey);
            await stripe.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_entries.TryGetValue(accountKey, out var entry))
                {
                    if (entry.Session != null && _duplicatePolicy == DuplicateLoginPolicy.RejectNew)
                    {
                        throw new DuplicateLoginException(accountKey);
                    }

                    kickAfterRelease = entry.Session;   // NewWins: 락 해제 후 킥 (재진입 교착 방지)
                    entry.DetachedAtTicksUtc = 0;
                    Attach(entry, session);
                    await SafeHookAsync(() => entry.Player.OnAttachedAsync(true), "OnAttachedAsync").ConfigureAwait(false);
                    return new SignInResult<TPlayer>(entry.Player, true);
                }

                // ponytail: 스트라이프 락 안 DB 로드 — 같은 스트라이프의 다른 키가 로드 시간만큼
                // 대기한다(256 스트라이프라 희박). 병목이 측정되면 키별 락 승격.
                var player = await _loader(accountKey).ConfigureAwait(false);
                player.AccountKey = accountKey;
                var created = new Entry(player);
                _entries[accountKey] = created;
                Attach(created, session);
                await SafeHookAsync(() => player.OnAttachedAsync(false), "OnAttachedAsync").ConfigureAwait(false);
                return new SignInResult<TPlayer>(player, false);
            }
            finally
            {
                stripe.Release();
                kickAfterRelease?.Kick();
            }
        }

        internal async ValueTask HandleSessionClosedAsync(PlayerSession<TPlayer> session)
        {
            var player = session.Player;
            if (player == null)
            {
                return;   // 미인증 세션
            }

            var accountKey = player.AccountKey;
            var stripe = GetStripe(accountKey);
            await stripe.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_entries.TryGetValue(accountKey, out var entry)
                    || !ReferenceEquals(entry.Session, session))
                {
                    return;   // 이미 다른 세션으로 재바인딩(중복 로그인)되었거나 은퇴함
                }

                entry.Session = null;
                player.CurrentSession = null;
                await SafeHookAsync(() => player.OnDetachedAsync(), "OnDetachedAsync").ConfigureAwait(false);

                if (_gracePeriod <= TimeSpan.Zero)
                {
                    _entries.TryRemove(accountKey, out _);
                    await SafeHookAsync(() => player.OnRetiredAsync(), "OnRetiredAsync").ConfigureAwait(false);
                }
                else
                {
                    entry.DetachedAtTicksUtc = DateTime.UtcNow.Ticks;
                }
            }
            finally
            {
                stripe.Release();
            }
        }

        /// <summary>전 Player 은퇴(저장 플러시) — 서버 정지 후 호출. 스윕도 함께 멈춘다.</summary>
        public async ValueTask RetireAllAsync()
        {
            _sweepCts.Cancel();
            foreach (var accountKey in _entries.Keys.ToArray())
            {
                PlayerSession<TPlayer>? toKick = null;
                var stripe = GetStripe(accountKey);
                await stripe.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_entries.TryRemove(accountKey, out var entry))
                    {
                        toKick = entry.Session;
                        entry.Player.CurrentSession = null;
                        await SafeHookAsync(() => entry.Player.OnRetiredAsync(), "OnRetiredAsync").ConfigureAwait(false);
                    }
                }
                finally
                {
                    stripe.Release();
                    toKick?.Kick();
                }
            }
        }

        private void Attach(Entry entry, PlayerSession<TPlayer> session)
        {
            entry.Session = session;
            entry.Player.CurrentSession = session;
            session.SetPlayer(entry.Player);
        }

        private SemaphoreSlim GetStripe(string accountKey) =>
            _stripes[(accountKey.GetHashCode() & int.MaxValue) % StripeCount];

        private async ValueTask SafeHookAsync(Func<ValueTask> hook, string name)
        {
            try
            {
                await hook().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Player 훅 {Hook} 예외", name);
            }
        }

        private async Task RunSweepAsync(CancellationToken ct)
        {
            var half = TimeSpan.FromTicks(_gracePeriod.Ticks / 2);
            var floor = TimeSpan.FromMilliseconds(50);
            var ceiling = TimeSpan.FromSeconds(15);
            var interval = half < floor ? floor : (half > ceiling ? ceiling : half);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    var cutoff = DateTime.UtcNow.Ticks - _gracePeriod.Ticks;
                    foreach (var pair in _entries)
                    {
                        var detachedAt = Volatile.Read(ref pair.Value.DetachedAtTicksUtc);
                        if (detachedAt == 0 || detachedAt > cutoff)
                        {
                            continue;
                        }

                        await RetireIfStillExpiredAsync(pair.Key, cutoff).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // RetireAll/종료로 인한 정상 취소
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "유예 스윕 루프 예외 — 스윕 중단");
            }
        }

        private async ValueTask RetireIfStillExpiredAsync(string accountKey, long cutoff)
        {
            var stripe = GetStripe(accountKey);
            await stripe.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_entries.TryGetValue(accountKey, out var entry)
                    && entry.Session == null
                    && entry.DetachedAtTicksUtc != 0
                    && entry.DetachedAtTicksUtc <= cutoff)
                {
                    _entries.TryRemove(accountKey, out _);
                    await SafeHookAsync(() => entry.Player.OnRetiredAsync(), "OnRetiredAsync").ConfigureAwait(false);
                }
            }
            finally
            {
                stripe.Release();
            }
        }
    }
}

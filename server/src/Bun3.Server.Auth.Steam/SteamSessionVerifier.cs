using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>클라 호스트(리슨 서버)용 Steam 검증기 — BeginAuthSession 네이티브 흐름의
    /// 상관관계(correlation)만 소유하고, 네이티브 호출은 게임이 델리게이트로 꽂는다.
    /// credential = "steamId64:ticketHex" (클라가 자기 SteamID를 주장 + 티켓).
    ///
    /// 게임 글루 계약: Steamworks의 ValidateAuthTicketResponse 콜백에서
    /// <see cref="HandleValidateResult"/>를 호출하고, 플레이어 퇴장 시
    /// <see cref="EndSession"/>을 호출한다. 실패·타임아웃 정리는 검증기가 스스로 한다.</summary>
    public sealed class SteamSessionVerifier : IIdentityVerifier
    {
        private readonly Func<byte[], ulong, int> _beginSession;
        private readonly Action<ulong> _endSession;
        private readonly TimeSpan _timeout;
        private readonly ConcurrentDictionary<ulong, TaskCompletionSource<AuthResult>> _pending = new();

        /// <inheritdoc />
        public string Provider => "steam";

        /// <summary>접속 승인 "이후" 도착한 무효화 통지(게임 중 밴, 티켓 취소) —
        /// (SteamID64, EAuthSessionResponse). 게임이 구독해서 해당 플레이어를 킥한다.
        /// 타임아웃으로 이미 실패한 검증의 늦은 콜백에도 발화될 수 있다 — steamId 킥은 그 경우 무해한 no-op이다.</summary>
        public event Action<ulong, int>? SessionInvalidated;

        /// <summary>검증기를 생성한다. 델리게이트 누락은 즉시 거부(부팅 시 즉사).</summary>
        public SteamSessionVerifier(SteamSessionOptions options)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            _beginSession = options.BeginSession
                ?? throw new ArgumentException("BeginSession delegate is required.", nameof(options));
            _endSession = options.EndSession
                ?? throw new ArgumentException("EndSession delegate is required.", nameof(options));
            _timeout = options.Timeout;
        }

        /// <inheritdoc />
        public async ValueTask<AuthResult> VerifyAsync(string credential, CancellationToken ct = default)
        {
            if (!TryParseCredential(credential, out var steamId, out var ticket))
                return SteamAuthResult.Fail(AuthFailure.InvalidCredential, "credential must be \"steamId64:ticketHex\"", 0);

            var tcs = new TaskCompletionSource<AuthResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(steamId, tcs))
                return SteamAuthResult.Fail(AuthFailure.Rejected, "verification already pending for this steamId", 0, steamId: steamId);

            int beginResult;
            try
            {
                beginResult = _beginSession(ticket, steamId);
            }
            catch
            {
                _pending.TryRemove(steamId, out _);
                throw;
            }

            if (beginResult != 0)
            {
                if (!_pending.TryRemove(steamId, out _))
                    return await tcs.Task.ConfigureAwait(false);   // 콜백이 경합에서 이김
                _endSession(steamId);
                return SteamAuthResult.Fail(AuthFailure.Rejected, "BeginAuthSession failed", beginResult, steamId: steamId);
            }

            var delay = Task.Delay(_timeout, ct);
            var completed = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);
            if (completed == tcs.Task)
                return await tcs.Task.ConfigureAwait(false);

            // 타임아웃 또는 외부 취소 — 콜백과의 경합은 pending 제거로 판정
            if (!_pending.TryRemove(steamId, out _))
                return await tcs.Task.ConfigureAwait(false);   // 콜백이 경합에서 이김

            _endSession(steamId);
            if (ct.IsCancellationRequested)
                await delay.ConfigureAwait(false);   // OperationCanceledException 전파 (인프라 취소)
            return SteamAuthResult.Fail(AuthFailure.Timeout, "auth callback not received", 0, steamId: steamId);
        }

        /// <summary>게임 글루가 Steamworks의 ValidateAuthTicketResponse 콜백에서 호출한다.
        /// 검증 대기 중이면 판정을 완성하고, 아니면(접속 승인 후 무효화) SessionInvalidated를 발화한다.
        /// 어느 스레드에서 호출해도 안전하며, Steam 콜백 스레드를 붙잡지 않는다.</summary>
        public void HandleValidateResult(ulong steamId, int authSessionResponse)
        {
            if (_pending.TryRemove(steamId, out var tcs))
            {
                AuthResult result;
                if (authSessionResponse == 0)
                {
                    result = SteamAuthResult.Success(steamId, steamId, false, false);
                }
                else
                {
                    _endSession(steamId);   // 실패 정리 규약
                    result = SteamAuthResult.Fail(MapFailure(authSessionResponse), "auth session rejected", authSessionResponse, steamId: steamId);
                }
                tcs.TrySetResult(result);
                return;
            }

            if (authSessionResponse != 0)
                SessionInvalidated?.Invoke(steamId, authSessionResponse);
        }

        /// <summary>인증 세션을 닫는다 — 성공 검증 후 플레이어 퇴장 시(OnRetiredAsync 등) 게임이 호출한다.</summary>
        public void EndSession(ulong steamId) => _endSession(steamId);

        private static AuthFailure MapFailure(int authSessionResponse) =>
            authSessionResponse == 3 || authSessionResponse == 9   // VACBanned / PublisherIssuedBan
                ? AuthFailure.Banned
                : AuthFailure.Rejected;

        private static bool TryParseCredential(string? credential, out ulong steamId, out byte[] ticket)
        {
            steamId = 0;
            ticket = Array.Empty<byte>();
            if (string.IsNullOrEmpty(credential)) return false;

            var separator = credential.IndexOf(':');
            if (separator <= 0 || separator == credential.Length - 1) return false;

            if (!ulong.TryParse(credential.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out steamId)
                || steamId == 0)
                return false;

            var hex = credential.Substring(separator + 1);
            if (hex.Length % 2 != 0) return false;

            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                var hi = HexValue(hex[i * 2]);
                var lo = HexValue(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return false;
                bytes[i] = (byte)((hi << 4) | lo);
            }

            ticket = bytes;
            return true;
        }

        private static int HexValue(char c) =>
            c >= '0' && c <= '9' ? c - '0' :
            c >= 'a' && c <= 'f' ? c - 'a' + 10 :
            c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
    }
}

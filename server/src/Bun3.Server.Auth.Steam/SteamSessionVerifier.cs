using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Bun3.Server.Auth.Steam
{
    /// <summary>Steam verifier for client hosts (listen servers) — owns only the correlation of the
    /// BeginAuthSession native flow; the game plugs in the native calls as delegates.
    /// credential = "steamId64:ticketHex" (the client claims its SteamID + ticket).
    ///
    /// Game glue contract: call <see cref="HandleValidateResult"/> from Steamworks'
    /// ValidateAuthTicketResponse callback, and call <see cref="EndSession"/> when the player
    /// leaves. Failure/timeout cleanup is handled by the verifier itself.</summary>
    public sealed class SteamSessionVerifier : IIdentityVerifier
    {
        private readonly Func<byte[], ulong, int> _beginSession;
        private readonly Action<ulong> _endSession;
        private readonly TimeSpan _timeout;
        private readonly ConcurrentDictionary<ulong, TaskCompletionSource<AuthResult>> _pending = new();

        /// <inheritdoc />
        public string Provider => "steam";

        /// <summary>Invalidation notice arriving after admission (mid-game ban, ticket cancel) —
        /// (SteamID64, EAuthSessionResponse). The game subscribes and kicks that player.
        /// May also fire for a late callback of a verification already failed by timeout — kicking that steamId is then a harmless no-op.</summary>
        public event Action<ulong, int>? SessionInvalidated;

        /// <summary>Creates the verifier. Missing delegates are rejected immediately (dies at boot).</summary>
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
                    return await tcs.Task.ConfigureAwait(false);   // the callback won the race
                _endSession(steamId);
                return SteamAuthResult.Fail(AuthFailure.Rejected, "BeginAuthSession failed", beginResult, steamId: steamId);
            }

            var delay = Task.Delay(_timeout, ct);
            var completed = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);
            if (completed == tcs.Task)
                return await tcs.Task.ConfigureAwait(false);

            // Timeout or external cancellation — the race with the callback is decided by the pending removal.
            if (!_pending.TryRemove(steamId, out _))
                return await tcs.Task.ConfigureAwait(false);   // the callback won the race

            _endSession(steamId);
            if (ct.IsCancellationRequested)
                await delay.ConfigureAwait(false);   // propagates OperationCanceledException (infrastructure cancellation)
            return SteamAuthResult.Fail(AuthFailure.Timeout, "auth callback not received", 0, steamId: steamId);
        }

        /// <summary>Called by game glue from Steamworks' ValidateAuthTicketResponse callback.
        /// Completes the verdict if a verification is pending; otherwise (post-admission invalidation) fires SessionInvalidated.
        /// Safe from any thread and never blocks the Steam callback thread.</summary>
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
                    _endSession(steamId);   // failure cleanup contract
                    result = SteamAuthResult.Fail(MapFailure(authSessionResponse), "auth session rejected", authSessionResponse, steamId: steamId);
                }
                tcs.TrySetResult(result);
                return;
            }

            if (authSessionResponse != 0)
                SessionInvalidated?.Invoke(steamId, authSessionResponse);
        }

        /// <summary>Closes the auth session — the game calls this when a successfully verified player leaves (OnRetiredAsync etc.).</summary>
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
                if (!Uri.IsHexDigit(hex[i * 2]) || !Uri.IsHexDigit(hex[i * 2 + 1])) return false;
                bytes[i] = (byte)((Uri.FromHex(hex[i * 2]) << 4) | Uri.FromHex(hex[i * 2 + 1]));
            }

            ticket = bytes;
            return true;
        }
    }
}

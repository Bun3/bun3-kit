namespace Bun3.Unity.Telemetry.GameAnalytics
{
    /// <summary>The GameAnalytics SDK calls the sink makes, behind an interface so tests can replace the static SDK.</summary>
    public interface IGameAnalyticsApi
    {
        /// <summary>Sets the user id. The SDK accepts it only before <see cref="Initialize"/>.</summary>
        void SetCustomId(string id);
        /// <summary>Sets the build reported with every event. A null build keeps the SDK default.</summary>
        void SetBuild(string build);
        /// <summary>Allows <paramref name="environment"/> for custom dimension 01 and sets it. A null environment sets nothing.</summary>
        void SetEnvironment(string environment);
        /// <summary>Sets the game key and secret key for the running platform.</summary>
        void SetKeys(string gameKey, string secretKey);
        /// <summary>Turns off the SDK's own error, FPS, memory and hardware reporting so only sessions and mapped events are sent.</summary>
        void DisableErrorReporting();
        /// <summary>Initializes the SDK and starts its session.</summary>
        void Initialize();
        /// <summary>Turns event submission on or off.</summary>
        void SetEnabled(bool enabled);
        /// <summary>Sends a design event with an optional value.</summary>
        void Design(string id, float? value);
        /// <summary>Sends a progression event. Missing levels are null.</summary>
        void Progression(GameAnalyticsEventKind status, string p1, string p2, string p3);
    }
}

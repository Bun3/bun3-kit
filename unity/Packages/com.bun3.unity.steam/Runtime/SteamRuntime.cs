using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Bun3.Unity.Core.PlayerLoop;
using Steamworks;
using UnityEngine;

namespace Bun3.Unity.Steam
{
    /// <summary>Outcome of <see cref="SteamRuntime.Initialize"/>.</summary>
    public enum SteamInitResult
    {
        /// <summary>Steam is initialized and callbacks run every frame.</summary>
        Ready,

        /// <summary>Steam is relaunching the game; quit this process now.</summary>
        RestartingThroughSteam,

        /// <summary>Steam is not running, not signed in, or the native library is missing.</summary>
        Unavailable,
    }

    /// <summary>Options for <see cref="SteamRuntime.Initialize"/>.</summary>
    public struct SteamRuntimeOptions
    {
        /// <summary>The game's Steam App ID.</summary>
        public uint AppId;

        /// <summary>Relaunch through Steam when started outside it. Ignored in the Editor; use for release builds.</summary>
        public bool RestartThroughSteam;
    }

    /// <summary>
    /// Owns the Steamworks lifetime: initialization, relaunch through Steam, running callbacks before
    /// <c>Update</c>, and shutdown when the application quits. Main thread only.
    /// </summary>
    public static class SteamRuntime
    {
        private static class CallbackSystem
        {
        }

        /// <summary>Whether Steamworks is initialized.</summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>The signed-in user's Steam profile name; empty before initialization.</summary>
        public static string PersonaName => IsInitialized ? SteamFriends.GetPersonaName() : string.Empty;

        /// <summary>
        /// Initializes Steamworks once. The App ID is also published through the <c>SteamAppId</c> and
        /// <c>SteamGameId</c> environment variables so Editor and development runs need no <c>steam_appid.txt</c>.
        /// </summary>
        /// <param name="options">App ID and relaunch policy.</param>
        /// <param name="error">Steam's English failure message when the result is <see cref="SteamInitResult.Unavailable"/>; otherwise empty.</param>
        public static SteamInitResult Initialize(SteamRuntimeOptions options, out string error)
        {
            error = string.Empty;
            if (IsInitialized) return SteamInitResult.Ready;
            try
            {
                if (options.RestartThroughSteam && !Application.isEditor && SteamAPI.RestartAppIfNecessary(new AppId_t(options.AppId)))
                    return SteamInitResult.RestartingThroughSteam;
                // Set after the relaunch check so the variables cannot mask a launch outside Steam.
                string appId = options.AppId.ToString(CultureInfo.InvariantCulture);
                Environment.SetEnvironmentVariable("SteamAppId", appId);
                Environment.SetEnvironmentVariable("SteamGameId", appId);
                if (SteamAPI.InitEx(out error) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                    return SteamInitResult.Unavailable;
            }
            catch (Exception broken) when (broken is DllNotFoundException || broken is EntryPointNotFoundException || broken is BadImageFormatException)
            {
                error = broken.Message;
                return SteamInitResult.Unavailable;
            }
            error = string.Empty;
            IsInitialized = true;
            PlayerLoopSystemHelper.InsertSystemBefore(typeof(CallbackSystem), SteamAPI.RunCallbacks,
                typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
            return SteamInitResult.Ready;
        }

        /// <summary>
        /// Tells the player why the game cannot continue, then quits. Logs <paramref name="message"/> as an error;
        /// Windows players also get a blocking native error dialog, and the Editor shows a dialog and leaves
        /// Play Mode. Batch mode and other platforms only log, so unattended runs never wait on a window.
        /// Use when <see cref="Initialize"/> returns <see cref="SteamInitResult.Unavailable"/>.
        /// </summary>
        /// <param name="title">Dialog title, usually the product name.</param>
        /// <param name="message">Player-facing text in the player's language.</param>
        public static void QuitWithError(string title, string message)
        {
            Debug.LogError(message);
#if UNITY_EDITOR
            if (!Application.isBatchMode) UnityEditor.EditorUtility.DisplayDialog(title, message, "OK");
            UnityEditor.EditorApplication.ExitPlaymode();
#else
#if UNITY_STANDALONE_WIN
            if (!Application.isBatchMode) MessageBoxW(IntPtr.Zero, message, title, ErrorDialogFlags);
#endif
            Application.Quit();
#endif
        }

        /// <summary>Stops running callbacks and shuts Steamworks down. Called automatically on quit; idempotent.</summary>
        public static void Shutdown()
        {
            Application.quitting -= Shutdown;
            if (!IsInitialized) return;
            PlayerLoopSystemHelper.TryRemoveSystem(typeof(CallbackSystem));
            SteamAPI.Shutdown();
            IsInitialized = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => IsInitialized = false;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // MB_OK | MB_ICONERROR | MB_SETFOREGROUND | MB_TOPMOST: keeps the dialog above a fullscreen game window.
        private const uint ErrorDialogFlags = 0x00000010 | 0x00010000 | 0x00040000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
#endif
    }
}

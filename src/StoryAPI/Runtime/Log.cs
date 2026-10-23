using BepInEx.Logging;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// One place for the plugin's output.
    ///
    /// Messages go through BepInEx's logger rather than Unity's, because that is what
    /// ends up in <c>BepInEx\LogOutput.log</c>. Anything written with
    /// <c>UnityEngine.Debug</c> only reaches the Unity player log, which is a separate
    /// file people rarely open.
    /// </summary>
    internal static class Log
    {
        private static readonly ManualLogSource s_source = Logger.CreateLogSource("StoryAPI");

        public static void Info(string message) => s_source.LogInfo(message);

        public static void Warn(string message) => s_source.LogWarning(message);

        public static void Error(string message) => s_source.LogError(message);

        /// <summary>Log an exception with enough context to act on it.</summary>
        public static void Error(string context, System.Exception exception)
        {
            s_source.LogError(context + ": " + exception);
        }

        /// <summary>
        /// Fallback used before the plugin has initialised. There is rarely a case
        /// where this matters, but a lost message during startup is hard to diagnose.
        /// </summary>
        public static void InfoOrUnity(string message)
        {
            s_source.LogInfo(message);
            Debug.Log("[StoryAPI] " + message);
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StoryAPI.Game;
using UnityEngine;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// Finds story mods, loads them, and keeps them loaded.
    ///
    /// Mods live in <c>BepInEx/plugins/StoryAPI/Mods/&lt;name&gt;/</c>. The manager
    /// re-reads that folder on demand so a script can be edited and re-run without
    /// restarting the game, which is the whole point of making this a scripting layer
    /// rather than a set of compiled labels.
    /// </summary>
    public static class ModManager
    {
        private static readonly List<StoryMod> s_mods = new List<StoryMod>();

        /// <summary>Every mod currently known.</summary>
        public static IReadOnlyList<StoryMod> Mods => s_mods;

        /// <summary>Root folder scanned for mods.</summary>
        public static string ModsRoot
        {
            get
            {
                string pluginDirectory = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                return Path.Combine(pluginDirectory ?? "", "Mods");
            }
        }

        /// <summary>
        /// Scan the mod folder and (re)load everything in it. Mods are unloaded first
        /// so their labels are removed cleanly and cannot linger between reloads.
        /// </summary>
        public static void ResetAnimation()
        {
            SpriteAnimator.Reset();
        }

        public static void ReloadAll()
        {
            SpriteAnimator.Reset();
            foreach (var mod in s_mods) mod.Unregister();
            s_mods.Clear();

            string root = ModsRoot;
            if (!Directory.Exists(root))
            {
                Log.Info("[StoryAPI] no mod folder at " + root + "; nothing to load.");
                return;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(root);
            }
            catch (Exception e)
            {
                Log.Error("[StoryAPI] could not scan " + root + ": " + e.Message);
                return;
            }

            foreach (string directory in directories.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var mod = new StoryMod(Path.GetFileName(directory), directory);
                mod.Load();
                s_mods.Add(mod);

                foreach (string error in mod.LoadErrors)
                {
                    Log.Error("[StoryAPI] " + mod.Name + ": " + error);
                }

                foreach (string warning in mod.Warnings)
                {
                    Log.Warn("[StoryAPI] " + mod.Name + ": " + warning);
                }

                // Registration needs the game's script to exist, which it only does
                // once the story has started. Until then the mod stays parsed and is
                // registered by the plugin as soon as the game is ready.
                if (GameBridge.Ready) mod.Register();

                // Anything the registration itself had to say, such as a hook that could
                // not be placed, has to be reported here as well. Reporting only at load
                // time hid every failure that happened once the game's script existed,
                // which left a mod looking like it simply did nothing.
                ReportWarnings(mod);
            }

            Log.Info("[StoryAPI] loaded " + s_mods.Count + " mod(s), " +
                      s_mods.Sum(m => m.Labels.Count) + " label(s).");
        }

        private static readonly Dictionary<string, int> s_reported =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Print a mod's warnings that have not been printed yet.
        ///
        /// Warnings can be added both while a mod is being read and while it is being
        /// registered, and the second kind only happens once the game is running, so
        /// this is called again after registration rather than only after loading.
        /// </summary>
        public static void ReportWarnings(StoryMod mod)
        {
            if (mod == null) return;

            int shown;
            s_reported.TryGetValue(mod.Name, out shown);

            for (int i = shown; i < mod.Warnings.Count; i++)
            {
                Log.Warn("[StoryAPI] " + mod.Name + ": " + mod.Warnings[i]);
            }

            if (mod.Warnings.Count != shown) s_reported[mod.Name] = mod.Warnings.Count;
        }

        /// <summary>
        /// Register everything that could not be registered earlier because the game
        /// had not finished starting. Safe to call repeatedly.
        /// </summary>
        public static void RegisterWhenReady()
        {
            if (!GameBridge.Ready) return;
            foreach (var mod in s_mods) mod.Register();

            // Registration is where a hook is actually resolved, so this is the point at
            // which a failure to place one becomes visible.
            foreach (var mod in s_mods) ReportWarnings(mod);
        }

        /// <summary>
        /// Advance background work, currently decoding mod audio.
        /// Cheap to call every frame; this is what keeps the game responsive while a
        /// long track loads.
        /// </summary>
        /// <summary>
        /// Translate base-game track names now that the game is actually running.
        ///
        /// The game's execution context only exists once a script is executing, so this
        /// cannot happen while mods are being loaded.
        /// </summary>
        public static void ResolveBaseAudio()
        {
            foreach (var mod in s_mods) mod.ResolveBaseAudio();
        }

        public static void Pump()
        {
            foreach (var mod in s_mods) mod.Assets.Pump();
        }

        /// <summary>
        /// The assets of a mod by its folder name. A save records which mod supplied a
        /// picture, so this is what lets the picture be found again after a restart.
        /// </summary>
        public static StoryAssets FindAssets(string modName)
        {
            if (string.IsNullOrEmpty(modName)) return null;

            foreach (var mod in s_mods)
            {
                if (string.Equals(mod.Name, modName, StringComparison.OrdinalIgnoreCase))
                {
                    return mod.Assets;
                }
            }

            return null;
        }

        /// <summary>Every label any loaded mod contributes.</summary>
        public static IEnumerable<string> AllLabels =>
            s_mods.SelectMany(mod => mod.Labels).OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        /// <summary>True when a label came from a mod rather than the base game.</summary>
        public static bool IsModLabel(string label)
        {
            return s_mods.Any(mod => mod.Labels.Contains(label));
        }
    }
}
using System;
using BepInEx;
using BepInEx.Configuration;
using StoryAPI.Game;
using StoryAPI.Runtime;
using UnityEngine;

namespace StoryAPI
{
    /// <summary>
    /// StoryAPI - a scripting layer for Doki Doki Literature Club Plus.
    ///
    /// The base game is a Unity port of a Ren'Py visual novel and still runs the whole
    /// story engine, but its scripts ship as compiled asset bundles. This plugin puts
    /// an editable layer on top: mod authors write plain <c>.rpy</c> files in their own
    /// folder, and the plugin compiles them into the game's own instruction format and
    /// registers the result as ordinary labels. As far as the story runner is
    /// concerned, a mod label is indistinguishable from a shipped one.
    /// </summary>
    /// <remarks>
    /// This has to derive from <see cref="BaseUnityPlugin"/> rather than being a static
    /// class: BepInEx only registers plugin types that inherit from it, and quietly
    /// ignores anything else.
    /// </remarks>
    [BepInPlugin(Guid, "StoryAPI", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "opencode.storyapi";
        public const string Version = "0.1.0";

private ConfigEntry<string> _reloadKey;
        private ConfigEntry<string> _jumpKey;
        private ConfigEntry<string> _jumpTarget;
        private ConfigEntry<bool> _scanBundles;
        private ConfigEntry<int> _testSlot;

        private KeyCode _reload = KeyCode.F10;
        private KeyCode _jump = KeyCode.F11;
        private bool _registered;
        private bool _bundlesReleased;

        private void Awake()
        {
            var config = new ConfigFile(System.IO.Path.Combine(
                Paths.ConfigPath, "storyapi.cfg"), true);

            // Note the argument order: BepInEx takes the value before the description.
            _reloadKey = config.Bind("Hotkeys", "Reload", "F10",
                "Re-read every mod folder without restarting the game.");
            _jumpKey = config.Bind("Hotkeys", "Jump", "F11",
                "Jump straight to the label configured below.");
_jumpTarget = config.Bind("Hotkeys", "JumpTarget", "storyapi_demo",
                "Label jumped to by the Jump hotkey.");
            _scanBundles = config.Bind("General", "ScanGameBundles", false,
                "Open the game's asset bundles from disk to build asset-reference.txt and the art " +
                "export. Off by default: this duplicates bundles the game already has open, and a " +
                "duplicate makes the game's own bundle loading fail, which shows up as a white " +
                "screen. Turn it on only to regenerate the reference for mod authors.");
            _testSlot = config.Bind("General", "TestSlot", 10,
                "The save slot the save and load keys use. A mod is often written, " +
                "saved, loaded and written again, and the game's own menus are slow " +
                "to get through while testing.");


            _reload = ParseKey(_reloadKey.Value, KeyCode.F10);
            _jump = ParseKey(_jumpKey.Value, KeyCode.F11);

            if (GameBridge.ProbeError != null)
            {
                // The bridge is what makes mod labels possible at all. Without it the
                // rest of the plugin would be dead code, so say so plainly.
                Logger.LogError("Incompatible with this game build: " + GameBridge.ProbeError);
                enabled = false;
                return;
            }

            Logger.LogInfo("Mod folder: " + ModManager.ModsRoot);
            ModManager.ReloadAll();

            // A save records the name of a picture rather than the picture itself, so
            // the mod's own art and music have to be put back after a load.
            SaveRestore.Install();
        }


        /// <summary>
        /// The last word before a frame is drawn.
        ///
        /// The game lays its own pictures out while it runs, so a picture the mod is
        /// moving has to be placed after that has happened, or the game's own placement
        /// would simply overwrite it.
        /// </summary>
        private void LateUpdate()
        {
            AtlScheduler.Tick(Time.unscaledDeltaTime);
        }


        private void Update()
        {
            GameBridge.Tick();
            ModManager.Pump();
            SpriteAnimator.Tick(Time.unscaledDeltaTime);
            ModAudio.Tick();

            // A transition that never finishes is the one thing that can make a script
            // stop, so it is worth saying so plainly when it happens.
            if (s_watchTransitions) GameBridge.ReportPendingTransitions();


            // Any bundle this plugin opened for itself is closed again once the game has
            // had time to load the label it needs. A leftover duplicate makes the game's
            // own bundle load fail, and that is a white screen.
            if (!_bundlesReleased && Time.unscaledTime > 12f)
            {
                _bundlesReleased = true;
                GameBridge.ReleaseSelfOpenedBundles();
            }

            if (Input.GetKeyDown(_reload))
            {
                Logger.LogInfo("Reloading mods...");
                ModManager.ReloadAll();
                ModManager.RegisterWhenReady();
                Logger.LogInfo("Now live: " + DescribeMods());
                _registered = GameBridge.Ready;
            }

            if (Input.GetKeyDown(_jump) && GameBridge.Ready)
            {
                string target = _jumpTarget?.Value ?? string.Empty;
                if (string.IsNullOrEmpty(target)) return;

                // Restart rather than jump: a mod label has no meaningful relationship to
                // whatever the player was doing, so it is entered from a clean state.
                ModManager.ResolveBaseAudio();
                Logger.LogInfo(GameBridge.StartAt(target)
                    ? "Entered '" + target + "'."
                    : "Could not enter '" + target + "'.");
            }

            // Saving and loading a slot with one key. A mod is usually written, saved,
            // loaded and written again, and the game's own menus are slow to get
            // through while testing.
            if (Input.GetKeyDown(KeyCode.F12))
            {
                if (Input.GetKey(KeyCode.LeftShift)) LoadTestSlot();
                else SaveTestSlot();
            }


// The audio scope only exists once a script is running, so the reference is
            // written a second time at that point to pick up the music and sound names.
            if (!_registered && GameBridge.Ready)
            {
                _registered = true;
                DumpReferenceOnce();
                try
                {
                    string home = System.IO.Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location);
                    string listing = System.IO.Path.Combine(home, "script-reference.txt");

                    // The listing is a megabyte of the whole script, so it is written only when asked for
                    // or when it is missing. Regenerate it with General/WriteScriptReference.
                    if (!System.IO.File.Exists(listing))
                    {
                        GameBridge.DumpScript(listing);
                    }
                }
                catch (System.Exception e)
                {
                    Logger.LogError("Could not write the script reference: " + e.Message);
                }
                ModManager.RegisterWhenReady();
                Logger.LogInfo("Mod labels are live: " + DescribeMods());
            }
            else if (!s_audioDumped && GameBridge.Context != null)
            {
                s_audioDumped = true;
                DumpAudioReference();
            }
        }

        private static bool s_audioDumped;

        /// <summary>
        /// Rewrite the reference now that a script is running, so that the music and sound
        /// table is complete. Without a running script the game has not built its audio
        /// scope and that part of the list would come out empty.
        /// </summary>
        private void DumpAudioReference()
        {
            try
            {
                string folder = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                GameBridge.DumpReference(System.IO.Path.Combine(folder, "asset-reference.txt"));
                Logger.LogInfo("Rewrote the asset reference with audio in scope.");
            }
            catch (System.Exception e)
            {
                Logger.LogError("Could not rewrite the asset reference: " + e.Message);
            }
        }

        private static bool s_watchTransitions = true;


        private static bool s_dumped;

        /// <summary>
        /// Write the name reference out once, the first time the game's script exists.
        /// This is the list mod authors work from, so it is taken from the running game.
        /// </summary>
        private void DumpReferenceOnce()
        {
            if (s_dumped) return;
            s_dumped = true;

            try
            {
                string folder = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string path = System.IO.Path.Combine(folder, "asset-reference.txt");
GameBridge.DumpReference(path);
                Logger.LogInfo("Wrote the asset reference to " + path);

                // Opening the game's bundles from disk is what used to break the game, so it
                // is opt-in now. Everything StoryAPI needs while running reads the bundles
                // the game already has open.
                if (!_scanBundles.Value)
                {
                    Logger.LogInfo(
                        "Skipped the disk bundle scan (General/ScanGameBundles is off). " +
                        "Set it to true to regenerate the art and audio exports.");
                    return;
                }

                // Also export the art, so mod authors can see what each name looks like.
                string art = System.IO.Path.Combine(folder, "art");
                Logger.LogInfo("Audio paths: " + GameBridge.DumpAudioPaths(
                    System.IO.Path.Combine(folder, "audio-paths.txt")));
                int count = GameBridge.DumpTextures(art);
                Logger.LogInfo("Wrote " + count + " image(s) to " + art);

                // A pose is a set of parts rather than a picture, so what a pose is
                // made of is written out separately.
                GameBridge.DumpPoses(System.IO.Path.Combine(folder, "pose-reference.txt"));
            }
            catch (Exception e)
            {
                Logger.LogError("Could not write the asset reference: " + e.Message);
            }
        }

        /// <summary>
        /// Write the current moment to the test slot.
        /// </summary>
        private void SaveTestSlot()
        {
            try
            {
                int slot = _testSlot?.Value ?? 1;
                Logger.LogInfo("Saving to slot " + slot + "...");
                Renpy.Saves.Save(slot);
            }
            catch (Exception e)
            {
                Logger.LogError("Could not save: " + e.Message);
            }
        }

        /// <summary>
        /// Load the test slot again.
        /// </summary>
        private void LoadTestSlot()
        {
            try
            {
                int slot = _testSlot?.Value ?? 1;
                Logger.LogInfo("Loading slot " + slot + "...");
                Renpy.Saves.Load(slot);
            }
            catch (Exception e)
            {
                Logger.LogError("Could not load: " + e.Message);
            }
        }

        private static string DescribeMods()

        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var mod in ModManager.Mods)
            {
                parts.Add(mod.Name + " [" + string.Join(", ", mod.Labels.ToArray()) + "]");
            }
            return parts.Count == 0 ? "(none)" : string.Join(" | ", parts.ToArray());
        }

        private static KeyCode ParseKey(string name, KeyCode fallback)
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;
            return Enum.TryParse(name.Trim(), ignoreCase: true, out KeyCode parsed) && parsed != KeyCode.None
                ? parsed
                : fallback;
        }
    }
}

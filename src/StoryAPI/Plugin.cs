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
    /// an editable layer on top: mod authors write plain .rpy files in their own folder,
    /// and the plugin compiles them into the game's own instruction format and registers
    /// the result as ordinary labels.
    /// </summary>
    [BepInPlugin(Guid, "StoryAPI", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "storyapi";
        public const string Version = "0.1.1";

        private ConfigEntry<string> _reloadKey;
        private ConfigEntry<string> _jumpKey;
        private ConfigEntry<string> _jumpTarget;
        private ConfigEntry<bool> _scanBundles;
        private ConfigEntry<bool> _generateReferences;
        private ConfigEntry<int> _testSlot;

        private KeyCode _reload = KeyCode.F10;
        private KeyCode _jump = KeyCode.F11;
        private bool _registered;
        private bool _bundlesReleased;

        // Temporary startup diagnostics. Remove this source after the cause is identified.
        private float _memoryDiagStart;
        private float _memoryDiagNext;
        private float _memoryDiagUntil;
        private bool _memoryReadyBeforeLogged;
        private bool _memoryReadyAfterLogged;

        private void Awake()
        {
            _memoryDiagStart = Time.realtimeSinceStartup;
            _memoryDiagNext = _memoryDiagStart;
            _memoryDiagUntil = _memoryDiagStart + 120f;
            Logger.LogInfo("[StoryAPI-MEM] DIAGNOSTIC BUILD ACTIVE; assembly=" +
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            LogMemory("Awake.enter");

            var config = new ConfigFile(System.IO.Path.Combine(
                Paths.ConfigPath, "storyapi.cfg"), true);

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
            _generateReferences = config.Bind("General", "GenerateReferences", false,
                "Write asset-reference.txt and script-reference.txt for mod development. " +
                "Off by default because reference generation scans game script and asset metadata.");
            _testSlot = config.Bind("General", "TestSlot", 10,
                "The save slot the save and load keys use. A mod is often written, saved, " +
                "loaded and written again, and the game's own menus are slow to get through while testing.");

            _reload = ParseKey(_reloadKey.Value, KeyCode.F10);
            _jump = ParseKey(_jumpKey.Value, KeyCode.F11);
            Logger.LogInfo("[StoryAPI-MEM] config: ScanGameBundles=" + _scanBundles.Value +
                ", GenerateReferences=" + _generateReferences.Value);
            LogMemory("Awake.after_config");

            if (GameBridge.ProbeError != null)
            {
                Logger.LogError("Incompatible with this game build: " + GameBridge.ProbeError);
                enabled = false;
                return;
            }

            Logger.LogInfo("Mod folder: " + ModManager.ModsRoot);
            LogMemory("Awake.before_ReloadAll");
            ModManager.ReloadAll();
            LogMemory("Awake.after_ReloadAll");
            // A save records the name of a picture rather than the picture itself, so
            // the mod's own art and music have to be put back after a load.
            SaveRestore.Install();
            LogMemory("Awake.after_SaveRestore.Install");
        }

        /// <summary>The last word before a frame is drawn.</summary>
        private void LateUpdate()
        {
            AtlScheduler.Tick(Time.unscaledDeltaTime);
        }

        private void Update()
        {
            GameBridge.Tick();
            // Keep mod dialogue available if the user changes the game's language,
            // without eagerly loading every locale table during mod registration.
            GameBridge.SyncRegisteredTextToCurrentLanguage();
            ModManager.Pump();
            SpriteAnimator.Tick(Time.unscaledDeltaTime);
            ModAudio.Tick();

            // Sample only during the first two minutes; periodic samples show exactly
            // when the process starts reserving/allocating gigabytes during game load.
            float now = Time.realtimeSinceStartup;
            if (now <= _memoryDiagUntil && now >= _memoryDiagNext)
            {
                _memoryDiagNext = now + 2f;
                LogMemory("startup.t+" + (now - _memoryDiagStart).ToString("0.0") + "s");
            }

            if (s_watchTransitions) GameBridge.ReportPendingTransitions();

            // Release any temporary bundles this plugin opened after the game has had
            // time to load the label it needs.
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
                ModManager.ResolveBaseAudio();
                Logger.LogInfo(GameBridge.StartAt(target)
                    ? "Entered '" + target + "'."
                    : "Could not enter '" + target + "'.");
            }

            if (Input.GetKeyDown(KeyCode.F12))
            {
                if (Input.GetKey(KeyCode.LeftShift)) LoadTestSlot();
                else SaveTestSlot();
            }

            // Reference exports are developer diagnostics. DumpScript walks the script
            // tables and references are not required to register runtime mod labels.
            if (!_registered && GameBridge.Ready)
            {
                _registered = true;
                if (!_memoryReadyBeforeLogged)
                {
                    _memoryReadyBeforeLogged = true;
                    LogMemory("Ready.before_reference_and_registration");
                }

                if (_generateReferences.Value || _scanBundles.Value)
                {
                    DumpReferenceOnce();

                    if (_generateReferences.Value)
                    {
                        try
                        {
                            string home = System.IO.Path.GetDirectoryName(
                                System.Reflection.Assembly.GetExecutingAssembly().Location);
                            string listing = System.IO.Path.Combine(home, "script-reference.txt");
                            if (!System.IO.File.Exists(listing))
                            {
                                GameBridge.DumpScript(listing);
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.LogError("Could not write the script reference: " + e.Message);
                        }
                    }
                }
                else
                {
                    Logger.LogInfo("Automatic reference generation is disabled (General/GenerateReferences).");
                }

                // Register mod labels regardless of whether reference exports are enabled.
                LogMemory("Ready.before_RegisterWhenReady");
                ModManager.RegisterWhenReady();
                Logger.LogInfo("Mod labels are live: " + DescribeMods());
                if (!_memoryReadyAfterLogged)
                {
                    _memoryReadyAfterLogged = true;
                    LogMemory("Ready.after_RegisterWhenReady");
                }
            }
            else if ((_generateReferences.Value || _scanBundles.Value) &&
                     !s_audioDumped && GameBridge.Context != null)
            {
                s_audioDumped = true;
                DumpAudioReference();
            }
        }

        private static bool s_audioDumped;

        private void DumpAudioReference()
        {
            try
            {
                string folder = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                GameBridge.DumpReference(System.IO.Path.Combine(folder, "asset-reference.txt"));
                Logger.LogInfo("Rewrote the asset reference with audio in scope.");
            }
            catch (Exception e)
            {
                Logger.LogError("Could not rewrite the asset reference: " + e.Message);
            }
        }

        private static bool s_watchTransitions = true;
        private static bool s_dumped;

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

                if (!_scanBundles.Value)
                {
                    Logger.LogInfo(
                        "Skipped the disk bundle scan (General/ScanGameBundles is off). " +
                        "Set it to true to regenerate the art and audio exports.");
                    return;
                }

                string art = System.IO.Path.Combine(folder, "art");
                Logger.LogInfo("Audio paths: " + GameBridge.DumpAudioPaths(
                    System.IO.Path.Combine(folder, "audio-paths.txt")));
                int count = GameBridge.DumpTextures(art);
                Logger.LogInfo("Wrote " + count + " image(s) to " + art);
                GameBridge.DumpPoses(System.IO.Path.Combine(folder, "pose-reference.txt"));
            }
            catch (Exception e)
            {
                Logger.LogError("Could not write the asset reference: " + e.Message);
            }
        }

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

        private void LogMemory(string phase)
        {
            try
            {
                long workingSet = -1;
                long privateBytes = -1;
                try
                {
                    using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    {
                        process.Refresh();
                        workingSet = process.WorkingSet64;
                        privateBytes = process.PrivateMemorySize64;
                    }
                }
                catch { }

                long managedBytes = GC.GetTotalMemory(false);
                long unityAllocated = ReadUnityProfilerCounter("GetTotalAllocatedMemoryLong");
                long unityReserved = ReadUnityProfilerCounter("GetTotalReservedMemoryLong");

                int modCount = 0;
                int imageCount = 0;
                int audioCount = 0;
                int labelCount = 0;
                try
                {
                    foreach (var mod in ModManager.Mods)
                    {
                        if (mod == null) continue;
                        modCount++;
                        labelCount += mod.Labels == null ? 0 : mod.Labels.Count;
                        if (mod.Assets != null)
                        {
                            imageCount += mod.Assets.ImageCount;
                            audioCount += mod.Assets.AudioCount;
                        }
                    }
                }
                catch { }

                Logger.LogInfo("[StoryAPI-MEM] " + phase +
                    " | WS=" + MiB(workingSet) + " MiB" +
                    "; Private=" + MiB(privateBytes) + " MiB" +
                    "; ManagedHeap=" + MiB(managedBytes) + " MiB" +
                    "; UnityAllocated=" + MiB(unityAllocated) + " MiB" +
                    "; UnityReserved=" + MiB(unityReserved) + " MiB" +
                    "; Mods=" + modCount + "; Labels=" + labelCount +
                    "; Images=" + imageCount + "; AudioTracks=" + audioCount);
            }
            catch (Exception e)
            {
                Logger.LogWarning("[StoryAPI-MEM] Failed to sample memory at '" + phase + "': " + e.Message);
            }
        }

        private static long ReadUnityProfilerCounter(string methodName)
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType("UnityEngine.Profiling.Profiler", false);
                    if (type == null) continue;
                    var method = type.GetMethod(methodName,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                        null, Type.EmptyTypes, null);
                    if (method == null) continue;
                    object value = method.Invoke(null, null);
                    if (value != null) return Convert.ToInt64(value);
                }
            }
            catch { }
            return -1;
        }

        private static string MiB(long bytes)
        {
            return bytes < 0 ? "n/a" : (bytes / (1024d * 1024d)).ToString("0.0");
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
            return Enum.TryParse(name.Trim(), true, out KeyCode parsed) && parsed != KeyCode.None
                ? parsed
                : fallback;
        }
    }
}

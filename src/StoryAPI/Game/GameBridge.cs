using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using RenPyParser.AssetManagement;
using RenPyParser.VGPrompter.DataHolders;
using RenPyParser.Transforms;
using RenpyParser;
using SimpleExpressionEngine;
using UnityEngine;

using StoryAPI.Runtime;

namespace StoryAPI.Game
{
    /// <summary>
    /// Single place where the mod touches the game's private plumbing.
    ///
    /// DDLC Plus keeps a working Ren'Py runtime, but three of the pieces a story mod
    /// needs are not reachable through public API: the running <c>RenpyScriptExecution</c>,
    /// the label entry-point table inside <c>Blocks</c>, and <c>RenpyDialogueLine</c>
    /// (which is internal). Everything here is a thin, cached wrapper so the rest of
    /// the mod can stay ordinary C#.
    /// </summary>
    public static class GameBridge
    {
        /// <summary>
        /// Diagnostics go through BepInEx rather than UnityEngine.Debug, because this runs
        /// during start-up, before Unity's own log listeners are attached.
        /// </summary>
        internal static readonly BepInEx.Logging.ManualLogSource Log =
            BepInEx.Logging.Logger.CreateLogSource("StoryAPI");

        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Languages the game actually ships translation bundles for.</summary>
        private static readonly ExtendedSystemLanguage[] ShippedLanguages =
        {
            ExtendedSystemLanguage.English,
            ExtendedSystemLanguage.German,
            ExtendedSystemLanguage.French,
            ExtendedSystemLanguage.Italian,
            ExtendedSystemLanguage.Japanese,
            ExtendedSystemLanguage.Korean,
            ExtendedSystemLanguage.Portuguese,
            ExtendedSystemLanguage.Russian,
            ExtendedSystemLanguage.Spanish,
            ExtendedSystemLanguage.SpanishLatinAmerica,
            ExtendedSystemLanguage.ChineseSimplified,
            ExtendedSystemLanguage.ChineseTraditional,
        };

        private static FieldInfo s_executionField;
        private static FieldInfo s_blockEntryPointsField;
        private static FieldInfo s_blocksField;
        private static ConstructorInfo s_dialogueCtor;
        private static bool s_probed;
        private static string s_probeError;
        private static bool s_dataValueCapabilitiesLogged;

        // ---------------------------------------------------------------- state

        /// <summary>True once the story is running and the script exists.</summary>
        public static bool Ready
        {
            get
            {
                EnsureProbed();
                return Main != null && Script != null;
            }
        }

        /// <summary>Reason the bridge is unavailable, or null.</summary>
        public static string ProbeError
        {
            get
            {
                EnsureProbed();
                return s_probeError;
            }
        }

        /// <summary>The live <c>RenpyMainBase</c>, or null outside the story.</summary>
        public static RenpyMainBase Main
        {
            get
            {
                try
                {
                    var screen = RenpyScreenManager.Instance;
                    return screen == null ? null : screen.RenpyMainBase;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>The live <c>RenpyScript</c>, or null.</summary>
        public static RenpyScript Script
        {
            get
            {
                try
                {
                    return Main?.RenpyScript;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>True while the game is showing its main menu.</summary>
        public static bool InMainMenu
        {
            get
            {
                try
                {
                    return RenpyScreenManager.Instance != null &&
                           RenpyScreenManager.Instance.InMainMenu;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Current Ren'Py execution context, or null.</summary>
        public static RenpyExecutionContext Context
        {
            get
            {
                try
                {
                    return Main?.GetExecutionContext();
                }
                catch
                {
                    return null;
                }
            }
        }

        private static RenpyScriptExecution Execution
        {
            get
            {
                var main = Main;
                if (main == null || s_executionField == null) return null;
                return s_executionField.GetValue(main) as RenpyScriptExecution;
            }
        }

        private static void EnsureProbed()
        {
            if (s_probed) return;
            s_probed = true;

            try
            {
                s_executionField = typeof(RenpyMainBase).GetField("scriptExecution", Instance);
                if (s_executionField == null)
                    s_probeError = "RenpyMainBase.scriptExecution not found - game version mismatch";

                var blocksType = typeof(Blocks);
                s_blockEntryPointsField = blocksType.GetField("blockEntryPoints", Instance);
                s_blocksField = blocksType.GetField("blocks", Instance);
                if (s_blockEntryPointsField == null || s_blocksField == null)
                    s_probeError = "Blocks internal dictionaries not found - game version mismatch";

                var dialogueType = typeof(RenpyScript).Assembly.GetType("RenpyParser.RenpyDialogueLine", false);
                if (dialogueType != null)
                {
                    s_dialogueCtor = dialogueType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(c =>
                        {
                            var p = c.GetParameters();
                            return p.Length == 15 && p[0].ParameterType == typeof(string);
                        });
                }
                if (s_dialogueCtor == null)
                    s_probeError = "RenpyDialogueLine constructor not found - game version mismatch";
            }
            catch (Exception e)
            {
                s_probeError = "probe failed: " + e.Message;
            }
        }

        // ------------------------------------------------------------- execution

        /// <summary>
        /// Log whether the base game's own art is reachable from a mod label.
        ///
        /// Printed once per entry so that a mod author whose background comes out black
        /// can tell from the log alone whether they mistyped a name or are hitting the
        /// dependency-table limitation.
        /// </summary>
        private static void ReportAssetReachability()
        {
            string[] probes = { "bedroom", "bg bedroom", "classroom", "monika 1" };
            var report = new List<string>();
            foreach (var name in probes) report.Add(ProbeAsset(name));
        }

        /// <summary>Art and sound bundles a story mod is allowed to reach into.</summary>
        private static readonly string[] SharedAssetBundles =
        {
            "bg", "cg", "monika", "yuri", "natsuki", "sayori", "sfx-coarse",
        };

        /// <summary>
        /// Music lives in its own, much larger bundles. They are already resident, so
        /// listing their clips costs a scan rather than any loading.
        /// </summary>
        private static readonly string[] SharedAudioBundles =
        {
            "bgm-coarse", "bgm-coarse_00",
        };

        private static bool s_assetMapInstalled;

        private static readonly List<AssetBundle> s_selfOpened = new List<AssetBundle>();

        /// <summary>
        /// Close any bundle this plugin opened itself.
        ///
        /// A bundle StoryAPI opens from disk is a duplicate of one the game may want to load
        /// later. Leaving it resident makes the game's own load fail, so every copy is closed
        /// as soon as the names have been read. Assets already pulled out of a bundle stay
        /// usable; only the container goes away.
        /// </summary>
        public static void ReleaseSelfOpenedBundles()
        {
            if (s_selfOpened.Count == 0) return;

            for (int i = 0; i < s_selfOpened.Count; i++)
            {
                try { s_selfOpened[i]?.Unload(true); }
                catch { /* already gone */ }
            }

            Log.LogInfo("[StoryAPI] released " + s_selfOpened.Count + " self-opened bundle(s).");
            s_selfOpened.Clear();
        }

        /// <summary>
        /// Find a bundle the game already has open, by the name Unity gave it.
        ///
        /// Opening a second copy of a bundle the game already holds is what breaks DDLC Plus.
        /// Unity refuses the duplicate ("another AssetBundle with the same files is already
        /// loaded"), the game keeps a reference to an instance Unity no longer recognises, and
        /// the next <c>AssetBundlesLoadUpdate</c> throws. When that happens while the label's
        /// dependency table is being swapped in, the table never finishes loading,
        /// <c>HasLabelAssetBundleLoaded</c> stays false for the rest of the session, and every
        /// later asset lookup throws ? which is a white screen with no error on screen.
        ///
        /// So: never open a copy of something that is already open. Ask Unity what is loaded
        /// and reuse that instance instead.
        /// </summary>
        public static AssetBundle FindLoadedBundle(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            try
            {
                var loaded = new List<AssetBundle>(AssetBundle.GetAllLoadedAssetBundles());
                if (loaded.Count == 0) return null;

                foreach (var bundle in loaded)
                {
                    if (bundle == null) continue;
                    string name;
                    try { name = bundle.name; } catch { continue; }
                    if (!string.IsNullOrEmpty(name) &&
                        name.Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        return bundle;
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] could not enumerate loaded bundles: " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// Teach the active label's dependency table about the base game's art.
        ///
        /// The game resolves an asset name to a bundle through a table that belongs to
        /// the label being played. A mod label has none, so entering one cold leaves the
        /// table describing whatever the launcher last loaded and every base-game
        /// background and sound becomes unresolvable -- while character sprites keep
        /// working, because those happen to be in the permanently resident set.
        ///
        /// The table is filled from each bundle's asset-path index. The path's leaf name is
        /// used as the key so no prefab, audio clip or scene object has to be materialized
        /// just to discover its name.
        /// </summary>
        private static void InstallSharedAssetMap()
        {
            if (s_assetMapInstalled) return;

            try
            {
                var resources = Renpy.Resources as ActiveLabelAssetBundles;
                if (resources == null) return;

                if (s_labelBundleProperty == null)
                {
                    s_labelBundleProperty = typeof(ActiveLabelAssetBundles).GetProperty(
                        "LabelAssetBundle", BindingFlags.Instance | BindingFlags.NonPublic);
                }

                var table = s_labelBundleProperty?.GetValue(resources) as LabelAssetBundleDependencies;
                var bundlesField = typeof(ActiveLabelAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic);
                var bundles = bundlesField?.GetValue(resources) as ActiveAssetBundles;
                var loaded = typeof(ActiveAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(bundles) as Dictionary<string, AssetBundle>;

                if (table == null || loaded == null) return;

                int mapped = 0;
                var mappedNames = new List<string>();

                // GetAllAssetNames reads the bundle's index. LoadAllAssets<T>() would
                // instantiate every matching object (including large audio/scene assets)
                // merely to learn its name, and Unity then keeps those objects resident.
                foreach (var bundleName in SharedAssetBundles)
                {
                    AssetBundle bundle;
                    if (!loaded.TryGetValue(bundleName, out bundle) || bundle == null) continue;

                    string[] paths;
                    try { paths = bundle.GetAllAssetNames(); }
                    catch (Exception e)
                    {
                        Log.LogWarning("Could not list assets in bundle '" + bundleName + "': " + e.Message);
                        continue;
                    }
                    if (paths == null) continue;

                    var seenInBundle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var path in paths)
                    {
                        // Preserve the old map's scope: scene/prefab assets plus sound files,
                        // not every texture or unrelated resource path in the bundle.
                        string extension = System.IO.Path.GetExtension(path) ?? string.Empty;
                        bool objectAsset = extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase) ||
                                           extension.Equals(".asset", StringComparison.OrdinalIgnoreCase) ||
                                           extension.Equals(".unity", StringComparison.OrdinalIgnoreCase);
                        if (!objectAsset && !IsAudioAssetPath(path)) continue;
                        string assetName = AssetNameFromPath(path);
                        if (string.IsNullOrEmpty(assetName) || !seenInBundle.Add(assetName)) continue;

                        try
                        {
                            table.AddAsset(assetName, bundleName, string.Empty);
                            mapped++;
                            mappedNames.Add(assetName);
                        }
                        catch (Exception e)
                        {
                            // A duplicate/unsupported name must not abort the whole map.
                            Log.LogDebug("Could not map asset '" + assetName + "' from '" + bundleName + "': " + e.Message);
                        }
                    }
                }

                foreach (var bundleName in SharedAudioBundles)
                {
                    AssetBundle bundle;
                    if (!loaded.TryGetValue(bundleName, out bundle) || bundle == null) continue;

                    string[] paths;
                    try { paths = bundle.GetAllAssetNames(); }
                    catch (Exception e)
                    {
                        Log.LogWarning("Could not list audio in bundle '" + bundleName + "': " + e.Message);
                        continue;
                    }
                    if (paths == null) continue;

                    var seenInBundle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var path in paths)
                    {
                        if (!IsAudioAssetPath(path)) continue;
                        string assetName = AssetNameFromPath(path);
                        if (string.IsNullOrEmpty(assetName) || !seenInBundle.Add(assetName)) continue;

                        try
                        {
                            table.AddAsset(assetName, bundleName, string.Empty);
                            mapped++;
                            mappedNames.Add(assetName);
                        }
                        catch (Exception e)
                        {
                            Log.LogDebug("Could not map audio '" + assetName + "' from '" + bundleName + "': " + e.Message);
                        }
                    }
                }

                s_assetMapInstalled = true;
                var samples = new List<string>();
                foreach (string known in mappedNames)
                {
                    if (samples.Count >= 16) break;
                    if (known.IndexOf("original", StringComparison.OrdinalIgnoreCase) < 0 &&
                        known.IndexOf("monika", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    samples.Add(known);
                }

                Log.LogInfo("Mapped " + mapped + " base-game asset path name(s) for mod use. Samples: " +
                         string.Join(", ", samples.ToArray()));
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not map base-game assets for mods: " + e.Message);
                s_assetMapInstalled = true;   // do not retry every entry
            }
        }

        /// <summary>
        /// List what the base game calls its music and sound, so a mod author does not
        /// have to guess: <c>play music1</c> fails silently if that name does not exist.
        /// </summary>
        private static void ReportAudioScope()
        {
            try
            {
                var context = Context;
                if (context == null) return;

                var scope = context.GetVariableObjectAs<Dictionary<string, DataValue>>("audio");
                if (scope == null)
                {
                    Log.LogInfo("No 'audio' scope is available; base-game music is unreachable.");
                    return;
                }

                var report = new List<string>();
                foreach (var pair in scope.OrderBy(p => p.Key, StringComparer.Ordinal).Take(6))
                {
                    var data = pair.Value.GetObjectAs<RenpyAudioData>();
                    report.Add(pair.Key + " => name='" + data.name + "' path='" + data.path +
                               "' asset='" + data.simpleAssetName + "'");
                }

                var keys = scope.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                var sample = keys.Take(16).ToArray();
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not read the audio scope: " + e.Message);
            }
        }

        /// <summary>
        /// Find the base game's audio entry for a readable name.
        ///
        /// The game looks a track up as <c>audio.&lt;key&gt;</c>, and those keys are
        /// hashes -- writing <c>play music1</c> finds nothing. Each entry does still carry
        /// its original path though, and the loader uses the path's file name
        /// (<c>bgm/9.ogg</c> becomes <c>9</c>). Matching on either is what makes the
        /// shipped music and sound effects reachable under names a mod author can type.
        ///
        /// Returns the scope key to use, or null.
        /// </summary>
        public static string ResolveBaseAudio(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath)) return null;

            string wanted = nameOrPath.Trim();
            string leaf = SanitizeAudioName(wanted);

            try
            {
                var scope = Context?.GetVariableObjectAs<Dictionary<string, DataValue>>("audio");
                if (scope == null)
                {
                    // Mods are compiled before the story starts, and the audio scope only
                    // exists once a script is running. Having no scope at that point is
                    // expected, not a fault, so nothing is said about it; the name is simply
                    // passed through and resolved again later when the scope exists.
                    if (Context == null) return null;

                    Log.LogWarning("resolve audio '" + wanted + "': no audio scope available");
                    return null;
                }

                foreach (var pair in scope)
                {
                    try
                    {
                        // Not every entry in the scope is an audio define, and asking for
                        // the wrong type throws rather than returning null, so each one is
                        // examined on its own instead of letting one bad entry abandon the
                        // whole search.
                        if (!pair.Value.IsObject<RenpyAudioData>()) continue;

                        var data = pair.Value.GetObjectAs<RenpyAudioData>();
                        if (string.IsNullOrEmpty(data.path)) continue;

                        if (string.Equals(data.path, wanted, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(data.simpleAssetName, leaf, StringComparison.OrdinalIgnoreCase))
                        {
                            Log.LogInfo("resolve audio '" + wanted + "' -> " + pair.Key);
                            return pair.Key;
                        }
                    }
                    catch
                    {
                        // Skip this entry and keep looking.
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("resolve audio '" + wanted + "' failed: " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// Reproduce the game's own rule for turning a track path into an asset name:
        /// drop the directory and the extension. <c>bgm/9.ogg</c> becomes <c>9</c>.
        /// </summary>
        private static string SanitizeAudioName(string pathOrName)
        {
            string result = pathOrName.Trim().Replace('\\', '/');
            int slash = result.LastIndexOf('/');
            if (slash >= 0) result = result.Substring(slash + 1);

            // Only the final extension is stripped, so "dialogue.1" survives.
            int dot = result.LastIndexOf('.');
            if (dot > 0) result = result.Substring(0, dot);

            return result;
        }

        /// <summary>Get the leaf asset name from Unity's bundle path without loading the asset.</summary>
        private static string AssetNameFromPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string normalized = assetPath.Replace('\\', '/');
            int slash = normalized.LastIndexOf('/');
            string name = slash >= 0 ? normalized.Substring(slash + 1) : normalized;
            int dot = name.LastIndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);
            return name;
        }

        private static bool IsAudioAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            return assetPath.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.EndsWith(".aif", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSpriteAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            return assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Write every name a mod author can actually call into a text file.
        ///
        /// This is the authoritative list, taken from the running game rather than from
        /// the mod's own tables: asset names are read out of the resident bundles, audio
        /// names out of the game's own audio scope, and speakers out of the script.
        /// </summary>
        public static string DumpReference(string path)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("# DDLC Plus reference, dumped by StoryAPI " + DateTime.Now.ToString("s"));
            report.AppendLine();

            try
            {
                var resources = Renpy.Resources as ActiveLabelAssetBundles;
                var bundlesField = typeof(ActiveLabelAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic);
                var bundles = bundlesField?.GetValue(resources) as ActiveAssetBundles;
                var loaded = typeof(ActiveAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(bundles) as Dictionary<string, AssetBundle>;

                if (loaded != null)
                {
                    foreach (var name in loaded.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    {
                        var assets = new List<string>();
                        try
                        {
                            var bundle = loaded[name];
                            if (bundle == null) continue;
                            var paths = bundle.GetAllAssetNames();
                            if (paths != null)
                            {
                                foreach (var assetPath in paths)
                                {
                                    string assetName = AssetNameFromPath(assetPath);
                                    if (string.IsNullOrEmpty(assetName)) continue;
                                    assets.Add(IsAudioAssetPath(assetPath)
                                        ? assetName + "  [audio]  (" + assetPath + ")"
                                        : assetName + "  (" + assetPath + ")");
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            report.AppendLine("## bundle " + name + " (failed: " + e.Message + ")");
                            continue;
                        }

                        if (assets.Count == 0) continue;
                        assets.Sort(StringComparer.Ordinal);
                        report.AppendLine("## bundle " + name + "  (" + assets.Count + ")");
                        foreach (var asset in assets) report.AppendLine(asset);
                        report.AppendLine();
                    }
                }
            }
            catch (Exception e)
            {
                report.AppendLine("## bundles failed: " + e.Message);
            }

            try
            {
                var scope = Context?.GetVariableObjectAs<Dictionary<string, DataValue>>("audio");
                if (scope != null)
                {
                    report.AppendLine("## audio (" + scope.Count + ")");
                    var rows = new List<string>();
                    foreach (var pair in scope)
                    {
                        try
                        {
                            if (!pair.Value.IsObject<RenpyAudioData>()) continue;
                            var data = pair.Value.GetObjectAs<RenpyAudioData>();
                            if (string.IsNullOrEmpty(data.path)) continue;
                            rows.Add(data.path + "  |  key=" + pair.Key +
                                     "  |  asset=" + data.simpleAssetName);
                        }
                        catch
                        {
                            // Not an audio define.
                        }
                    }
                    rows.Sort(StringComparer.Ordinal);
                    foreach (var row in rows) report.AppendLine(row);
                    report.AppendLine();
                }
            }
            catch (Exception e)
            {
                report.AppendLine("## audio failed: " + e.Message);
            }

            try
            {
                var script = Script;
                if (script?.Characters != null)
                {
                    var table = script.Characters.GetDictionary();
                    report.AppendLine("## characters (" + table.Count + ")");
                    foreach (var pair in table.OrderBy(p => p.Key, StringComparer.Ordinal))
                    {
                        report.AppendLine(pair.Key + "  |  name=" + pair.Value.name +
                                         "  |  image=" + pair.Value.image);
                    }
                    report.AppendLine();
                }
            }
            catch (Exception e)
            {
                report.AppendLine("## characters failed: " + e.Message);
            }

            System.IO.File.WriteAllText(path, report.ToString());
            return path;
        }

        /// <summary>
        /// List every music and sound file the game ships, by the path a script uses.
        ///
        /// Sound is addressed by file path rather than by a short name, so the path is the
        /// thing a mod needs to know. Reading it out of the bundles avoids having to start
        /// a script, because the scope the game would answer from is not populated yet.
        /// </summary>
        public static int DumpAudioPaths(string path)
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                // This is a listing of shipped bundle paths, so it remains stable between
                // launches. Delete audio-paths.txt when an explicit refresh is required.
                try
                {
                    int existingCount = System.IO.File.ReadAllLines(path).Count(line =>
                        line.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ||
                        line.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase));
                    Log.LogInfo("[StoryAPI] Audio path listing already exists; skipped bundle scan. Delete the file to regenerate it.");
                    return existingCount;
                }
                catch { /* Rebuild it if the cached file cannot be read. */ }
            }

            var report = new StringBuilder();
            int count = 0;

            string source = null;
            foreach (var candidate in new[]
            {
                System.IO.Path.Combine(Application.streamingAssetsPath, "Windows"),
                System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "AssetBundles", "Windows"),
            })
            {
                if (!System.IO.Directory.Exists(candidate)) continue;
                if (System.IO.Directory.GetFiles(candidate, "*.cy").Length == 0) continue;
                source = candidate;
                break;
            }

            if (source == null)
            {
                Log.LogWarning("[StoryAPI] No bundle folder found for the audio list.");
                return 0;
            }

            string scratch = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "storyapi-bundles");
            System.IO.Directory.CreateDirectory(scratch);

            // A bundle the game has already loaded cannot be opened a second time from disk,
            // so the resident copy is used when there is one.
            var resident = new Dictionary<string, AssetBundle>();
            try
            {
                var resources = Renpy.Resources as ActiveLabelAssetBundles;
                var field = typeof(ActiveLabelAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(resources) is ActiveAssetBundles holder)
                {
                    var loaded = typeof(ActiveAssetBundles).GetField(
                        "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(holder) as Dictionary<string, AssetBundle>;
                    if (loaded != null)
                    {
                        foreach (var pair in loaded) resident[pair.Key] = pair.Value;
                    }
                }
            }
            catch (Exception)
            {
                // Nothing resident; everything is read from disk instead.
            }

            foreach (var file in System.IO.Directory.GetFiles(source, "*.cy").OrderBy(f => f))
            {
                string key = System.IO.Path.GetFileNameWithoutExtension(file);
                if (!key.Contains("bgm") && !key.Contains("sfx") &&
                    !key.Contains("jukebox") && !key.Contains("credits") &&
                    !key.Contains("gui") && !key.Contains("poem")) continue;

                AssetBundle bundle = null;
                bool openedHere = false;
                try
                {
                    // Prefer the exact instance owned by the game's resource manager.
                    if (resident.TryGetValue(key, out var live)) bundle = live;
                    if (bundle == null) bundle = FindLoadedBundle(key);

                    if (bundle == null)
                    {
                        string plain = System.IO.Path.Combine(scratch, key + ".bundle");
                        if (!System.IO.File.Exists(plain) ||
                            System.IO.File.GetLastWriteTimeUtc(plain) < System.IO.File.GetLastWriteTimeUtc(file))
                        {
                            Deobfuscate(file, plain);
                        }
                        bundle = AssetBundle.LoadFromFile(plain);
                        openedHere = bundle != null;
                    }

                    if (bundle == null) continue;

                    // GetAllAssetNames reads the bundle directory; it does not require loading
                    // the audio clips. Do not keep disk-opened bundles in s_selfOpened just
                    // for a text listing: on the first scan that would retain every audio
                    // bundle until a later timer released them.
                    var rows = new List<string>();
                    try
                    {
                        foreach (var asset in bundle.GetAllAssetNames())
                        {
                            if (!asset.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) &&
                                !asset.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)) continue;
                            rows.Add(asset);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.LogDebug("[StoryAPI] Could not list audio paths in bundle '" + key + "': " + e.Message);
                    }

                    if (rows.Count == 0) continue;
                    rows.Sort(StringComparer.Ordinal);
                    report.AppendLine("## " + key + "  (" + rows.Count + ")");
                    foreach (var row in rows) report.AppendLine(row);
                    report.AppendLine();
                    count += rows.Count;
                }
                catch (Exception e)
                {
                    Log.LogWarning("[StoryAPI] Audio bundle " + key + " skipped: " + e.Message);
                }
                finally
                {
                    if (openedHere && bundle != null)
                    {
                        try { bundle.Unload(true); }
                        catch (Exception e)
                        {
                            Log.LogDebug("[StoryAPI] Could not unload audio-list bundle '" + key + "': " + e.Message);
                        }
                    }
                }
            }

            System.IO.File.WriteAllText(path, report.ToString());
            return count;
        }

        /// <summary>
        /// Load the prefab behind a name the game shows, if there is one.
        ///
        /// Character poses are prefabs rather than plain images, so this is what makes a
        /// pose reproducible: the same object the game itself puts on screen, with its
        /// layers intact.
        /// </summary>
        public static bool TryLoadPrefab(string assetName, out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrEmpty(assetName)) return false;

            string suffix = "gennedprefabs/" + assetName + ".prefab";

            foreach (var bundle in ResidentBundles())
            {
                string[] paths;
                try { paths = bundle.GetAllAssetNames(); } catch { continue; }
                if (paths == null) continue;

                foreach (var path in paths)
                {
                    if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    try { prefab = bundle.LoadAsset<GameObject>(path); } catch { }
                    if (prefab != null) return true;
                }
            }

            // Search disk bundles one at a time. Bundles that do not contain this prefab
            // are fully unloaded before the next bundle is opened.
            GameObject foundPrefab = null;
            bool foundOnDisk = FindInDiskBundles((key, bundle) =>
            {
                string[] paths;
                try { paths = bundle.GetAllAssetNames(); }
                catch { return false; }
                if (paths == null) return false;

                foreach (var path in paths)
                {
                    if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    try { foundPrefab = bundle.LoadAsset<GameObject>(path); } catch { foundPrefab = null; }
                    if (foundPrefab != null) return true;
                }
                return false;
            }, key => IsCharacterBundle(key));

            if (foundOnDisk)
            {
                prefab = foundPrefab;
                return prefab != null;
            }

            return false;
        }

        /// <summary>
        /// Find a sprite the game ships by name.
        ///
        /// Layer swapping needs the replacement art as a sprite, because the parts of a
        /// pose are sprite renderers.
        /// </summary>
        public static bool TryLoadSprite(string assetName, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(assetName)) return false;

            // Fast path: the sprite is exposed as a top-level asset by its name.
            var resident = ResidentBundles();
            foreach (var bundle in resident)
            {
                try { sprite = bundle == null ? null : bundle.LoadAsset<Sprite>(assetName); }
                catch { sprite = null; }
                if (sprite != null) return true;
            }

            // DDLC's character poses are often sub-sprites of a character atlas. Only
            // inspect those four bundles, and only load sub-assets from individual image
            // paths instead of materializing every Sprite in every bundle.
            foreach (var bundle in resident)
            {
                string name;
                try { name = bundle == null ? null : bundle.name; }
                catch { continue; }
                if (!IsCharacterBundle(name)) continue;
                sprite = FindSpriteInBundle(bundle, assetName);
                if (sprite != null) return true;
            }

            Sprite foundSprite = null;
            if (FindInDiskBundles((key, bundle) =>
            {
                try { foundSprite = bundle.LoadAsset<Sprite>(assetName); }
                catch { foundSprite = null; }
                return foundSprite != null;
            }))
            {
                sprite = foundSprite;
                return true;
            }

            foundSprite = null;
            if (FindInDiskBundles((key, bundle) =>
            {
                foundSprite = FindSpriteInBundle(bundle, assetName);
                return foundSprite != null;
            }, key => IsCharacterBundle(key)))
            {
                sprite = foundSprite;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Look up one named sprite. Sprite names can be sub-assets of a texture, so try
        /// the direct name first, then inspect image assets one at a time. This deliberately
        /// avoids LoadAllAssets<Sprite>(), which retains every sprite in the bundle.
        /// </summary>
        private static Sprite FindSpriteInBundle(AssetBundle bundle, string spriteName)
        {
            if (bundle == null || string.IsNullOrEmpty(spriteName)) return null;

            try
            {
                var direct = bundle.LoadAsset<Sprite>(spriteName);
                if (direct != null) return direct;
            }
            catch
            {
                // The sprite may be a sub-asset rather than a top-level asset.
            }

            string[] paths;
            try { paths = bundle.GetAllAssetNames(); }
            catch { return null; }
            if (paths == null || paths.Length == 0) return null;

            string wanted = AssetNameFromPath(spriteName);
            var candidates = new List<string>();
            foreach (var path in paths)
            {
                if (IsSpriteAssetPath(path)) candidates.Add(path);
            }

            // Prefer a path with the same leaf name; atlas fallback is only needed when
            // the sprite is named differently from its backing image.
            candidates.Sort((left, right) =>
            {
                bool leftExact = string.Equals(AssetNameFromPath(left), wanted, StringComparison.OrdinalIgnoreCase);
                bool rightExact = string.Equals(AssetNameFromPath(right), wanted, StringComparison.OrdinalIgnoreCase);
                if (leftExact == rightExact) return StringComparer.OrdinalIgnoreCase.Compare(left, right);
                return leftExact ? -1 : 1;
            });

            foreach (var path in candidates)
            {
                try
                {
                    var direct = bundle.LoadAsset<Sprite>(path);
                    if (direct != null && string.Equals(direct.name, spriteName, StringComparison.OrdinalIgnoreCase))
                        return direct;
                }
                catch
                {
                    // Try the texture's sprite sub-assets below.
                }

                try
                {
                    var subAssets = bundle.LoadAssetWithSubAssets<Sprite>(path);
                    if (subAssets == null) continue;
                    foreach (var candidate in subAssets)
                    {
                        if (candidate != null &&
                            string.Equals(candidate.name, spriteName, StringComparison.OrdinalIgnoreCase))
                            return candidate;
                    }
                }
                catch
                {
                    // Not every image asset contains sprites.
                }
            }

            return null;
        }

        /// <summary>Bundle files the game has already loaded into memory.</summary>
        public static List<AssetBundle> ResidentBundles()
        {
            var list = new List<AssetBundle>();
            try
            {
                var resources = Renpy.Resources as ActiveLabelAssetBundles;
                var field = typeof(ActiveLabelAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(resources) is not ActiveAssetBundles holder) return list;

                var loaded = typeof(ActiveAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(holder) as Dictionary<string, AssetBundle>;
                if (loaded != null) list.AddRange(loaded.Values);
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Could not list resident bundles: " + e.Message);
            }
            return list;
        }

        /// <summary>Where the game's bundle files live.</summary>
        public static string BundleSource()
        {
            foreach (var candidate in new[]
            {
                System.IO.Path.Combine(Application.streamingAssetsPath, "Windows"),
                System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "AssetBundles", "Windows"),
                Application.streamingAssetsPath,
            })
            {
                if (!System.IO.Directory.Exists(candidate)) continue;
                if (System.IO.Directory.GetFiles(candidate, "*.cy").Length > 0) return candidate;
            }
            return null;
        }

        /// <summary>Keys of bundles already owned by the game's active resource manager.</summary>
        private static HashSet<string> ResidentBundleKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var resources = Renpy.Resources as ActiveLabelAssetBundles;
                var field = typeof(ActiveLabelAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic);
                var holder = field?.GetValue(resources) as ActiveAssetBundles;
                var loaded = typeof(ActiveAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(holder) as Dictionary<string, AssetBundle>;
                if (loaded != null)
                    foreach (var key in loaded.Keys) keys.Add(key);
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Could not list active bundle keys: " + e.Message);
            }
            return keys;
        }

        /// <summary>
        /// Search disk bundles one by one. A bundle we opened is unloaded after each miss;
        /// if the visitor found its asset, Unload(false) releases the container but leaves
        /// that returned Unity object valid. Bundles already owned by the game are never
        /// unloaded here.
        /// </summary>
        private static bool FindInDiskBundles(
            Func<string, AssetBundle, bool> visitor,
            Func<string, bool> includeBundle = null)
        {
            if (visitor == null) return false;
            string source = BundleSource();
            if (source == null)
            {
                Log.LogWarning("[StoryAPI] No bundle folder found for disk search.");
                return false;
            }

            string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "storyapi-bundles");
            System.IO.Directory.CreateDirectory(scratch);
            var resident = ResidentBundleKeys();

            foreach (var file in System.IO.Directory.GetFiles(source, "*.cy").OrderBy(f => f))
            {
                string key = System.IO.Path.GetFileNameWithoutExtension(file);
                if (resident.Contains(key)) continue;
                if (includeBundle != null && !includeBundle(key)) continue;

                AssetBundle bundle = FindLoadedBundle(key);
                bool openedHere = false;
                if (bundle == null)
                {
                    try
                    {
                        string plain = System.IO.Path.Combine(scratch, key + ".bundle");
                        if (!System.IO.File.Exists(plain) ||
                            System.IO.File.GetLastWriteTimeUtc(plain) < System.IO.File.GetLastWriteTimeUtc(file))
                        {
                            Deobfuscate(file, plain);
                        }
                        bundle = AssetBundle.LoadFromFile(plain);
                        openedHere = bundle != null;
                    }
                    catch (Exception e)
                    {
                        Log.LogWarning("[StoryAPI] Disk bundle '" + key + "' skipped: " + e.Message);
                    }
                }
                if (bundle == null) continue;

                bool found = false;
                try
                {
                    found = visitor(key, bundle);
                }
                catch (Exception e)
                {
                    Log.LogDebug("[StoryAPI] Disk bundle search failed in '" + key + "': " + e.Message);
                }
                finally
                {
                    if (openedHere)
                    {
                        try { bundle.Unload(!found); }
                        catch (Exception e) { Log.LogDebug("[StoryAPI] Could not release bundle '" + key + "': " + e.Message); }
                    }
                }

                if (found) return true;
            }

            return false;
        }

        /// <summary>
        /// Enumerate disk bundles for read-only scans. Each bundle opened here is fully
        /// unloaded after its caller has finished with it, so pose/reference exports do not
        /// accumulate the game's entire bundle set in RAM.
        /// </summary>
        private static IEnumerable<AssetBundle> EnumerateDiskBundlesForScan()
        {
            string source = BundleSource();
            if (source == null) yield break;

            string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "storyapi-bundles");
            System.IO.Directory.CreateDirectory(scratch);
            var resident = ResidentBundleKeys();

            foreach (var file in System.IO.Directory.GetFiles(source, "*.cy").OrderBy(f => f))
            {
                string key = System.IO.Path.GetFileNameWithoutExtension(file);
                if (resident.Contains(key)) continue;

                AssetBundle bundle = FindLoadedBundle(key);
                bool openedHere = false;
                if (bundle == null)
                {
                    try
                    {
                        string plain = System.IO.Path.Combine(scratch, key + ".bundle");
                        if (!System.IO.File.Exists(plain) ||
                            System.IO.File.GetLastWriteTimeUtc(plain) < System.IO.File.GetLastWriteTimeUtc(file))
                        {
                            Deobfuscate(file, plain);
                        }
                        bundle = AssetBundle.LoadFromFile(plain);
                        openedHere = bundle != null;
                    }
                    catch (Exception e)
                    {
                        Log.LogDebug("[StoryAPI] Bundle '" + key + "' skipped during scan: " + e.Message);
                    }
                }
                if (bundle == null) continue;

                if (openedHere)
                {
                    try { yield return bundle; }
                    finally
                    {
                        try { bundle.Unload(true); }
                        catch (Exception e) { Log.LogDebug("[StoryAPI] Could not unload scanned bundle '" + key + "': " + e.Message); }
                    }
                }
                else
                {
                    yield return bundle;
                }
            }
        }

        private static readonly List<AssetBundle> s_diskBundles = new List<AssetBundle>();
        private static bool s_diskTried;

        /// <summary>
        /// Legacy eager API: opens every disk bundle and keeps it resident for compatibility.
        /// Internal lookup/export paths use the streaming helpers above instead.
        /// Calling this method explicitly can consume substantial memory.
        /// </summary>
        public static List<AssetBundle> BundlesFromDisk()
        {
            if (s_diskTried) return s_diskBundles;
            s_diskTried = true;

            string source = BundleSource();
            if (source == null)
            {
                Log.LogWarning("[StoryAPI] No bundle folder found.");
                return s_diskBundles;
            }

            string scratch = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "storyapi-bundles");
            System.IO.Directory.CreateDirectory(scratch);

            var resident = new HashSet<string>();
            try
            {
                var resources = Renpy.Resources as ActiveLabelAssetBundles;
                var field = typeof(ActiveLabelAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(resources) is ActiveAssetBundles holder)
                {
                    var loaded = typeof(ActiveAssetBundles).GetField(
                        "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(holder) as Dictionary<string, AssetBundle>;
                    if (loaded != null) foreach (var key in loaded.Keys) resident.Add(key);
                }
            }
            catch
            {
                // Nothing is known to be resident, so everything is read from disk.
            }

            foreach (var file in System.IO.Directory.GetFiles(source, "*.cy").OrderBy(f => f))
            {
                string key = System.IO.Path.GetFileNameWithoutExtension(file);
                if (resident.Contains(key)) continue;

                // Reuse what the game already has open rather than opening a second copy.
                var live = FindLoadedBundle(key);
                if (live != null)
                {
                    s_diskBundles.Add(live);
                    continue;
                }

                try
                {
                    string plain = System.IO.Path.Combine(scratch, key + ".bundle");
                    if (!System.IO.File.Exists(plain) ||
                        System.IO.File.GetLastWriteTimeUtc(plain) <
                        System.IO.File.GetLastWriteTimeUtc(file))
                    {
                        Deobfuscate(file, plain);
                    }

                    var bundle = AssetBundle.LoadFromFile(plain);
                    if (bundle != null)
                    {
                        s_diskBundles.Add(bundle);
                        s_selfOpened.Add(bundle);
                    }
                }
                catch (Exception)
                {
                    // Not a bundle this player can open.
                }
            }

            Log.LogInfo("[StoryAPI] Read " + s_diskBundles.Count + " bundle(s) from disk.");
            return s_diskBundles;
        }

        /// <summary>
        /// Describe the inside of a character sprite prefab.
        ///
        /// A pose is not one picture: it is a prefab holding the body, the eyes and the
        /// mouth as separate children, each with its own placement. This reports that
        /// structure so the layers can be named, swapped and animated deliberately rather
        /// than guessed at.
        /// </summary>
        public static void ProbePrefab(string prefabName)
        {
            string source = null;
            foreach (var candidate in new[]
            {
                System.IO.Path.Combine(Application.streamingAssetsPath, "Windows"),
                System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "AssetBundles", "Windows"),
            })
            {
                if (System.IO.Directory.Exists(candidate) &&
                    System.IO.Directory.GetFiles(candidate, "*.cy").Length > 0)
                {
                    source = candidate;
                    break;
                }
            }

            if (source == null) { Log.LogWarning("[StoryAPI] No bundle folder for the probe."); return; }

            var resident = new Dictionary<string, AssetBundle>();
            try
            {
                var resources = Renpy.Resources as ActiveLabelAssetBundles;
                var field = typeof(ActiveLabelAssetBundles).GetField(
                    "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(resources) is ActiveAssetBundles holder)
                {
                    var loaded = typeof(ActiveAssetBundles).GetField(
                        "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(holder) as Dictionary<string, AssetBundle>;
                    if (loaded != null) foreach (var pair in loaded) resident[pair.Key] = pair.Value;
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Probe could not list resident bundles: " + e.Message);
            }

            string scratch = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "storyapi-bundles");
            System.IO.Directory.CreateDirectory(scratch);

            GameObject prefab = null;
            string found = null;

            foreach (var bundle in resident.Values)
            {
                string[] paths;
                try { paths = bundle.GetAllAssetNames(); } catch { continue; }
                if (paths == null) continue;

                foreach (var path in paths)
                {
                    if (!path.EndsWith(prefabName + ".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        prefab = bundle.LoadAsset<GameObject>(path);
                        found = path;
                    }
                    catch { }
                    if (prefab != null) break;
                }
                if (prefab != null) break;
            }

            if (prefab == null)
            {
                // Not resident, so it is read from disk the same way the art exporter does.
                foreach (var file in System.IO.Directory.GetFiles(source, "*.cy").OrderBy(f => f))
                {
                    string key = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (resident.ContainsKey(key)) continue;
                    if (FindLoadedBundle(key) != null) continue;

                    AssetBundle bundle;
                    try
                    {
                        string plain = System.IO.Path.Combine(scratch, key + ".bundle");
                        if (!System.IO.File.Exists(plain)) Deobfuscate(file, plain);
                        bundle = AssetBundle.LoadFromFile(plain);
                    }
                    catch { continue; }

                    if (bundle == null) continue;

                    string[] paths;
                    try { paths = bundle.GetAllAssetNames(); } catch { continue; }
                    if (paths == null) continue;

                    foreach (var path in paths)
                    {
                        if (!path.EndsWith(prefabName + ".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                        try { prefab = bundle.LoadAsset<GameObject>(path); found = path; } catch { }
                        if (prefab != null) break;
                    }
                    if (prefab != null) break;
                }
            }

            if (prefab == null)
            {
                Log.LogWarning("[StoryAPI] Probe could not find prefab " + prefabName);
                return;
            }

            Log.LogInfo("[StoryAPI] probe prefab '" + prefab.name + "' at " + found);
            Describe(prefab.transform, prefab.transform, 0);

            // Instantiating shows the values a component really holds once it is live, which
            // can differ from what the saved prefab carries.
            GameObject live = null;
            try
            {
                live = UnityEngine.Object.Instantiate(prefab);
                live.name = prefab.name + " (live)";
                Log.LogInfo("[StoryAPI] probe instantiated " + live.name);
                Describe(live.transform, live.transform, 0);
                UnityEngine.Object.Destroy(live);
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Probe could not instantiate: " + e.Message);
            }
        }

        /// <summary>Report one node of a prefab and everything under it.</summary>
        private static void Describe(Transform node, Transform root, int depth)
        {
            if (depth > 6) return;

            var local = node.localPosition;
            var scale = node.localScale;
            var siblings = node.GetSiblingIndex();

            string parts = node.name + "  pos=" + local.x.ToString("0.##") + "," +
                           local.y.ToString("0.##") + "," + local.z.ToString("0.##") +
                           " scale=" + scale.x.ToString("0.###") + "," + scale.y.ToString("0.##");

            foreach (var component in node.GetComponents<Component>())
            {
                if (component == null) { parts += " [" + "missing script" + "]"; continue; }

                string detail = component.GetType().Name;
                foreach (var field in ComponentFields(component))
                {
                    object value;
                    try { value = field.GetValue(component); } catch { continue; }

                    if (value is Sprite sprite)
                    {
                        detail += " {" + field.Name + "=sprite '" + sprite.name + "'" +
                                  " rect=" + sprite.rect +
                                  " sheet=" + (sprite.texture == null ? "none" :
                                  sprite.texture.width + "x" + sprite.texture.height) + "}";
                    }
                    else if (value is Texture2D texture)
                    {
                        detail += " {" + field.Name + "=texture '" + texture.name + "'" +
                                  " " + texture.width + "x" + texture.height + "}";
                    }
                }
                parts += " [" + detail + "]";
            }

            Log.LogInfo("[StoryAPI] " + new string(' ', depth * 2) + parts);

            foreach (Transform child in node)
            {
                Describe(child, root, depth + 1);
            }
        }

        /// <summary>
        /// Write out every line of dialogue the game contains, and every script block.
        ///
        /// A line is identified by the pair (label, id), and that pair is what a mod uses to
        /// replace or to continue from a particular moment, so this listing is what makes
        /// those choices possible: without it there is no way to know which lines exist.
        /// </summary>
        /// <summary>
        /// Say what the game is holding back for the next transition.
        ///
        /// A line that shows or hides something is put on a queue and applied when
        /// the next transition runs. That is how the base game works, so a queue that
        /// holds something is not in itself a fault; what matters is what is in it,
        /// and that is what is written down here.
        /// </summary>
        public static void ReportPendingTransitions()
        {
            try
            {
                var context = Context;
                if (context == null) return;

                var queue = context.GetType().GetField("transitionQueue",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(context) as System.Collections.IEnumerable;

                if (queue == null) return;

                var names = new List<string>();
                foreach (var item in queue)
                {
                    if (item == null) continue;
                    names.Add(item.GetType().Name);
                }

                if (names.Count == 0) { s_pendingReported = null; return; }

                string summary = string.Join(", ", names.ToArray());
                if (summary == s_pendingReported) return;
                s_pendingReported = summary;

                Log.LogInfo("[StoryAPI] the game is holding " + names.Count +
                            " line(s) for the next transition: " + summary);
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] could not check for pending transitions: " + e.Message);
            }
        }

        private static string s_pendingReported;

        /// <summary>
        /// The command text of a step, when the step carries one.

        ///
        /// The game keeps the original command next to the parsed form, so the text
        /// can be read straight off the step: "scene bg club_day", "show monika 5",
        /// "with dissolve". This is what makes the script reference usable for mod
        /// authors, because it names the things a script refers to.
        /// </summary>
        private static string StepPayload(Line step)
        {
            if (step is RenpyScene scene) return scene.SceneData;
            if (step is RenpyShow show) return show.ShowData;
            if (step is RenpyHide hide) return hide.HideData;
            if (step is RenpyWith with) return with.WithData;
            if (step is RenpyPlay play) return play.PlayData;
            if (step is RenpyPause pause) return pause.PauseData;
            return null;
        }

        /// <summary>
        /// Say which of the usual transition names the running game can resolve.
        ///
        /// A `with` names a transition, and the game looks that name up as a plain
        /// variable. Whether the name is there is a property of the running game, not
        /// of the mod, so it is worth reporting once instead of guessing.
        /// </summary>
        public static void ReportTransitions(RenpyExecutionContext context)
        {
            if (context == null) return;

            // These are the names the base game's own script uses, so this says whether
            // a mod can ask for the same transitions the game asks for.
            string[] names =
            {
                "dissolve_scene_full", "dissolve_scene_half", "wipeleft_scene",
                "dissolve", "fade",
            };

            var parts = new List<string>();
            foreach (string name in names)
            {
                string found;
                try
                {
                    object value = context.GetVariableObject(name);
                    found = value == null ? "none" : value.GetType().Name;
                }
                catch (Exception e)
                {
                    found = "error: " + e.Message;
                }
                parts.Add(name + "=" + found);
            }

            Log.LogInfo("[StoryAPI] transitions in scope: " + string.Join(", ", parts));
        }

        /// <summary>
        /// The names of the poses the base game ships.
        ///
        /// A pose is a generated prefab, so the names come from the bundles the game
        /// keeps on disk rather than from a list.
        /// </summary>
        public static List<string> PoseNames()
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                foreach (var bundle in AllBundles())
                {
                    string bundleName;
                    try { bundleName = bundle.name; } catch { continue; }
                    if (!IsCharacterBundle(bundleName)) continue;

                    foreach (var assetPath in AssetPaths(bundle))
                    {
                        int at = assetPath.IndexOf("gennedprefabs/", StringComparison.OrdinalIgnoreCase);
                        if (at < 0) continue;

                        string rest = assetPath.Substring(at + "gennedprefabs/".Length);
                        if (!rest.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                        string poseName = rest.Substring(0, rest.Length - ".prefab".Length);
                        if (string.IsNullOrEmpty(poseName)) continue;
                        if (!seen.Add(poseName)) continue;

                        names.Add(poseName);
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Could not list the poses: " + e.Message);
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>
        /// Write out how every base-game pose is put together.
        ///
        /// A pose is not a picture: it is a prefab whose parts - the body, the eyes, the
        /// mouth - are separate sprite renderers, each pointing at a frame inside an
        /// atlas. That is why the pose cannot be exported as one image, and it is also
        /// why the layers are what a mod can move and replace.
        ///
        /// The file is written as a table, one row per layer:
        /// pose, layer, texture, frame rectangle, pivot, position, scale, order.
        /// </summary>
        public static int DumpPoses(string path)
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                // Pose export loads prefab objects and sprite references. Reusing the prior
                // reference avoids repeating that expensive scan on every game launch.
                try
                {
                    var poseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var line in System.IO.File.ReadAllLines(path))
                    {
                        int separator = line.IndexOf(" | ", StringComparison.Ordinal);
                        if (separator > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                            poseNames.Add(line.Substring(0, separator));
                    }
                    Log.LogInfo("[StoryAPI] Pose reference already exists; skipped bundle scan. Delete pose-reference.txt to regenerate it.");
                    return poseNames.Count;
                }
                catch { /* Rebuild it if the cached file cannot be read. */ }
            }

            var report = new StringBuilder();
            report.AppendLine("# DDLC Plus pose reference");
            report.AppendLine("# Every pose is a prefab of separate parts; this is what it is made of.");
            report.AppendLine("# pose | layer | sprite | texture | frame | pivot | position | scale | frames");
            int poses = 0;

            try
            {
                foreach (var bundle in AllBundles())
                {
                    string bundleName;
                    try { bundleName = bundle.name; } catch { continue; }
                    if (!IsCharacterBundle(bundleName)) continue;

                    foreach (var assetPath in AssetPaths(bundle))
                    {
                        int at = assetPath.IndexOf("gennedprefabs/", StringComparison.OrdinalIgnoreCase);
                        if (at < 0) continue;

                        string rest = assetPath.Substring(at + "gennedprefabs/".Length);
                        if (!rest.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                        string poseName = rest.Substring(0, rest.Length - ".prefab".Length);
                        if (string.IsNullOrEmpty(poseName)) continue;

                        GameObject prefab;
                        try { prefab = bundle.LoadAsset<GameObject>(assetPath); } catch { continue; }
                        if (prefab == null) continue;

                        var rows = PoseRows(prefab, poseName);
                        if (rows.Count == 0) continue;

                        foreach (string row in rows) report.AppendLine(row);
                        poses++;
                    }
                }
            }
            catch (Exception e)
            {
                report.AppendLine("poses unavailable: " + e.Message);
            }

            try
            {
                System.IO.File.WriteAllText(path, report.ToString());
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Could not write the pose reference: " + e.Message);
            }

            Log.LogInfo("[StoryAPI] Pose reference: " + poses + " pose(s).");
            return poses;
        }

        /// <summary>One row per layer of a pose, in the order the game draws them.</summary>
        private static List<string> PoseRows(GameObject prefab, string poseName)
        {
            var rows = new List<string>();

            var renderers = prefab.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            var ordered = renderers.OrderBy(r => r.transform.GetSiblingIndex()).ToArray();

            for (int i = 0; i < ordered.Length; i++)
            {
                var renderer = ordered[i];
                if (renderer == null || renderer.sprite == null) continue;

                string layer = BasePose.LayerName(renderer);
                if (string.IsNullOrEmpty(layer)) layer = renderer.gameObject.name;

                var sprite = renderer.sprite;
                var rect = sprite.textureRect;
                var pivot = sprite.pivot;

                // A sprite that comes out of an atlas is a multi-sprite: the frame
                // itself is one of its sub-sprites, and the multi-sprite itself has no
                // rectangle of its own. The frame is what the layer actually shows.
                string frames = "";
                if (rect.width <= 0f || rect.height <= 0f)
                {
                    foreach (var sub in SubSprites(sprite))
                    {
                        var subRect = sub.textureRect;
                        frames += (frames.Length > 0 ? ";" : "") + sub.name + "@" +
                                  string.Format("{0},{1},{2},{3}", subRect.x, subRect.y,
                                                subRect.width, subRect.height) +
                                  "@" + string.Format("{0:0.####},{1:0.####}",
                                                      sub.pivot.x, sub.pivot.y);
                    }
                }

                // The placement is relative to the pose's own root, which is what the
                // game puts on screen; the local position of a layer is relative to its
                // parent, so the chain is walked up to the root.
                var position = renderer.transform.localPosition;
                var scale = renderer.transform.lossyScale;
                var parent = renderer.transform.parent;
                while (parent != null && parent != prefab.transform)
                {
                    position += parent.localPosition;
                    scale = Vector3.Scale(scale, parent.localScale);
                    parent = parent.parent;
                }

                rows.Add(string.Join(" | ",
                    poseName,
                    layer,
                    sprite.name,
                    sprite.texture != null ? sprite.texture.name : "",
                    string.Format("{0},{1},{2},{3}", rect.x, rect.y, rect.width, rect.height) +
                        " rect " + string.Format("{0},{1},{2},{3}",
                            sprite.rect.x, sprite.rect.y, sprite.rect.width, sprite.rect.height) +
                        " ppu " + sprite.pixelsPerUnit.ToString("0.###"),
                    string.Format("{0:0.####},{1:0.####}", pivot.x, pivot.y),
                    string.Format("{0:0.###},{1:0.###}", position.x, position.y),
                    string.Format("{0:0.####},{1:0.####}", scale.x, scale.y),
                    frames));
            }

            return rows;
        }

        /// <summary>
        /// The frames of a multi-sprite.
        ///
        /// A sprite that comes out of an atlas holds no rectangle of its own: the frame
        /// is one of its sub-sprites. The property is looked up by name because it is
        /// not part of the sprite type in every build of Unity.
        /// </summary>
        private static IEnumerable<Sprite> SubSprites(Sprite sprite)
        {
            if (sprite == null) yield break;

            Sprite[] value = null;
            try
            {
                var property = sprite.GetType().GetProperty("sprites",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                value = property?.GetValue(sprite) as Sprite[];
            }
            catch
            {
                // A sprite without sub-sprites is simply a plain picture.
            }

            if (value == null) yield break;

            foreach (var sub in value)
            {
                if (sub != null) yield return sub;
            }
        }

        /// <summary>The bundles that hold the four main characters.</summary>
        private static bool IsCharacterBundle(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("monika", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("natsuki", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("sayori", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("yuri", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The bundles the game has open, plus streamed disk bundles for exports.</summary>
        private static IEnumerable<AssetBundle> AllBundles()
        {
            var seen = new HashSet<AssetBundle>();
            foreach (var bundle in ResidentBundles())
            {
                if (bundle != null && seen.Add(bundle)) yield return bundle;
            }

            foreach (var bundle in EnumerateDiskBundlesForScan())
            {
                if (bundle != null && seen.Add(bundle)) yield return bundle;
            }
        }

        public static string DumpScript(string path)

        {
            var report = new StringBuilder();
            report.AppendLine("# DDLC Plus script reference");
            report.AppendLine();

            int lineCount = 0;

            // Dialogue, in every language the game has loaded. English is written first
            // because it is the one a mod author will read.
            var labels = new SortedDictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
            foreach (var store in TextStores())
            {
                Dictionary<string, Dictionary<int, string>> dictionary;
                try { dictionary = store.GetDictionary(); }
                catch (Exception) { continue; }
                if (dictionary == null) continue;

                foreach (var pair in dictionary)
                {
                    if (pair.Value == null) continue;

                    SortedDictionary<int, string> bucket;
                    if (!labels.TryGetValue(pair.Key, out bucket))
                    {
                        bucket = new SortedDictionary<int, string>();
                        labels[pair.Key] = bucket;
                    }

                    foreach (var line in pair.Value)
                    {
                        // First language wins, so English is not overwritten by another.
                        if (!bucket.ContainsKey(line.Key)) bucket[line.Key] = line.Value;
                    }
                }
            }

            foreach (var label in labels)
            {
                if (label.Value.Count == 0) continue;
                report.AppendLine("## " + label.Key + "  (" + label.Value.Count + " line(s))");
                foreach (var line in label.Value)
                {
                    var text = (line.Value ?? string.Empty)
                        .Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
                    report.AppendLine(label.Key + " = " + line.Key + "\t" + text);
                    lineCount++;
                }
                report.AppendLine();
            }

            report.AppendLine();
            report.AppendLine("# Blocks");
            report.AppendLine("# key, then one line per step");
            report.AppendLine("# step format: <type> [tag=..] [id=..] text");

            int blockCount = 0;
            int stepCount = 0;
            try
            {
                var script = Script;
                var table = s_blocksField?.GetValue(script?.Blocks) as Dictionary<string, RenpyBlock>;
                if (table != null)
                {
                    foreach (var key in table.Keys.OrderBy(k => k, StringComparer.Ordinal))
                    {
                        var block = table[key];
                        var steps = BlockSteps(block);
                        if (steps == null || steps.Count == 0) continue;

                        report.AppendLine("## " + key + "  (" + steps.Count + " step(s))");
                        for (int i = 0; i < steps.Count; i++)
                        {
                            var step = steps[i];
                            if (step == null) continue;

                            string tag = null;
                            long id = 0;
                            bool hasId = false;

                            // A spoken line carries who says it and which text it says,
                            // which is exactly what a hook needs in order to stop after it.
                            try
                            {
                                var type = step.GetType();
                                var tagProperty = type.GetProperty("Tag",
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (tagProperty != null) tag = tagProperty.GetValue(step) as string;

                                var idProperty = type.GetProperty("TextID",
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (idProperty != null)
                                {
                                    object value = idProperty.GetValue(step);
                                    if (value != null)
                                    {
                                        id = Convert.ToInt64(value);
                                        hasId = true;
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                // A step that is not a spoken line simply has no tag or id.
                            }

                            string text = null;
                            if (hasId && labels.TryGetValue(key, out var bucket))
                            {
                                bucket.TryGetValue((int)id, out text);
                            }

                            var line = "  [" + i + "] " + step.GetType().Name;
                            if (!string.IsNullOrEmpty(tag)) line += " tag=" + tag;
                            if (hasId) line += " id=" + id;

                            // The command text is what a mod author needs: it says which
                            // background a scene uses, which image a show puts on screen
                            // and which transition a `with` asks for.
                            string payload = StepPayload(step);
                            if (!string.IsNullOrEmpty(payload)) line += " '" + payload + "'";
                            if (!string.IsNullOrEmpty(text))
                            {
                                line += " " + text.Replace("\r", " ").Replace("\n", " ");
                            }
                            report.AppendLine(line);

                            stepCount++;
                        }
                        report.AppendLine();
                        blockCount++;
                    }
                }
            }
            catch (Exception e)
            {
                report.AppendLine("blocks unavailable: " + e.Message);
            }

            System.IO.File.WriteAllText(path, report.ToString());
            Log.LogInfo("[StoryAPI] Script reference: " + labels.Count + " label(s), " +
                        lineCount + " line(s), " + blockCount + " block(s), " +
                        stepCount + " step(s).");
            return path;
        }

        /// <summary>
        /// Take a block the game owns up to and including one of its lines.
        ///
        /// The lines are the game's own objects, used as they are, so the scene still
        /// plays exactly as shipped right up to the point the mod asked for. A jump out of
        /// the block ends the usable run: replaying it would send the player back into the
        /// original story, so the run is cut short of the first jump.
        /// </summary>
        public static bool TryTakeBlockSteps(string key, long textId, out List<Line> steps)
        {
            steps = new List<Line>();
            if (string.IsNullOrEmpty(key)) return false;

            try
            {
                var script = Script;
                var table = s_blocksField?.GetValue(script?.Blocks) as Dictionary<string, RenpyBlock>;
                if (table == null || !table.TryGetValue(key, out var block)) return false;

                var contents = BlockSteps(block);
                if (contents == null || contents.Count == 0) return false;

                int last = -1;
                for (int i = 0; i < contents.Count; i++)
                {
                    var step = contents[i];
                    if (step == null) continue;

                    long? id = StepTextId(step);
                    if (id.HasValue && id.Value == textId) last = i;
                }

                if (last < 0) return false;

                // Jumps before the chosen line are left alone. The original scene really
                // does pass through them and come back into this block, so replaying them
                // is what keeps the scene identical up to the hook point. Cutting the run
                // short at the first jump would instead make the mod fire somewhere the
                // original never reaches.
                for (int i = 0; i <= last; i++)
                {
                    if (contents[i] != null) steps.Add(contents[i]);
                }

                // Only the run up to the hook point is kept. Jumps after it belong to the
                // part of the scene the mod is replacing, so they are dropped along with
                // that part and there is nothing to warn about.
                return steps.Count > 0;
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Could not read block '" + key + "': " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Give every scene in a list the name the game knows it by.
        ///
        /// The game writes a background in two words - "bg club_day" - and a plain
        /// colour in one - "black". Which of the two a name is cannot be told from
        /// the name alone, so the game's own block table is asked: if the name is a
        /// block on its own it is left alone, and if the block is "bg " + the name,
        /// the layer is added.
        /// </summary>
        public static void ResolveSceneNames(IEnumerable<Line> lines)
        {
            if (lines == null) return;

            var table = BlockTable();
            if (table == null) return;

            foreach (var line in lines)
            {
                if (!(line is RenpyScene scene)) continue;

                string data = scene.SceneData ?? string.Empty;
                string name = data.StartsWith("scene ", StringComparison.Ordinal)
                    ? data.Substring("scene ".Length).Trim()
                    : data.Trim();

                if (string.IsNullOrEmpty(name)) continue;

                string resolved = ResolveSceneName(name, table);
                if (resolved == name) continue;

                scene.SceneData = "scene " + resolved;
                scene.scene = new Scene(scene.SceneData);
            }
        }

        /// <summary>
        /// The name a scene should use, with the layer added when the game wants one.
        /// </summary>
        private static string ResolveSceneName(string name, Dictionary<string, RenpyBlock> table)
        {
            if (name.Contains(" ")) return name;
            if (table.ContainsKey(name)) return name;
            if (table.ContainsKey("bg " + name)) return "bg " + name;

            Log.LogWarning("[StoryAPI] The game has no scene called '" + name +
                           "'. Known background names are listed in asset-reference.txt.");
            return name;
        }

        /// <summary>The game's blocks by name, or null when the script is not ready.</summary>
        private static Dictionary<string, RenpyBlock> BlockTable()
        {
            try
            {
                return s_blocksField?.GetValue(Script?.Blocks) as Dictionary<string, RenpyBlock>;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The dialogue id a step speaks, if it speaks one.</summary>
        private static long? StepTextId(Line step)
        {
            try
            {
                var property = step.GetType().GetProperty("TextID",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var value = property?.GetValue(step);
                return value == null ? (long?)null : Convert.ToInt64(value);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The lines a block is made of.
        ///
        /// A block inherits its contents from a container base class rather than keeping
        /// them in a field of its own, which is why reading the block's own fields finds
        /// nothing. The container's list is the real content.
        /// </summary>
        private static IList<Line> BlockSteps(RenpyBlock block)
        {
            if (block == null) return null;
            try
            {
                var property = block.GetType().GetProperty("Contents",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var value = property?.GetValue(block);
                return value as IList<Line>;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Read the state a named transform starts from.
        ///
        /// A transform in this game is a block holding an immediate state and a target to
        /// ease towards. The motion needs the animation runner, which does not run inside a
        /// mod label, but the starting state is only data. Reading it is what lets a sprite
        /// be placed exactly where the game would have put it.
        /// </summary>
        public static bool TryGetTransformData(string blockName,
            out RenpyTransformData? initial, out RenpyTransformData? destination)
        {
            initial = null;
            destination = null;
            if (string.IsNullOrEmpty(blockName)) return false;

            try
            {
                var script = Script;
                var table = s_blocksField?.GetValue(script?.Blocks) as Dictionary<string, RenpyBlock>;
                if (table == null)
                {
                    Log.LogWarning("[StoryAPI] transform '" + blockName +
                                   "': the game's block table is not reachable");
                    return false;
                }

                if (!table.TryGetValue(blockName, out var block))
                {
                    Log.LogWarning("[StoryAPI] transform '" + blockName +
                                   "': no such block (" + table.Count + " blocks exist)");
                    return false;
                }

                var contents = BlockSteps(block);
                if (contents == null)
                {
                    Log.LogWarning("[StoryAPI] transform '" + blockName +
                                   "': the block has no readable steps");
                    return false;
                }

                foreach (var step in contents)
                {
                    if (step == null) continue;

                    string kind = step.GetType().Name;

                    // A transform is written as two steps: one that puts the picture where
                    // it starts, and one that eases it somewhere. Both carry the numbers,
                    // and the second one is the one that says how big the picture ends up.
                    if (kind != "RenpyImmediateTransform" && kind != "RenpyEasedTransform")
                    {
                        continue;
                    }

                    var type = step.GetType();

                    var command = type.GetField("TransformCommand",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(step) as string;

                    object start = type.GetField("TransformData",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(step);
                    if (start is RenpyTransformData data) initial = data;

                    object end = type.GetField("Destination",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(step);
                    if (end is RenpyTransformData goal) destination = goal;

                    if (initial.HasValue || destination.HasValue) return true;
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Could not read transform '" + blockName + "': " + e.Message);
            }

            return false;
        }

        /// <summary>
        /// The first number an expression was built from.
        ///
        /// The game keeps sizes and positions as expressions rather than as plain values,
        /// and the numbers inside them are the only place the real figure appears.
        /// </summary>
        private static float FirstConstant(object expression)
        {
            if (expression == null) return 0f;

            try
            {
                var field = expression.GetType().GetField("constantFloats",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var list = field?.GetValue(expression) as System.Collections.IList;
                if (list == null || list.Count == 0) return 0f;

                return Convert.ToSingle(list[0]);
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        /// <summary>
        /// Find a picture the game ships, by the name a script uses to show it.
        /// </summary>
        public static bool TryLoadNativeSprite(string imageName, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(imageName)) return false;

            // First try direct lookups, which do not enumerate or materialize whole bundles.
            foreach (var bundle in ResidentBundles())
            {
                if (TryLoadNativeSpriteFromBundle(bundle, imageName, out sprite)) return true;
            }

            // The sprite can be a sub-asset of a resident character atlas whose filename
            // differs from the pose. Search these small, known bundles before opening disk copies.
            foreach (var bundle in ResidentBundles())
            {
                string name;
                try { name = bundle == null ? null : bundle.name; }
                catch { continue; }
                if (!IsCharacterBundle(name)) continue;
                sprite = FindSpriteInBundle(bundle, imageName);
                if (sprite != null) return true;
            }

            Sprite foundSprite = null;
            if (FindInDiskBundles((key, bundle) =>
            {
                return TryLoadNativeSpriteFromBundle(bundle, imageName, out foundSprite);
            }))
            {
                sprite = foundSprite;
                return sprite != null;
            }

            // Only scan character atlases for sub-assets; other bundles are searched by
            // direct asset path above so they are not exhaustively materialized.
            foundSprite = null;
            if (FindInDiskBundles((key, bundle) =>
            {
                foundSprite = FindSpriteInBundle(bundle, imageName);
                return foundSprite != null;
            }, key => IsCharacterBundle(key)))
            {
                sprite = foundSprite;
                return true;
            }

            return false;
        }

        private static bool TryLoadNativeSpriteFromBundle(AssetBundle bundle, string imageName, out Sprite sprite)
        {
            sprite = null;
            if (bundle == null) return false;

            try
            {
                sprite = bundle.LoadAsset<Sprite>(imageName);
                if (sprite != null) return true;
            }
            catch
            {
                // Try the texture name below.
            }

            try
            {
                var texture = bundle.LoadAsset<Texture2D>(imageName);
                if (texture == null) return false;

                // Pixels-per-unit must be 1, exactly as for mod art. The game lays everything
                // out in raw pixels against a fixed 1280x720 reference.
                sprite = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 1f);
                return sprite != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Move index-based jumps along by a number of steps.
        ///
        /// A branch does not jump to a label, it jumps to a step number. Those numbers are
        /// worked out while the mod's own steps are emitted, so when the game's steps are
        /// put in front of them every such jump ends up short by the length of that
        /// prefix. Without this shift a branch inside a hook would quietly jump into the
        /// middle of the original scene.
        /// </summary>
        public static void ShiftLineJumps(IEnumerable<Line> lines, int offset)
        {
            if (lines == null || offset == 0) return;

            foreach (var line in lines)
            {
                if (line == null) continue;

                // A choice in a menu is a jump as well: the runner continues at the
                // step the choice points at, so that number has to move too.
                if (line is RenpyMenuInput menu && menu.entries != null)
                {
                    foreach (var entry in menu.entries)
                    {
                        if (entry == null) continue;
                        if (entry.gotoLineTarget >= 0) entry.gotoLineTarget += offset;
                    }
                    continue;
                }

                var type = line.GetType();
                if (type.Name != "RenpyGoToLine" && type.Name != "RenpyGoToLineUnless") continue;

                try
                {
                    var field = type.GetField("targetLine",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field == null) continue;

                    int target = Convert.ToInt32(field.GetValue(line));
                    if (target >= 0) field.SetValue(line, target + offset);
                }
                catch (Exception)
                {
                    // A jump that cannot be moved is left alone rather than guessed at.
                }
            }
        }

        /// <summary>
        /// The pixel size a picture is displayed at by the game.
        ///
        /// Cut scenes are not shown at their own size: the game stores the size it uses in
        /// the picture's own block. Reading it is what makes a picture shown by a mod match
        /// the same picture shown by the shipped script.
        /// </summary>
        public static bool TryGetImageSize(string imageName, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (string.IsNullOrEmpty(imageName)) return false;

            try
            {
                var script = Script;
                var table = s_blocksField?.GetValue(script?.Blocks) as Dictionary<string, RenpyBlock>;
                if (table == null || !table.TryGetValue(imageName, out var block)) return false;

                var contents = BlockSteps(block);
                if (contents == null) return false;

                foreach (var step in contents)
                {
                    if (step == null) continue;

                    // A picture that is only loaded says nothing about its size, so it
                    // leaves the size to the transform that places it.
                    if (step.GetType().Name == "RenpyLoadImage") continue;

                    if (step.GetType().Name != "RenpySize") continue;

                    var type = step.GetType();
                    var x = type.GetProperty("SizeX",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var y = type.GetProperty("SizeY",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    width = Convert.ToInt32(x?.GetValue(step) ?? 0);
                    height = Convert.ToInt32(y?.GetValue(step) ?? 0);

                    // The size is usually kept as an expression rather than as a plain
                    // number, so the numbers themselves have to be read out of the
                    // expression's own constants.
                    if (width <= 0 || height <= 0)
                    {
                        var exprX = type.GetField("SizeXExpr",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?.GetValue(step);
                        var exprY = type.GetField("SizeYExpr",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?.GetValue(step);

                        if (width <= 0) width = (int)FirstConstant(exprX);
                        if (height <= 0) height = (int)FirstConstant(exprY);
                    }

                    if (width > 0 && height > 0) return true;
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Could not read the size of '" + imageName + "': " + e.Message);
            }

            return false;
        }

        /// <summary>
        /// What a transform says, in the terms a picture can be placed by.
        ///
        /// The game writes a transform as text, and the numbers in that text are the only
        /// place the real values appear: the fields beside it hold zeroes when a transform
        /// does not change a property. So the text is read and the values taken from it.
        /// </summary>
        public sealed class TransformState
        {
            /// <summary>
            /// Where the picture hangs, as a fraction of the screen.
            ///
            /// The two axes are kept apart, because a transform may move a picture
            /// sideways and say nothing about up and down. Treating the pair as one value
            /// would throw the untouched axis away and move the picture wrongly.
            /// </summary>
            public bool HasXAlign { get; set; }
            public float AlignX { get; set; }
            public bool HasYAlign { get; set; }
            public float AlignY { get; set; }

            /// <summary>Where the picture's corner sits, in pixels.</summary>
            public bool HasXPos { get; set; }
            public float PosX { get; set; }
            public bool HasYPos { get; set; }
            public float PosY { get; set; }

            public bool HasZoom { get; set; }
            public float ZoomX { get; set; } = 1f;
            public float ZoomY { get; set; } = 1f;

            public bool HasAlpha { get; set; }
            public float Alpha { get; set; } = 1f;

            public bool HasRotate { get; set; }
            public float Rotate { get; set; }

            /// <summary>A nudge in pixels, along or across.</summary>
            public bool HasXOffset { get; set; }
            public float OffsetX { get; set; }
            public bool HasYOffset { get; set; }
            public float OffsetY { get; set; }
        }

        /// <summary>
        /// A transform that moves a picture from one state to another.
        ///
        /// This is what a scheduler needs: where the picture starts, where it ends, how long
        /// the move takes and how it eases along the way.
        /// </summary>
        public sealed class TransformAnimation
        {
            public TransformState Start { get; set; }
            public TransformState End { get; set; }

            /// <summary>How long the move takes, in seconds.</summary>
            public float Duration { get; set; }

            /// <summary>How the move eases, as the game's own name for it.</summary>
            public string EaseType { get; set; }

            /// <summary>True when the transform only places a picture and does not move it.</summary>
            public bool IsStill { get; set; }
        }

        /// <summary>
        /// Read what a named transform does.
        ///
        /// A transform is written as two steps: one that puts the picture where it starts,
        /// and one that eases it somewhere else. The numbers of both are in their text.
        /// </summary>
        public static bool TryGetTransformAnimation(string blockName, out TransformAnimation animation)
        {
            animation = null;
            if (string.IsNullOrEmpty(blockName)) return false;

            try
            {
                var script = Script;
                var table = s_blocksField?.GetValue(script?.Blocks) as Dictionary<string, RenpyBlock>;
                if (table == null || !table.TryGetValue(blockName, out var block)) return false;

                var contents = BlockSteps(block);
                if (contents == null) return false;

                var result = new TransformAnimation { IsStill = true };
                bool sawImmediate = false;

                foreach (var step in contents)
                {
                    if (step == null) continue;

                    string kind = step.GetType().Name;
                    if (kind != "RenpyImmediateTransform" && kind != "RenpyEasedTransform") continue;

                    var command = step.GetType().GetField("TransformCommand",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.GetValue(step) as string;
                    if (string.IsNullOrEmpty(command)) continue;

                    var state = new TransformState();
                    if (!ParseTransformCommand(command, state)) continue;

                    if (kind == "RenpyImmediateTransform")
                    {
                        result.Start = state;
                        sawImmediate = true;
                        Log.LogInfo("[StoryAPI] transform '" + blockName + "' starts: " + command);
                    }
                    else
                    {
                        result.End = state;

                        var duration = step.GetType().GetField("Duration",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?.GetValue(step);
                        result.Duration = duration == null
                            ? 0f : Convert.ToSingle(duration);

                        var ease = step.GetType().GetField("Type",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            ?.GetValue(step);
                        result.EaseType = ease == null ? "Linear" : ease.ToString();

                        Log.LogInfo("[StoryAPI] transform '" + blockName + "' ends: " + command);
                    }
                }

                if (!sawImmediate && result.End == null) return false;

                // A transform that only eases to the same place it starts does not move.
                if (result.End != null && result.Duration > 0f) result.IsStill = false;

                animation = result;
                return true;
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Could not read transform '" + blockName + "': " + e.Message);
            }

            return false;
        }

        /// <summary>
        /// Read the numbers out of a transform written as text.
        ///
        /// The text is a list of words, each followed by a value, and a value may be a
        /// product such as "1.10*0.6666667", which is how a transform scales a picture by
        /// its own size and by a factor at the same time.
        /// </summary>
        private static bool ParseTransformCommand(string command, TransformState state)
        {
            var words = command.Split(new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            bool any = false;
            for (int i = 0; i + 1 < words.Length; i++)
            {
                string word = words[i];
                string value = words[i + 1];

                float number;
                if (!TryEvaluateNumber(value, out number)) continue;

                switch (word)
                {
                    case "xalign":
                        state.HasXAlign = true; state.AlignX = number; any = true; break;
                    case "yalign":
                        state.HasYAlign = true; state.AlignY = number; any = true; break;
                    case "xpos":
                        state.HasXPos = true; state.PosX = number; any = true; break;
                    case "ypos":
                        state.HasYPos = true; state.PosY = number; any = true; break;
                    case "zoom":
                        state.HasZoom = true;
                        state.ZoomX = number; state.ZoomY = number; any = true; break;
                    case "xzoom":
                        state.HasZoom = true; state.ZoomX = number; any = true; break;
                    case "yzoom":
                        state.HasZoom = true; state.ZoomY = number; any = true; break;
                    case "alpha":
                        state.HasAlpha = true; state.Alpha = number; any = true; break;
                    case "rotate":
                        state.HasRotate = true; state.Rotate = number; any = true; break;
                    case "xoffset":
                        state.HasXOffset = true; state.OffsetX = number; any = true; break;
                    case "yoffset":
                        state.HasYOffset = true; state.OffsetY = number; any = true; break;
                }
            }

            return any;
        }

        /// <summary>
        /// Work out the value of a number that may be a product of factors.
        /// </summary>
        private static bool TryEvaluateNumber(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(text)) return false;

            var parts = text.Split('*');
            float product = 1f;
            bool any = false;

            foreach (var part in parts)
            {
                float factor;
                if (!float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out factor))
                {
                    return false;
                }
                product *= factor;
                any = true;
            }

            value = product;
            return any;
        }

        /// <summary>
        /// Write every texture in the resident bundles out as a PNG.
        ///
        /// The bundles are already decompressed and resident, so the pixels can be read
        /// straight out of the running game with Unity's own API. That avoids having to
        /// implement the asset container's compression ourselves, and it means the files
        /// are exactly what the game displays, alpha included.
        ///
        /// Returns the number of images written.
        /// </summary>
        public static int DumpTextures(string folder, string wantedBundle = null)
        {
            string previous = System.IO.Path.Combine(folder, "sizes.txt");
            if (System.IO.File.Exists(previous))
            {
                // The game art rarely changes between launches. Do not decode every bundle
                // again unless the user deletes sizes.txt to request a fresh export.
                try
                {
                    foreach (var line in System.IO.File.ReadAllLines(previous))
                    {
                        int tab = line.IndexOf("  ", StringComparison.Ordinal);
                        if (tab <= 0) continue;
                        Sizes[line.Substring(0, tab)] = line.Substring(tab + 2);
                    }
                }
                catch { }
                Log.LogInfo("[StoryAPI] Texture export already exists; skipped. Delete sizes.txt to regenerate it.");
                return 0;
            }

            System.IO.Directory.CreateDirectory(folder);
            var resources = Renpy.Resources as ActiveLabelAssetBundles;
            var bundlesField = typeof(ActiveLabelAssetBundles).GetField(
                "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic);
            var bundles = bundlesField?.GetValue(resources) as ActiveAssetBundles;
            var loaded = typeof(ActiveAssetBundles).GetField(
                "m_ActiveAssetBundles", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(bundles) as Dictionary<string, AssetBundle>;
            if (loaded == null) return 0;

            string source = null;
            foreach (var candidate in new[]
            {
                System.IO.Path.Combine(Application.streamingAssetsPath, "Windows"),
                Application.streamingAssetsPath,
                System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "AssetBundles", "Windows"),
                System.IO.Path.Combine(Application.dataPath, "StreamingAssets", "AssetBundles"),
            })
            {
                if (!System.IO.Directory.Exists(candidate)) continue;
                if (System.IO.Directory.GetFiles(candidate, "*.cy").Length == 0) continue;
                source = candidate;
                break;
            }

            if (source == null)
            {
                Log.LogWarning("[StoryAPI] Found no .cy bundles to read art from.");
            }
            else
            {
                Log.LogInfo("[StoryAPI] Streaming art export from " + source);
            }

            string scratch = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "storyapi-bundles");
            System.IO.Directory.CreateDirectory(scratch);
            int written = 0;
            Sizes.Clear();

            try
            {
                // First inspect only bundles which the game already owns. Do not unload them.
                foreach (var pair in loaded.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var bundle = pair.Value;
                    if (bundle == null) continue;
                    string name;
                    try { name = bundle.name; }
                    catch { name = pair.Key; }
                    if (!string.IsNullOrEmpty(wantedBundle) &&
                        name.IndexOf(wantedBundle, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    ExportTextureBundle(bundle, name, folder, ref written);
                    CleanupExportTextures();
                }

                // Then process disk-only bundles one by one. The old implementation opened
                // every .cy bundle into a List before exporting any of them; that kept many
                // bundles and their decoded assets alive at the same time.
                if (source != null)
                {
                    var residentKeys = new HashSet<string>(loaded.Keys, StringComparer.OrdinalIgnoreCase);
                    foreach (var file in System.IO.Directory.GetFiles(source, "*.cy").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        string key = System.IO.Path.GetFileNameWithoutExtension(file);
                        if (residentKeys.Contains(key)) continue;
                        if (!string.IsNullOrEmpty(wantedBundle) &&
                            key.IndexOf(wantedBundle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (FindLoadedBundle(key) != null) continue;

                        string plain = System.IO.Path.Combine(scratch, key + ".bundle");
                        AssetBundle bundle = null;
                        try
                        {
                            if (!System.IO.File.Exists(plain) ||
                                System.IO.File.GetLastWriteTimeUtc(plain) < System.IO.File.GetLastWriteTimeUtc(file))
                            {
                                Deobfuscate(file, plain);
                            }

                            bundle = AssetBundle.LoadFromFile(plain);
                            if (bundle == null)
                            {
                                Log.LogWarning("[StoryAPI] Would not open bundle " + key);
                                continue;
                            }

                            string name;
                            try { name = bundle.name; }
                            catch { name = key; }
                            ExportTextureBundle(bundle, name, folder, ref written);
                        }
                        catch (Exception e)
                        {
                            Log.LogWarning("[StoryAPI] Skipped bundle " + key + ": " + e.Message);
                        }
                        finally
                        {
                            // This bundle was opened only for this export. Unload its objects
                            // immediately instead of holding it until a later Update tick.
                            if (bundle != null)
                            {
                                try { bundle.Unload(true); }
                                catch (Exception e) { Log.LogDebug("[StoryAPI] Could not unload exported bundle '" + key + "': " + e.Message); }
                            }
                            CleanupExportTextures();
                        }
                    }
                }
            }
            finally
            {
                ActiveBundle = null;
                CleanupExportTextures();
            }

            try
            {
                System.IO.File.WriteAllLines(System.IO.Path.Combine(folder, "sizes.txt"),
                    Sizes.OrderBy(p => p.Key, StringComparer.Ordinal)
                         .Select(p => p.Key + "  " + p.Value));
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Could not write texture sizes: " + e.Message);
            }

            return written;
        }

        private static void ExportTextureBundle(AssetBundle bundle, string name, string folder, ref int written)
        {
            if (bundle == null || string.IsNullOrEmpty(name)) return;

            int nullLoads = 0;
            ActiveBundle = bundle;
            try
            {
                var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in AssetPaths(bundle))
                {
                    string asset = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrEmpty(asset)) continue;
                    bool isPng = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                    if (!isPng &&
                        !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                        !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!isPng)
                    {
                        // Prefab export is only needed for assets that can actually carry art.
                        // Do not load an arbitrary object just to see whether it exists.
                        if (done.Contains(asset)) continue;
                        GameObject obj;
                        try { obj = bundle.LoadAsset<GameObject>(path); }
                        catch { obj = null; }
                        if (obj == null)
                        {
                            nullLoads++;
                            continue;
                        }

                        Texture2D art = FindTexture(obj, new HashSet<object>(), asset);
                        if (art == null)
                        {
                            nullLoads++;
                            continue;
                        }

                        Write(name, folder, obj.name, art, path, ref written);
                        done.Add(obj.name);
                        continue;
                    }

                    try
                    {
                        Texture2D texture = bundle.LoadAsset<Texture2D>(path);
                        if (texture == null)
                        {
                            Sprite sprite = bundle.LoadAsset<Sprite>(path);
                            if (sprite == null || sprite.texture == null)
                            {
                                nullLoads++;
                                continue;
                            }
                            texture = Crop(sprite);
                        }
                        if (texture == null)
                        {
                            nullLoads++;
                            continue;
                        }

                        // Write performs one encode; the previous implementation encoded a
                        // complete PNG here and discarded it, then encoded it again in Write.
                        Write(name, folder, asset, texture, path, ref written);
                        done.Add(asset);
                    }
                    catch (Exception e)
                    {
                        Log.LogDebug("[StoryAPI] Could not export texture '" + path + "': " + e.Message);
                    }
                }
            }
            finally
            {
                ActiveBundle = null;
            }

            if (nullLoads > 0)
            {
                Log.LogWarning("[StoryAPI] bundle '" + name + "': " + nullLoads +
                               " listed image(s) could not be loaded");
            }
        }

        private static readonly HashSet<Texture2D> s_exportCutTextures = new HashSet<Texture2D>();

        private static void CleanupExportTextures()
        {
            var toDestroy = new HashSet<Texture2D>(s_exportCutTextures);
            foreach (var texture in ReadBackCache.Values)
            {
                if (texture != null) toDestroy.Add(texture);
            }
            foreach (var texture in toDestroy)
            {
                if (texture == null) continue;
                try { UnityEngine.Object.Destroy(texture); }
                catch { /* already destroyed */ }
            }
            s_exportCutTextures.Clear();
            ReadBackCache.Clear();
        }

        /// <summary>
        /// Cut a sprite's own rectangle out of the sheet it was packed into.
        ///
        /// A sprite that fills its sheet needs no work, and its texture is returned as it
        /// is, because copying would cost memory for no gain.
        /// </summary>
        private static Texture2D Crop(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;

            Texture2D sheet = sprite.texture;
            Rect rect = sprite.rect;
            int width = (int)rect.width;
            int height = (int)rect.height;
            int left = (int)rect.x;
            int bottom = (int)rect.y;

            if (width <= 0 || height <= 0) return null;

            // A full-sheet sprite can be written directly. MakeReadable will create a
            // readable copy if necessary; there is no reason to copy the atlas here too.
            if (width >= sheet.width && height >= sheet.height) return sheet;

            Texture2D cut = null;
            try
            {
                cut = new Texture2D(width, height, TextureFormat.RGBA32, false);
                if (sheet.isReadable)
                {
                    // Only allocate pixel data for the requested rectangle, not the entire
                    // atlas. Color[] is 16 bytes/pixel, so a whole-sheet read can be huge.
                    Color[] cutPixels = sheet.GetPixels(left, bottom, width, height);
                    cut.SetPixels(cutPixels);
                    cut.Apply();
                }
                else
                {
                    // Read back only this sprite's rectangle on the GPU. The former path
                    // copied the full atlas to CPU memory before cropping it, which could
                    // create hundreds of megabytes of transient allocations per atlas.
                    RenderTexture target = RenderTexture.GetTemporary(width, height, 0);
                    RenderTexture previous = RenderTexture.active;
                    try
                    {
                        Vector2 scale = new Vector2((float)width / sheet.width,
                                                    (float)height / sheet.height);
                        Vector2 offset = new Vector2((float)left / sheet.width,
                                                     (float)bottom / sheet.height);
                        Graphics.Blit(sheet, target, scale, offset);
                        RenderTexture.active = target;
                        cut.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        cut.Apply();
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        RenderTexture.ReleaseTemporary(target);
                    }
                }

                if (ActiveBundle != null) s_exportCutTextures.Add(cut);
                return cut;
            }
            catch (Exception e)
            {
                if (cut != null)
                {
                    try { UnityEngine.Object.Destroy(cut); }
                    catch { /* already destroyed */ }
                }
                Log.LogDebug("[StoryAPI] Could not crop sprite '" + sprite.name + "': " + e.Message);
                // A full sheet is still a usable export fallback.
                return sheet;
            }
        }

        /// <summary>
        /// Save one piece of art, unless it is a loose part rather than a pose.
        ///
        /// Loose parts (a mouth, an eye, a mask) exist only to be combined into a pose and
        /// are never shown on their own, so they are recorded in the reference but no image
        /// is written for them. Their size and path are kept either way.
        /// </summary>
        private static void Write(string bundleName, string folder, string asset,
            Texture2D texture, string path, ref int written)
        {
            if (string.IsNullOrEmpty(asset)) return;

            Texture2D readable = null;
            try
            {
                Sizes[asset] = texture.width + "x" + texture.height + "  " + path;
                if (DetailNames.Contains(asset)) return;

                readable = MakeReadable(texture);
                if (readable == null) return;

                string sub = System.IO.Path.Combine(folder, SanitizeFileName(bundleName));
                System.IO.Directory.CreateDirectory(sub);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(
                    sub, SanitizeFileName(asset) + ".png"), readable.EncodeToPNG());
                written++;
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] Could not save '" + asset + "': " + e.Message);
            }
            finally
            {
                // MakeReadable returns the source when it is already CPU-readable. Only
                // destroy a copy allocated specifically for this write.
                if (readable != null && !object.ReferenceEquals(readable, texture))
                {
                    try { UnityEngine.Object.Destroy(readable); }
                    catch { /* already destroyed */ }
                }
            }
        }

        /// <summary>
        /// Return a texture whose pixels can be read back.
        ///
        /// Art shipped for a game is often imported without CPU read access, which is
        /// correct for shipping but stops it being turned into a file. Drawing it back
        /// through the GPU produces a copy that can be read, without changing the original.
        /// A texture that is already readable is returned untouched.
        /// </summary>
        private static Texture2D MakeReadable(Texture2D texture)
        {
            if (texture == null || texture.isReadable) return texture;

            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0);
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;

                copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply();
                return copy;
            }
            catch (Exception)
            {
                if (copy != null)
                {
                    try { UnityEngine.Object.Destroy(copy); }
                    catch { /* already destroyed */ }
                }
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        /// <summary>Names that are building blocks of a pose rather than a pose in their own right.</summary>
        private static readonly HashSet<string> DetailNames = new HashSet<string>(
            new[]
            {
                "mouth", "eye", "eyes", "n_eye", "n_blackeyes", "eyesfull",
                "mask", "maskb", "mask_2", "mask_3",
                "child", "child_2", "ghost_blood", "ghost", "blood_drop",
                "g2", "g3", "g4", "g2a", "g3a", "g4a",
                "noise1", "noise2", "noise3", "noise4",
                "vignette", "veinmask", "veinmaskb",
            }, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The image assets a bundle holds.
        ///
        /// Prefers the bundle's own list of asset paths, because that is the game's real
        /// naming and needs no assumptions about how the art is wired up.
        /// </summary>
        private static string[] AssetPaths(AssetBundle bundle)
        {
            if (bundle == null) return new string[0];
            try
            {
                var paths = bundle.GetAllAssetNames();
                return paths ?? new string[0];
            }
            catch (Exception e)
            {
                // Do not fall back to LoadAllAssets<Texture2D>(); that can force every
                // texture in a bundle into memory just to reconstruct a list of names.
                Log.LogDebug("[StoryAPI] Could not read asset path metadata: " + e.Message);
                return new string[0];
            }
        }

        /// <summary>
        /// Find the picture belonging to a scene object.
        ///
        /// DDLC's own prefabs hold their artwork on the game's custom components rather
        /// than on a Unity sprite renderer, and those component types are internal, so
        /// there is no name to code against. Instead every component is scanned for a
        /// texture field. That way the lookup keeps working whatever the art is attached
        /// to, which is what makes this usable as a reference tool.
        /// </summary>
        private static Texture2D FindTexture(GameObject go, HashSet<object> inspected, string assetName)
        {
            // Poses are packed into texture atlases, so the picture is a rectangle inside
            // a shared sheet and has to be cut out. Try one named sprite from this bundle
            // first; walking the scene object is the fallback.
            Sprite sprite = FindSpriteInBundle(ActiveBundle, assetName);
            if (sprite != null)
            {
                var cut = Crop(sprite);
                if (cut != null) return cut;
            }

            var sprites = new List<Sprite>();
            var textures = new List<Texture2D>();
            CollectArt(go, inspected, sprites, textures);

            sprite = ChooseSprite(sprites, assetName);
            if (sprite != null && sprite.texture != null) return Crop(sprite);

            // An object that references exactly one sprite is showing that sprite, so
            // the picture is known without needing the name to match.
            var only = sprites.Distinct().ToList();
            if (only.Count == 1) return Crop(only[0]);

            if (Probed < 3)
            {
                Probed++;
                Log.LogInfo("[StoryAPI] probe '" + assetName + "': " + only.Count +
                            " sprite(s), " + textures.Count + " texture(s)");
                foreach (var candidate in only)
                {
                    Log.LogInfo("[StoryAPI]    sprite '" + candidate.name + "' rect=" +
                                candidate.rect + " sheet=" +
                                (candidate.texture == null ? "none" :
                                 candidate.texture.width + "x" + candidate.texture.height));
                }
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component != null)
                    {
                        Log.LogInfo("[StoryAPI]    component " + component.GetType().Name);
                    }
                }
            }

            var texture = ChooseTexture(textures, assetName);
            if (texture != null && !texture.isReadable) texture = ReadBack(texture);
            return texture;
        }

        private static int Probed;

        /// <summary>The bundle being read by the art exporter.</summary>
        [ThreadStatic] private static AssetBundle ActiveBundle;

        /// <summary>Readable copies, so a shared atlas is only copied out once.</summary>
        private static readonly Dictionary<int, Texture2D> ReadBackCache =
            new Dictionary<int, Texture2D>();


        /// <summary>Gather every sprite and texture an object or its children reference.</summary>
        private static void CollectArt(GameObject go, HashSet<object> inspected,
            List<Sprite> sprites, List<Texture2D> textures)
        {
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;
                if (!inspected.Add(component)) continue;

                if (component is SpriteRenderer)
                {
                    var sprite = ((SpriteRenderer)component).sprite;
                    if (sprite != null) { sprites.Add(sprite); if (sprite.texture != null) textures.Add(sprite.texture); }
                }

                if (component is UnityEngine.UI.Image)
                {
                    var sprite = ((UnityEngine.UI.Image)component).sprite;
                    if (sprite != null) { sprites.Add(sprite); if (sprite.texture != null) textures.Add(sprite.texture); }
                }

                foreach (var field in ComponentFields(component))
                {
                    object value;
                    try { value = field.GetValue(component); }
                    catch { continue; }

                    if (value is Sprite sprite)
                    {
                        sprites.Add(sprite);
                        if (sprite.texture != null) textures.Add(sprite.texture);
                    }
                    else if (value is Texture2D texture)
                    {
                        textures.Add(texture);
                    }
                }
            }

            foreach (Transform child in go.transform)
            {
                CollectArt(child.gameObject, inspected, sprites, textures);
            }
        }

        /// <summary>Pick the sprite that is the object's own picture.</summary>
        private static Sprite ChooseSprite(List<Sprite> sprites, string assetName)
        {
            var distinct = sprites.GroupBy(s => s.GetInstanceID())
                                  .Select(g => g.First()).ToList();
            if (distinct.Count == 0) return null;

            foreach (var sprite in distinct)
            {
                if (string.Equals(sprite.name, assetName, StringComparison.OrdinalIgnoreCase))
                {
                    return sprite;
                }
            }

            foreach (var sprite in distinct)
            {
                if (!string.IsNullOrEmpty(sprite.name) && !string.IsNullOrEmpty(assetName) &&
                    sprite.name.IndexOf(assetName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return sprite;
                }
            }

            return null;
        }

        /// <summary>Fall back to the largest texture when no sprite names the picture.</summary>
        private static Texture2D ChooseTexture(List<Texture2D> textures, string assetName)
        {
            var distinct = textures.GroupBy(t => t.GetInstanceID())
                                   .Select(g => g.First()).ToList();
            if (distinct.Count == 0) return null;

            var biggest = distinct.OrderByDescending(t => (long)t.width * t.height).First();
            Log.LogInfo("[StoryAPI] '" + assetName + "': no matching sprite, taking " +
                        biggest.name + " (" + biggest.width + "x" + biggest.height + ")");
            return biggest;
        }

        /// <summary>
        /// Copy a texture that the game does not allow read access to.
        ///
        /// Drawing it back through the GPU gives a copy that can be read back and saved,
        /// which is what makes the art exportable at all.
        /// </summary>
        private static Texture2D ReadBack(Texture2D texture)
        {
            if (texture == null || texture.isReadable) return texture;

            // Keep one CPU-readable copy per source during a bundle export, so multiple
            // prefabs that point at the same fallback texture do not recopy it each time.
            Texture2D cached;
            if (ReadBackCache.TryGetValue(texture.GetInstanceID(), out cached) && cached != null)
            {
                return cached;
            }

            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0);
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;

                copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply();
                ReadBackCache[texture.GetInstanceID()] = copy;
                return copy;
            }
            catch (Exception)
            {
                if (copy != null)
                {
                    try { UnityEngine.Object.Destroy(copy); }
                    catch { /* already destroyed */ }
                }
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        /// <summary>Every field of a component type, base types included, read once per type.</summary>
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache =
            new Dictionary<Type, FieldInfo[]>();

        private static FieldInfo[] ComponentFields(object component)
        {
            var type = component.GetType();
            FieldInfo[] cached;
            if (FieldCache.TryGetValue(type, out cached)) return cached;

            var all = type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                     BindingFlags.NonPublic);
            FieldCache[type] = all;
            return all;
        }

        /// <summary>
        /// Undo the byte flip the game applies to its bundle files.
        ///
        /// Every byte of the file is XORed with 0x28, which turns a normal "UnityFS"
        /// header into the unreadable bytes you see on disk. It is a self-inverse
        /// operation, so the same routine both scrambles and restores.
        /// </summary>
        private static void Deobfuscate(string source, string destination)
        {
            const byte key = 0x28;
            var buffer = new byte[1024 * 1024];

            using (var input = System.IO.File.OpenRead(source))
            using (var output = System.IO.File.Create(destination))
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++) buffer[i] ^= key;
                    output.Write(buffer, 0, read);
                }
            }
        }

        /// <summary>Pixel size per asset name, filled in as textures are dumped.</summary>
        public static Dictionary<string, string> Sizes = new Dictionary<string, string>();

        /// <summary>Make a name safe to use as a Windows file name.</summary>
        private static string SanitizeFileName(string name)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars).Trim();
        }

        /// <summary>Start (or restart) execution at a label, replacing the current stack.</summary>
        public static bool Run(string label)
        {
            var execution = Execution;
            if (execution == null || string.IsNullOrEmpty(label)) return false;
            try
            {
                execution.Run(label);
                return true;
            }
            catch (Exception e)
            {
                Log.LogError("[StoryAPI] Run('" + label + "') failed: " + e);
                return false;
            }
        }

        /// <summary>
        /// Jump without tearing down the current call stack.
        ///
        /// The runner is a coroutine that spends most of its life parked on
        /// "wait for a click". Naming a new target does not move it, because the
        /// coroutine does not look at the call stack until the player advances.
        /// Briefly engaging auto-mode is what actually walks it into the new label;
        /// <see cref="Tick"/> hands control back to the player afterwards.
        /// </summary>
        public static bool Jump(string label)
        {
            var execution = Execution;
            if (execution == null || string.IsNullOrEmpty(label)) return false;

            if (!LabelExists(label))
            {
                Log.LogWarning("Cannot jump to '" + label + "': no such label.");
                return false;
            }

            try
            {
                execution.Jump(label);
                NudgeOutOfDialogue();
                return true;
            }
            catch (Exception e)
            {
                Log.LogError("[StoryAPI] Jump('" + label + "') failed: " + e);
                return false;
            }
        }

        private static float s_releaseAutoAt;

        private static void NudgeOutOfDialogue()
        {
            try
            {
                var window = UnityEngine.Object.FindObjectOfType<RenpyWindowManager>();
                if (window?.auto == null) return;

                window.auto.isOn = true;
                s_releaseAutoAt = Time.realtimeSinceStartup + 0.4f;
            }
            catch
            {
                // Nothing parked to release.
            }
        }

        /// <summary>
        /// Restart the story at a label, the same way the launcher drops the player into
        /// a chapter.
        ///
        /// This is deliberately heavier than <see cref="Jump"/>. A plain jump inherits
        /// whatever the previous label left behind: its asset bundles, its variables,
        /// and a callstack that may be parked mid-line. For a mod label, none of that
        /// is meaningful, so a mod is entered from a clean state instead.
        /// </summary>
        public static bool StartAt(string label)
        {
            var main = Main;
            if (main?.RenpyScript == null || string.IsNullOrEmpty(label)) return false;

            if (!LabelExists(label))
            {
                Log.LogWarning("Cannot start '" + label + "': no such label.");
                return false;
            }

            try
            {
                // The context is deliberately left alone.
                //
                // The game builds it once at start-up: it clears every variable and then
                // runs InitializeContext, which repopulates the system scopes -- `audio`
                // and the style tables among them -- and installs the Python proxy.
                //
                // Doing either half again is worse than useless. Re-initialising re-runs
                // the proxy setup and throws while stamping the already-set variables;
                // resetting alone drops the `audio` scope, after which `play music1`
                // cannot find its track and kills the script runner. A mod label runs
                // perfectly well on the context the game already built.
                var context = main.GetExecutionContext();

                // Which transitions the game can resolve is a property of the running
                // game, so it is reported once per entry rather than guessed at.
                ReportTransitions(context);

                // Some of the game's blocks read this before Act 1 ever sets it.
                if (context != null && !context.TryGetVariableFloat("pause_length", out float _))
                {
                    context.SetVariableFloat("pause_length", 0.05f);
                }
                // Not Renpy.ContextControl.GameLoaded(): that is the save-load path, and
                // once the context is initialised it tries to stamp attributes onto the
                // persistent object and throws. A fresh start has nothing to load.

                var window = UnityEngine.Object.FindObjectOfType<RenpyWindowManager>();
                window?.ResetForNewGame();

                EnsureBaseAssets();

                InstallSharedAssetMap();

                main.HardJump(label);
                Log.LogInfo("Started '" + label + "'.");
                ReportAssetReachability();
                ReportAudioScope();
                return true;
            }
            catch (Exception e)
            {
                Log.LogError("[StoryAPI] StartAt('" + label + "') failed: " + e);
                return false;
            }
        }

        /// <summary>
        /// Make sure the bundles holding the base game's art are resident.
        ///
        /// The game normally swaps these per label. A label it does not know about --
        /// which is every mod label -- leaves them alone, but a mod entered after a
        /// save load or a launcher chapter may find a leaner set in place. Asking for
        /// the permanent set is cheap when they are already loaded and is the
        /// difference between a mod sprite showing and the screen staying black.
        /// </summary>
        private static readonly string[] BaseBundles =
        {
            "gui", "bg", "cg", "monika", "yuri", "natsuki", "sayori", "sfx-coarse",
        };

        private static void EnsureBaseAssets()
        {
            try
            {
                var resources = Renpy.Resources;
                var field = resources?.GetType().GetField(
                    "m_ActiveAssetBundles",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                var bundles = field?.GetValue(resources) as ActiveAssetBundles;
                if (bundles == null) return;

                // Whatever is already resident is kept: the game's applier throws when
                // asked to unload a bundle that was never loaded, which is exactly what
                // happens to overlay-like bundles during Act 1.
                var desired = new List<string>(bundles.ActiveBundles ?? Enumerable.Empty<string>());
                foreach (var name in BaseBundles)
                {
                    if (!desired.Contains(name)) desired.Add(name);
                }

                bundles.ApplyDesiredBundles(desired, false);
            }
            catch (Exception e)
            {
                Log.LogWarning("Could not ensure base art bundles: " + e.Message);
            }
        }

        /// <summary>Called once per frame to return control to the player after a jump.</summary>
        public static void Tick()
        {
            if (s_releaseAutoAt <= 0f) return;
            if (Time.realtimeSinceStartup < s_releaseAutoAt) return;

            s_releaseAutoAt = 0f;
            try
            {
                var window = UnityEngine.Object.FindObjectOfType<RenpyWindowManager>();
                if (window?.auto != null) window.auto.isOn = false;
            }
            catch
            {
                // The window went away; nothing to release.
            }
        }

        // ----------------------------------------------------------- registration

        /// <summary>
        /// Publish a compiled block so the script runner can resolve its label.
        /// The public <c>AddFromDictionary</c> only fills the block table; the separate
        /// entry-point table is what <c>TryResolveLabel</c> actually consults.
        /// </summary>
        public static bool RegisterBlock(string key, RenpyBlock block)
        {
            var script = Script;
            if (script?.Blocks == null || block == null) return false;
            try
            {
                var entryPoints = s_blockEntryPointsField?.GetValue(script.Blocks) as Dictionary<string, BlockEntryPoint>;
                var table = s_blocksField?.GetValue(script.Blocks) as Dictionary<string, RenpyBlock>;
                if (entryPoints == null || table == null) return false;

                block.RenpyScript = script;

                if (!table.ContainsKey(key)) table.Add(key, block);
                else table[key] = block;

                if (!entryPoints.ContainsKey(key))
                    entryPoints.Add(key, new BlockEntryPoint(key));

                return true;
            }
            catch (Exception e)
            {
                Log.LogError("[StoryAPI] RegisterBlock('" + key + "') failed: " + e);
                return false;
            }
        }

        /// <summary>True when a label resolves through the game's own lookup.</summary>
        public static bool LabelExists(string label)
        {
            var script = Script;
            if (script?.Blocks == null || string.IsNullOrEmpty(label)) return false;
            try
            {
                return script.Blocks.GetEntryPoint(label) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Drop a previously registered label so its mod can be reloaded.
        /// The tables the game holds are ordinary dictionaries, so this is the exact
        /// inverse of <see cref="RegisterBlock"/>.
        /// </summary>
        public static bool UnregisterBlock(string key)
        {
            var script = Script;
            if (script?.Blocks == null || string.IsNullOrEmpty(key)) return false;
            try
            {
                var entryPoints = s_blockEntryPointsField?.GetValue(script.Blocks) as Dictionary<string, BlockEntryPoint>;
                var table = s_blocksField?.GetValue(script.Blocks) as Dictionary<string, RenpyBlock>;
                if (entryPoints == null || table == null) return false;

                entryPoints.Remove(key);
                table.Remove(key);
                return true;
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] UnregisterBlock('" + key + "') failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Labels contributed by mods, for tooling and the command list.</summary>
        public static IEnumerable<string> ModLabels
        {
            get
            {
                var script = Script;
                if (script?.Blocks == null) return Array.Empty<string>();

                try
                {
                    var entryPoints = s_blockEntryPointsField?.GetValue(script.Blocks)
                                      as Dictionary<string, BlockEntryPoint>;
                    return entryPoints == null ? Array.Empty<string>() : new List<string>(entryPoints.Keys);
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }
        }

        private static PropertyInfo s_labelBundleProperty;

        /// <summary>
        /// Report whether a named base-game asset could be resolved right now.
        ///
        /// Two independent things have to line up for <c>scene classroom</c> to draw
        /// anything: the base game's ATL image block for that name has to be in the
        /// script, and the bundle holding the texture has to be resident *and*
        /// reachable through the active label's dependency table. This says which of
        /// the two is missing, which is the difference between "that background does
        /// not exist" and "this label cannot see any of the game's art".
        /// </summary>
        public static string ProbeAsset(string name)
        {
            var script = Script;
            bool blockExists = LabelExists(name);

            bool tableLoaded = false;
            bool mapsToBundle = false;
            try
            {
                var resources = Renpy.Resources as ActiveLabelAssetBundles;
                if (resources != null)
                {
                    tableLoaded = resources.HasLabelAssetBundleLoaded;

                    if (s_labelBundleProperty == null)
                    {
                        s_labelBundleProperty = typeof(ActiveLabelAssetBundles).GetProperty(
                            "LabelAssetBundle", BindingFlags.Instance | BindingFlags.NonPublic);
                    }

                    var table = s_labelBundleProperty?.GetValue(resources) as LabelAssetBundleDependencies;
                    mapsToBundle = table != null && table.TryGetBundle(name, out _);
                }
            }
            catch (Exception e)
            {
                return name + ": probe failed (" + e.Message + ")";
            }

            return name + ": atlBlock=" + blockExists +
                   " dependencyTableLoaded=" + tableLoaded +
                   " mapsToBundle=" + mapsToBundle;
        }

        // ------------------------------------------------------------------ text

        /// <summary>
        /// Store one line of dialogue.
        ///
        /// The runner never holds text directly: a dialogue instruction carries only a
        /// numeric id, and the words are looked up per language. So a custom line has to
        /// be written into every translation table the game has, otherwise it renders
        /// as "!MISSING" the moment the player is not running in English.
        /// </summary>
        // Keep StoryAPI's lines independently of the game's translation providers. The
        // providers are lazy: asking for every language during registration can instantiate
        // all of the game's large translation dictionaries in one frame.
        private static readonly Dictionary<string, Dictionary<int, string>> s_registeredModText =
            new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
        private static Lines s_lastSyncedActiveTextStore;

        /// <summary>
        /// Register one line in the currently active text provider only. Do not enumerate
        /// all shipped languages here: that forces Unity to construct every locale table
        /// during startup and can make the managed heap grow by several gigabytes.
        /// </summary>
        public static bool RegisterText(string label, int id, string text)
        {
            if (string.IsNullOrEmpty(label) || id <= 0 || text == null) return false;

            Dictionary<int, string> modLines;
            if (!s_registeredModText.TryGetValue(label, out modLines))
            {
                modLines = new Dictionary<int, string>();
                s_registeredModText.Add(label, modLines);
            }
            modLines[id] = text;

            var store = GetActiveTextStore();
            if (store == null) return false;

            if (!TryWriteText(store, label, id, text)) return false;
            s_lastSyncedActiveTextStore = store;
            return true;
        }

        /// <summary>
        /// Called each frame to detect a language change. When Renpy.Text points at a new
        /// provider, copy StoryAPI's registered lines into that one provider only. This
        /// preserves language switching without eagerly touching all 12 locale tables.
        /// </summary>
        public static void SyncRegisteredTextToCurrentLanguage()
        {
            if (s_registeredModText.Count == 0) return;

            Lines store = null;
            try { store = Renpy.Text as Lines; }
            catch { /* Story runtime may be between scenes. */ }
            if (store == null || object.ReferenceEquals(store, s_lastSyncedActiveTextStore)) return;

            try
            {
                var dictionary = store.GetDictionary();
                foreach (var labelPair in s_registeredModText)
                {
                    Dictionary<int, string> bucket;
                    if (!dictionary.TryGetValue(labelPair.Key, out bucket) || bucket == null)
                    {
                        bucket = new Dictionary<int, string>();
                        dictionary[labelPair.Key] = bucket;
                    }
                    foreach (var line in labelPair.Value)
                        bucket[line.Key] = line.Value;
                }

                s_lastSyncedActiveTextStore = store;
                Log.LogInfo("[StoryAPI] synced " + s_registeredModText.Sum(p => p.Value.Count) +
                            " mod text line(s) to the active language table.");
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] could not sync mod text to the active language table: " + e.Message);
            }
        }

        private static Lines GetActiveTextStore()
        {
            try
            {
                var current = Renpy.Text as Lines;
                if (current != null) return current;
            }
            catch { /* Fall back only if the active provider is not available yet. */ }

            try { return Renpy.EnglishText as Lines; }
            catch { return null; }
        }

        private static bool TryWriteText(Lines store, string label, int id, string text)
        {
            if (store == null) return false;
            try
            {
                var dictionary = store.GetDictionary();
                Dictionary<int, string> bucket;
                if (!dictionary.TryGetValue(label, out bucket) || bucket == null)
                {
                    bucket = new Dictionary<int, string>();
                    dictionary[label] = bucket;
                }
                bucket[id] = text;
                return true;
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] could not write mod text to the active language table: " + e.Message);
                return false;
            }
        }

        /// <summary>Every language table currently loaded, English first.</summary>
        private static IEnumerable<Lines> TextStores()
        {
            var seen = new HashSet<Lines>();
            var candidates = new List<Lines>();

            // The game's own Renpy object only exists once the story has started, so
            // every lookup here is best-effort rather than assumed.
            TryAdd(candidates, () => Renpy.EnglishText as Lines);

            foreach (var language in ShippedLanguages)
            {
                var lang = language;
                TryAdd(candidates, () => Renpy.GetTextProviderForPrefix(lang) as Lines);
            }

            TryAdd(candidates, () => Renpy.Text as Lines);

            foreach (var candidate in candidates)
            {
                if (seen.Add(candidate)) yield return candidate;
            }
        }

        private static void TryAdd(List<Lines> candidates, Func<Lines> probe)
        {
            try
            {
                var value = probe();
                if (value != null) candidates.Add(value);
            }
            catch
            {
                // Not initialised yet, or no bundle for that language.
            }
        }

        // -------------------------------------------------------------- dialogue

        /// <summary>
        /// Build a dialogue instruction. The type is internal to the game assembly, so
        /// the constructor is invoked reflectively; the shape is fixed by
        /// <see cref="RenpyDialogueLine"/>, so the argument order is stable.
        /// </summary>
        public static Line CreateDialogueLine(
            string label,
            int textId,
            string speaker,
            string variant,
            bool interpolate,
            bool skipWait,
            int cps,
            int cpsStart,
            int cpsEnd)
        {
            if (s_dialogueCtor == null)
            {
                EnsureProbed();
                if (s_dialogueCtor == null) return null;
            }

            try
            {
                return (Line)s_dialogueCtor.Invoke(new object[]
                {
                    label,
                    textId,
                    speaker,
                    variant,
                    interpolate,
                    skipWait,
                    cps > 0,          // hasCps
                    cps,
                    cpsStart,
                    cpsEnd,
                    false,            // cpsMultiplier
                    false,            // developerCommentary
                    -1,               // immediateUntil
                    new List<Tuple<int, float>>(),  // wait tuples
                    "say",            // command type
                });
            }
            catch (Exception e)
            {
                Log.LogError("[StoryAPI] CreateDialogueLine failed: " + e);
                return null;
            }
        }

        // ------------------------------------------------------------- variables

        /// <summary>Read a Ren'Py variable by name.</summary>
        public static bool TryGetVariable(string name, out DataValue value)
        {
            value = default;
            var context = Context;
            if (context == null || string.IsNullOrEmpty(name)) return false;
            try
            {
                return context.ResolveVariable(name, out value);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Write a Ren'Py variable by name.</summary>
        public static void SetVariable(string name, DataValue value)
        {
            var context = Context;
            if (context == null || string.IsNullOrEmpty(name)) return;
            TrySetContextVariable(context, name, value);
        }

        /// <summary>
        /// Calls the SetVariable overload exposed by the installed DDLC+ assemblies.
        /// Some game builds expose SetVariable(name, value, flag), while others expose
        /// SetVariable(name, value). Reflection keeps StoryAPI source-compatible with both.
        /// The three-argument form is preferred to preserve the original false flag.
        /// </summary>
        private static bool TrySetContextVariable(IContext context, string name, DataValue value)
        {
            if (context == null) return false;

            var candidateTypes = new[] { typeof(IContext), context.GetType() };
            MethodInfo selected = null;

            // Prefer the three-argument form where it exists.
            foreach (Type candidateType in candidateTypes)
            {
                foreach (MethodInfo method in candidateType.GetMethods(Instance))
                {
                    if (method.Name != "SetVariable" &&
                        !method.Name.EndsWith(".SetVariable", StringComparison.Ordinal))
                        continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length != 3) continue;
                    if (parameters[0].ParameterType != typeof(string) ||
                        parameters[1].ParameterType != typeof(DataValue) ||
                        parameters[2].ParameterType != typeof(bool))
                        continue;

                    selected = method;
                    break;
                }
                if (selected != null) break;
            }

            // Older/newer builds may expose only the two-argument form.
            if (selected == null)
            {
                foreach (Type candidateType in candidateTypes)
                {
                    foreach (MethodInfo method in candidateType.GetMethods(Instance))
                    {
                        if (method.Name != "SetVariable" &&
                            !method.Name.EndsWith(".SetVariable", StringComparison.Ordinal))
                            continue;

                        var parameters = method.GetParameters();
                        if (parameters.Length != 2) continue;
                        if (parameters[0].ParameterType != typeof(string) ||
                            parameters[1].ParameterType != typeof(DataValue))
                            continue;

                        selected = method;
                        break;
                    }
                    if (selected != null) break;
                }
            }

            if (selected == null)
            {
                Log.LogWarning("[StoryAPI] No compatible SetVariable overload was found on " +
                    context.GetType().FullName + ".");
                return false;
            }

            try
            {
                object[] arguments = selected.GetParameters().Length == 3
                    ? new object[] { name, value, false }
                    : new object[] { name, value };

                selected.Invoke(context, arguments);
                return true;
            }
            catch (Exception e)
            {
                Log.LogWarning("[StoryAPI] SetVariable('" + name + "') failed: " +
                    e.GetBaseException().Message);
                return false;
            }
        }

        /// <summary>
        /// Initialize a scalar script variable. Reflection bridges primitive C# values to
        /// the game's DataValue wrapper, whose conversion operators can vary between builds.
        /// If defaultOnly is true, an existing variable is never overwritten.
        /// </summary>
        public static bool TrySetVariableLiteral(string name, object literal, bool defaultOnly,
            IContext activeContext = null)
        {
            // Registration happens before a story is executing, so the game's execution
            // context may not exist yet. A VariableInitializationLine passes the live
            // instruction context when the declaration is actually reached.
            IContext context = activeContext ?? Context;
            if (string.IsNullOrEmpty(name) || context == null) return false;

            if (defaultOnly)
            {
                try
                {
                    if (context.ResolveVariable(name, out DataValue _)) return true;
                }
                catch { }
            }

            DataValue value;
            if (!TryConvertDataValue(literal, out value)) return false;

            bool assigned = TrySetContextVariable(context, name, value);
            if (literal == null)
            {
                string resolvedDescription = "<unresolved>";
                try
                {
                    if (context.ResolveVariable(name, out DataValue resolved))
                        resolvedDescription = DescribeDataValue(resolved);
                }
                catch (Exception e)
                {
                    resolvedDescription = "<resolve failed: " + e.GetBaseException().Message + ">";
                }

                Log.LogInfo("[StoryAPI] Null assignment diagnostic for '" + name +
                    "': converted=" + DescribeDataValue(value) +
                    "; defaultDataValue=" + DescribeDataValue(default(DataValue)) +
                    "; assigned=" + assigned +
                    "; resolved=" + resolvedDescription);
            }

            return assigned;
        }

        private static string DescribeDataValue(DataValue value)
        {
            try
            {
                var fields = new List<string>();
                foreach (FieldInfo field in typeof(DataValue).GetFields(Instance))
                {
                    object fieldValue = field.GetValue(value);
                    string formatted = fieldValue == null
                        ? "<null>"
                        : fieldValue + " [" + fieldValue.GetType().FullName + "]";
                    fields.Add(field.Name + "=" + formatted);
                }

                return fields.Count == 0
                    ? value.ToString()
                    : string.Join(", ", fields);
            }
            catch (Exception e)
            {
                return "<DataValue diagnostic failed: " + e.GetBaseException().Message + ">";
            }
        }

        /// <summary>
        /// Log the actual game-build value wrapper surface when a mod declares a value type
        /// StoryAPI does not yet support. The data-value implementation lives in DDLC+'s
        /// game assemblies, so its collection constructors/conversions must be discovered
        /// at runtime instead of guessed from the parser.
        /// </summary>
        public static void ReportDataValueCapabilities(string sourcePath, int sourceLine, string expression)
        {
            if (s_dataValueCapabilitiesLogged) return;
            s_dataValueCapabilitiesLogged = true;

            Type type = typeof(DataValue);
            Log.LogInfo("[StoryAPI] Value capability probe requested by " + sourcePath + ":" +
                sourceLine + " for initializer: " + expression);
            Log.LogInfo("[StoryAPI] DataValue runtime type: " + type.AssemblyQualifiedName +
                "; IsValueType=" + type.IsValueType + "; BaseType=" +
                (type.BaseType == null ? "<none>" : type.BaseType.FullName));

            var conversions = type.GetMethods(Static)
                .Where(m => m.Name == "op_Implicit" || m.Name == "op_Explicit")
                .Select(m => FormatSignature(m))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            Log.LogInfo("[StoryAPI] DataValue conversions: " +
                (conversions.Length == 0 ? "<none>" : string.Join(" | ", conversions)));

            var constructors = type.GetConstructors(Instance)
                .Select(c => FormatSignature(c))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            Log.LogInfo("[StoryAPI] DataValue constructors: " +
                (constructors.Length == 0 ? "<none>" : string.Join(" | ", constructors)));

            var members = type.GetFields(Instance).Select(f =>
                    (f.IsPublic ? "public " : "nonpublic ") + "field " + f.FieldType.FullName + " " + f.Name)
                .Concat(type.GetProperties(Instance).Select(p =>
                    "property " + (p.PropertyType == null ? "<unknown>" : p.PropertyType.FullName) +
                    " " + p.Name))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            Log.LogInfo("[StoryAPI] DataValue fields/properties: " +
                (members.Length == 0 ? "<none>" : string.Join(" | ", members)));

            Type expressionType = typeof(CompiledExpression);
            var expressionBuilders = expressionType.GetMethods(Instance)
                .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object))
                .Select(m => FormatSignature(m))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            Log.LogInfo("[StoryAPI] CompiledExpression API: " +
                (expressionBuilders.Length == 0 ? "<none>" : string.Join(" | ", expressionBuilders)));
        }

        private static string FormatSignature(MethodBase method)
        {
            string parameters = string.Join(", ", method.GetParameters()
                .Select(p => p.ParameterType.FullName + " " + p.Name).ToArray());
            if (method is ConstructorInfo)
                return method.DeclaringType.FullName + "(" + parameters + ")";
            var info = method as MethodInfo;
            return (info == null ? string.Empty : info.ReturnType.FullName + " ") +
                method.DeclaringType.FullName + "." + method.Name + "(" + parameters + ")";
        }

        private static bool TryConvertDataValue(object source, out DataValue value)
        {
            value = default(DataValue);
            Type targetType = typeof(DataValue);

            // In SimpleExpressionEngine a script-level None/null value is
            // represented by the default DataValue (DataType.Unknown). Wrapping a
            // null reference with DataValue(object) instead produces ObjectRef(null),
            // which does not compare equal to a value initialized from the None literal.
            if (source == null)
            {
                value = default(DataValue);
                return true;
            }

            Type sourceType = source == null ? typeof(string) : source.GetType();

            // DDLC+'s DataValue(bool) constructor can normalize booleans to Float
            // (1/0) in some builds. That representation is indistinguishable from a
            // numeric literal and breaks strict equality with boolean list items.
            // Prefer DataValue(object) so the boxed System.Boolean stays in objectRef.
            // Verify the resulting wrapper rather than assuming the constructor preserves it.
            if (source is bool booleanSource)
            {
                ConstructorInfo objectConstructor = targetType.GetConstructor(
                    Instance, null, new[] { typeof(object) }, null);
                if (objectConstructor != null)
                {
                    try
                    {
                        DataValue objectBacked = (DataValue)objectConstructor.Invoke(
                            new object[] { (object)booleanSource });
                        FieldInfo objectReferenceField = targetType.GetField("objectRef", Instance);
                        object objectReference = objectReferenceField == null
                            ? null
                            : objectReferenceField.GetValue(objectBacked);
                        if (objectReference is bool && (bool)objectReference == booleanSource)
                        {
                            value = objectBacked;
                            return true;
                        }
                    }
                    catch
                    {
                        // Try the native Boolean representation below.
                    }
                }

                // Retain support for builds whose Boolean constructor has a real Boolean
                // discriminator/payload instead of coercing bool to a float.
                ConstructorInfo booleanConstructor = targetType.GetConstructor(
                    Instance, null, new[] { typeof(bool) }, null);
                if (booleanConstructor != null)
                {
                    try
                    {
                        DataValue booleanValue = (DataValue)booleanConstructor.Invoke(
                            new object[] { booleanSource });
                        FieldInfo typeField = targetType.GetField("type", Instance);
                        object typeValue = typeField == null ? null : typeField.GetValue(booleanValue);
                        string typeName = typeValue == null
                            ? string.Empty
                            : Convert.ToString(typeValue, System.Globalization.CultureInfo.InvariantCulture);
                        if (typeName.IndexOf("bool", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            value = booleanValue;
                            return true;
                        }
                    }
                    catch
                    {
                        // Report the unsupported representation below.
                    }
                }

                Log.LogWarning("[StoryAPI] This DDLC+ build cannot preserve a Boolean literal " +
                    "as a boolean DataValue; refusing to store it as numeric 1/0.");
                return false;
            }

            // Prefer an implicit conversion supplied by the game, then try a public
            // one-argument constructor for builds that expose it instead.
            foreach (Type declaringType in new[] { targetType, sourceType }.Distinct())
            {
                foreach (MethodInfo method in declaringType.GetMethods(Static))
                {
                    if (method.Name != "op_Implicit" || !targetType.IsAssignableFrom(method.ReturnType)) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length != 1) continue;
                    object argument;
                    if (!TryPrepareDataValueArgument(source, parameters[0].ParameterType, out argument)) continue;
                    try
                    {
                        value = (DataValue)method.Invoke(null, new[] { argument });
                        return true;
                    }
                    catch { /* Try another conversion. */ }
                }
            }

            foreach (ConstructorInfo constructor in targetType.GetConstructors(Instance))
            {
                var parameters = constructor.GetParameters();
                if (parameters.Length != 1) continue;
                object argument;
                if (!TryPrepareDataValueArgument(source, parameters[0].ParameterType, out argument)) continue;
                try
                {
                    value = (DataValue)constructor.Invoke(new[] { argument });
                    return true;
                }
                catch { /* This constructor is not compatible. */ }
            }

            Log.LogWarning("[StoryAPI] This DDLC+ build cannot convert a " + sourceType.Name +
                " literal to a Ren'Py variable value.");
            return false;
        }

        private static bool TryPrepareDataValueArgument(object source, Type parameterType, out object argument)
        {
            argument = null;
            Type nullableType = Nullable.GetUnderlyingType(parameterType);
            Type effectiveType = nullableType ?? parameterType;
            if (source == null)
                return !parameterType.IsValueType || nullableType != null;

            if (parameterType.IsInstanceOfType(source) || effectiveType.IsInstanceOfType(source))
            {
                argument = source;
                return true;
            }

            Type sourceType = source.GetType();
            if (!IsNumericLiteralType(sourceType) || !IsNumericLiteralType(effectiveType)) return false;
            bool sourceIsIntegral = sourceType != typeof(float) && sourceType != typeof(double) && sourceType != typeof(decimal);
            bool targetIsIntegral = effectiveType != typeof(float) && effectiveType != typeof(double) && effectiveType != typeof(decimal);
            if (!sourceIsIntegral && targetIsIntegral) return false;

            try
            {
                argument = Convert.ChangeType(source, effectiveType, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsNumericLiteralType(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return true;
                default:
                    return false;
            }
        }

    }
}
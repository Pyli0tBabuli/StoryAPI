using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RenpyParser;
using StoryAPI.Game;
using StoryAPI.Scripting;
using UnityEngine;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// One mod folder: its scripts, its art, and the label it contributes to the game.
    ///
    /// A mod is a directory of <c>.rpy</c> files plus whatever images and audio those
    /// scripts mention. Loading is a straight two-step pass: first every file is parsed
    /// and its declarations (characters, images, audio, overrides) are collected, then
    /// labels are compiled. That order matters, because a script referring to a
    /// mod-supplied image has to be able to tell that the image is not one of the
    /// base game's.
    /// </summary>
    public sealed class StoryMod
    {
        public string Name { get; }
        public string Directory { get; }
        public StoryAssets Assets { get; } = new StoryAssets();

        public List<string> Labels { get; } = new List<string>();
        public List<string> LoadErrors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();

        private readonly List<OverrideStatement> _overrides = new List<OverrideStatement>();
        private readonly List<CharacterStatement> _characters = new List<CharacterStatement>();
        private readonly List<VariableDeclarationStatement> _variables = new List<VariableDeclarationStatement>();
        private readonly Dictionary<VariableDeclarationStatement, VariableInitializationState> _variableInitializationStates =
            new Dictionary<VariableDeclarationStatement, VariableInitializationState>();
        private bool _variableInitLinesInjected;
        private readonly List<LabelStatement> _labelStatements = new List<LabelStatement>();
        private readonly List<StoryAPI.Scripting.HookStatement> _hookStatements = new List<StoryAPI.Scripting.HookStatement>();

        public StoryMod(string name, string directory)
        {
            Name = name;
            Directory = directory;
            Assets.ModName = name;
        }

        /// <summary>Read every script in the folder. Safe to call again on reload.</summary>
        public void Load()
        {
            Assets.Clear();
            Labels.Clear();
            LoadErrors.Clear();
            Warnings.Clear();
            _overrides.Clear();
            _characters.Clear();
            _variables.Clear();
            _variableInitializationStates.Clear();
            _variableInitLinesInjected = false;
            _images.Clear();
            _audio.Clear();
            _labelStatements.Clear();
            _hookStatements.Clear();
            _compiledLabels.Clear();

            string[] files;
            try
            {
                files = System.IO.Directory.GetFiles(Directory, "*.rpy", SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                LoadErrors.Add("Could not read mod folder: " + e.Message);
                return;
            }

            if (files.Length == 0)
            {
                Warnings.Add("No .rpy files found in '" + Directory + "'.");
                return;
            }

            // Pass one: parse everything and gather declarations.
            foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                _currentFile = file;

                string source;
                try
                {
                    source = File.ReadAllText(file);
                }
                catch (Exception e)
                {
                    LoadErrors.Add(Relative(file) + ": " + e.Message);
                    continue;
                }

                List<Statement> statements;
                try
                {
                    statements = new StoryAPI.Scripting.Parser(source, file).ParseFile();
                }
                catch (ScriptSyntaxException e)
                {
                    LoadErrors.Add(Relative(file) + ":" + e.Line + ": " + e.Message);
                    continue;
                }

                foreach (var statement in statements) Collect(statement);
            }

            // Images and audio are registered before labels so the compiler can tell
            // mod assets apart from the base game's.
            foreach (var image in _images) Assets.AddImage(image.Name, ResolveAsset(image.File, ImageExtensions));
            foreach (var audio in _audio) Assets.AddAudio(audio.Name, ResolveAsset(audio.File, AudioExtensions));

            // Pass two: compile labels.
            foreach (var label in _labelStatements)
            {
                var compiler = new Compiler(Assets);
                var result = compiler.Compile(label);

                foreach (string warning in result.Warnings) Warnings.Add(Relative(label.SourcePath) + ": " + warning);
                if (!result.Ok) continue;

                _compiledLabels[label.Name] = result;
                Labels.Add(label.Name);
            }

            // Hooks are compiled the same way, but they replace a block the game already
            // has, so they are kept under the name of the block they take over.
            foreach (var hook in _hookStatements)
            {
                var compiler = new Compiler(Assets);
                var result = compiler.CompileHook(hook);

                foreach (string warning in result.Warnings) Warnings.Add(Relative(hook.SourcePath) + ": " + warning);
                if (!result.Ok) continue;

                _compiledLabels[hook.Block] = result;
                Labels.Add(hook.Block);
            }
        }

        private readonly Dictionary<string, CompileResult> _compiledLabels =
            new Dictionary<string, CompileResult>(StringComparer.Ordinal);

        private readonly List<ImageStatement> _images = new List<ImageStatement>();
        private readonly List<AudioStatement> _audio = new List<AudioStatement>();

        /// <summary>Where each label's compiled instructions live, for tests and tooling.</summary>
        public IEnumerable<KeyValuePair<string, CompileResult>> CompiledLabels => _compiledLabels;

        private void Collect(Statement statement)
        {
            switch (statement)
            {
                case LabelStatement label:
                    label.SourcePath = _currentFile;
                    _labelStatements.Add(label);
                    break;
                case StoryAPI.Scripting.HookStatement hook:
                    hook.SourcePath = _currentFile;
                    _hookStatements.Add(hook);
                    break;
                case ImageStatement image: _images.Add(image); break;
                case AudioStatement audio: _audio.Add(audio); break;
                case CharacterStatement character: _characters.Add(character); break;
                case VariableDeclarationStatement variable:
                    variable.SourcePath = _currentFile;
                    _variables.Add(variable);
                    break;
                case OverrideStatement replace: _overrides.Add(replace); break;
                case GroupStatement group:
                    foreach (var inner in group.Body) Collect(inner);
                    break;
            }
        }

        private string _currentFile;

        /// <summary>
        /// Find an asset on disk. Declared paths are tried as written first, then
        /// against the conventional sub-folders, so <c>image room = room.png</c>
        /// works whether the author put the file at the top level or in images/.
        /// </summary>
        private string ResolveAsset(string file, string[] extensions)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;

            string direct = Path.Combine(Directory, file);
            if (File.Exists(direct)) return direct;

            // A declared path normally carries its own extension - "room.png" - but the
            // folder search below appends one. Without stripping it the candidate comes
            // out as "room.png.png" and the documented images/ and audio/ layout never
            // matches. An unrecognised extension is left alone, so a name that merely
            // contains a dot still resolves.
            string stem = file;
            string declared = Path.GetExtension(file);
            if (!string.IsNullOrEmpty(declared) && Array.IndexOf(extensions, declared.ToLowerInvariant()) >= 0)
            {
                string folderPart = Path.GetDirectoryName(file);
                stem = Path.Combine(folderPart ?? string.Empty, Path.GetFileNameWithoutExtension(file));
            }

            foreach (string extension in extensions)
            {
                foreach (string folder in new[] { "images", "image", "audio", "" })
                {
                    string candidate = Path.Combine(Directory, folder, stem + extension);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            LoadErrors.Add("Asset not found: " + file);
            return null;
        }

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };
        private static readonly string[] AudioExtensions = { ".ogg", ".wav", ".mp3" };

        // ------------------------------------------------------------- registering

        /// <summary>Publish this mod's labels, speakers and text into the running game.</summary>
        public bool Register()
        {
            if (!GameBridge.Ready)
            {
                Warnings.Add("The game script is not ready yet; registration deferred.");
                return false;
            }

            RegisterVariables();
            RegisterCharacters();

            // The engine's execution context does not exist while mods are being
            // registered. Put scalar declarations at the start of labels and initialize
            // them only when that instruction is reached during live script execution.
            // Shared state ensures define values are not reset on every label transition.
            if (!_variableInitLinesInjected) PrepareVariableInitializers();

            int registered = 0;
            int writtenText = 0;
            foreach (var pair in _compiledLabels)
            {
                List<Line> lines = pair.Value.Lines;

                if (pair.Value.HookAfterTextId != 0)
                {
                    // The scene up to the chosen line is kept exactly as the game has it,
                    // and the mod's own statements follow on from there.
                    List<Line> original;
                    if (GameBridge.TryTakeBlockSteps(
                            pair.Key, pair.Value.HookAfterTextId, out original))
                    {
                        var combined = new List<Line>(original);
                        combined.AddRange(pair.Value.Lines);

                        // Branches jump to a step number, and those numbers were worked out
                        // without the original scene in front of them. The shift is what
                        // keeps an `if` or a `menu` inside a hook pointing at the mod's own
                        // steps instead of into the middle of the shipped scene.
                        GameBridge.ShiftLineJumps(pair.Value.Lines, original.Count);

                        lines = combined;
                        Log.Info("hook '" + pair.Key + "': kept " + original.Count +
                                 " original step(s) up to line " +
                                 pair.Value.HookAfterTextId + ", then " +
                                 pair.Value.Lines.Count + " step(s) from the mod.");
                    }
                    else
                    {
                        Warnings.Add("hook '" + pair.Key + "' after " +
                                     pair.Value.HookAfterTextId +
                                     ": the game has no block or line by that id. " +
                                     "Check the script reference for the right id.");
                        continue;
                    }
                }

                var block = new RenpyBlock(pair.Key, true, RenpyBlockAttributes.None, lines);
                block.Label = pair.Key;

                // The game names its backgrounds in two words and its plain colours in
                // one, and only the game's own block table can say which is which.
                GameBridge.ResolveSceneNames(lines);

                // Dialogue is stored per (label, id) rather than inline, so the strings
                // have to go into the game's tables before the label can run.
                foreach (var text in pair.Value.Text)
                {
                    if (GameBridge.RegisterText(pair.Key, text.Key, text.Value)) writtenText++;
                }

                if (GameBridge.RegisterBlock(pair.Key, block)) registered++;
            }

            Assets.BeginAudioLoads();

            ApplyOverrides();

            Log.Info("Mod '" + Name + "' registered " + registered + " label(s), " +
                     writtenText + " line(s) of text, " +
                     Assets.ImageCount + " image(s), " + Assets.AudioCount + " track(s).");
            return registered > 0;
        }

        /// <summary>
        /// Point base-game tracks at the keys the game actually stores them under.
        ///
        /// A script names a track the way a person would ("bgm/9.ogg"), but the game
        /// keeps its audio scope keyed by hash, so the name has to be translated. This
        /// is deliberately not done at compile time: mods are parsed long before the
        /// game has an execution context, and that context only exists once a script is
        /// actually running. The compiled instructions are shared with the registered
        /// block, so rewriting the name here is enough.
        /// </summary>
        public void ResolveBaseAudio()
        {
            foreach (var compiled in _compiledLabels.Values)
            {
                foreach (var line in compiled.Lines)
                {
                    if (!(line is RenpyPlay play) || play.play == null) continue;

                    string key = GameBridge.ResolveBaseAudio(play.play.Asset);
                    if (key != null) play.play.Asset = key;
                }
            }
        }

        /// <summary>Remove this mod's labels again, so the folder can be reloaded.</summary>
        public void Unregister()
        {
            if (!GameBridge.Ready) return;
            try
            {
                foreach (string label in Labels) GameBridge.UnregisterBlock(label);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StoryAPI] '" + Name + "' could not be fully unloaded: " + e.Message);
            }
        }

        private string ExpressionGateName(VariableDeclarationStatement variable)
        {
            // A stable private Ren'Py variable avoids piling up a new gate on every F10 reload.
            // The mod/path/variable/line tuple is hashed to a valid, compact identifier.
            string identity = Name + "|" + variable.SourcePath + "|" + variable.Name + "|" + variable.Line;
            ulong hash = 14695981039346656037UL;
            unchecked
            {
                foreach (char c in identity)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }
            }

            return "_storyapi_expression_gate_" + hash.ToString("x16");
        }

        private void RegisterVariables()
        {
            foreach (var variable in _variables)
            {
                if (!variable.HasLiteral && !variable.HasRuntimeExpression)
                {
                    Warnings.Add(Relative(variable.SourcePath) + ":" + variable.Line +
                        ": " + (variable.IsDefault ? "default" : "define") + " '" + variable.Name +
                        "' uses an initializer not supported by StoryAPI: " + variable.Expression +
                        ". Use a scalar literal or a scalar arithmetic/logical expression. " +
                        "Lists, dictionaries, indexing, function calls, and arbitrary Python are not supported.");
                }
            }
        }

        private void PrepareVariableInitializers()
        {
            if (_variables.Count == 0)
            {
                _variableInitLinesInjected = true;
                return;
            }

            foreach (var pair in _compiledLabels)
            {
                var initLines = new List<Line>();
                foreach (var variable in _variables)
                {
                    if (!variable.HasLiteral && !variable.HasRuntimeExpression) continue;

                    if (!_variableInitializationStates.TryGetValue(variable, out var state))
                    {
                        state = new VariableInitializationState();
                        _variableInitializationStates[variable] = state;
                    }

                    if (variable.HasLiteral)
                    {
                        initLines.Add(new VariableInitializationLine(
                            variable.Name,
                            variable.LiteralValue,
                            variable.IsDefault,
                            variable.SourcePath,
                            variable.Line,
                            state));
                    }
                    else
                    {
                        string gateName = ExpressionGateName(variable);
                        // Four instructions: choose whether to run, skip when false, assign,
                        // and mark initialized. The jump target is an absolute index because
                        // this initializer block is inserted at the beginning of the label.
                        int skipTarget = initLines.Count + 4;

                        initLines.Add(new VariableInitializationLine(
                            variable.Name,
                            variable.IsDefault,
                            gateName,
                            variable.SourcePath,
                            variable.Line,
                            state));
                        initLines.Add(new RenpyGoToLineUnless(gateName, skipTarget));
                        initLines.Add(new RenpyOneLinePython("$ " + variable.Name + " = " + variable.Expression));
                        initLines.Add(new VariableExpressionCompleteLine(
                            variable.Name,
                            variable.IsDefault,
                            variable.SourcePath,
                            variable.Line,
                            state));
                    }
                }

                if (initLines.Count == 0) continue;

                // These initializers are prepended, so compiled branch/menu targets need
                // to move down by the same number of instructions.
                GameBridge.ShiftLineJumps(pair.Value.Lines, initLines.Count);
                pair.Value.Lines.InsertRange(0, initLines);
            }

            _variableInitLinesInjected = true;
        }

        private void RegisterCharacters()
        {
            var script = GameBridge.Script;
            if (script?.Characters == null) return;

            try
            {
                var table = script.Characters.GetDictionary();

                foreach (var character in _characters)
                {
                    // The game's name box drops the first letter of a speaker whose
                    // `image` is set when the name is not in its translation table:
                    // it splits the letter off to capitalise it and only puts it back
                    // if the lookup succeeds. Mod names are never in that table, so a
                    // mod sprite must not be passed as the image. A base-game sprite
                    // is fine and still gets the game's focus behaviour.
                    string image = character.Image;
                    if (!string.IsNullOrEmpty(image) && Assets.IsCustomImage(image))
                    {
                        image = string.Empty;
                    }

                    table[character.Tag] = new CharacterData(character.DisplayName, image: image);
                }

// Any speaker a script used but never declared gets registered under its
                // own name, so the name box shows something sensible instead of a gap.
                // No image: an unknown speaker is by definition not one of the game's
                // own sprites, and setting one would trigger the missing letter above.
                foreach (var pair in _compiledLabels)
                {
                    foreach (string speaker in pair.Value.Speakers)
                    {
                        if (!table.ContainsKey(speaker))
                        {
                            table[speaker] = new CharacterData(speaker);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[StoryAPI] '" + Name + "' could not register characters: " + e.Message);
            }
        }

        /// <summary>
        /// Rewrite lines of the base game. This needs no patching at all: dialogue is
        /// already stored in a plain table keyed by label and line id, so replacing a
        /// line means writing a different string at the same key.
        /// </summary>
        private void ApplyOverrides()
        {
            foreach (var replace in _overrides)
            {
                if (!GameBridge.RegisterText(replace.Label, replace.TextId, replace.Text))
                {
                    Warnings.Add("Could not override " + replace.Label + " #" + replace.TextId + ".");
                }
            }
        }

        private string Relative(string path)
        {
            if (string.IsNullOrEmpty(path)) return Name;

            // netstandard2.0 has no Path.GetRelativePath, and the goal here is only a
            // short readable name for a diagnostic.
            string root = Directory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(root.Length)
                : path;
        }
    }
}
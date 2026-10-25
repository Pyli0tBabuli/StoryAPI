using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using RenpyParser;
using SimpleExpressionEngine;
using StoryAPI.Game;
using StoryAPI.Runtime;

namespace StoryAPI.Scripting
{
    /// <summary>Outcome of compiling one label.</summary>
    public sealed class CompileResult
    {
        public List<Line> Lines { get; } = new List<Line>();
        public List<string> Warnings { get; } = new List<string>();
        public bool Ok { get; set; } = true;

        /// <summary>Speaker tags this label used, so they can be registered once.</summary>
        public List<string> Speakers { get; } = new List<string>();

        /// <summary>
        /// Dialogue text this label needs, as (line id, text).
        ///
        /// The compiler never touches the running game: mods are parsed long before
        /// the story has started, and the game only has somewhere to put text once it
        /// has built its script. Collecting the strings here keeps compilation pure
        /// and lets registration happen at the right moment.
        /// </summary>
        public List<KeyValuePair<int, string>> Text { get; } = new List<KeyValuePair<int, string>>();

        /// <summary>
        /// Set when this entry hooks a block the game already has, rather than being a
        /// label of the mod's own. The value is the dialogue id to stop after; zero means
        /// this is an ordinary label.
        /// </summary>
        public long HookAfterTextId { get; set; } = 0;
    }

    /// <summary>
    /// Turns a parsed label into the flat instruction list the game's script runner
    /// actually executes.
    ///
    /// The runner has no structured control flow: branching is expressed by jumping to
    /// a raw line index, and those indices are only known once the branch has been
    /// emitted. So the compiler writes placeholder jumps while going forward and
    /// patches them at the end. This mirrors how the base game's own compiler
    /// produces its scripts, which is what keeps generated labels interchangeable with
    /// native ones.
    /// </summary>
    public sealed class Compiler
    {
        private static readonly Regex InterpolationPattern = new Regex(@"\[(\w+)\]", RegexOptions.Compiled);

        private readonly StoryAssets _assets;
        private readonly CompileResult _result = new CompileResult();
        private readonly List<Line> _out = new List<Line>();
        private readonly HashSet<string> _speakers = new HashSet<string>(StringComparer.Ordinal);

        private sealed class LoopContext
        {
            public int Start;
            public readonly List<RenpyGoToLine> Breaks = new List<RenpyGoToLine>();
        }
        private readonly Stack<LoopContext> _loops = new Stack<LoopContext>();

        private string _textLabel;
        private int _nextTextId = 1;

        public Compiler(StoryAssets assets)
        {
            _assets = assets ?? new StoryAssets();
        }

        /// <summary>Speaker tags seen while compiling, so they can be registered once.</summary>
        public IEnumerable<string> Speakers => _speakers;

        public CompileResult Compile(LabelStatement label)
        {
            _textLabel = label.Name;
            _out.Clear();
            _loops.Clear();

            try
            {
                EmitBody(label.Body);
                _out.Add(new RenpyReturn());
            }
            catch (Exception e)
            {
                _result.Ok = false;
                _result.Warnings.Add("label '" + label.Name + "' failed to compile: " + e.Message);

                // The message alone hides which of the many game types was null, and
                // that is the whole diagnosis when a node cannot be built.
                Log.Error("Compiling label '" + label.Name + "' threw:", e);
            }

            _result.Lines.AddRange(_out);
            _result.Speakers.AddRange(_speakers);
            return _result;
        }

        /// <summary>
        /// Compiles the mod's part of a hook.
        ///
        /// Only the mod's own statements are produced here. The game's lines cannot be
        /// compiled offline because they do not exist until the story is running, so the
        /// result is marked with the id to stop after and stitched onto the original
        /// block when the mod is registered.
        /// </summary>
        public CompileResult CompileHook(HookStatement hook)
        {
            _textLabel = hook.Block;
            _out.Clear();
            _loops.Clear();

            try
            {
                EmitBody(hook.Body);
                _out.Add(new RenpyReturn());
            }
            catch (Exception e)
            {
                _result.Ok = false;
                _result.Warnings.Add("hook '" + hook.Block + "' failed to compile: " + e.Message);
                Log.Error("Compiling hook '" + hook.Block + "' threw:", e);
            }

            _result.Lines.AddRange(_out);
            _result.Speakers.AddRange(_speakers);
            _result.HookAfterTextId = hook.AfterTextId;
            return _result;
        }

        // ------------------------------------------------------------- emission

        private void EmitBody(List<Statement> body)
        {
            foreach (var statement in body) Emit(statement);
        }

        private void Emit(Statement statement)
        {
            switch (statement)
            {
                case SayStatement say: EmitSay(say); break;
                case SceneStatement scene: EmitScene(scene); break;
                case ShowStatement show: EmitShow(show); break;
                case HideStatement hide: EmitHide(hide); break;
                case WithStatement with: EmitWith(with); break;
                case PlayStatement play: EmitPlay(play); break;
                case StopStatement stop: EmitStop(stop); break;
                case JumpStatement jump: _out.Add(new RenpyGoTo(jump.Target, false)); break;
                case CallStatement call: EmitCall(call); break;
                case ReturnStatement _: _out.Add(new RenpyReturn()); break;
                case PauseStatement pause: EmitPause(pause); break;
                case MoveStatement move: EmitMove(move); break;
                case ZoomStatement zoom: EmitZoom(zoom); break;
                case FadeStatement fade: EmitFade(fade); break;
                case TurnStatement turn: EmitTurn(turn); break;
                case PoseStatement pose: EmitPose(pose); break;
                case LayerStatement layer: EmitLayer(layer); break;
                case PythonStatement python: _out.Add(new RenpyOneLinePython("$ " + python.Code)); break;
                case IfStatement branch: EmitIf(branch); break;
                case WhileStatement loop: EmitWhile(loop); break;
                case LoopControlStatement control: EmitLoopControl(control); break;
                case PassStatement _: break;
                case MenuStatement menu: EmitMenu(menu); break;
                case GroupStatement group: EmitBody(group.Body); break;

                // Declarations are handled by the mod loader, not at runtime.
                case CharacterStatement _:
                case ImageStatement _:
                case AudioStatement _:
                case OverrideStatement _:
                    break;

                default:
                    Warn("Unsupported statement '" + statement.GetType().Name + "' on line " + statement.Line + ".");
                    break;
            }
        }

        // ---------------------------------------------------------- dialogue

        private void EmitSay(SayStatement say)
        {
            if (!string.IsNullOrEmpty(say.Speaker))
            {
                _speakers.Add(say.Speaker);
            }

            // A pose given inline ("m happy ...") means "redraw the speaker", not
            // "say with a variant". Doing it as its own instruction keeps the
            // dialogue line itself free of sprite-swapping the runner may not be able
            // to resolve.
            if (!string.IsNullOrEmpty(say.Variant))
            {
                EmitPose(say.Speaker, say.Variant);
            }

            int id = AllocateTextId(say.Text);
            bool interpolate = InterpolationPattern.IsMatch(say.Text);

            var line = GameBridge.CreateDialogueLine(
                _textLabel,
                id,
                say.Speaker ?? string.Empty,
                null,
                interpolate,
                false,
                0,
                0,
                0);

            if (line == null)
            {
                Warn("Could not build a dialogue line on line " + say.Line + "; skipping.");
                return;
            }

            _out.Add(line);
        }

        private void EmitPose(string speaker, string variant)
        {
            if (string.IsNullOrEmpty(speaker)) return;
            if (!_assets.IsCustomImage(speaker + " " + variant)) return;

            var show = new ShowStatement
            {
                Image = speaker,
                Variant = variant,
                Line = 0,
            };
            EmitShow(show);
        }

        /// <summary>
        /// Reserve an id for a line of text and remember the text itself. Writing it
        /// into the game is the mod's job at registration time, not the compiler's.
        /// </summary>
        private int AllocateTextId(string text)
        {
            int id = _nextTextId++;
            _result.Text.Add(new KeyValuePair<int, string>(id, text));
            return id;
        }

        // --------------------------------------------------------- presentation

        private void EmitScene(SceneStatement statement)
        {
            if (_assets.IsCustomImage(statement.Image))
            {
                _out.Add(new CustomBackgroundLine(_assets, statement.Image));
            }
            else
            {
                // The name is written out as it stands. The game names its backgrounds
                // in two words ("bg club_day") and its plain colours in one ("black"),
                // and which of the two a name is only known from the game's own block
                // table, which does not exist yet at compile time. The layer is added
                // when the mod is registered.
                _out.Add(new RenpyScene("scene " + statement.Image));
            }

            if (!string.IsNullOrEmpty(statement.Transition))
            {
                EmitWith(new WithStatement
                {
                    Transition = statement.Transition,
                    Line = statement.Line,
                });
            }
        }

        /// <summary>
        /// Words that mean a place on screen rather than the name of a transform.
        ///
        /// The two are written the same way after "at", so they have to be told apart. A
        /// position is a simple place; anything else is the name of a transform block.
        /// </summary>
        private static bool IsPosition(string at)
        {
            return at == "left" || at == "center" || at == "right" || at == "truecenter" ||
                   at == "true_left" || at == "true_right" || at == "offscreenleft" ||
                   at == "offscreenright";
        }


        private void EmitShow(ShowStatement statement)
        {
            string image = statement.Image;
            string variant = statement.Variant;

            if (_assets.IsCustomImage(variant == null ? image : image + " " + variant))
            {
                _out.Add(new CustomSpriteLine(_assets, image, variant, statement.Alias,
                                             statement.At, statement.ZOrder ?? 1));
                return;
            }

            // A named transform on art the game owns is read and applied by the mod,
            // because the game's own animation runner does not run in a mod label. Plain
            // position words are left to the game as before.
            if (!string.IsNullOrEmpty(statement.At) && !IsPosition(statement.At))
            {
                _out.Add(new NativeSpriteLine(image, statement.Alias ?? image,
                                              statement.At, statement.ZOrder ?? 1));
                return;
            }

            // The game takes the whole command as text, and that text is the only place
            // the slot name can appear: the properties that would hold it are read-only.
            // So the command is written out in full, the way the game's own compiler
            // writes it, rather than assembled from fields.
            var showText = "show " + image +
                           (variant == null ? "" : " " + variant) +
                           (string.IsNullOrEmpty(statement.Alias) ? "" : " as " + statement.Alias);

            var show = new Show(showText);
            show.ImageName = image;
            show.Variant = variant ?? string.Empty;
            show.Layer = "master";
            show.AllowAutoFocus = true;

            if (statement.ZOrder.HasValue)
            {
                show.ZOrder = statement.ZOrder.Value;
                show.HasZOrder = true;
            }

            var line = new RenpyShow(string.Empty);
            line.show = show;
            line.ShowData = showText;
            _out.Add(line);
        }

        private void EmitHide(HideStatement statement)
        {
            var hide = new Hide(string.Empty, false, false);
            hide.Name = statement.Image;

            var line = new RenpyHide();
            line.hide = hide;
            line.HideData = "hide " + statement.Image;
            _out.Add(line);
        }

        private void EmitWith(WithStatement statement)
        {
            string transition = Transitions.Expand(statement.Transition);
            if (transition == null) return;

            // "None" is the game's own way of asking for no transition. The engine
            // compares the command with that word, so it is written on its own
            // rather than as "with None".
            if (Transitions.IsNone(transition))
            {
                _out.Add(new RenpyWith(Transitions.None, null));
                return;
            }

            var expression = CompileExpression(transition);
            if (expression == null)
            {
                Warn("Could not read the transition '" + statement.Transition +
                     "'; the line was left out.");
                return;
            }

            _out.Add(new RenpyWith("with " + transition, expression));
        }

        private void EmitPlay(PlayStatement statement)
        {
            if (_assets.IsCustomAudio(statement.Name))
            {
                _out.Add(new CustomAudioLine(_assets, statement.Channel, statement.Name, statement.Looping));
                return;
            }

            var play = new Play
            {
                Channel = statement.Channel == "music" ? Channel.Music
                         : statement.Channel == "music_poem" ? Channel.MusicPoem
                         : Channel.Sound,
                // The game's own scope is keyed by hash, so a readable name such as
                // "bgm/9.ogg" or "monikapound" is translated to the key it actually
                // lives under. A name that is already a key is left alone.
                Asset = GameBridge.ResolveBaseAudio(statement.Name) ?? statement.Name,
                fadein = statement.FadeIn,
                fadeout = statement.FadeOut,
            };

            var line = new RenpyPlay();
            SetBackingField(line, "PlayData", statement.Channel + " " + statement.Name);
            line.play = play;
            _out.Add(line);
        }

        private void EmitStop(StopStatement statement)
        {
            _out.Add(new CustomAudioStopLine(statement.Channel));
        }

        private void EmitPause(PauseStatement statement)
        {
            string duration = NumberText(statement.Seconds);
            _out.Add(new RenpyPause(duration, CompileExpression(duration)));
        }

        /// <summary>
        /// Compile a fragment of the game's expression language.
        ///
        /// Some instructions carry a raw string for display but a compiled expression
        /// for behaviour: a transition is written as <c>with fade</c> yet is evaluated
        /// as the bare name <c>fade</c>, and a pause duration likewise. Handing those a
        /// null expression makes the runner dereference nothing and the whole script
        /// coroutine dies, so every such instruction has to be given a real one.
        /// </summary>
        private CompiledExpression CompileExpression(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            try
            {
                return SimpleExpressionEngine.Parser.Compile(source);
            }
            catch (Exception e)
            {
                Warn("Could not compile expression '" + source + "': " + e.Message);
                return null;
            }
        }

        private void EmitCall(CallStatement statement)
        {
            _out.Add(new RenpyGoTo(statement.Target, true, null));
        }

        // -------------------------------------------------------- control flow

        /// <summary>
        /// Emit a conditional.
        ///
        /// The runner has two kinds of jump: an unconditional one, and a guarded one
        /// that jumps when its condition is false. The base game's own scripts are
        /// written that way as well - a guard, the body, then a jump past the rest
        /// of the chain - so a branch in a mod behaves exactly as it does in the
        /// shipped game.
        /// </summary>
        private void EmitIf(IfStatement statement)
        {
            var branches = statement.Branches;
            if (branches.Count == 0) return;

            // A branch with no condition at all is just a body.
            if (branches.Count == 1 && branches[0].Condition == null)
            {
                EmitBody(branches[0].Body);
                return;
            }

            // A lone conditional branch: jump over it when the condition is false.
            if (branches.Count == 1)
            {
                int guard = EmitUnless(branches[0].Condition);
                EmitBody(branches[0].Body);
                PatchUnless(branches[0].Condition, guard, _out.Count);
                _out.Add(new RenpyNOP());
                return;
            }

            var jumps = new List<RenpyGoToLine>();
            int pendingGuard = -1;
            string pendingCondition = null;

            for (int i = 0; i < branches.Count; i++)
            {
                var branch = branches[i];

                if (branch.Condition == null)
                {
                    // The fallback: everything below is inside it, and the guard in
                    // front of it lands here.
                    if (pendingGuard >= 0) PatchUnless(pendingCondition, pendingGuard, _out.Count);
                    pendingGuard = -1;

                    EmitBody(branch.Body);
                    break;
                }

                // The guard in front of this branch is where the previous one lands
                // when its condition was false.
                if (pendingGuard >= 0) PatchUnless(pendingCondition, pendingGuard, _out.Count);

                pendingCondition = branch.Condition;
                pendingGuard = EmitUnless(branch.Condition);

                EmitBody(branch.Body);

                // The body is done, so the chain is left behind.
                var jump = new RenpyGoToLine(-1);
                _out.Add(jump);
                jumps.Add(jump);
            }

            // The end of the chain: a branch that was taken jumps here, and so does
            // a guard that was never satisfied.
            int end = _out.Count;
            if (pendingGuard >= 0) PatchUnless(pendingCondition, pendingGuard, end);
            foreach (var jump in jumps) jump.targetLine = end;
            _out.Add(new RenpyNOP());
        }

        private int EmitUnless(string condition)
        {
            _out.Add(new RenpyGoToLineUnless(condition, -1));
            return _out.Count - 1;
        }

        private void EmitWhile(WhileStatement statement)
        {
            int start = _out.Count;
            int guard = EmitUnless(statement.Condition);
            var loop = new LoopContext { Start = start };
            _loops.Push(loop);
            try
            {
                EmitBody(statement.Body);
            }
            finally
            {
                _loops.Pop();
            }

            _out.Add(new RenpyGoToLine(start));
            int end = _out.Count;
            PatchUnless(statement.Condition, guard, end);
            foreach (var jump in loop.Breaks) jump.targetLine = end;
            _out.Add(new RenpyNOP());
        }

        private void EmitLoopControl(LoopControlStatement statement)
        {
            if (_loops.Count == 0)
                throw new InvalidOperationException("'" + (statement.IsContinue ? "continue" : "break") +
                    "' is only valid inside a while loop (line " + statement.Line + ").");

            var loop = _loops.Peek();
            if (statement.IsContinue)
            {
                _out.Add(new RenpyGoToLine(loop.Start));
            }
            else
            {
                var jump = new RenpyGoToLine(-1);
                _out.Add(jump);
                loop.Breaks.Add(jump);
            }
        }

        private void PatchUnless(string condition, int index, int target)
        {
            if (_out[index] is RenpyGoToLineUnless guard) guard.targetLine = target;
        }

        /// <summary>
        /// Emit a choice. Every option is laid out inline behind a jump that skips the
        /// whole block, then a menu instruction hands control to the player and the
        /// selected option's index is used to continue.
        /// </summary>
        private void EmitMenu(MenuStatement statement)
        {
            var entries = new List<RenpyMenuInputEntry>();
            var skip = new RenpyGoToLine(-1);
            _out.Add(skip);

            foreach (var choice in statement.Choices)
            {
                int target = _out.Count;
                EmitBody(choice.Body);
                var jump = new RenpyGoToLine(-1);
                _out.Add(jump);
                jump.targetLine = -2;   // patched below, once the end is known
                entries.Add(new RenpyMenuInputEntry(
                    AllocateMenuTextId(choice.Caption),
                    InterpolationPattern.IsMatch(choice.Caption),
                    null,
                    target));
            }

            int menuIndex = _out.Count;
            _out.Add(new RenpyMenuInput(_textLabel, entries, statement.Caption != null));
            _out.Add(new RenpyNOP());

            skip.targetLine = menuIndex;

            // Every branch has to land after the menu instruction, not on it: landing
            // on it would ask the player to choose again.
            int afterMenu = menuIndex + 1;
            foreach (var line in _out)
            {
                if (line is RenpyGoToLine jump && jump.targetLine == -2) jump.targetLine = afterMenu;
            }
        }

        private int AllocateMenuTextId(string caption)
        {
            return AllocateTextId(caption);
        }

        // ------------------------------------------------------------- animation
        //
        // Animation is not a game instruction. The plugin runs the tween itself and hands
        // the result to the sprite, followed by a pause of the same length so the script
        // does not run ahead. Doing it this way leaves the existing execution model
        // untouched and lets a script keep talking while a sprite moves.

        private void EmitPose(PoseStatement statement)
        {
            _out.Add(new PoseLine(statement.Name, statement.At, 0));
        }

        private void EmitLayer(LayerStatement statement)
        {
            _out.Add(new LayerLine(statement.Pose, statement.Layer, statement.Asset));
        }


        private void EmitMove(MoveStatement statement)
        {
            _out.Add(new SpriteTweenLine(statement.Name, TweenTarget.Position,
                statement.XPos, statement.HasY ? statement.YPos : float.NaN, statement.Seconds));
            _out.Add(WaitFor(statement.Seconds));
        }

        private void EmitZoom(ZoomStatement statement)
        {
            _out.Add(new SpriteTweenLine(statement.Name, TweenTarget.Scale,
                statement.Scale, 0f, statement.Seconds));
            _out.Add(WaitFor(statement.Seconds));
        }

        private void EmitFade(FadeStatement statement)
        {
            _out.Add(new SpriteTweenLine(statement.Name, TweenTarget.Alpha,
                statement.Alpha, 0f, statement.Seconds));
            _out.Add(WaitFor(statement.Seconds));
        }

        private void EmitTurn(TurnStatement statement)
        {
            _out.Add(new SpriteTweenLine(statement.Name, TweenTarget.Rotation,
                statement.Degrees, 0f, statement.Seconds));
            _out.Add(WaitFor(statement.Seconds));
        }

        /// <summary>
        /// The game reads a pause by evaluating an expression and taking a float from it.
        /// The expression is required: passed as null it yields no duration at all, the
        /// pause returns instantly and whatever it was waiting for never gets to run.
        /// </summary>
        /// <summary>
        /// Wait for a number of seconds.
        ///
        /// The pause is handed to the game as text that its own expression engine has to
        /// parse. A C# style literal such as "1f" is not valid there, so only the plain
        /// number is written; with the suffix the expression silently fails to compile and
        /// the wait lasts no time at all, which would make every animation a jump.
        /// </summary>
        private RenpyPause WaitFor(float seconds)
        {
            string text = NumberText(seconds);
            return new RenpyPause(text, CompileExpression(text));
        }

        /// <summary>A number in the form the game's expression engine accepts.</summary>
        private static string NumberText(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return "0";
            return seconds.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private string PauseText(float seconds)
        {
            return NumberText(seconds);
        }

        // -------------------------------------------------------------- helpers

        /// <summary>
        /// <c>PlayData</c> and friends are get-only properties kept for validation only.
        /// Nothing on the execution path reads them, but filling them in keeps the
        /// instruction self-consistent in case the game ever validates a block.
        /// </summary>
        private static void SetBackingField(object target, string property, object value)
        {
            try
            {
                var field = target.GetType().GetField(
                    "<" + property + ">k__BackingField",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                field?.SetValue(target, value);
            }
            catch
            {
                // Cosmetic only.
            }
        }

        private void Warn(string message)
        {
            _result.Warnings.Add(message);
            Log.Warn("[StoryAPI] " + message);
        }
    }
}
using System.Collections.Generic;

namespace StoryAPI.Scripting
{
    /// <summary>
    /// The story author's syntax tree.
    ///
    /// This is deliberately independent of the game's own node classes: a mod file is
    /// parsed here first, reported on with real line numbers if it is broken, and only
    /// turned into game instructions by <see cref="Compiler"/> once it is known-good.
    /// </summary>
    public abstract class Statement
    {
        /// <summary>1-based line in the source file, used for error messages.</summary>
        public int Line { get; set; }
    }

    /// <summary>A named block of script. The unit the player jumps to.</summary>
    public sealed class LabelStatement : Statement
    {
        public string Name { get; set; }

        /// <summary>File this label was read from, used in diagnostics.</summary>
        public string SourcePath { get; set; }

        public List<Statement> Body { get; } = new List<Statement>();
    }

    /// <summary>A character speaking. Tag empty means narration.</summary>
    public sealed class SayStatement : Statement
    {
        public string Speaker { get; set; }
        public string Text { get; set; }
        public string Variant { get; set; }
    }

    /// <summary>Replaces the whole background.</summary>
    public sealed class SceneStatement : Statement
    {
        public string Image { get; set; }
        public string Transition { get; set; }
    }

    /// <summary>Puts a sprite on screen.</summary>
    public sealed class ShowStatement : Statement
    {
        public string Image { get; set; }

        /// <summary>Optional pose, written as <c>show monika 1</c>.</summary>
        public string Variant { get; set; }

        /// <summary>
        /// Slot to file the sprite under, written as <c>show x as y</c>.
        ///
        /// This is how a script shows the same art twice and then moves or hides each
        /// copy on its own, so the name has to be kept rather than treated as a pose.
        /// </summary>
        public string Alias { get; set; }

        public string Transform { get; set; }
        public string At { get; set; }
        public int? ZOrder { get; set; }
    }

    /// <summary>Takes a sprite off screen.</summary>
    public sealed class HideStatement : Statement
    {
        public string Image { get; set; }
    }

    /// <summary>Screen transition applied to everything queued so far.</summary>
    public sealed class WithStatement : Statement
    {
        public string Transition { get; set; }
    }

    /// <summary>Starts music or a sound effect.</summary>
    public sealed class PlayStatement : Statement
    {
        public string Channel { get; set; }   // "music" or "sound"
        public string Name { get; set; }
        public float FadeIn { get; set; }
        public float FadeOut { get; set; }
        public bool Looping { get; set; } = true;
    }

    /// <summary>Stops a channel.</summary>
    public sealed class StopStatement : Statement
    {
        public string Channel { get; set; }
        public float FadeOut { get; set; }
    }

    public sealed class JumpStatement : Statement
    {
        public string Target { get; set; }
    }

    public sealed class CallStatement : Statement
    {
        public string Target { get; set; }
        public List<string> Arguments { get; } = new List<string>();
    }

    public sealed class ReturnStatement : Statement
    {
    }

    /// <summary>A pause before the next line.</summary>
    public sealed class PauseStatement : Statement
    {
        public float Seconds { get; set; }
    }

    /// <summary>
    /// A single expression statement, written as <c>$ points = 10</c>.
    /// The text is handed to the game's own expression engine.
    /// </summary>
    public sealed class PythonStatement : Statement
    {
        public string Code { get; set; }
    }

    /// <summary>
    /// A top-level define/default declaration. Scalar and supported container literals are
    /// initialized directly; conservative arithmetic/logical scalar expressions can be
    /// evaluated by the live game's own expression runner.
    /// </summary>
    public sealed class VariableDeclarationStatement : Statement
    {
        public string Name { get; set; }
        public string Expression { get; set; }
        public bool IsDefault { get; set; }
        public bool HasLiteral { get; set; }
        public object LiteralValue { get; set; }

        /// <summary>True when Expression is a scalar expression safe to delegate to the game.</summary>
        public bool HasRuntimeExpression { get; set; }

        public string SourcePath { get; set; }
    }

    /// <summary>Registers a new speaker so it can be used as a dialogue tag.</summary>
    /// <summary>Slides a sprite to a new spot over time.</summary>
    /// <summary>
    /// Takes a pose the game owns over, so the mod can move it and edit its layers.
    ///
    /// The game rebuilds its own poses from its own script data every frame, which a mod
    /// cannot reach. Rebuilding the pose here instead puts it under the mod's control,
    /// and is what makes a game pose animatable.
    /// </summary>
    public sealed class PoseStatement : Statement
    {
        /// <summary>Pose name exactly as the game spells it, such as "monika 1".</summary>
        public string Name { get; set; }

        /// <summary>Where to put it, such as left or right.</summary>
        public string At { get; set; }
    }

    /// <summary>Replaces one layer of a pose that the mod has taken over.</summary>
    public sealed class LayerStatement : Statement
    {
        /// <summary>Pose to change.</summary>
        public string Pose { get; set; }

        /// <summary>Layer to replace, such as an eye or mouth layer.</summary>
        public string Layer { get; set; }

        /// <summary>Name of the art to show there instead.</summary>
        public string Asset { get; set; }
    }

    /// <summary>
    /// Continues the game's own scene from one of its lines.
    ///
    /// Everything the original block does up to and including the named line is kept, and
    /// the mod's own statements follow it. That is what makes it possible for a character
    /// to finish a line from the shipped script and for the mod to take over from there,
    /// instead of the mod having to rewrite the whole scene.
    /// </summary>
    public sealed class HookStatement : Statement
    {
        /// <summary>Key of the game's block to hook, such as "ch1_m_start".</summary>
        public string Block { get; set; }

        /// <summary>File this hook was read from, used in diagnostics.</summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// Dialogue id to stop after, as it appears in the script reference.
        ///
        /// The ids are hashes, not a running count, so the exact value has to be taken
        /// from the reference rather than guessed.
        /// </summary>
        public long AfterTextId { get; set; }

        public List<Statement> Body { get; } = new List<Statement>();
    }

    public sealed class MoveStatement : Statement
    {
        public string Name { get; set; }

        /// <summary>Horizontal position, 0 at the left edge and 1 at the right.</summary>
        public float XPos { get; set; }

        public float YPos { get; set; }
        public bool HasY { get; set; }
        public float Seconds { get; set; } = 0.5f;
    }

    /// <summary>Grows or shrinks a sprite over time. 1 is natural size.</summary>
    public sealed class ZoomStatement : Statement
    {
        public string Name { get; set; }
        public float Scale { get; set; } = 1f;
        public float Seconds { get; set; } = 0.5f;
    }

    /// <summary>Fades a sprite in or out over time, 0 to 1.</summary>
    public sealed class FadeStatement : Statement
    {
        public string Name { get; set; }
        public float Alpha { get; set; } = 1f;
        public float Seconds { get; set; } = 0.5f;
    }

    /// <summary>Turns a sprite over time, in degrees.</summary>
    public sealed class TurnStatement : Statement
    {
        public string Name { get; set; }
        public float Degrees { get; set; }
        public float Seconds { get; set; } = 0.5f;
    }

    /// <summary>Registers a new speaker so it can be used as a dialogue tag.</summary>
    public sealed class CharacterStatement : Statement
    {
        public string Tag { get; set; }
        public string DisplayName { get; set; }
        public string Image { get; set; }
    }

    /// <summary>Registers a mod-provided image under a script-facing name.</summary>
    public sealed class ImageStatement : Statement
    {
        public string Name { get; set; }
        public string File { get; set; }
    }

    /// <summary>Registers a mod-provided audio track under a script-facing name.</summary>
    public sealed class AudioStatement : Statement
    {
        public string Name { get; set; }
        public string File { get; set; }
    }

    /// <summary>A branch. First matching branch wins; <c>otherwise</c> is the fallback.</summary>
    public sealed class IfStatement : Statement
    {
        public List<Branch> Branches { get; } = new List<Branch>();
    }

    public sealed class Branch
    {
        /// <summary>Condition text, or null for <c>otherwise</c>.</summary>
        public string Condition { get; set; }

        public List<Statement> Body { get; set; } = new List<Statement>();
    }

    /// <summary>One branch of a choice.</summary>
    public sealed class MenuChoice
    {
        public string Caption { get; set; }
        public string Condition { get; set; }
        public List<Statement> Body { get; } = new List<Statement>();
        public int Line { get; set; }
    }

    public sealed class MenuStatement : Statement
    {
        /// <summary>Optional prompt shown with the choices.</summary>
        public string Caption { get; set; }

        public List<MenuChoice> Choices { get; } = new List<MenuChoice>();
    }

    /// <summary>
    /// Replaces the wording of a line the base game already contains.
    /// </summary>
    public sealed class OverrideStatement : Statement
    {
        public string Label { get; set; }
        public int TextId { get; set; }
        public string Text { get; set; }
    }

    /// <summary>Runs several statements as one block (used by <c>python:</c> bodies).</summary>
    public sealed class GroupStatement : Statement
    {
        public List<Statement> Body { get; } = new List<Statement>();
    }

    /// <summary>A Ren'Py-style while loop, compiled to conditional and backward jumps.</summary>
    public sealed class WhileStatement : Statement
    {
        public string Condition { get; set; }
        public List<Statement> Body { get; } = new List<Statement>();
    }

    /// <summary>Loop control. It is an error to use it outside a while loop.</summary>
    public sealed class LoopControlStatement : Statement
    {
        public bool IsContinue { get; set; }
    }

    /// <summary>An intentionally empty statement.</summary>
    public sealed class PassStatement : Statement
    {
    }

}
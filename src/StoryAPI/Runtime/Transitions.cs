namespace StoryAPI.Runtime
{
    /// <summary>
    /// The screen transitions a mod script can ask for.
    ///
    /// The base game writes a transition as an expression: <c>with Dissolve(2)</c>,
    /// <c>with ImageDissolve("gui/wipes/wipeleft.png", 16.0)</c>, or the name of a
    /// transition the game has already built. A mod script may write the same thing,
    /// and a few shorter names are accepted as well, because a mod author should not
    /// have to know the game's class names to ask for a cross-fade.
    /// </summary>
    public static class Transitions
    {
        /// <summary>How long the short names last, in seconds.</summary>
        private const float DefaultDuration = 0.5f;

        /// <summary>
        /// The command the game expects for "no transition at all".
        ///
        /// The engine compares the whole command with this word, so it has to be
        /// written exactly like this: "with None" would be read as a transition called
        /// "None" and there is no such transition.
        /// </summary>
        public const string None = "None";

        /// <summary>
        /// Turn what a mod wrote into the expression the game evaluates.
        ///
        /// Anything that is already an expression is passed through untouched, so the
        /// game's own vocabulary keeps working inside a mod.
        /// </summary>
        public static string Expand(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string trimmed = text.Trim();

            if (string.Equals(trimmed, None, System.StringComparison.OrdinalIgnoreCase))
            {
                return None;
            }

            switch (trimmed.ToLowerInvariant())
            {
                case "dissolve":
                    return "Dissolve(" + Duration(DefaultDuration) + ")";
                default:
                    return trimmed;
            }
        }

        /// <summary>
        /// Whether a transition asks for nothing at all.
        /// </summary>
        public static bool IsNone(string text)
        {
            return string.Equals(text?.Trim(), None, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A number in the form the game's expression engine accepts.
        /// </summary>
        private static string Duration(float seconds)
        {
            return seconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

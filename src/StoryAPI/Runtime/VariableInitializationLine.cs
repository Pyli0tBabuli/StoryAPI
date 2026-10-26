using SimpleExpressionEngine;
using RenpyParser;
using RenPyParser;
using StoryAPI.Game;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// Shared state for one declaration across all mod labels. Each registered label
    /// gets its own instruction instance, but only the first reached instance initializes
    /// a define/default declaration.
    /// </summary>
    internal sealed class VariableInitializationState
    {
        public bool Initialized;
        public bool Failed;
    }

    /// <summary>
    /// Initializes a top-level scalar variable while the game's story runner has a live
    /// execution context. This must not run during mod registration.
    /// </summary>
    internal sealed class VariableInitializationLine : Line, IApply
    {
        private readonly string _name;
        private readonly object _literal;
        private readonly bool _isDefault;
        private readonly string _sourcePath;
        private readonly int _sourceLine;
        private readonly VariableInitializationState _state;

        public VariableInitializationLine(string name, object literal, bool isDefault,
            string sourcePath, int sourceLine, VariableInitializationState state)
        {
            _name = name;
            _literal = literal;
            _isDefault = isDefault;
            _sourcePath = sourcePath;
            _sourceLine = sourceLine;
            _state = state;
        }

        public void Apply(IContext context)
        {
            if (_state == null || _state.Initialized || _state.Failed) return;

            if (GameBridge.TrySetVariableLiteral(_name, _literal, _isDefault, context))
            {
                _state.Initialized = true;
                Log.Info("Initialized " + (_isDefault ? "default" : "define") +
                         " variable '" + _name + "' at first mod-label entry.");
                return;
            }

            _state.Failed = true;
            Log.Warn("Could not initialize " + (_isDefault ? "default" : "define") +
                     " variable '" + _name + "' at " + _sourcePath + ":" + _sourceLine +
                     " during script execution. Check the preceding GameBridge warning.");
        }
    }
}

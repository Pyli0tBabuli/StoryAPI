using System;
using System.Linq;
using System.Reflection;
using RenPyParser.Images;
using RenPyParser.Sprites;
using RenPyParser.Transforms;
using RenPyParser;
using RenpyParser;
using SimpleExpressionEngine;
using UnityEngine;
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
    /// Initializes a top-level scalar literal or supported scalar expression while the
    /// game's story runner has a live execution context. This must not run during registration.
    /// </summary>
    internal sealed class VariableInitializationLine : Line, IApply
    {
        private readonly string _name;
        private readonly object _literal;
        private readonly string _expression;
        private readonly bool _isRuntimeExpression;
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

        public VariableInitializationLine(string name, string expression, bool isDefault,
            string sourcePath, int sourceLine, VariableInitializationState state)
        {
            _name = name;
            _expression = expression;
            _isRuntimeExpression = true;
            _isDefault = isDefault;
            _sourcePath = sourcePath;
            _sourceLine = sourceLine;
            _state = state;
        }

        public void Apply(IContext context)
        {
            if (_state == null || _state.Initialized || _state.Failed) return;

            if (_isRuntimeExpression)
            {
                ApplyRuntimeExpression(context);
                return;
            }

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

        private void ApplyRuntimeExpression(IContext context)
        {
            if (context == null)
            {
                Fail("no live script context was available");
                return;
            }

            if (_isDefault)
            {
                try
                {
                    if (context.ResolveVariable(_name, out DataValue _))
                    {
                        _state.Initialized = true;
                        Log.Info("Kept existing value for default variable '" + _name + "'.");
                        return;
                    }
                }
                catch
                {
                    // The variable is not present in this context; try initializing it.
                }
            }

            try
            {
                // Reuse the same instruction DDLC+ executes for a normal `$ name = expr`.
                // This keeps expression semantics in the game's own runtime, not a second evaluator.
                var assignment = new RenpyOneLinePython("$ " + _name + " = " + _expression);
                var apply = assignment as IApply;
                if (apply == null)
                {
                    Fail("the game's one-line expression instruction does not expose IApply");
                    return;
                }

                apply.Apply(context);
                _state.Initialized = true;
                Log.Info("Evaluated " + (_isDefault ? "default" : "define") +
                         " expression for variable '" + _name + "' at first mod-label entry.");
            }
            catch (Exception e)
            {
                Fail(e.GetBaseException().Message);
            }
        }

        private void Fail(string reason)
        {
            _state.Failed = true;
            Log.Warn("Could not initialize " + (_isDefault ? "default" : "define") +
                     " expression for variable '" + _name + "' at " + _sourcePath + ":" +
                     _sourceLine + ": " + reason);
        }
    }
}

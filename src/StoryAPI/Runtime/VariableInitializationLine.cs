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
        private readonly string _gateName;
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

        public VariableInitializationLine(string name, bool isDefault,
            string gateName, string sourcePath, int sourceLine, VariableInitializationState state)
        {
            _name = name;
            _gateName = gateName;
            _isRuntimeExpression = true;
            _isDefault = isDefault;
            _sourcePath = sourcePath;
            _sourceLine = sourceLine;
            _state = state;
        }

        public void Apply(IContext context)
        {
            if (_state == null) return;

            if (_isRuntimeExpression)
            {
                PrepareRuntimeExpression(context);
                return;
            }

            if (_state.Initialized || _state.Failed) return;

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

        private void PrepareRuntimeExpression(IContext context)
        {
            if (context == null)
            {
                Fail("no live script context was available");
                return;
            }

            // The following native RenpyOneLinePython instruction will execute the expression
            // if and only if this flag is true. The compiled expression object is a bytecode
            // builder, not an evaluator, so it must be run by DDLC+'s normal script runner.
            bool shouldEvaluate = !_state.Initialized && !_state.Failed;

            if (shouldEvaluate && _isDefault)
            {
                try
                {
                    if (context.ResolveVariable(_name, out DataValue _))
                    {
                        _state.Initialized = true;
                        shouldEvaluate = false;
                        Log.Info("Kept existing value for default variable '" + _name + "'.");
                    }
                }
                catch
                {
                    // The variable is not present in this context; try initializing it.
                }
            }

            if (!GameBridge.TrySetVariableLiteral(_gateName, shouldEvaluate, false, context))
                Fail("could not set the runtime expression gate variable '" + _gateName + "'");
        }

        private void Fail(string reason)
        {
            _state.Failed = true;
            // A failed declaration must not accidentally execute if its gate had a stale value.
            if (!string.IsNullOrEmpty(_gateName))
                GameBridge.TrySetVariableLiteral(_gateName, false, false);
            Log.Warn("Could not initialize " + (_isDefault ? "default" : "define") +
                     " expression for variable '" + _name + "' at " + _sourcePath + ":" +
                     _sourceLine + ": " + reason);
        }
    }

    /// <summary>
    /// Marks a runtime expression initialized after DDLC+ has executed its native
    /// RenpyOneLinePython assignment instruction.
    /// </summary>
    internal sealed class VariableExpressionCompleteLine : Line, IApply
    {
        private readonly string _name;
        private readonly bool _isDefault;
        private readonly string _sourcePath;
        private readonly int _sourceLine;
        private readonly VariableInitializationState _state;

        public VariableExpressionCompleteLine(string name, bool isDefault, string sourcePath,
            int sourceLine, VariableInitializationState state)
        {
            _name = name;
            _isDefault = isDefault;
            _sourcePath = sourcePath;
            _sourceLine = sourceLine;
            _state = state;
        }

        public void Apply(IContext context)
        {
            if (_state == null || _state.Initialized || _state.Failed) return;

            _state.Initialized = true;
            Log.Info("Evaluated " + (_isDefault ? "default" : "define") +
                     " expression for variable '" + _name + "' at " + _sourcePath + ":" +
                     _sourceLine + " during script execution.");
        }
    }
}

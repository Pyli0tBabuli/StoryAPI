using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using RenPyParser;
using RenpyParser;
using SimpleExpressionEngine;
using StoryAPI.Game;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// Evaluates a restricted chain of container index reads without sending the
    /// subscription syntax to DDLC+'s scalar expression evaluator.
    /// Supported examples: items[0], settings["count"], items[2]["nested"].
    /// </summary>
    internal sealed class VariableIndexExpressionLine : Line, IApply
    {
        private readonly string _name;
        private readonly string _rootName;
        private readonly List<object> _indices;
        private readonly bool _isDefault;
        private readonly string _sourcePath;
        private readonly int _sourceLine;
        private readonly VariableInitializationState _state;

        private VariableIndexExpressionLine(string name, string rootName, List<object> indices,
            bool isDefault, string sourcePath, int sourceLine, VariableInitializationState state)
        {
            _name = name;
            _rootName = rootName;
            _indices = indices;
            _isDefault = isDefault;
            _sourcePath = sourcePath;
            _sourceLine = sourceLine;
            _state = state;
        }

        public static bool ContainsIndexSyntax(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return false;

            bool inString = false;
            bool escaped = false;
            char quote = '\0';

            for (int i = 0; i < expression.Length; i++)
            {
                char c = expression[i];
                if (inString)
                {
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') { escaped = true; continue; }
                    if (c == quote) inString = false;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                }
                else if (c == '[')
                {
                    return true;
                }
            }

            return false;
        }

        public static bool CanHandle(string expression)
        {
            string rootName;
            List<object> indices;
            return TryParse(expression, out rootName, out indices);
        }

        public static bool TryCreate(string name, string expression, bool isDefault,
            string sourcePath, int sourceLine, VariableInitializationState state,
            out VariableIndexExpressionLine line)
        {
            string rootName;
            List<object> indices;
            if (!TryParse(expression, out rootName, out indices))
            {
                line = null;
                return false;
            }

            line = new VariableIndexExpressionLine(name, rootName, indices, isDefault,
                sourcePath, sourceLine, state);
            return true;
        }

        private static bool TryParse(string expression, out string rootName, out List<object> indices)
        {
            rootName = null;
            indices = new List<object>();
            if (string.IsNullOrWhiteSpace(expression)) return false;

            int cursor = 0;
            SkipWhitespace(expression, ref cursor);
            int nameStart = cursor;
            if (cursor >= expression.Length || !IsIdentifierStart(expression[cursor])) return false;

            cursor++;
            while (cursor < expression.Length && IsIdentifierPart(expression[cursor])) cursor++;
            rootName = expression.Substring(nameStart, cursor - nameStart);

            while (true)
            {
                SkipWhitespace(expression, ref cursor);
                if (cursor >= expression.Length) break;
                if (expression[cursor] != '[') return false;
                cursor++;
                SkipWhitespace(expression, ref cursor);
                if (cursor >= expression.Length) return false;

                object key;
                if (expression[cursor] == '"' || expression[cursor] == '\'')
                {
                    if (!TryReadString(expression, ref cursor, out string stringKey)) return false;
                    key = stringKey;
                }
                else
                {
                    int numberStart = cursor;
                    if (expression[cursor] == '-' || expression[cursor] == '+') cursor++;
                    int digitsStart = cursor;
                    while (cursor < expression.Length && char.IsDigit(expression[cursor])) cursor++;
                    if (cursor == digitsStart) return false;

                    if (!int.TryParse(expression.Substring(numberStart, cursor - numberStart),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                        return false;
                    key = number;
                }

                SkipWhitespace(expression, ref cursor);
                if (cursor >= expression.Length || expression[cursor] != ']') return false;
                cursor++;
                indices.Add(key);
            }

            return indices.Count > 0;
        }

        private static bool TryReadString(string expression, ref int cursor, out string value)
        {
            value = null;
            char quote = expression[cursor++];
            var builder = new StringBuilder();

            while (cursor < expression.Length)
            {
                char c = expression[cursor++];
                if (c == quote)
                {
                    value = builder.ToString();
                    return true;
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (cursor >= expression.Length) return false;
                char escaped = expression[cursor++];
                switch (escaped)
                {
                    case '\\': builder.Append('\\'); break;
                    case '"': builder.Append('"'); break;
                    case '\'': builder.Append('\''); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    default: builder.Append(escaped); break;
                }
            }

            return false;
        }

        private static void SkipWhitespace(string expression, ref int cursor)
        {
            while (cursor < expression.Length && char.IsWhiteSpace(expression[cursor])) cursor++;
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static bool TryGetObjectBackedVariable(string name, IContext context, out object value)
        {
            value = null;
            if (context == null || string.IsNullOrEmpty(name)) return false;

            try
            {
                if (!context.ResolveVariable(name, out DataValue dataValue)) return false;

                FieldInfo field = typeof(DataValue).GetField(
                    "objectRef", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null) return false;

                value = field.GetValue(dataValue);
                return value is IList || value is IDictionary;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        public void Apply(IContext context)
        {
            if (_state == null || _state.Initialized || _state.Failed) return;

            try
            {
                if (context == null)
                    throw new InvalidOperationException("no live script context was available");

                if (!TryGetObjectBackedVariable(_rootName, context, out object current))
                    throw new InvalidOperationException("base variable '" + _rootName +
                        "' does not contain an object-backed list or dictionary");

                foreach (object key in _indices)
                {
                    if (current is IDictionary dictionary)
                    {
                        if (!(key is string) || !dictionary.Contains(key))
                            throw new InvalidOperationException("dictionary key '" + key +
                                "' was not found or is not a string key");
                        current = dictionary[key];
                    }
                    else if (current is IList list)
                    {
                        if (!(key is int))
                            throw new InvalidOperationException("a list index must be an integer");
                        int index = (int)key;
                        if (index < 0) index += list.Count;
                        if (index < 0 || index >= list.Count)
                            throw new IndexOutOfRangeException("list index " + key +
                                " is outside the valid range");
                        current = list[index];
                    }
                    else
                    {
                        throw new InvalidOperationException("value is not a list or dictionary");
                    }
                }

                if (!GameBridge.TrySetVariableLiteral(_name, current, _isDefault, context))
                    throw new InvalidOperationException("could not write the indexed value");

                _state.Initialized = true;
                Log.Info("Evaluated indexed " + (_isDefault ? "default" : "define") +
                    " variable '" + _name + "' at " + _sourcePath + ":" + _sourceLine + ".");
            }
            catch (Exception e)
            {
                _state.Failed = true;
                Log.Warn("Could not evaluate indexed " + (_isDefault ? "default" : "define") +
                    " variable '" + _name + "' at " + _sourcePath + ":" + _sourceLine +
                    ": " + e.GetBaseException().Message);
            }
        }
    }
}

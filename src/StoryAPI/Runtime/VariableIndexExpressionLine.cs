using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using RenPyParser.Images;
using RenPyParser.Sprites;
using RenPyParser.Transforms;
using RenPyParser;
using RenpyParser;
using SimpleExpressionEngine;
using StoryAPI.Game;

namespace StoryAPI.Runtime
{
    /// <summary>
    /// Evaluates restricted container index reads without sending subscription syntax
    /// to DDLC+'s scalar expression evaluator. It can also replace index reads inside
    /// scalar expressions with temporary variables before the native evaluator runs.
    /// Supported examples include literal and computed indexes, nested collection reads, and scalar expressions.
    /// </summary>
    internal sealed class VariableIndexExpressionLine : Line, IApply
    {
        private sealed class DynamicIndex
        {
            public string Name { get; }

            public DynamicIndex(string name)
            {
                Name = name;
            }

            public override string ToString() => Name;
        }

        private readonly string _name;
        private readonly string _rootName;
        private readonly List<object> _indices;
        private readonly bool _isDefault;
        private readonly string _sourcePath;
        private readonly int _sourceLine;
        private readonly VariableInitializationState _state;
        private readonly VariableInitializationState _failureState;
        private readonly string _failureGateName;
        private readonly string _reportedName;
        private readonly bool _isTemporary;

        private VariableIndexExpressionLine(string name, string rootName, List<object> indices,
            bool isDefault, string sourcePath, int sourceLine, VariableInitializationState state,
            VariableInitializationState failureState = null, string failureGateName = null,
            string reportedName = null, bool isTemporary = false)
        {
            _name = name;
            _rootName = rootName;
            _indices = indices;
            _isDefault = isDefault;
            _sourcePath = sourcePath;
            _sourceLine = sourceLine;
            _state = state;
            _failureState = failureState;
            _failureGateName = failureGateName;
            _reportedName = reportedName ?? name;
            _isTemporary = isTemporary;
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

        /// <summary>
        /// Replaces each supported literal index chain inside a scalar expression with a
        /// temporary variable. The returned pairs are (temporary variable name, source chain).
        /// Example: "items[0] * 2 + items[1]" becomes
        /// "_storyapi_expr_index_0 * 2 + _storyapi_expr_index_1".
        /// </summary>
        public static bool TryRewriteExpression(string expression, string temporaryPrefix,
            out string rewrittenExpression, out List<KeyValuePair<string, string>> indexReads)
        {
            List<KeyValuePair<string, string>> ignoredDynamicIndexes;
            return TryRewriteExpression(expression, temporaryPrefix, out rewrittenExpression,
                out indexReads, out ignoredDynamicIndexes);
        }

        /// <summary>
        /// Replaces supported container reads in a scalar expression with temporary variables.
        /// Dynamic index expressions are assigned to their own temporary variables first.
        /// </summary>
        public static bool TryRewriteExpression(string expression, string temporaryPrefix,
            out string rewrittenExpression, out List<KeyValuePair<string, string>> indexReads,
            out List<KeyValuePair<string, string>> dynamicIndexExpressions)
        {
            rewrittenExpression = null;
            indexReads = new List<KeyValuePair<string, string>>();
            dynamicIndexExpressions = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(expression) || string.IsNullOrWhiteSpace(temporaryPrefix))
                return false;

            var builder = new StringBuilder(expression.Length + 32);
            bool inString = false;
            bool escaped = false;
            char quote = '\0';
            int cursor = 0;

            while (cursor < expression.Length)
            {
                char c = expression[cursor];

                if (inString)
                {
                    builder.Append(c);
                    cursor++;
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) inString = false;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    builder.Append(c);
                    cursor++;
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    int nameStart = cursor;
                    cursor++;
                    while (cursor < expression.Length && IsIdentifierPart(expression[cursor]))
                        cursor++;

                    int chainEnd;
                    string rewrittenChain;
                    var candidateDynamicIndexes = new List<KeyValuePair<string, string>>();
                    if (TryFindIndexExpressionEnd(expression, nameStart, cursor, temporaryPrefix,
                        dynamicIndexExpressions.Count, candidateDynamicIndexes, out chainEnd, out rewrittenChain))
                    {
                        string temporaryName = temporaryPrefix + "_index_" + indexReads.Count;
                        indexReads.Add(new KeyValuePair<string, string>(temporaryName, rewrittenChain));
                        dynamicIndexExpressions.AddRange(candidateDynamicIndexes);
                        builder.Append(temporaryName);
                        cursor = chainEnd;
                    }
                    else
                    {
                        // A '[' after an identifier that cannot be parsed as a supported
                        // literal/dynamic index chain makes the whole expression unsupported.
                        int lookahead = cursor;
                        SkipWhitespace(expression, ref lookahead);
                        if (lookahead < expression.Length && expression[lookahead] == '[')
                            return false;

                        builder.Append(expression, nameStart, cursor - nameStart);
                    }

                    continue;
                }

                builder.Append(c);
                cursor++;
            }

            if (indexReads.Count == 0)
                return false;

            rewrittenExpression = builder.ToString();
            return true;
        }

        private static bool TryFindIndexExpressionEnd(string expression, int nameStart, int afterName,
            string temporaryPrefix, int dynamicIndexOffset,
            List<KeyValuePair<string, string>> dynamicIndexExpressions, out int end, out string rewrittenChain)
        {
            end = afterName;
            rewrittenChain = null;
            int cursor = afterName;
            int bracketCount = 0;
            var chainBuilder = new StringBuilder(expression.Substring(nameStart, afterName - nameStart));

            while (true)
            {
                int bracketStart = cursor;
                SkipWhitespace(expression, ref bracketStart);
                if (bracketStart >= expression.Length || expression[bracketStart] != '[')
                    break;

                int scan = bracketStart + 1;
                bool inKeyString = false;
                bool escaped = false;
                char quote = '\0';
                bool foundClose = false;

                while (scan < expression.Length)
                {
                    char current = expression[scan];
                    if (inKeyString)
                    {
                        if (escaped) escaped = false;
                        else if (current == '\\') escaped = true;
                        else if (current == quote) inKeyString = false;
                        scan++;
                        continue;
                    }

                    if (current == '"' || current == '\'')
                    {
                        inKeyString = true;
                        quote = current;
                        scan++;
                        continue;
                    }

                    if (current == '[')
                        return false; // Nested subscriptions inside a key are intentionally unsupported.

                    if (current == ']')
                    {
                        foundClose = true;
                        break;
                    }

                    scan++;
                }

                if (!foundClose || inKeyString)
                    return false;

                string keyExpression = expression.Substring(
                    bracketStart + 1, scan - bracketStart - 1).Trim();
                if (IsLiteralIndexExpression(keyExpression))
                {
                    chainBuilder.Append(expression, bracketStart, scan - bracketStart + 1);
                }
                else
                {
                    if (!IsSupportedDynamicIndexExpression(keyExpression))
                        return false;

                    string keyName = temporaryPrefix + "_key_" +
                        (dynamicIndexOffset + dynamicIndexExpressions.Count);
                    dynamicIndexExpressions.Add(new KeyValuePair<string, string>(
                        keyName, keyExpression));
                    chainBuilder.Append('[').Append(keyName).Append(']');
                }

                cursor = scan + 1;
                bracketCount++;
            }

            if (bracketCount == 0)
                return false;

            end = cursor;
            rewrittenChain = chainBuilder.ToString();
            return true;
        }

        private static bool IsLiteralIndexExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            string trimmed = expression.Trim();
            if (trimmed[0] == '"' || trimmed[0] == '\'')
            {
                int cursor = 0;
                string ignored;
                if (!TryReadString(trimmed, ref cursor, out ignored))
                    return false;
                SkipWhitespace(trimmed, ref cursor);
                return cursor == trimmed.Length;
            }

            int number;
            return int.TryParse(trimmed, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out number);
        }

        /// <summary>
        /// Dynamic indexes are deliberately limited to variable/scalar arithmetic syntax.
        /// Calls, attribute access, nested indexes, slices and arbitrary Python are rejected.
        /// </summary>
        private static bool IsSupportedDynamicIndexExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            bool inString = false;
            bool escaped = false;
            char quote = '\0';
            int parenthesisDepth = 0;
            bool hasValue = false;

            for (int i = 0; i < expression.Length;)
            {
                char c = expression[i];

                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) inString = false;
                    i++;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    hasValue = true;
                    i++;
                    continue;
                }

                if (c == '[' || c == ']' || c == '.' || c == ',' ||
                    c == '=' || c == '<' || c == '>' || c == '!' ||
                    c == '&' || c == '|')
                    return false;

                if (IsIdentifierStart(c) || char.IsDigit(c))
                {
                    int start = i;
                    bool identifier = IsIdentifierStart(c);
                    i++;
                    while (i < expression.Length && IsIdentifierPart(expression[i]))
                        i++;

                    if (identifier)
                    {
                        int lookahead = i;
                        SkipWhitespace(expression, ref lookahead);
                        if (lookahead < expression.Length && expression[lookahead] == '(')
                            return false; // Function calls are outside this feature.
                    }

                    hasValue = true;
                    continue;
                }

                if (c == '(')
                {
                    parenthesisDepth++;
                    i++;
                    continue;
                }

                if (c == ')')
                {
                    parenthesisDepth--;
                    if (parenthesisDepth < 0) return false;
                    i++;
                    continue;
                }

                if (c == '+' || c == '-' || c == '*' || c == '/' || c == '%' ||
                    char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                return false;
            }

            return hasValue && !inString && parenthesisDepth == 0;
        }

        public static bool CanRewriteExpression(string expression)
        {
            string rewritten;
            List<KeyValuePair<string, string>> reads;
            List<KeyValuePair<string, string>> dynamicIndexes;
            return TryRewriteExpression(expression, "_storyapi_probe", out rewritten,
                out reads, out dynamicIndexes);
        }

        /// <summary>
        /// Converts a restricted indexed assignment (for example, items[index + 1] = score + 2)
        /// into native scalar-evaluation instructions and one guarded collection mutation line.
        /// </summary>
        public static bool TryCreateAssignmentLines(string code, string temporaryPrefix,
            string sourcePath, int sourceLine, int blockStart, out List<Line> lines)
        {
            lines = null;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(temporaryPrefix))
                return false;

            string rootName;
            List<object> targetIndices;
            List<KeyValuePair<string, string>> targetDynamicIndexes;
            string rightExpression;
            if (!TryParseCollectionAssignment(code, temporaryPrefix, out rootName,
                out targetIndices, out targetDynamicIndexes, out rightExpression))
                return false;

            string valueExpression = rightExpression;
            List<KeyValuePair<string, string>> rightIndexReads =
                new List<KeyValuePair<string, string>>();
            List<KeyValuePair<string, string>> rightDynamicIndexes =
                new List<KeyValuePair<string, string>>();

            if (ContainsIndexSyntax(rightExpression) &&
                !TryRewriteExpression(rightExpression, temporaryPrefix + "_rhs",
                    out valueExpression, out rightIndexReads, out rightDynamicIndexes))
                return false;

            var state = new VariableInitializationState();
            string gateName = temporaryPrefix + "_gate";
            string valueName = temporaryPrefix + "_value";
            var rightReadLines = new List<Line>();

            foreach (KeyValuePair<string, string> read in rightIndexReads)
            {
                VariableIndexExpressionLine readLine;
                if (!TryCreateTemporary(read.Key, read.Value, sourcePath, sourceLine,
                    state, gateName, rootName, out readLine))
                    return false;
                rightReadLines.Add(readLine);
            }

            // Fixed layout: gate + RHS dynamic keys + RHS collection reads + guard jump +
            // RHS assignment + target dynamic keys + mutation. The guard jumps to the first
            // instruction after this assignment if any RHS collection read failed.
            int blockSize = 4 + rightDynamicIndexes.Count + rightReadLines.Count +
                targetDynamicIndexes.Count;
            int skipTarget = blockStart + blockSize;
            var result = new List<Line>(blockSize);
            result.Add(new RenpyOneLinePython("$ " + gateName + " = True"));

            foreach (KeyValuePair<string, string> dynamicIndex in rightDynamicIndexes)
                result.Add(new RenpyOneLinePython(
                    "$ " + dynamicIndex.Key + " = " + dynamicIndex.Value));

            result.AddRange(rightReadLines);
            result.Add(new RenpyGoToLineUnless(gateName, skipTarget));
            result.Add(new RenpyOneLinePython("$ " + valueName + " = " + valueExpression));

            foreach (KeyValuePair<string, string> dynamicIndex in targetDynamicIndexes)
                result.Add(new RenpyOneLinePython(
                    "$ " + dynamicIndex.Key + " = " + dynamicIndex.Value));

            result.Add(new CollectionMutationLine(
                rootName, targetIndices, valueName, sourcePath, sourceLine, state));

            lines = result;
            return true;
        }

        private static bool TryParseCollectionAssignment(string code, string temporaryPrefix,
            out string rootName, out List<object> indices,
            out List<KeyValuePair<string, string>> dynamicIndexes, out string rightExpression)
        {
            rootName = null;
            indices = new List<object>();
            dynamicIndexes = new List<KeyValuePair<string, string>>();
            rightExpression = null;

            int cursor = 0;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;

            int nameStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            rootName = code.Substring(nameStart, cursor - nameStart);

            while (true)
            {
                int bracketStart = cursor;
                SkipWhitespace(code, ref bracketStart);
                if (bracketStart >= code.Length || code[bracketStart] != '[') break;

                int keyStart = bracketStart + 1;
                int scan = keyStart;
                bool inString = false;
                bool escaped = false;
                char quote = '\0';
                int close = -1;

                while (scan < code.Length)
                {
                    char c = code[scan];
                    if (inString)
                    {
                        if (escaped) escaped = false;
                        else if (c == '\\') escaped = true;
                        else if (c == quote) inString = false;
                        scan++;
                        continue;
                    }

                    if (c == '"' || c == '\'')
                    {
                        inString = true;
                        quote = c;
                        scan++;
                        continue;
                    }

                    // Nested subscription syntax inside an index is not part of Stage 5.
                    if (c == '[') return false;
                    if (c == ']')
                    {
                        close = scan;
                        break;
                    }

                    scan++;
                }

                if (close < 0 || inString) return false;
                string keyExpression = code.Substring(keyStart, close - keyStart).Trim();
                object literalKey;
                if (TryParseIndexKey(keyExpression, false, out literalKey))
                {
                    indices.Add(literalKey);
                }
                else
                {
                    if (!IsSupportedDynamicIndexExpression(keyExpression)) return false;
                    string keyName = temporaryPrefix + "_target_key_" + dynamicIndexes.Count;
                    dynamicIndexes.Add(new KeyValuePair<string, string>(keyName, keyExpression));
                    indices.Add(new DynamicIndex(keyName));
                }

                cursor = close + 1;
            }

            if (indices.Count == 0) return false;

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '=') return false;
            if (cursor + 1 < code.Length && code[cursor + 1] == '=') return false;
            cursor++;

            rightExpression = code.Substring(cursor).Trim();
            return !string.IsNullOrWhiteSpace(rightExpression);
        }

        private sealed class CollectionMutationLine : Line, IApply
        {
            private readonly string _rootName;
            private readonly List<object> _indices;
            private readonly string _valueName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;
            private readonly VariableInitializationState _state;

            public CollectionMutationLine(string rootName, List<object> indices,
                string valueName, string sourcePath, int sourceLine,
                VariableInitializationState state)
            {
                _rootName = rootName;
                _indices = indices;
                _valueName = valueName;
                _sourcePath = sourcePath;
                _sourceLine = sourceLine;
                _state = state;
            }

            public void Apply(IContext context)
            {
                if (_state != null && _state.Failed) return;

                try
                {
                    if (context == null)
                        throw new InvalidOperationException("no live script context was available");

                    if (!TryGetObjectBackedVariable(_rootName, context, out object current))
                        throw new InvalidOperationException("base variable '" + _rootName +
                            "' does not contain an object-backed list or dictionary");

                    if (!TryGetDynamicIndexValue(_valueName, context, out object value))
                        throw new InvalidOperationException("assigned value must be a supported scalar");

                    for (int i = 0; i < _indices.Count - 1; i++)
                    {
                        object key = ResolveMutationKey(_indices[i], context);
                        current = ReadContainerItem(current, key);
                    }

                    object finalKey = ResolveMutationKey(_indices[_indices.Count - 1], context);
                    if (current is IList list)
                    {
                        if (!TryConvertListIndex(finalKey, out int index))
                            throw new InvalidOperationException(
                                "a list assignment index must evaluate to an integer");
                        if (index < 0) index += list.Count;
                        if (index < 0 || index >= list.Count)
                            throw new IndexOutOfRangeException("list assignment index " + index +
                                " is outside the valid range");
                        list[index] = value;
                    }
                    else if (current is IDictionary dictionary)
                    {
                        if (!(finalKey is string))
                            throw new InvalidOperationException(
                                "a dictionary assignment key must evaluate to a string");
                        dictionary[finalKey] = value;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "assignment target is not a list or dictionary");
                    }

                    Log.Info("Updated indexed collection '" + _rootName + "' at " +
                        _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    if (_state != null) _state.Failed = true;
                    Log.Warn("Could not update indexed collection '" + _rootName +
                        "' at " + _sourcePath + ":" + _sourceLine + ": " +
                        e.GetBaseException().Message);
                }
            }
        }

        private static object ResolveMutationKey(object key, IContext context)
        {
            DynamicIndex dynamicIndex = key as DynamicIndex;
            if (dynamicIndex == null) return key;

            if (!TryGetDynamicIndexValue(dynamicIndex.Name, context, out object value))
                throw new InvalidOperationException("dynamic index '" + dynamicIndex.Name +
                    "' was not a supported scalar value");

            return value;
        }

        private static object ReadContainerItem(object current, object key)
        {
            if (current is IDictionary dictionary)
            {
                if (!(key is string) || !dictionary.Contains(key))
                    throw new InvalidOperationException("dictionary key '" + key +
                        "' was not found or did not evaluate to a string key");
                return dictionary[key];
            }

            if (current is IList list)
            {
                if (!TryConvertListIndex(key, out int index))
                    throw new InvalidOperationException("a list index must evaluate to an integer");
                if (index < 0) index += list.Count;
                if (index < 0 || index >= list.Count)
                    throw new IndexOutOfRangeException("list index " + index +
                        " is outside the valid range");
                return list[index];
            }

            throw new InvalidOperationException("value is not a list or dictionary");
        }

        public static bool TryCreateTemporary(string temporaryName, string chainExpression,
            string sourcePath, int sourceLine, VariableInitializationState declarationState,
            string gateName, string declarationName, out VariableIndexExpressionLine line)
        {
            string rootName;
            List<object> indices;
            if (!TryParse(chainExpression, out rootName, out indices, true))
            {
                line = null;
                return false;
            }

            line = new VariableIndexExpressionLine(
                temporaryName,
                rootName,
                indices,
                false,
                sourcePath,
                sourceLine,
                new VariableInitializationState(),
                declarationState,
                gateName,
                declarationName,
                true);
            return true;
        }

        private static bool TryParse(string expression, out string rootName,
            out List<object> indices, bool allowDynamicIndexes = false)
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

                int contentStart = ++cursor;
                bool inKeyString = false;
                bool escaped = false;
                char quote = '\0';
                int close = -1;

                while (cursor < expression.Length)
                {
                    char c = expression[cursor];
                    if (inKeyString)
                    {
                        if (escaped) escaped = false;
                        else if (c == '\\') escaped = true;
                        else if (c == quote) inKeyString = false;
                        cursor++;
                        continue;
                    }

                    if (c == '"' || c == '\'')
                    {
                        inKeyString = true;
                        quote = c;
                        cursor++;
                        continue;
                    }

                    if (c == '[') return false;
                    if (c == ']')
                    {
                        close = cursor;
                        break;
                    }

                    cursor++;
                }

                if (close < 0 || inKeyString) return false;

                string keyExpression = expression.Substring(
                    contentStart, close - contentStart).Trim();
                object key;
                if (!TryParseIndexKey(keyExpression, allowDynamicIndexes, out key))
                    return false;

                indices.Add(key);
                cursor = close + 1;
            }

            return indices.Count > 0;
        }

        private static bool TryParseIndexKey(string expression, bool allowDynamicIndexes,
            out object key)
        {
            key = null;
            if (string.IsNullOrWhiteSpace(expression)) return false;

            string trimmed = expression.Trim();
            if (trimmed[0] == '"' || trimmed[0] == '\'')
            {
                int cursor = 0;
                string stringKey;
                if (!TryReadString(trimmed, ref cursor, out stringKey)) return false;
                SkipWhitespace(trimmed, ref cursor);
                if (cursor != trimmed.Length) return false;
                key = stringKey;
                return true;
            }

            int number;
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                key = number;
                return true;
            }

            if (allowDynamicIndexes && IsSimpleIdentifier(trimmed))
            {
                key = new DynamicIndex(trimmed);
                return true;
            }

            return false;
        }

        private static bool IsSimpleIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || !IsIdentifierStart(value[0])) return false;
            for (int i = 1; i < value.Length; i++)
                if (!IsIdentifierPart(value[i])) return false;
            return true;
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

        private static bool TryGetDynamicIndexValue(string name, IContext context, out object value)
        {
            value = null;
            if (context == null || string.IsNullOrEmpty(name)) return false;

            try
            {
                if (!context.ResolveVariable(name, out DataValue dataValue)) return false;
                Type dataValueType = typeof(DataValue);

                FieldInfo typeField = dataValueType.GetField(
                    "type", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                object typeValue = typeField == null ? null : typeField.GetValue(dataValue);
                string typeName = typeValue == null ? string.Empty :
                    Convert.ToString(typeValue, CultureInfo.InvariantCulture);
                bool isString = typeName.IndexOf("string", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isBoolean = typeName.IndexOf("bool", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isNumeric = typeName.IndexOf("float", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("number", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("numeric", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("integer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("int", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isNullOrObject = typeName.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("object", StringComparison.OrdinalIgnoreCase) >= 0;

                FieldInfo objectField = dataValueType.GetField(
                    "objectRef", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                object objectValue = objectField == null ? null : objectField.GetValue(dataValue);

                // Prefer the discriminator over the backing fields: DDLC+ keeps inactive
                // fields at their default values (for example stringVal may be empty for a
                // numeric DataValue). Reading stringVal before type used to turn index 1
                // into the empty string and made valid dynamic list indexes fail.
                if (isString)
                {
                    FieldInfo stringField = dataValueType.GetField(
                        "stringVal", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    object stringValue = stringField == null ? null : stringField.GetValue(dataValue);
                    if (!(stringValue is string)) return false;
                    value = (string)stringValue;
                    return true;
                }

                if (objectValue is bool)
                {
                    value = objectValue;
                    return true;
                }

                if (objectValue is string || objectValue is byte || objectValue is sbyte ||
                    objectValue is short || objectValue is ushort || objectValue is int ||
                    objectValue is uint || objectValue is long || objectValue is ulong ||
                    objectValue is float || objectValue is double || objectValue is decimal)
                {
                    value = objectValue;
                    return true;
                }

                if (isBoolean)
                {
                    FieldInfo boolField = dataValueType.GetField(
                        "boolVal", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    object boolValue = boolField == null ? null : boolField.GetValue(dataValue);
                    if (boolValue is bool)
                    {
                        value = (bool)boolValue;
                        return true;
                    }

                    FieldInfo boolNumberField = dataValueType.GetField(
                        "floatVal", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    object boolNumber = boolNumberField == null ? null : boolNumberField.GetValue(dataValue);
                    if (boolNumber is float)
                    {
                        value = (float)boolNumber != 0f;
                        return true;
                    }

                    return false;
                }

                if (isNumeric)
                {
                    FieldInfo numberField = dataValueType.GetField(
                        "floatVal", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    object numberValue = numberField == null ? null : numberField.GetValue(dataValue);
                    if (!(numberValue is float) && !(numberValue is double) &&
                        !(numberValue is decimal) && !(numberValue is int))
                        return false;

                    value = numberValue;
                    return true;
                }

                if (isNullOrObject || objectValue != null)
                    return false;

                // Compatibility fallback for a game build that renames its numeric enum
                // members but keeps the documented numeric/string backing fields.
                FieldInfo fallbackStringField = dataValueType.GetField(
                    "stringVal", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                object fallbackStringValue = fallbackStringField == null ? null :
                    fallbackStringField.GetValue(dataValue);
                if (fallbackStringValue is string && !string.IsNullOrEmpty((string)fallbackStringValue))
                {
                    value = (string)fallbackStringValue;
                    return true;
                }

                FieldInfo fallbackNumberField = dataValueType.GetField(
                    "floatVal", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                object fallbackNumberValue = fallbackNumberField == null ? null :
                    fallbackNumberField.GetValue(dataValue);
                if (fallbackNumberValue is float || fallbackNumberValue is double ||
                    fallbackNumberValue is decimal || fallbackNumberValue is int)
                {
                    value = fallbackNumberValue;
                    return true;
                }

                return false;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        private static bool TryConvertListIndex(object value, out int index)
        {
            index = 0;
            if (value is int)
            {
                index = (int)value;
                return true;
            }

            double number;
            if (value is float) number = (float)value;
            else if (value is double) number = (double)value;
            else if (value is decimal) number = (double)(decimal)value;
            else if (value is short) number = (short)value;
            else if (value is byte) number = (byte)value;
            else if (value is long) number = (long)value;
            else return false;

            if (double.IsNaN(number) || double.IsInfinity(number) ||
                number != Math.Truncate(number) ||
                number < int.MinValue || number > int.MaxValue)
                return false;

            index = (int)number;
            return true;
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
                    object resolvedKey = key;
                    DynamicIndex dynamicIndex = key as DynamicIndex;
                    if (dynamicIndex != null &&
                        !TryGetDynamicIndexValue(dynamicIndex.Name, context, out resolvedKey))
                        throw new InvalidOperationException("dynamic index '" + dynamicIndex.Name +
                            "' was not a supported scalar value");

                    if (current is IDictionary dictionary)
                    {
                        if (!(resolvedKey is string) || !dictionary.Contains(resolvedKey))
                            throw new InvalidOperationException("dictionary key '" + resolvedKey +
                                "' was not found or did not evaluate to a string key");
                        current = dictionary[resolvedKey];
                    }
                    else if (current is IList list)
                    {
                        int index;
                        if (!TryConvertListIndex(resolvedKey, out index))
                            throw new InvalidOperationException(
                                "a list index must evaluate to an integer scalar");
                        if (index < 0) index += list.Count;
                        if (index < 0 || index >= list.Count)
                            throw new IndexOutOfRangeException("list index " + index +
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
                if (_isTemporary)
                {
                    Log.Info("Resolved an indexed value for variable '" + _reportedName +
                        "' at " + _sourcePath + ":" + _sourceLine + ".");
                }
                else
                {
                    Log.Info("Evaluated indexed " + (_isDefault ? "default" : "define") +
                        " variable '" + _name + "' at " + _sourcePath + ":" + _sourceLine + ".");
                }
            }
            catch (Exception e)
            {
                _state.Failed = true;
                if (_failureState != null)
                {
                    _failureState.Failed = true;

                    // Keep a failed read from passing an undefined temporary into the
                    // native expression engine, and prevent that expression from running.
                    GameBridge.TrySetVariableLiteral(_name, 0, false, context);
                    if (!string.IsNullOrEmpty(_failureGateName))
                        GameBridge.TrySetVariableLiteral(_failureGateName, false, false, context);
                }

                string targetName = _reportedName ?? _name;
                Log.Warn("Could not evaluate indexed " + (_isDefault ? "default" : "define") +
                    " variable '" + targetName + "' at " + _sourcePath + ":" + _sourceLine +
                    ": " + e.GetBaseException().Message);
            }
        }
    }
}

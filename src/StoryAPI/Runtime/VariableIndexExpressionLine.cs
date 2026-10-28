using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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
        /// Prepares a scalar condition that contains collection reads by resolving the reads
        /// into temporary variables before the native conditional evaluator sees the expression.
        /// The returned condition is guarded so a failed collection read makes the branch false.
        /// </summary>
        public static bool TryPrepareCondition(string condition, string temporaryPrefix,
            string sourcePath, int sourceLine, out List<Line> preparatoryLines,
            out string preparedCondition)
        {
            preparatoryLines = new List<Line>();
            preparedCondition = condition;

            List<Line> membershipLines;
            string membershipExpression;
            bool hasMembership = TryRewriteMembershipConditions(condition, temporaryPrefix,
                sourcePath, sourceLine, out membershipLines, out membershipExpression);

            string expressionAfterMembership = hasMembership ? membershipExpression : condition;
            List<Line> lengthLines;
            string lengthExpression;
            bool hasLength = TryRewriteLengthConditions(expressionAfterMembership, temporaryPrefix,
                sourcePath, sourceLine, out lengthLines, out lengthExpression);

            string expressionToRewrite = hasLength ? lengthExpression : expressionAfterMembership;
            string rewrittenExpression;
            List<KeyValuePair<string, string>> indexReads;
            List<KeyValuePair<string, string>> dynamicIndexes;
            bool hasIndexReads = TryRewriteExpression(expressionToRewrite, temporaryPrefix,
                out rewrittenExpression, out indexReads, out dynamicIndexes);

            if (!hasMembership && !hasLength && !hasIndexReads)
                return false;

            if (!hasIndexReads)
            {
                rewrittenExpression = expressionToRewrite;
                indexReads = new List<KeyValuePair<string, string>>();
                dynamicIndexes = new List<KeyValuePair<string, string>>();
            }

            string gateName = temporaryPrefix + "_gate";
            var result = new List<Line>
            {
                new RenpyOneLinePython("$ " + gateName + " = True")
            };

            if (hasMembership)
                result.AddRange(membershipLines);
            if (hasLength)
                result.AddRange(lengthLines);

            foreach (KeyValuePair<string, string> dynamicIndex in dynamicIndexes)
            {
                result.Add(new RenpyOneLinePython(
                    "$ " + dynamicIndex.Key + " = " + dynamicIndex.Value));
            }

            var state = new VariableInitializationState();
            foreach (KeyValuePair<string, string> read in indexReads)
            {
                VariableIndexExpressionLine readLine;
                if (!TryCreateTemporary(read.Key, read.Value, sourcePath, sourceLine,
                    state, gateName, read.Value, out readLine))
                {
                    preparatoryLines.Clear();
                    preparedCondition = condition;
                    return false;
                }

                result.Add(readLine);
            }

            preparatoryLines = result;
            preparedCondition = gateName + " and (" + rewrittenExpression + ")";
            return true;
        }

        /// <summary>
        /// Rewrites len(collection) calls for named list/dictionary variables into
        /// temporary scalar values that the native condition evaluator can compare.
        /// </summary>
        private static bool TryRewriteLengthConditions(string condition, string temporaryPrefix,
            string sourcePath, int sourceLine, out List<Line> preparatoryLines,
            out string rewrittenCondition)
        {
            preparatoryLines = new List<Line>();
            rewrittenCondition = condition;
            if (string.IsNullOrWhiteSpace(condition)) return false;

            var pattern = new Regex(
                @"(?<![\w.])len\s*\(\s*(?<collection>[A-Za-z_][A-Za-z0-9_]*)\s*\)",
                RegexOptions.Compiled);
            MatchCollection matches = pattern.Matches(condition);
            if (matches.Count == 0) return false;

            var replacements = new List<Tuple<int, int, string>>();
            var lines = new List<Line>();
            var state = new VariableInitializationState();
            string gateName = temporaryPrefix + "_gate";
            int lengthIndex = 0;

            foreach (Match match in matches)
            {
                if (!IsOutsideStringAt(condition, match.Index)) continue;

                string collectionName = match.Groups["collection"].Value;
                string resultName = temporaryPrefix + "_length_result_" +
                    lengthIndex.ToString(CultureInfo.InvariantCulture);

                lines.Add(new CollectionLengthLine(collectionName, resultName, gateName,
                    sourcePath, sourceLine, state));
                replacements.Add(Tuple.Create(match.Index, match.Length, resultName));
                lengthIndex++;
            }

            if (replacements.Count == 0) return false;

            var builder = new StringBuilder();
            int cursor = 0;
            foreach (Tuple<int, int, string> item in replacements)
            {
                builder.Append(condition, cursor, item.Item1 - cursor);
                builder.Append(item.Item3);
                cursor = item.Item1 + item.Item2;
            }
            builder.Append(condition, cursor, condition.Length - cursor);

            preparatoryLines = lines;
            rewrittenCondition = builder.ToString();
            return true;
        }

        /// <summary>
        /// Resolves restricted list/dictionary membership tests before DDLC+'s native
        /// scalar condition evaluator runs. The right-hand side is a collection variable;
        /// the left-hand side is a scalar literal or a simple variable name.
        /// </summary>
        private static bool TryRewriteMembershipConditions(string condition, string temporaryPrefix,
            string sourcePath, int sourceLine, out List<Line> preparatoryLines,
            out string rewrittenCondition)
        {
            preparatoryLines = new List<Line>();
            rewrittenCondition = condition;
            if (string.IsNullOrWhiteSpace(condition)) return false;

            var pattern = new Regex(
                @"(?<left>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|[A-Za-z_][A-Za-z0-9_]*|[-+]?(?:\d+(?:\.\d*)?|\.\d+))\s+(?<operator>not\s+in|in)\s+(?<collection>[A-Za-z_][A-Za-z0-9_]*)",
                RegexOptions.Compiled);
            MatchCollection matches = pattern.Matches(condition);
            if (matches.Count == 0) return false;

            var replacements = new List<Tuple<int, int, string>>();
            var lines = new List<Line>();
            var state = new VariableInitializationState();
            string gateName = temporaryPrefix + "_gate";
            int membershipIndex = 0;

            foreach (Match match in matches)
            {
                // Do not interpret text inside a string literal as a membership operator.
                // Match boundaries also keep arbitrary comparison expressions outside
                // this deliberately small feature.
                if (!IsOutsideStringAt(condition, match.Index)) continue;
                if (!IsMembershipBoundaryBefore(condition, match.Index) ||
                    !IsMembershipBoundaryAfter(condition, match.Index + match.Length))
                    return false;

                string left = match.Groups["left"].Value;
                string operatorText = match.Groups["operator"].Value;
                string collectionName = match.Groups["collection"].Value;
                if (left == "and" || left == "or" || left == "not" || left == "in" ||
                    collectionName == "and" || collectionName == "or" ||
                    collectionName == "not" || collectionName == "in")
                    return false;

                string valueName = temporaryPrefix + "_membership_value_" +
                    membershipIndex.ToString(CultureInfo.InvariantCulture);
                string resultName = temporaryPrefix + "_membership_result_" +
                    membershipIndex.ToString(CultureInfo.InvariantCulture);

                lines.Add(new RenpyOneLinePython("$ " + valueName + " = " + left));
                lines.Add(new CollectionMembershipLine(collectionName, valueName, resultName,
                    gateName, sourcePath, sourceLine, state));

                bool isNotIn = operatorText.TrimStart().StartsWith("not",
                    StringComparison.Ordinal);
                replacements.Add(Tuple.Create(match.Index, match.Length,
                    isNotIn ? "not " + resultName : resultName));
                membershipIndex++;
            }

            if (replacements.Count == 0) return false;

            var builder = new StringBuilder();
            int cursor = 0;
            foreach (Tuple<int, int, string> item in replacements)
            {
                builder.Append(condition, cursor, item.Item1 - cursor);
                builder.Append(item.Item3);
                cursor = item.Item1 + item.Item2;
            }
            builder.Append(condition, cursor, condition.Length - cursor);

            preparatoryLines = lines;
            rewrittenCondition = builder.ToString();
            return true;
        }

        private static bool IsOutsideStringAt(string expression, int position)
        {
            bool inString = false;
            bool escaped = false;
            char quote = '\0';

            for (int i = 0; i < position && i < expression.Length; i++)
            {
                char c = expression[i];
                if (!inString)
                {
                    if (c == '"' || c == '\'')
                    {
                        inString = true;
                        quote = c;
                    }
                    continue;
                }

                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == quote) inString = false;
            }

            return !inString;
        }

        private static bool IsMembershipBoundaryBefore(string expression, int position)
        {
            string before = expression.Substring(0, position).TrimEnd();
            if (before.Length == 0 || before[before.Length - 1] == '(') return true;
            return EndsWithKeyword(before, "and") || EndsWithKeyword(before, "or") ||
                EndsWithKeyword(before, "not");
        }

        private static bool IsMembershipBoundaryAfter(string expression, int position)
        {
            string after = expression.Substring(position).TrimStart();
            if (after.Length == 0 || after[0] == ')') return true;
            return StartsWithKeyword(after, "and") || StartsWithKeyword(after, "or");
        }

        private static bool EndsWithKeyword(string text, string keyword)
        {
            return text.Length >= keyword.Length &&
                string.Compare(text, text.Length - keyword.Length, keyword, 0,
                    keyword.Length, StringComparison.Ordinal) == 0 &&
                (text.Length == keyword.Length ||
                 !IsIdentifierPart(text[text.Length - keyword.Length - 1]));
        }

        private static bool StartsWithKeyword(string text, string keyword)
        {
            return text.StartsWith(keyword, StringComparison.Ordinal) &&
                (text.Length == keyword.Length || !IsIdentifierPart(text[keyword.Length]));
        }

        /// <summary>
        /// Compiles the restricted list methods append(value) and remove(value). Only one
        /// scalar argument is accepted; indexed reads inside that value are resolved first.
        /// </summary>
        /// <summary>
        /// Recognizes clear() on a named object-backed list or dictionary. No arguments
        /// are accepted; the collection is cleared in-place at runtime.
        /// </summary>
        public static bool TryCreateCollectionClearLine(string code, string sourcePath,
            int sourceLine, out Line line)
        {
            line = null;
            if (string.IsNullOrWhiteSpace(code)) return false;

            int cursor = 0;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;

            int nameStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            string rootName = code.Substring(nameStart, cursor - nameStart);

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '.') return false;
            cursor++;
            SkipWhitespace(code, ref cursor);

            const string methodName = "clear";
            if (cursor + methodName.Length > code.Length ||
                string.Compare(code, cursor, methodName, 0, methodName.Length,
                    StringComparison.Ordinal) != 0)
                return false;
            cursor += methodName.Length;

            if (cursor < code.Length && IsIdentifierPart(code[cursor])) return false;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '(') return false;
            cursor++;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != ')') return false;
            cursor++;
            SkipWhitespace(code, ref cursor);
            if (cursor != code.Length) return false;

            line = new CollectionClearLine(rootName, sourcePath, sourceLine);
            return true;
        }

        /// <summary>
        /// Recognizes extend(otherList) on a named object-backed list. The argument must
        /// be another simple variable name; expressions and indexed/nested targets are unsupported.
        /// </summary>
        public static bool TryCreateListExtendLine(string code, string sourcePath,
            int sourceLine, out Line line)
        {
            line = null;
            if (string.IsNullOrWhiteSpace(code)) return false;

            int cursor = 0;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;

            int nameStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            string rootName = code.Substring(nameStart, cursor - nameStart);

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '.') return false;
            cursor++;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;

            int methodStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            if (!string.Equals(code.Substring(methodStart, cursor - methodStart),
                "extend", StringComparison.Ordinal))
                return false;

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '(') return false;
            cursor++;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;

            int sourceStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            string sourceName = code.Substring(sourceStart, cursor - sourceStart);

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != ')') return false;
            cursor++;
            SkipWhitespace(code, ref cursor);
            if (cursor != code.Length) return false;

            line = new ListExtendLine(rootName, sourceName, sourcePath, sourceLine);
            return true;
        }

        /// <summary>
        /// Recognizes update(other_dict) on a named object-backed dictionary. The argument
        /// must be another simple variable name; expressions and nested targets are unsupported.
        /// </summary>
        public static bool TryCreateDictionaryUpdateLine(string code, string sourcePath,
            int sourceLine, out Line line)
        {
            line = null;
            if (string.IsNullOrWhiteSpace(code)) return false;

            int cursor = 0;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;
            int targetStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            string targetName = code.Substring(targetStart, cursor - targetStart);

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor++] != '.') return false;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;
            int methodStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            if (!string.Equals(code.Substring(methodStart, cursor - methodStart),
                "update", StringComparison.Ordinal))
                return false;

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor++] != '(') return false;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;
            int sourceStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            string sourceName = code.Substring(sourceStart, cursor - sourceStart);

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor++] != ')') return false;
            SkipWhitespace(code, ref cursor);
            if (cursor != code.Length) return false;

            line = new DictionaryUpdateLine(targetName, sourceName, sourcePath, sourceLine);
            return true;
        }

        /// <summary>
        /// Recognizes dictionary.pop("key"), optionally assigning the removed value to a
        /// simple variable. Keys must be quoted string literals in this supported subset.
        /// </summary>
        public static bool TryCreateDictionaryPopLines(string code, string sourcePath,
            int sourceLine, out List<Line> lines)
        {
            lines = null;
            if (string.IsNullOrWhiteSpace(code)) return false;

            int cursor = 0;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;
            int firstStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            string firstName = code.Substring(firstStart, cursor - firstStart);
            int afterFirstName = cursor;
            SkipWhitespace(code, ref cursor);

            string destinationName = null;
            string rootName;
            if (cursor < code.Length && code[cursor] == '=' &&
                (cursor + 1 >= code.Length || code[cursor + 1] != '='))
            {
                destinationName = firstName;
                cursor++;
                SkipWhitespace(code, ref cursor);
                if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;
                int rootStart = cursor++;
                while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
                rootName = code.Substring(rootStart, cursor - rootStart);
            }
            else
            {
                rootName = firstName;
                cursor = afterFirstName;
            }

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor++] != '.') return false;
            SkipWhitespace(code, ref cursor);
            if (cursor + 3 > code.Length ||
                string.Compare(code, cursor, "pop", 0, 3, StringComparison.Ordinal) != 0)
                return false;
            cursor += 3;
            if (cursor < code.Length && IsIdentifierPart(code[cursor])) return false;

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor++] != '(') return false;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || (code[cursor] != '"' && code[cursor] != '\''))
                return false;

            string key;
            if (!TryReadString(code, ref cursor, out key)) return false;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor++] != ')') return false;
            SkipWhitespace(code, ref cursor);
            if (cursor != code.Length) return false;

            lines = new List<Line>
            {
                new DictionaryPopLine(rootName, destinationName, key, sourcePath, sourceLine)
            };
            return true;
        }

        /// <summary>
        /// Compiles a restricted list.insert(index, value) call. Index expressions must be
        /// scalar expressions without collection reads; value expressions may use supported
        /// collection reads, which are resolved before the mutation.
        /// </summary>
        public static bool TryCreateListInsertLines(string code, string temporaryPrefix,
            string sourcePath, int sourceLine, int blockStart, out List<Line> lines)
        {
            lines = null;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(temporaryPrefix))
                return false;

            string rootName;
            string indexExpression;
            string valueExpression;
            if (!TryParseListInsertCall(code, out rootName, out indexExpression, out valueExpression))
                return false;

            // A collection lookup in the index would need its own preparation phase. Keep the
            // index syntax scalar-only for this first implementation.
            if (ContainsIndexSyntax(indexExpression))
                return false;

            string rewrittenValue = valueExpression;
            List<KeyValuePair<string, string>> valueIndexReads =
                new List<KeyValuePair<string, string>>();
            List<KeyValuePair<string, string>> valueDynamicIndexes =
                new List<KeyValuePair<string, string>>();

            if (ContainsIndexSyntax(valueExpression) &&
                !TryRewriteExpression(valueExpression, temporaryPrefix + "_insert_arg",
                    out rewrittenValue, out valueIndexReads, out valueDynamicIndexes))
                return false;

            var state = new VariableInitializationState();
            string gateName = temporaryPrefix + "_insert_gate";
            string indexName = temporaryPrefix + "_insert_index";
            string valueName = temporaryPrefix + "_insert_value";
            var valueReadLines = new List<Line>();

            foreach (KeyValuePair<string, string> read in valueIndexReads)
            {
                VariableIndexExpressionLine readLine;
                if (!TryCreateTemporary(read.Key, read.Value, sourcePath, sourceLine,
                    state, gateName, rootName, out readLine))
                    return false;
                valueReadLines.Add(readLine);
            }

            int blockSize = 5 + valueDynamicIndexes.Count + valueReadLines.Count;
            int skipTarget = blockStart + blockSize;
            var result = new List<Line>(blockSize);
            result.Add(new RenpyOneLinePython("$ " + gateName + " = True"));
            result.Add(new RenpyOneLinePython("$ " + indexName + " = " + indexExpression));

            foreach (KeyValuePair<string, string> dynamicIndex in valueDynamicIndexes)
                result.Add(new RenpyOneLinePython(
                    "$ " + dynamicIndex.Key + " = " + dynamicIndex.Value));

            result.AddRange(valueReadLines);
            result.Add(new RenpyGoToLineUnless(gateName, skipTarget));
            result.Add(new RenpyOneLinePython("$ " + valueName + " = " + rewrittenValue));
            result.Add(new ListInsertLine(rootName, indexName, valueName,
                sourcePath, sourceLine, state));

            lines = result;
            return true;
        }

        /// <summary>
        /// Compiles list.pop() or list.pop(index), optionally assigning the removed scalar
        /// to a simple variable, as in "last_item = inventory.pop()".
        /// </summary>
        public static bool TryCreateListPopLines(string code, string temporaryPrefix,
            string sourcePath, int sourceLine, out List<Line> lines)
        {
            lines = null;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(temporaryPrefix))
                return false;

            string rootName;
            string destinationName;
            string indexExpression;
            if (!TryParseListPopCall(code, out rootName, out destinationName, out indexExpression))
                return false;

            if (indexExpression != null && ContainsIndexSyntax(indexExpression))
                return false;

            string indexName = null;
            var result = new List<Line>();
            if (indexExpression != null)
            {
                indexName = temporaryPrefix + "_pop_index";
                result.Add(new RenpyOneLinePython("$ " + indexName + " = " + indexExpression));
            }

            result.Add(new ListPopLine(rootName, destinationName, indexName,
                sourcePath, sourceLine));
            lines = result;
            return true;
        }

        public static bool TryCreateListMethodMutationLines(string code, string temporaryPrefix,
            string sourcePath, int sourceLine, int blockStart, out List<Line> lines)
        {
            lines = null;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(temporaryPrefix))
                return false;

            string rootName;
            string methodName;
            string argumentExpression;
            if (!TryParseListMethodCall(code, out rootName, out methodName, out argumentExpression))
                return false;

            string valueExpression = argumentExpression;
            List<KeyValuePair<string, string>> rightIndexReads =
                new List<KeyValuePair<string, string>>();
            List<KeyValuePair<string, string>> rightDynamicIndexes =
                new List<KeyValuePair<string, string>>();

            if (ContainsIndexSyntax(argumentExpression) &&
                !TryRewriteExpression(argumentExpression, temporaryPrefix + "_arg",
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

            // Layout: gate + dynamic argument keys + argument collection reads + guard +
            // scalar argument evaluation + list method. The guard skips mutation if an
            // indexed argument cannot be resolved.
            int blockSize = 4 + rightDynamicIndexes.Count + rightReadLines.Count;
            int skipTarget = blockStart + blockSize;
            var result = new List<Line>(blockSize);
            result.Add(new RenpyOneLinePython("$ " + gateName + " = True"));

            foreach (KeyValuePair<string, string> dynamicIndex in rightDynamicIndexes)
                result.Add(new RenpyOneLinePython(
                    "$ " + dynamicIndex.Key + " = " + dynamicIndex.Value));

            result.AddRange(rightReadLines);
            result.Add(new RenpyGoToLineUnless(gateName, skipTarget));
            result.Add(new RenpyOneLinePython("$ " + valueName + " = " + valueExpression));
            result.Add(new ListMethodMutationLine(
                rootName, methodName, valueName, sourcePath, sourceLine, state));

            lines = result;
            return true;
        }

        private static bool TryParseListMethodCall(string code, out string rootName,
            out string methodName, out string argumentExpression)
        {
            rootName = null;
            methodName = null;
            argumentExpression = null;
            if (string.IsNullOrWhiteSpace(code)) return false;

            int cursor = 0;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;

            int nameStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            rootName = code.Substring(nameStart, cursor - nameStart);

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '.') return false;
            cursor++;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;

            int methodStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            methodName = code.Substring(methodStart, cursor - methodStart);
            if (methodName != "append" && methodName != "remove") return false;

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '(') return false;
            int argumentStart = ++cursor;
            int nestedParentheses = 0;
            int nestedBrackets = 0;
            bool inString = false;
            bool escaped = false;
            char quote = '\0';
            int close = -1;

            while (cursor < code.Length)
            {
                char c = code[cursor];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) inString = false;
                    cursor++;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    cursor++;
                    continue;
                }

                if (c == '(')
                {
                    nestedParentheses++;
                    cursor++;
                    continue;
                }

                if (c == ')')
                {
                    if (nestedParentheses == 0)
                    {
                        close = cursor;
                        break;
                    }
                    nestedParentheses--;
                    cursor++;
                    continue;
                }

                if (c == '[')
                {
                    nestedBrackets++;
                    cursor++;
                    continue;
                }

                if (c == ']')
                {
                    if (nestedBrackets == 0) return false;
                    nestedBrackets--;
                    cursor++;
                    continue;
                }

                if (c == ',' && nestedParentheses == 0 && nestedBrackets == 0)
                    return false; // Deliberately support one positional argument only.

                cursor++;
            }

            if (close < 0 || inString || nestedParentheses != 0 || nestedBrackets != 0)
                return false;

            argumentExpression = code.Substring(argumentStart, close - argumentStart).Trim();
            if (string.IsNullOrWhiteSpace(argumentExpression)) return false;

            cursor = close + 1;
            SkipWhitespace(code, ref cursor);
            return cursor == code.Length;
        }

        /// <summary>
        /// Converts a restricted indexed assignment (for example, items[index + 1] = score + 2)
        /// into native scalar-evaluation instructions and one guarded collection mutation line.
        /// </summary>
        /// <summary>
        /// Compiles compound assignments for a simple variable (+=, -=, *=, /=, %=).
        /// Collection reads on the right-hand side are resolved before the native scalar assignment.
        /// Indexed targets are handled by the indexed-assignment pipeline.
        /// </summary>
        public static bool TryCreateCompoundAssignmentLines(string code, string temporaryPrefix,
            string sourcePath, int sourceLine, int blockStart, out List<Line> lines)
        {
            lines = null;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(temporaryPrefix))
                return false;

            int cursor = 0;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;

            int nameStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            string targetName = code.Substring(nameStart, cursor - nameStart);

            SkipWhitespace(code, ref cursor);
            if (cursor + 1 >= code.Length || code[cursor + 1] != '=' ||
                (code[cursor] != '+' && code[cursor] != '-' && code[cursor] != '*' &&
                 code[cursor] != '/' && code[cursor] != '%'))
                return false;

            char operation = code[cursor];
            cursor += 2;
            string rightExpression = code.Substring(cursor).Trim();
            if (string.IsNullOrWhiteSpace(rightExpression) ||
                ContainsUnquotedSemicolon(rightExpression))
                return false;

            string valueExpression = rightExpression;
            var rightIndexReads = new List<KeyValuePair<string, string>>();
            var rightDynamicIndexes = new List<KeyValuePair<string, string>>();
            bool hasIndexReads = ContainsIndexSyntax(rightExpression);
            if (hasIndexReads &&
                !TryRewriteExpression(rightExpression, temporaryPrefix + "_compound_rhs",
                    out valueExpression, out rightIndexReads, out rightDynamicIndexes))
                return false;

            string assignment = "$ " + targetName + " = " + targetName + " " +
                operation + " (" + valueExpression + ")";
            string moduloRightName = temporaryPrefix + "_compound_rhs_value";
            string moduloRightAssignment = "$ " + moduloRightName + " = (" +
                valueExpression + ")";
            if (!hasIndexReads)
            {
                if (operation == '%')
                {
                    lines = new List<Line>
                    {
                        new RenpyOneLinePython(moduloRightAssignment),
                        new ScalarModuloAssignmentLine(targetName, moduloRightName,
                            sourcePath, sourceLine)
                    };
                }
                else
                {
                    lines = new List<Line> { new RenpyOneLinePython(assignment) };
                }
                return true;
            }

            var state = new VariableInitializationState();
            string gateName = temporaryPrefix + "_compound_gate";
            var rightReadLines = new List<Line>();
            foreach (KeyValuePair<string, string> read in rightIndexReads)
            {
                VariableIndexExpressionLine readLine;
                if (!TryCreateTemporary(read.Key, read.Value, sourcePath, sourceLine,
                    state, gateName, targetName, out readLine))
                    return false;
                rightReadLines.Add(readLine);
            }

            int blockSize = (operation == '%' ? 4 : 3) +
                rightDynamicIndexes.Count + rightReadLines.Count;
            int skipTarget = blockStart + blockSize;
            var result = new List<Line>(blockSize);
            result.Add(new RenpyOneLinePython("$ " + gateName + " = True"));
            foreach (KeyValuePair<string, string> dynamicIndex in rightDynamicIndexes)
                result.Add(new RenpyOneLinePython(
                    "$ " + dynamicIndex.Key + " = " + dynamicIndex.Value));
            result.AddRange(rightReadLines);
            result.Add(new RenpyGoToLineUnless(gateName, skipTarget));
            if (operation == '%')
            {
                result.Add(new RenpyOneLinePython(moduloRightAssignment));
                result.Add(new ScalarModuloAssignmentLine(targetName, moduloRightName,
                    sourcePath, sourceLine));
            }
            else
            {
                result.Add(new RenpyOneLinePython(assignment));
            }
            lines = result;
            return true;
        }

        private static bool ContainsUnquotedSemicolon(string expression)
        {
            bool inString = false;
            bool escaped = false;
            char quote = '\0';
            for (int i = 0; i < expression.Length; i++)
            {
                char c = expression[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) inString = false;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                }
                else if (c == ';')
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Compiles +=, -=, and *= assignments to indexed list/dictionary entries by
        /// translating them into the existing guarded indexed-assignment pipeline.
        /// Only targets accepted by the regular indexed assignment parser are supported.
        /// </summary>
        public static bool TryCreateIndexedCompoundAssignmentLines(string code,
            string temporaryPrefix, string sourcePath, int sourceLine, int blockStart,
            out List<Line> lines)
        {
            lines = null;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(temporaryPrefix))
                return false;

            string targetExpression;
            char operation;
            string rightExpression;
            if (!TryParseIndexedCompoundAssignment(code, out targetExpression,
                out operation, out rightExpression))
                return false;

            string probeAssignment = targetExpression + " = 0";
            string rootName;
            List<object> targetIndices;
            List<KeyValuePair<string, string>> targetDynamicIndexes;
            string ignoredRight;
            if (!TryParseCollectionAssignment(probeAssignment, temporaryPrefix,
                out rootName, out targetIndices, out targetDynamicIndexes, out ignoredRight) ||
                targetIndices.Count == 0)
                return false;

            // Modulo is computed by StoryAPI at runtime because DDLC+'s scalar expression
            // evaluator does not reliably execute the native % operator. Keep RHS resolution
            // and the guarded collection mutation pipeline, but let the mutation line combine
            // the existing target value with the resolved RHS.
            if (operation == '%')
                return TryCreateAssignmentLines(targetExpression + " = " + rightExpression,
                    temporaryPrefix, sourcePath, sourceLine, blockStart, out lines, '%');

            // Reuse the standard assignment pipeline: it resolves any indexed values on the
            // RHS, guards failed reads, then mutates in-place.
            string expandedAssignment = targetExpression + " = " + targetExpression + " " +
                operation + " (" + rightExpression + ")";
            return TryCreateAssignmentLines(expandedAssignment, temporaryPrefix, sourcePath,
                sourceLine, blockStart, out lines);
        }

        private static bool TryParseIndexedCompoundAssignment(string code,
            out string targetExpression, out char operation, out string rightExpression)
        {
            targetExpression = null;
            operation = '\0';
            rightExpression = null;
            if (string.IsNullOrWhiteSpace(code)) return false;

            bool inString = false;
            bool escaped = false;
            char quote = '\0';
            int bracketDepth = 0;
            int operatorPosition = -1;
            char foundOperator = '\0';

            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) inString = false;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    continue;
                }

                if (c == '[')
                {
                    bracketDepth++;
                    continue;
                }
                if (c == ']')
                {
                    if (bracketDepth == 0) return false;
                    bracketDepth--;
                    continue;
                }

                if (bracketDepth != 0 || i + 1 >= code.Length || code[i + 1] != '=' ||
                    (c != '+' && c != '-' && c != '*' && c != '/' && c != '%'))
                    continue;

                if (operatorPosition >= 0)
                    return false; // Do not treat multiple statements/compound operators as one call.
                operatorPosition = i;
                foundOperator = c;
                i++;
            }

            if (inString || bracketDepth != 0 || operatorPosition < 0)
                return false;

            targetExpression = code.Substring(0, operatorPosition).Trim();
            rightExpression = code.Substring(operatorPosition + 2).Trim();
            operation = foundOperator;
            if (string.IsNullOrWhiteSpace(targetExpression) ||
                string.IsNullOrWhiteSpace(rightExpression) ||
                ContainsUnquotedSemicolon(rightExpression))
                return false;

            return true;
        }

        public static bool TryCreateAssignmentLines(string code, string temporaryPrefix,
            string sourcePath, int sourceLine, int blockStart, out List<Line> lines,
            char compoundOperation = '\0')
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
                rootName, targetIndices, valueName, sourcePath, sourceLine, state,
                compoundOperation));

            lines = result;
            return true;
        }

        private static bool TryParseListInsertCall(string code, out string rootName,
            out string indexExpression, out string valueExpression)
        {
            rootName = null;
            indexExpression = null;
            valueExpression = null;
            if (string.IsNullOrWhiteSpace(code)) return false;

            int cursor = 0;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;
            int nameStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            rootName = code.Substring(nameStart, cursor - nameStart);

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '.') return false;
            cursor++;
            SkipWhitespace(code, ref cursor);
            const string methodName = "insert";
            if (cursor + methodName.Length > code.Length ||
                string.Compare(code, cursor, methodName, 0, methodName.Length,
                    StringComparison.Ordinal) != 0)
                return false;
            cursor += methodName.Length;
            if (cursor < code.Length && IsIdentifierPart(code[cursor])) return false;

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '(') return false;
            int argumentsStart = ++cursor;
            int nestedParentheses = 0;
            int nestedBrackets = 0;
            bool inString = false;
            bool escaped = false;
            char quote = '\0';
            int comma = -1;
            int close = -1;

            while (cursor < code.Length)
            {
                char c = code[cursor];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) inString = false;
                    cursor++;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    cursor++;
                    continue;
                }

                if (c == '(') { nestedParentheses++; cursor++; continue; }
                if (c == ')')
                {
                    if (nestedParentheses == 0 && nestedBrackets == 0)
                    {
                        close = cursor;
                        break;
                    }
                    if (nestedParentheses == 0) return false;
                    nestedParentheses--;
                    cursor++;
                    continue;
                }

                if (c == '[') { nestedBrackets++; cursor++; continue; }
                if (c == ']')
                {
                    if (nestedBrackets == 0) return false;
                    nestedBrackets--;
                    cursor++;
                    continue;
                }

                if (c == ',' && nestedParentheses == 0 && nestedBrackets == 0)
                {
                    if (comma >= 0) return false;
                    comma = cursor;
                }
                cursor++;
            }

            if (close < 0 || inString || nestedParentheses != 0 || nestedBrackets != 0 ||
                comma < 0)
                return false;

            indexExpression = code.Substring(argumentsStart, comma - argumentsStart).Trim();
            valueExpression = code.Substring(comma + 1, close - comma - 1).Trim();
            if (string.IsNullOrWhiteSpace(indexExpression) ||
                string.IsNullOrWhiteSpace(valueExpression))
                return false;

            cursor = close + 1;
            SkipWhitespace(code, ref cursor);
            return cursor == code.Length;
        }

        private static bool TryParseListPopCall(string code, out string rootName,
            out string destinationName, out string indexExpression)
        {
            rootName = null;
            destinationName = null;
            indexExpression = null;
            if (string.IsNullOrWhiteSpace(code)) return false;

            int cursor = 0;
            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;

            int firstStart = cursor++;
            while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
            string firstName = code.Substring(firstStart, cursor - firstStart);
            int afterFirstName = cursor;
            SkipWhitespace(code, ref cursor);

            if (cursor < code.Length && code[cursor] == '=' &&
                (cursor + 1 >= code.Length || code[cursor + 1] != '='))
            {
                destinationName = firstName;
                cursor++;
                SkipWhitespace(code, ref cursor);
                if (cursor >= code.Length || !IsIdentifierStart(code[cursor])) return false;
                int rootStart = cursor++;
                while (cursor < code.Length && IsIdentifierPart(code[cursor])) cursor++;
                rootName = code.Substring(rootStart, cursor - rootStart);
            }
            else
            {
                rootName = firstName;
                cursor = afterFirstName;
            }

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '.') return false;
            cursor++;
            SkipWhitespace(code, ref cursor);
            const string methodName = "pop";
            if (cursor + methodName.Length > code.Length ||
                string.Compare(code, cursor, methodName, 0, methodName.Length,
                    StringComparison.Ordinal) != 0)
                return false;
            cursor += methodName.Length;
            if (cursor < code.Length && IsIdentifierPart(code[cursor])) return false;

            SkipWhitespace(code, ref cursor);
            if (cursor >= code.Length || code[cursor] != '(') return false;
            int argumentStart = ++cursor;
            int nestedParentheses = 0;
            int nestedBrackets = 0;
            bool inString = false;
            bool escaped = false;
            char quote = '\0';
            int close = -1;
            bool hasArgument = false;

            while (cursor < code.Length)
            {
                char c = code[cursor];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == quote) inString = false;
                    cursor++;
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                    cursor++;
                    continue;
                }
                if (c == '(') { nestedParentheses++; cursor++; continue; }
                if (c == ')')
                {
                    if (nestedParentheses == 0 && nestedBrackets == 0)
                    {
                        close = cursor;
                        break;
                    }
                    if (nestedParentheses == 0) return false;
                    nestedParentheses--;
                    cursor++;
                    continue;
                }
                if (c == '[') { nestedBrackets++; cursor++; continue; }
                if (c == ']')
                {
                    if (nestedBrackets == 0) return false;
                    nestedBrackets--;
                    cursor++;
                    continue;
                }
                if (c == ',' && nestedParentheses == 0 && nestedBrackets == 0)
                    return false; // pop accepts zero or one positional index in this subset.
                if (!char.IsWhiteSpace(c)) hasArgument = true;
                cursor++;
            }

            if (close < 0 || inString || nestedParentheses != 0 || nestedBrackets != 0)
                return false;

            if (hasArgument)
            {
                indexExpression = code.Substring(argumentStart, close - argumentStart).Trim();
                if (string.IsNullOrWhiteSpace(indexExpression)) return false;
            }

            cursor = close + 1;
            SkipWhitespace(code, ref cursor);
            return cursor == code.Length;
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

        private sealed class CollectionClearLine : Line, IApply
        {
            private readonly string _rootName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;

            public CollectionClearLine(string rootName, string sourcePath, int sourceLine)
            {
                _rootName = rootName;
                _sourcePath = sourcePath;
                _sourceLine = sourceLine;
            }

            public void Apply(IContext context)
            {
                try
                {
                    if (context == null)
                        throw new InvalidOperationException("no live script context was available");

                    if (!TryGetObjectBackedVariable(_rootName, context, out object collection))
                        throw new InvalidOperationException("variable '" + _rootName +
                            "' does not contain an object-backed list or dictionary");

                    if (collection is IList list)
                    {
                        list.Clear();
                    }
                    else if (collection is IDictionary dictionary)
                    {
                        dictionary.Clear();
                    }
                    else
                    {
                        throw new InvalidOperationException("clear() target is not a list or dictionary");
                    }

                    Log.Info("Cleared collection '" + _rootName + "' at " +
                        _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    Log.Warn("Could not clear collection '" + _rootName + "' at " +
                        _sourcePath + ":" + _sourceLine + ": " +
                        e.GetBaseException().Message);
                }
            }
        }

        private sealed class ListExtendLine : Line, IApply
        {
            private readonly string _rootName;
            private readonly string _sourceName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;

            public ListExtendLine(string rootName, string sourceName, string sourcePath, int sourceLine)
            {
                _rootName = rootName;
                _sourceName = sourceName;
                _sourcePath = sourcePath;
                _sourceLine = sourceLine;
            }

            public void Apply(IContext context)
            {
                try
                {
                    if (context == null)
                        throw new InvalidOperationException("no live script context was available");

                    if (!TryGetObjectBackedVariable(_rootName, context, out object target))
                        throw new InvalidOperationException("target variable '" + _rootName +
                            "' does not contain an object-backed list");

                    if (!(target is IList targetList))
                        throw new InvalidOperationException("list.extend() requires a list target");

                    if (!TryGetObjectBackedVariable(_sourceName, context, out object source))
                        throw new InvalidOperationException("source variable '" + _sourceName +
                            "' does not contain an object-backed list");

                    if (!(source is IList sourceList))
                        throw new InvalidOperationException("list.extend() argument must be a list");

                    // Copy before mutation so extending a list with itself is deterministic.
                    var itemsToAppend = new List<object>(sourceList.Count);
                    for (int i = 0; i < sourceList.Count; i++)
                        itemsToAppend.Add(sourceList[i]);

                    foreach (object item in itemsToAppend)
                        targetList.Add(item);

                    Log.Info("Applied list.extend on '" + _rootName + "' using '" +
                        _sourceName + "' at " + _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    Log.Warn("Could not apply list.extend on '" + _rootName + "' using '" +
                        _sourceName + "' at " + _sourcePath + ":" + _sourceLine + ": " +
                        e.GetBaseException().Message);
                }
            }
        }

        private sealed class DictionaryUpdateLine : Line, IApply
        {
            private readonly string _targetName;
            private readonly string _sourceName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;

            public DictionaryUpdateLine(string targetName, string sourceName,
                string sourcePath, int sourceLine)
            {
                _targetName = targetName;
                _sourceName = sourceName;
                _sourcePath = sourcePath;
                _sourceLine = sourceLine;
            }

            public void Apply(IContext context)
            {
                try
                {
                    if (context == null)
                        throw new InvalidOperationException("no live script context was available");
                    if (!TryGetObjectBackedVariable(_targetName, context, out object target) ||
                        !(target is IDictionary targetDictionary))
                        throw new InvalidOperationException("dict.update() requires a dictionary target");
                    if (targetDictionary.IsReadOnly || targetDictionary.IsFixedSize)
                        throw new InvalidOperationException("dict.update() target is not mutable");
                    if (!TryGetObjectBackedVariable(_sourceName, context, out object source) ||
                        !(source is IDictionary sourceDictionary))
                        throw new InvalidOperationException("dict.update() argument must be a dictionary");

                    // Snapshot entries first so self-update is safe and iteration remains stable.
                    var entries = new List<DictionaryEntry>();
                    foreach (DictionaryEntry entry in sourceDictionary)
                        entries.Add(entry);

                    foreach (DictionaryEntry entry in entries)
                        targetDictionary[entry.Key] = entry.Value;

                    Log.Info("Applied dict.update on '" + _targetName + "' using '" +
                        _sourceName + "' at " + _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    Log.Warn("Could not apply dict.update on '" + _targetName + "' using '" +
                        _sourceName + "' at " + _sourcePath + ":" + _sourceLine + ": " +
                        e.GetBaseException().Message);
                }
            }
        }

        private sealed class DictionaryPopLine : Line, IApply
        {
            private readonly string _rootName;
            private readonly string _destinationName;
            private readonly string _key;
            private readonly string _sourcePath;
            private readonly int _sourceLine;

            public DictionaryPopLine(string rootName, string destinationName, string key,
                string sourcePath, int sourceLine)
            {
                _rootName = rootName;
                _destinationName = destinationName;
                _key = key;
                _sourcePath = sourcePath;
                _sourceLine = sourceLine;
            }

            public void Apply(IContext context)
            {
                try
                {
                    if (context == null)
                        throw new InvalidOperationException("no live script context was available");
                    if (!TryGetObjectBackedVariable(_rootName, context, out object target) ||
                        !(target is IDictionary dictionary))
                        throw new InvalidOperationException("dict.pop() requires a dictionary target");
                    if (dictionary.IsReadOnly || dictionary.IsFixedSize)
                        throw new InvalidOperationException("dict.pop() target is not mutable");
                    if (!dictionary.Contains(_key))
                        throw new InvalidOperationException("dictionary key '" + _key + "' was not found");

                    object removed = dictionary[_key];
                    if (_destinationName != null &&
                        !GameBridge.TrySetVariableLiteral(_destinationName, removed, false, context))
                        throw new InvalidOperationException(
                            "could not assign the popped value to '" + _destinationName + "'");

                    dictionary.Remove(_key);
                    Log.Info("Applied dict.pop on '" + _rootName + "' for key '" + _key +
                        "' at " + _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    Log.Warn("Could not apply dict.pop on '" + _rootName + "' at " +
                        _sourcePath + ":" + _sourceLine + ": " + e.GetBaseException().Message);
                }
            }
        }

        private sealed class ListInsertLine : Line, IApply
        {
            private readonly string _rootName;
            private readonly string _indexName;
            private readonly string _valueName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;
            private readonly VariableInitializationState _state;

            public ListInsertLine(string rootName, string indexName, string valueName,
                string sourcePath, int sourceLine, VariableInitializationState state)
            {
                _rootName = rootName;
                _indexName = indexName;
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
                    if (!TryGetObjectBackedVariable(_rootName, context, out object target) ||
                        !(target is IList list))
                        throw new InvalidOperationException("list.insert() requires a list target");
                    if (list.IsReadOnly || list.IsFixedSize)
                        throw new InvalidOperationException("list.insert() target is not mutable");

                    if (!TryGetDynamicIndexValue(_indexName, context, out object rawIndex) ||
                        !TryConvertIntegerIndex(rawIndex, out int index))
                        throw new InvalidOperationException("insert() index must evaluate to an integer");
                    if (!TryGetDynamicIndexValue(_valueName, context, out object value))
                        throw new InvalidOperationException("insert() value must be a supported value");

                    // Match Python list.insert(): negative indexes count from the end and
                    // indexes outside the collection are clamped to the nearest valid position.
                    long normalized = index;
                    if (normalized < 0)
                        normalized = Math.Max(0L, (long)list.Count + normalized);
                    else
                        normalized = Math.Min(normalized, list.Count);

                    list.Insert((int)normalized, value);
                    Log.Info("Applied list.insert on '" + _rootName + "' at " +
                        _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    if (_state != null) _state.Failed = true;
                    Log.Warn("Could not apply list.insert on '" + _rootName + "' at " +
                        _sourcePath + ":" + _sourceLine + ": " + e.GetBaseException().Message);
                }
            }
        }

        private sealed class ListPopLine : Line, IApply
        {
            private readonly string _rootName;
            private readonly string _destinationName;
            private readonly string _indexName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;

            public ListPopLine(string rootName, string destinationName, string indexName,
                string sourcePath, int sourceLine)
            {
                _rootName = rootName;
                _destinationName = destinationName;
                _indexName = indexName;
                _sourcePath = sourcePath;
                _sourceLine = sourceLine;
            }

            public void Apply(IContext context)
            {
                try
                {
                    if (context == null)
                        throw new InvalidOperationException("no live script context was available");
                    if (!TryGetObjectBackedVariable(_rootName, context, out object target) ||
                        !(target is IList list))
                        throw new InvalidOperationException("list.pop() requires a list target");
                    if (list.IsReadOnly || list.IsFixedSize)
                        throw new InvalidOperationException("list.pop() target is not mutable");
                    if (list.Count == 0)
                        throw new InvalidOperationException("pop from an empty list");

                    int index = list.Count - 1;
                    if (_indexName != null)
                    {
                        if (!TryGetDynamicIndexValue(_indexName, context, out object rawIndex) ||
                            !TryConvertIntegerIndex(rawIndex, out index))
                            throw new InvalidOperationException("pop() index must evaluate to an integer");

                        if (index < 0) index = list.Count + index;
                        if (index < 0 || index >= list.Count)
                            throw new InvalidOperationException("pop() index out of range");
                    }

                    object removed = list[index];
                    if (_destinationName != null &&
                        !GameBridge.TrySetVariableLiteral(_destinationName, removed, false, context))
                        throw new InvalidOperationException(
                            "could not assign the popped value to '" + _destinationName + "'");

                    list.RemoveAt(index);
                    Log.Info("Applied list.pop on '" + _rootName + "' at " +
                        _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    Log.Warn("Could not apply list.pop on '" + _rootName + "' at " +
                        _sourcePath + ":" + _sourceLine + ": " + e.GetBaseException().Message);
                }
            }
        }

        private sealed class ListMethodMutationLine : Line, IApply
        {
            private readonly string _rootName;
            private readonly string _methodName;
            private readonly string _valueName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;
            private readonly VariableInitializationState _state;

            public ListMethodMutationLine(string rootName, string methodName, string valueName,
                string sourcePath, int sourceLine, VariableInitializationState state)
            {
                _rootName = rootName;
                _methodName = methodName;
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
                            "' does not contain an object-backed list");

                    if (!(current is IList list))
                        throw new InvalidOperationException("list." + _methodName +
                            " requires a list target");

                    if (!TryGetDynamicIndexValue(_valueName, context, out object value))
                        throw new InvalidOperationException("method argument must be a supported scalar");

                    if (_methodName == "append")
                    {
                        list.Add(value);
                    }
                    else if (_methodName == "remove")
                    {
                        int foundIndex = -1;
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (ScalarValuesEqual(list[i], value))
                            {
                                foundIndex = i;
                                break;
                            }
                        }

                        if (foundIndex < 0)
                            throw new InvalidOperationException(
                                "remove(value) could not find a matching list item");

                        list.RemoveAt(foundIndex);
                    }
                    else
                    {
                        throw new InvalidOperationException("unsupported list method '" +
                            _methodName + "'");
                    }

                    Log.Info("Applied list." + _methodName + " on '" + _rootName +
                        "' at " + _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    if (_state != null) _state.Failed = true;
                    Log.Warn("Could not apply list." + _methodName + " on '" + _rootName +
                        "' at " + _sourcePath + ":" + _sourceLine + ": " +
                        e.GetBaseException().Message);
                }
            }
        }

        private sealed class CollectionLengthLine : Line, IApply
        {
            private readonly string _collectionName;
            private readonly string _resultName;
            private readonly string _gateName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;
            private readonly VariableInitializationState _state;

            public CollectionLengthLine(string collectionName, string resultName,
                string gateName, string sourcePath, int sourceLine,
                VariableInitializationState state)
            {
                _collectionName = collectionName;
                _resultName = resultName;
                _gateName = gateName;
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

                    if (!TryGetObjectBackedVariable(_collectionName, context, out object collection))
                        throw new InvalidOperationException("variable '" + _collectionName +
                            "' does not contain an object-backed list or dictionary");

                    int count;
                    if (collection is IList list)
                    {
                        count = list.Count;
                    }
                    else if (collection is IDictionary dictionary)
                    {
                        count = dictionary.Count;
                    }
                    else
                    {
                        throw new InvalidOperationException("len() target is not a list or dictionary");
                    }

                    if (!GameBridge.TrySetVariableLiteral(_resultName, count, false, context))
                        throw new InvalidOperationException("could not write the collection length");

                    Log.Info("Evaluated len(" + _collectionName + ") at " +
                        _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    if (_state != null) _state.Failed = true;
                    GameBridge.TrySetVariableLiteral(_resultName, 0, false, context);
                    GameBridge.TrySetVariableLiteral(_gateName, false, false, context);
                    Log.Warn("Could not evaluate len(" + _collectionName + ") at " +
                        _sourcePath + ":" + _sourceLine + ": " +
                        e.GetBaseException().Message);
                }
            }
        }

        private sealed class CollectionMembershipLine : Line, IApply
        {
            private readonly string _collectionName;
            private readonly string _valueName;
            private readonly string _resultName;
            private readonly string _gateName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;
            private readonly VariableInitializationState _state;

            public CollectionMembershipLine(string collectionName, string valueName,
                string resultName, string gateName, string sourcePath, int sourceLine,
                VariableInitializationState state)
            {
                _collectionName = collectionName;
                _valueName = valueName;
                _resultName = resultName;
                _gateName = gateName;
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

                    if (!TryGetObjectBackedVariable(_collectionName, context, out object collection))
                        throw new InvalidOperationException("variable '" + _collectionName +
                            "' does not contain an object-backed list or dictionary");

                    if (!TryGetDynamicIndexValue(_valueName, context, out object value))
                        throw new InvalidOperationException("membership operand must be a supported scalar");

                    bool contains = false;
                    if (collection is IList list)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (ScalarValuesEqual(list[i], value))
                            {
                                contains = true;
                                break;
                            }
                        }
                    }
                    else if (collection is IDictionary dictionary)
                    {
                        // StoryAPI dictionary literals use string keys. A non-string
                        // operand simply cannot match one of those keys.
                        contains = value is string && dictionary.Contains(value);
                    }
                    else
                    {
                        throw new InvalidOperationException("membership target is not a list or dictionary");
                    }

                    if (!GameBridge.TrySetVariableLiteral(_resultName, contains, false, context))
                        throw new InvalidOperationException("could not write the membership result");

                    Log.Info("Evaluated membership in '" + _collectionName + "' at " +
                        _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    if (_state != null) _state.Failed = true;
                    GameBridge.TrySetVariableLiteral(_resultName, false, false, context);
                    GameBridge.TrySetVariableLiteral(_gateName, false, false, context);
                    Log.Warn("Could not evaluate membership in '" + _collectionName + "' at " +
                        _sourcePath + ":" + _sourceLine + ": " +
                        e.GetBaseException().Message);
                }
            }
        }

        private static bool ScalarValuesEqual(object left, object right)
        {
            if (left == null || right == null) return left == right;

            if (left is string || right is string)
                return left is string && right is string &&
                    string.Equals((string)left, (string)right, StringComparison.Ordinal);

            if (left is bool || right is bool)
                return left is bool && right is bool && (bool)left == (bool)right;

            if (IsNumericScalar(left) && IsNumericScalar(right))
            {
                try
                {
                    return Convert.ToDouble(left, CultureInfo.InvariantCulture) ==
                        Convert.ToDouble(right, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return false;
                }
            }

            return object.Equals(left, right);
        }

        private static bool IsNumericScalar(object value)
        {
            return value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong ||
                value is float || value is double || value is decimal;
        }

        private static float CalculatePythonModulo(object left, object right)
        {
            if (!IsNumericScalar(left) || !IsNumericScalar(right))
                throw new InvalidOperationException("modulo operands must be numeric scalars");

            double dividend = Convert.ToDouble(left, CultureInfo.InvariantCulture);
            double divisor = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            if (double.IsNaN(dividend) || double.IsInfinity(dividend) ||
                double.IsNaN(divisor) || double.IsInfinity(divisor))
                throw new InvalidOperationException("modulo operands must be finite numbers");
            if (divisor == 0d)
                throw new DivideByZeroException("modulo by zero");

            double remainder = dividend % divisor;
            // Python/Ren'Py modulo follows the sign of the divisor; C# remainder follows
            // the dividend, so adjust non-zero results when their signs differ.
            if (remainder != 0d && ((remainder < 0d) != (divisor < 0d)))
                remainder += divisor;
            return (float)remainder;
        }

        private sealed class ScalarModuloAssignmentLine : Line, IApply
        {
            private readonly string _targetName;
            private readonly string _rightValueName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;

            public ScalarModuloAssignmentLine(string targetName, string rightValueName,
                string sourcePath, int sourceLine)
            {
                _targetName = targetName;
                _rightValueName = rightValueName;
                _sourcePath = sourcePath;
                _sourceLine = sourceLine;
            }

            public void Apply(IContext context)
            {
                try
                {
                    if (!TryGetDynamicIndexValue(_targetName, context, out object left))
                        throw new InvalidOperationException("target must be a supported numeric scalar");
                    if (!TryGetDynamicIndexValue(_rightValueName, context, out object right))
                        throw new InvalidOperationException("right-hand value must be a supported numeric scalar");

                    float result = CalculatePythonModulo(left, right);
                    if (!GameBridge.TrySetVariableLiteral(_targetName, result, false, context))
                        throw new InvalidOperationException("could not write the modulo result");

                    Log.Info("Applied scalar %= assignment to '" + _targetName + "' at " +
                        _sourcePath + ":" + _sourceLine + ".");
                }
                catch (Exception e)
                {
                    Log.Warn("Could not apply scalar %= assignment to '" + _targetName +
                        "' at " + _sourcePath + ":" + _sourceLine + ": " +
                        e.GetBaseException().Message);
                }
            }
        }

        private sealed class CollectionMutationLine : Line, IApply
        {
            private readonly string _rootName;
            private readonly List<object> _indices;
            private readonly string _valueName;
            private readonly string _sourcePath;
            private readonly int _sourceLine;
            private readonly VariableInitializationState _state;
            private readonly char _compoundOperation;

            public CollectionMutationLine(string rootName, List<object> indices,
                string valueName, string sourcePath, int sourceLine,
                VariableInitializationState state, char compoundOperation = '\0')
            {
                _rootName = rootName;
                _indices = indices;
                _valueName = valueName;
                _sourcePath = sourcePath;
                _sourceLine = sourceLine;
                _state = state;
                _compoundOperation = compoundOperation;
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
                    if (_compoundOperation == '%')
                        value = CalculatePythonModulo(ReadContainerItem(current, finalKey), value);

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

        private static bool TryConvertIntegerIndex(object value, out int index)
        {
            index = 0;
            if (value == null || value is bool) return false;

            try
            {
                decimal numeric = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (numeric != decimal.Truncate(numeric) ||
                    numeric < int.MinValue || numeric > int.MaxValue)
                    return false;

                index = decimal.ToInt32(numeric);
                return true;
            }
            catch
            {
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

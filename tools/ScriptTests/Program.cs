using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using StoryAPI.Scripting;
using StoryAPI.Runtime;
using StoryAPI.Game;

namespace StoryAPI.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main()
        {
            Run("dialogue and narration", TestDialogueAndNarration);
            Run("digit-letter sprite attribute", TestDigitLetterSpriteAttribute);
            Run("sprite alias and position", TestSpriteAliasAndPosition);
            Run("define/default scalar declarations", TestVariableDeclarations);
            Run("runtime scalar initializer expressions", TestRuntimeExpressionDeclarations);
            Run("container index expressions", TestContainerIndexExpressions);
            Run("runtime index expression rewriting", TestRuntimeIndexExpressionRewriting);
            Run("dynamic index scalar value extraction", TestDynamicIndexScalarValueExtraction);
            Run("re-evaluate indexed reads on repeated execution", TestRepeatedIndexedTemporaryReads);
            Run("indexed collection assignment", TestIndexedCollectionAssignments);
            Run("list append and remove methods", TestListMethodMutations);
            Run("indexed collection conditions", TestIndexedCollectionConditions);
            Run("list and dictionary membership conditions", TestCollectionMembershipConditions);
            Run("list and dictionary length conditions", TestCollectionLengthConditions);
            Run("None comparison conditions", TestNoneComparisonConditions);
            Run("list and dictionary clear methods", TestCollectionClearMethods);
            Run("list reverse method", TestListReverseMethod);
            Run("list count method", TestListCountMethod);
            Run("list extend method", TestListExtendMethod);
            Run("list insert and pop methods", TestListInsertAndPopMethods);
            Run("dictionary update and pop methods", TestDictionaryUpdateAndPopMethods);
            Run("dictionary pop default argument", TestDictionaryPopDefaultArgument);
            Run("dictionary get method", TestDictionaryGetMethod);
            Run("dictionary setdefault method", TestDictionarySetDefaultMethod);
            Run("dictionary copy method", TestDictionaryCopyMethod);
            Run("compound scalar assignments", TestCompoundScalarAssignments);
            Run("compound indexed collection assignments", TestIndexedCompoundAssignments);
            Run("if/elif/else branches", TestConditionalBranches);
            Run("menu choices and nested statements", TestMenuChoices);
            Run("while and loop-control statements", TestWhileAndLoopControl);
            Run("for-list iteration and loop control", TestForListIteration);
            Run("for-dictionary key iteration", TestForDictionaryIteration);
            Run("for-dictionary key/value iteration", TestForDictionaryItemsIteration);
            Run("for-dictionary values iteration", TestForDictionaryValuesIteration);
            Run("for-dictionary keys() iteration", TestForDictionaryKeysMethodIteration);
            Run("range() for-loop arguments", TestForRangeIteration);
            Run("image/audio asset paths", TestAssetPaths);
            Run("comments and blank lines", TestCommentsAndBlankLines);
            Run("reject malformed scripts", TestMalformedScripts);
            Run("parse every checked-in example script", TestExampleScripts);

            Console.WriteLine();
            Console.WriteLine("StoryAPI parser regression tests: " + _passed +
                " passed, " + _failed + " failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception e)
            {
                _failed++;
                Console.Error.WriteLine("[FAIL] " + name + ": " + e.Message);
            }
        }

        private static List<Statement> Parse(string source, string path = "regression-test.rpy")
        {
            return new Parser(source, path).ParseFile();
        }

        private static T Require<T>(object value, string message) where T : class
        {
            T result = value as T;
            if (result == null)
                throw new InvalidOperationException(message + " (actual: " +
                    (value == null ? "null" : value.GetType().Name) + ")");
            return result;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void ExpectSyntaxError(string source, string messagePart)
        {
            try
            {
                Parse(source);
            }
            catch (ScriptSyntaxException e)
            {
                Check(e.Line > 0, "Syntax error should include a positive source line.");
                Check(e.Message.IndexOf(messagePart, StringComparison.OrdinalIgnoreCase) >= 0,
                    "Expected error containing '" + messagePart + "', got: " + e.Message);
                return;
            }

            throw new InvalidOperationException("Expected ScriptSyntaxException containing '" +
                messagePart + "', but parsing succeeded.");
        }

        private static void TestDialogueAndNarration()
        {
            var statements = Parse(
                "label start:\n" +
                "    monika \"Hello.\"\n" +
                "    _ \"Narration.\"\n" +
                "    return\n");

            var label = Require<LabelStatement>(statements[0], "First statement should be label.");
            Check(label.Name == "start", "Label name should be start.");
            Check(label.Body.Count == 3, "Label should contain three statements.");

            var dialogue = Require<SayStatement>(label.Body[0], "First body item should be dialogue.");
            Check(dialogue.Speaker == "monika" && dialogue.Text == "Hello.",
                "Speaker dialogue was not preserved.");

            var narration = Require<SayStatement>(label.Body[1], "Second body item should be narration.");
            Check(narration.Speaker == string.Empty && narration.Text == "Narration.",
                "Narration speaker/text was not preserved.");
            Require<ReturnStatement>(label.Body[2], "Final statement should be return.");
        }

        private static void TestDigitLetterSpriteAttribute()
        {
            var statements = Parse(
                "label sprite_test:\n" +
                "    show monika 2a at left\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0], "Expected label.");
            var show = Require<ShowStatement>(label.Body[0], "Expected show statement.");

            Check(show.Image == "monika", "Image name should be monika.");
            Check(show.Variant == "2a", "The adjacent digit-letter attribute should be 2a.");
            Check(show.At == "left", "Position should be left.");
        }

        private static void TestSpriteAliasAndPosition()
        {
            var statements = Parse(
                "label sprite_alias:\n" +
                "    show s_kill as the_kill at center\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0], "Expected label.");
            var show = Require<ShowStatement>(label.Body[0], "Expected show statement.");

            Check(show.Image == "s_kill", "Source image should be s_kill.");
            Check(show.Alias == "the_kill", "Alias should be retained.");
            Check(show.At == "center", "Position should be center.");
        }

        private static void TestVariableDeclarations()
        {
            var statements = Parse(
                "define starting_points = 3\n" +
                "default points = 0\n" +
                "default has_key = False\n" +
                "define monokuma = Character(\"Monokuma\")\n" +
                "label variables:\n" +
                "    return\n");

            var defined = Require<VariableDeclarationStatement>(statements[0], "Expected define declaration.");
            Check(defined.Name == "starting_points" && !defined.IsDefault,
                "define name/type mismatch.");
            Check(defined.HasLiteral && defined.LiteralValue is int &&
                (int)defined.LiteralValue == 3, "Integer define literal should be parsed.");

            var defaultPoints = Require<VariableDeclarationStatement>(statements[1], "Expected numeric default.");
            Check(defaultPoints.Name == "points" && defaultPoints.IsDefault &&
                defaultPoints.HasLiteral && (int)defaultPoints.LiteralValue == 0,
                "Numeric default declaration should be parsed.");

            var defaultFlag = Require<VariableDeclarationStatement>(statements[2], "Expected bool default.");
            Check(defaultFlag.Name == "has_key" && defaultFlag.IsDefault &&
                defaultFlag.HasLiteral && defaultFlag.LiteralValue is bool &&
                !(bool)defaultFlag.LiteralValue, "Boolean default declaration should be false.");

            var character = Require<CharacterStatement>(statements[3],
                "define tag = Character(\"Name\") should map to a character declaration.");
            Check(character.Tag == "monokuma" && character.DisplayName == "Monokuma",
                "Character display name/tag mismatch.");
        }

        private static void TestRuntimeExpressionDeclarations()
        {
            var statements = Parse(
                "define calculated = (base_points + 2) * 3\n" +
                "default available = has_key and points >= 3\n" +
                "default optional = None\n" +
                "define list_value = [1, 2, {\"nested\": True}]\n" +
                "define dictionary_value = {\"answer\": 42, \"flags\": [True, False]}\n" +
                "define empty_list = []\n" +
                "define empty_dictionary = {}\n" +
                "define tuple_value = (1, 2)\n" +
                "define call_value = random()\n" +
                "label expression_variables:\n" +
                "    return\n");

            var calculated = Require<VariableDeclarationStatement>(statements[0],
                "Expected arithmetic define.");
            Check(!calculated.HasLiteral && calculated.HasRuntimeExpression,
                "Arithmetic expressions should be deferred to the runtime evaluator.");
            Check(calculated.Expression == "(base_points + 2) * 3",
                "The arithmetic expression should be preserved exactly.");

            var available = Require<VariableDeclarationStatement>(statements[1],
                "Expected logical default.");
            Check(available.IsDefault && available.HasRuntimeExpression &&
                available.Expression == "has_key and points >= 3",
                "Logical expressions should be preserved as runtime defaults.");

            var optional = Require<VariableDeclarationStatement>(statements[2],
                "Expected None initializer.");
            Check(optional.HasRuntimeExpression && optional.Expression == "None",
                "None should be delegated to the native expression evaluator.");

            var list = Require<VariableDeclarationStatement>(statements[3],
                "Expected list declaration.");
            Check(list.HasLiteral && list.LiteralValue is List<object>,
                "List literal should be parsed as an object-backed literal value.");
            var listItems = (List<object>)list.LiteralValue;
            Check(listItems.Count == 3 && (int)listItems[0] == 1 &&
                listItems[2] is Dictionary<string, object>,
                "List values and nested dictionary literals should be retained.");

            var dictionary = Require<VariableDeclarationStatement>(statements[4],
                "Expected dictionary declaration.");
            Check(dictionary.HasLiteral && dictionary.LiteralValue is Dictionary<string, object>,
                "Dictionary literal should be parsed as an object-backed literal value.");
            var dictionaryItems = (Dictionary<string, object>)dictionary.LiteralValue;
            Check(dictionaryItems.Count == 2 && (int)dictionaryItems["answer"] == 42 &&
                dictionaryItems["flags"] is List<object>,
                "Dictionary values and nested list literals should be retained.");

            var emptyList = Require<VariableDeclarationStatement>(statements[5],
                "Expected empty list declaration.");
            Check(emptyList.HasLiteral && ((List<object>)emptyList.LiteralValue).Count == 0,
                "Empty list literals should be supported.");

            var emptyDictionary = Require<VariableDeclarationStatement>(statements[6],
                "Expected empty dictionary declaration.");
            Check(emptyDictionary.HasLiteral &&
                ((Dictionary<string, object>)emptyDictionary.LiteralValue).Count == 0,
                "Empty dictionary literals should be supported.");

            var tuple = Require<VariableDeclarationStatement>(statements[7],
                "Expected tuple declaration.");
            Check(!tuple.HasLiteral && !tuple.HasRuntimeExpression,
                "Tuple literals should remain unsupported.");

            var function = Require<VariableDeclarationStatement>(statements[8],
                "Expected function-call declaration.");
            Check(!function.HasLiteral && !function.HasRuntimeExpression,
                "Function calls should not be automatically accepted as scalar expressions.");
        }

        private static void TestContainerIndexExpressions()
        {
            var statements = Parse(
                "define first_item = starting_points[0]\n" +
                "define dictionary_item = starting_points[\"count\"]\n" +
                "define nested_item = starting_points[2][\"nested\"]\n" +
                "define calculated_item = starting_points[index_value + 1] * 2\n" +
                "define bracket_key = dictionary_value[\"]\"]\n" +
                "define bracket_string = label_text == \"[\"\n" +
                "define empty_index = starting_points[]\n" +
                "define call_index = starting_points[random()]\n" +
                "define call_value = random()\n" +
                "default default_first = starting_points[0]\n" +
                "define unclosed_index = starting_points[0\n" +
                "define extra_closing_index = starting_points[0]]\n" +
                "define multiplied_item = starting_points[0] * 2\n" +
                "define summed_items = starting_points[0] + starting_points[1]\n" +
                "default combined_condition = starting_points[0] >= 1 and has_key\n" +
                "label container_index_test:\n" +
                "    return\n");

            var first = Require<VariableDeclarationStatement>(statements[0],
                "Expected a list index initializer.");
            Check(first.HasRuntimeExpression && first.Expression == "starting_points[0]",
                "List index reads should be delegated to the runtime evaluator.");

            var dictionary = Require<VariableDeclarationStatement>(statements[1],
                "Expected a dictionary index initializer.");
            Check(dictionary.HasRuntimeExpression &&
                dictionary.Expression == "starting_points[\"count\"]",
                "Dictionary string-key reads should be delegated to the runtime evaluator.");

            var nested = Require<VariableDeclarationStatement>(statements[2],
                "Expected a nested container index initializer.");
            Check(nested.HasRuntimeExpression &&
                nested.Expression == "starting_points[2][\"nested\"]",
                "Chained list/dictionary index reads should be supported.");

            var calculated = Require<VariableDeclarationStatement>(statements[3],
                "Expected an index expression with arithmetic.");
            Check(calculated.HasRuntimeExpression &&
                calculated.Expression == "starting_points[index_value + 1] * 2",
                "The parser should preserve dynamic index expressions for restricted runtime evaluation.");

            var bracketKey = Require<VariableDeclarationStatement>(statements[4],
                "Expected a dictionary key containing a closing bracket.");
            Check(bracketKey.HasRuntimeExpression &&
                bracketKey.Expression == "dictionary_value[\"]\"]",
                "Bracket characters inside quoted dictionary keys must remain string content.");

            var bracketString = Require<VariableDeclarationStatement>(statements[5],
                "Expected a string comparison containing an opening bracket.");
            Check(bracketString.HasRuntimeExpression &&
                bracketString.Expression == "label_text == \"[\"",
                "Bracket characters inside string literals must not affect subscription parsing.");

            var empty = Require<VariableDeclarationStatement>(statements[6],
                "Expected empty-index declaration.");
            Check(!empty.HasLiteral && !empty.HasRuntimeExpression,
                "Empty indexes should not be accepted as valid runtime expressions.");

            var callIndex = Require<VariableDeclarationStatement>(statements[7],
                "Expected call-index declaration.");
            Check(!callIndex.HasLiteral && !callIndex.HasRuntimeExpression,
                "Function calls inside indexes should remain unsupported.");

            var call = Require<VariableDeclarationStatement>(statements[8],
                "Expected function-call declaration.");
            Check(!call.HasLiteral && !call.HasRuntimeExpression,
                "Function calls should remain unsupported.");

            var defaultFirst = Require<VariableDeclarationStatement>(statements[9],
                "Expected a default declaration using a list index.");
            Check(defaultFirst.IsDefault && defaultFirst.HasRuntimeExpression,
                "Default declarations should support the same restricted index-read expressions.");

            var unclosed = Require<VariableDeclarationStatement>(statements[10],
                "Expected an unclosed-index declaration.");
            Check(!unclosed.HasLiteral && !unclosed.HasRuntimeExpression,
                "Unclosed indexes should remain unsupported.");

            var extraClosing = Require<VariableDeclarationStatement>(statements[11],
                "Expected a declaration with an unmatched closing bracket.");
            Check(!extraClosing.HasLiteral && !extraClosing.HasRuntimeExpression,
                "Unmatched closing brackets should remain unsupported.");

            var multiplied = Require<VariableDeclarationStatement>(statements[12],
                "Expected arithmetic applied to an indexed list value.");
            Check(multiplied.HasRuntimeExpression &&
                multiplied.Expression == "starting_points[0] * 2",
                "Index reads should be valid operands in scalar multiplication.");

            var summed = Require<VariableDeclarationStatement>(statements[13],
                "Expected an expression adding two indexed values.");
            Check(summed.HasRuntimeExpression &&
                summed.Expression == "starting_points[0] + starting_points[1]",
                "Multiple index reads should be retained in one scalar expression.");

            var combinedCondition = Require<VariableDeclarationStatement>(statements[14],
                "Expected a logical expression with an indexed comparison.");
            Check(combinedCondition.IsDefault && combinedCondition.HasRuntimeExpression &&
                combinedCondition.Expression == "starting_points[0] >= 1 and has_key",
                "Index reads should be valid operands in scalar comparisons and logical expressions.");
        }

        private static void TestRuntimeIndexExpressionRewriting()
        {
            Check(VariableIndexExpressionLine.CanHandle("starting_points[0]"),
                "A pure list index chain should use the direct runtime reader.");

            string rewritten;
            List<KeyValuePair<string, string>> reads;
            Check(VariableIndexExpressionLine.TryRewriteExpression(
                    "starting_points[0] * 2", "_storyapi_test",
                    out rewritten, out reads),
                "An indexed value multiplied by a scalar should be rewriteable.");
            Check(rewritten == "_storyapi_test_index_0 * 2",
                "The index read should be replaced with a temporary variable.");
            Check(reads.Count == 1 &&
                reads[0].Key == "_storyapi_test_index_0" &&
                reads[0].Value == "starting_points[0]",
                "The rewriter should preserve the exact source chain for runtime reading.");

            Check(VariableIndexExpressionLine.TryRewriteExpression(
                    "starting_points[0] + settings[\"count\"]",
                    "_storyapi_test", out rewritten, out reads),
                "Two index reads in one expression should both be rewriteable.");
            Check(rewritten ==
                    "_storyapi_test_index_0 + _storyapi_test_index_1" &&
                reads.Count == 2 &&
                reads[1].Value == "settings[\"count\"]",
                "Multiple index reads should create distinct temporary variables.");

            Check(VariableIndexExpressionLine.TryRewriteExpression(
                    "settings[\"count\"] >= 2 and has_key",
                    "_storyapi_test", out rewritten, out reads) &&
                rewritten == "_storyapi_test_index_0 >= 2 and has_key",
                "Index reads should be rewriteable inside comparisons and logical expressions.");

            Check(VariableIndexExpressionLine.TryRewriteExpression(
                    "starting_points[0] + settings[\"]\"]",
                    "_storyapi_test", out rewritten, out reads) &&
                reads.Count == 2 &&
                reads[1].Value == "settings[\"]\"]",
                "A closing bracket inside a quoted key must not end the index early.");

            List<KeyValuePair<string, string>> dynamicIndexes;
            Check(VariableIndexExpressionLine.TryRewriteExpression(
                    "items[index_value + 1] * 2",
                    "_storyapi_test", out rewritten, out reads, out dynamicIndexes),
                "A scalar arithmetic expression should be supported as a dynamic list index.");
            Check(rewritten == "_storyapi_test_index_0 * 2" &&
                reads.Count == 1 &&
                reads[0].Value == "items[_storyapi_test_key_0]" &&
                dynamicIndexes.Count == 1 &&
                dynamicIndexes[0].Key == "_storyapi_test_key_0" &&
                dynamicIndexes[0].Value == "index_value + 1",
                "The rewriter should schedule dynamic index calculation before collection lookup.");

            Check(!VariableIndexExpressionLine.CanHandle("items[index_value + 1]"),
                "Dynamic index expressions must use the rewrite path, not the literal-only reader.");

            VariableIndexExpressionLine dynamicTemporary;
            Check(VariableIndexExpressionLine.TryCreateTemporary(
                    reads[0].Key, reads[0].Value, "dynamic-index-test.rpy", 1,
                    new VariableInitializationState(), "_storyapi_test_gate", "selected_item",
                    out dynamicTemporary),
                "A rewritten collection chain with a generated dynamic-key placeholder should be constructible.");

            Check(VariableIndexExpressionLine.TryRewriteExpression(
                    "settings[key_name]",
                    "_storyapi_test", out rewritten, out reads, out dynamicIndexes) &&
                reads[0].Value == "settings[_storyapi_test_key_0]" &&
                dynamicIndexes.Count == 1 &&
                dynamicIndexes[0].Value == "key_name",
                "A variable string key should be represented as a dynamic dictionary index.");

            Check(VariableIndexExpressionLine.TryRewriteExpression(
                    "items[first_index] + items[second_index + 1]",
                    "_storyapi_test", out rewritten, out reads, out dynamicIndexes) &&
                reads.Count == 2 &&
                reads[0].Value == "items[_storyapi_test_key_0]" &&
                reads[1].Value == "items[_storyapi_test_key_1]" &&
                dynamicIndexes.Count == 2 &&
                dynamicIndexes[0].Key == "_storyapi_test_key_0" &&
                dynamicIndexes[1].Key == "_storyapi_test_key_1",
                "Dynamic index temporaries must be unique across multiple collection reads.");

            Check(!VariableIndexExpressionLine.TryRewriteExpression(
                    "items[random()] * 2",
                    "_storyapi_test", out rewritten, out reads, out dynamicIndexes),
                "Function calls inside a dynamic index should remain unsupported.");

            Check(!VariableIndexExpressionLine.TryRewriteExpression(
                    "label_text == \"[\"",
                    "_storyapi_test", out rewritten, out reads),
                "Bracket characters inside strings must not be treated as index reads.");
        }

        private static void TestDynamicIndexScalarValueExtraction()
        {
            MethodInfo reader = typeof(VariableIndexExpressionLine).GetMethod(
                "TryGetDynamicIndexValue", BindingFlags.NonPublic | BindingFlags.Static);
            Check(reader != null, "The dynamic scalar reader should exist.");

            var context = new RenpyParser.DynamicIndexTestContext();
            context.SetVariable("one", new SimpleExpressionEngine.DataValue(1f));
            context.SetVariable("zero", new SimpleExpressionEngine.DataValue(0f));
            context.SetVariable("key", new SimpleExpressionEngine.DataValue("answer"));

            object[] arguments = { "one", context, null };
            bool success = (bool)reader.Invoke(null, arguments);
            Check(success && arguments[2] is float && (float)arguments[2] == 1f,
                "A numeric DataValue must read floatVal even when its inactive stringVal is empty.");

            arguments = new object[] { "zero", context, null };
            success = (bool)reader.Invoke(null, arguments);
            Check(success && arguments[2] is float && (float)arguments[2] == 0f,
                "Numeric zero must not be mistaken for an empty string.");

            arguments = new object[] { "key", context, null };
            success = (bool)reader.Invoke(null, arguments);
            Check(success && arguments[2] is string && (string)arguments[2] == "answer",
                "A string DataValue should read stringVal using the discriminator.");
        }

        private static void TestIndexedCompoundAssignments()
        {
            const string sourcePath = "indexed-compound-assignment-test.rpy";
            const string listPrefix = "_storyapi_indexed_compound_list";
            var context = new RenpyParser.DynamicIndexTestContext();
            var inventory = new List<object> { 10f, 20f };
            context.SetVariable("inventory", new SimpleExpressionEngine.DataValue((object)inventory));

            List<RenPyParser.Line> lines;
            Check(VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory[0] += 2", listPrefix, sourcePath, 10, 100, out lines),
                "A list index += assignment should be recognized.");
            Check(lines.Last().GetType().Name == "CollectionMutationLine",
                "Indexed compound assignment should reuse the guarded collection mutation.");
            Check(lines.OfType<RenpyParser.RenpyOneLinePython>().Any(x =>
                    x.Code == "$ " + listPrefix + "_value = " +
                        listPrefix + "_rhs_index_0 + (2)"),
                "The list compound assignment should add the RHS to the current indexed value.");
            context.SetVariable(listPrefix + "_value", new SimpleExpressionEngine.DataValue(12f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(Convert.ToSingle(inventory[0]) == 12f,
                "The indexed += assignment should write the computed value to the list.");

            const string subtractPrefix = "_storyapi_indexed_compound_subtract";
            Check(VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory[1] -= 3", subtractPrefix, sourcePath, 11, 110, out lines),
                "A list index -= assignment should be recognized.");
            Check(lines.OfType<RenpyParser.RenpyOneLinePython>().Any(x =>
                    x.Code == "$ " + subtractPrefix + "_value = " +
                        subtractPrefix + "_rhs_index_0 - (3)"),
                "The list compound assignment should subtract the RHS.");
            context.SetVariable(subtractPrefix + "_value", new SimpleExpressionEngine.DataValue(17f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(Convert.ToSingle(inventory[1]) == 17f,
                "The indexed -= assignment should update the selected list element.");

            const string multiplyPrefix = "_storyapi_indexed_compound_multiply";
            Check(VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory[0] *= 2", multiplyPrefix, sourcePath, 12, 120, out lines),
                "A list index *= assignment should be recognized.");
            Check(lines.OfType<RenpyParser.RenpyOneLinePython>().Any(x =>
                    x.Code == "$ " + multiplyPrefix + "_value = " +
                        multiplyPrefix + "_rhs_index_0 * (2)"),
                "The list compound assignment should multiply the current value.");
            context.SetVariable(multiplyPrefix + "_value", new SimpleExpressionEngine.DataValue(24f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(Convert.ToSingle(inventory[0]) == 24f,
                "The indexed *= assignment should write the computed value to the list.");

            const string dictionaryPrefix = "_storyapi_indexed_compound_dictionary";
            var settings = new Dictionary<string, object> { { "count", 5f } };
            context.SetVariable("settings", new SimpleExpressionEngine.DataValue((object)settings));
            Check(VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "settings[\"count\"] += inventory[0]", dictionaryPrefix, sourcePath, 13, 130,
                    out lines),
                "A dictionary value may use += with an indexed collection read on the RHS.");
            Check(lines.OfType<RenpyParser.RenpyOneLinePython>().Any(x =>
                    x.Code == "$ " + dictionaryPrefix + "_value = " +
                        dictionaryPrefix + "_rhs_index_0 + (" +
                        dictionaryPrefix + "_rhs_index_1)"),
                "The dictionary target value and RHS collection read should both be resolved.");
            context.SetVariable(dictionaryPrefix + "_value", new SimpleExpressionEngine.DataValue(29f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(Convert.ToSingle(settings["count"]) == 29f,
                "The dictionary compound assignment should write the computed value.");

            const string dividePrefix = "_storyapi_indexed_compound_divide";
            Check(VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory[0] /= 4", dividePrefix, sourcePath, 14, 140, out lines),
                "A list index /= assignment should be recognized.");
            Check(lines.OfType<RenpyParser.RenpyOneLinePython>().Any(x =>
                    x.Code == "$ " + dividePrefix + "_value = " +
                        dividePrefix + "_rhs_index_0 / (4)"),
                "The indexed compound assignment should divide the current value.");
            context.SetVariable(dividePrefix + "_value", new SimpleExpressionEngine.DataValue(6f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(Convert.ToSingle(inventory[0]) == 6f,
                "The indexed /= assignment should write the divided value.");

            const string moduloPrefix = "_storyapi_indexed_compound_modulo";
            Check(VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory[0] %= 4", moduloPrefix, sourcePath, 15, 150, out lines),
                "A list index %= assignment should be recognized.");
            Check(lines.OfType<RenpyParser.RenpyOneLinePython>().Any(x =>
                    x.Code == "$ " + moduloPrefix + "_value = 4"),
                "The indexed %= assignment should evaluate the RHS without using native modulo.");
            inventory[0] = 18f;
            context.SetVariable(moduloPrefix + "_value", new SimpleExpressionEngine.DataValue(4f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(Convert.ToSingle(inventory[0]) == 2f,
                "The indexed %= assignment should compute and write the remainder at runtime.");

            const string dictionaryModuloPrefix = "_storyapi_indexed_dictionary_modulo";
            Check(VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "settings[\"count\"] %= 4", dictionaryModuloPrefix, sourcePath, 16, 160, out lines),
                "A dictionary value %= assignment should be recognized.");
            Check(lines.OfType<RenpyParser.RenpyOneLinePython>().Any(x =>
                    x.Code == "$ " + dictionaryModuloPrefix + "_value = 4"),
                "The dictionary %= assignment should evaluate its RHS without native modulo.");
            settings["count"] = 10f;
            context.SetVariable(dictionaryModuloPrefix + "_value",
                new SimpleExpressionEngine.DataValue(4f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(Convert.ToSingle(settings["count"]) == 2f,
                "The dictionary indexed %= assignment should compute and write the remainder.");

            Check(!VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "coins += 1", "_storyapi_scalar_only", sourcePath, 20, 200, out lines),
                "This recognizer must not capture scalar-only compound assignments.");
            Check(!VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory[0] **= 2", listPrefix, sourcePath, 21, 210, out lines),
                "Unsupported exponent compound assignments must remain rejected.");
            Check(!VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory[0] += ", listPrefix, sourcePath, 22, 220, out lines),
                "An indexed compound assignment must have a RHS.");
            Check(!VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory[0] += 1; inventory.clear()", listPrefix, sourcePath, 23, 230, out lines),
                "Multiple statements must not be accepted as one indexed compound assignment.");
            Check(!VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory[0] == 1", listPrefix, sourcePath, 24, 240, out lines),
                "Comparisons must not be interpreted as compound assignments.");
            Check(!VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory += 1", listPrefix, sourcePath, 25, 250, out lines),
                "The target must be indexed.");
            Check(!VariableIndexExpressionLine.TryCreateIndexedCompoundAssignmentLines(
                    "inventory[0] += 1, settings[\"count\"] += 2", listPrefix,
                    sourcePath, 26, 260, out lines),
                "Multiple compound operators must be rejected.");

            var statements = Parse(
                "label indexed_compound_test:\n" +
                "    $ inventory[0] += 1\n" +
                "    $ settings[\"count\"] -= 1\n" +
                "    $ inventory[1] /= 2\n" +
                "    $ settings[\"count\"] %= 3\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label with indexed compound assignments.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The list indexed compound assignment should parse as Python code.").Code ==
                    "inventory[0] += 1",
                "The parser should preserve list compound-assignment syntax.");
            Check(Require<PythonStatement>(label.Body[1],
                    "The dictionary indexed compound assignment should parse as Python code.").Code ==
                    "settings[\"count\"] -= 1",
                "The parser should preserve dictionary compound-assignment syntax.");
            Check(Require<PythonStatement>(label.Body[2],
                    "The list indexed /= assignment should parse as Python code.").Code ==
                    "inventory[1] /= 2",
                "The parser should preserve indexed /= syntax.");
            Check(Require<PythonStatement>(label.Body[3],
                    "The dictionary indexed %= assignment should parse as Python code.").Code ==
                    "settings[\"count\"] %= 3",
                "The parser should preserve indexed %= syntax.");
        }

        private static void TestCompoundScalarAssignments()
        {
            const string prefix = "_storyapi_compound_test";
            const string sourcePath = "compound-assignment-test.rpy";
            List<RenPyParser.Line> lines;

            Check(VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "coins += 5", prefix, sourcePath, 10, 100, out lines),
                "A simple += assignment should be recognized.");
            Check(lines.Count == 1 &&
                    lines[0] is RenpyParser.RenpyOneLinePython &&
                    ((RenpyParser.RenpyOneLinePython)lines[0]).Code == "$ coins = coins + (5)",
                "+= should translate to a parenthesized scalar expression.");

            Check(VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "coins -= 2", prefix, sourcePath, 11, 110, out lines) &&
                    ((RenpyParser.RenpyOneLinePython)lines[0]).Code == "$ coins = coins - (2)",
                "-= should translate to subtraction.");
            Check(VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "coins *= 3", prefix, sourcePath, 12, 120, out lines) &&
                    ((RenpyParser.RenpyOneLinePython)lines[0]).Code == "$ coins = coins * (3)",
                "*= should translate to multiplication.");

            Check(VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "coins += 2 * (bonus + 1)", prefix, sourcePath, 13, 130, out lines) &&
                    ((RenpyParser.RenpyOneLinePython)lines[0]).Code ==
                        "$ coins = coins + (2 * (bonus + 1))",
                "The right-hand expression should retain its precedence with parentheses.");

            Check(VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "score += inventory[0]", prefix, sourcePath, 14, 140, out lines),
                "Compound RHS expressions may read supported collection indexes.");
            Check(lines.Count == 4 &&
                    lines[0] is RenpyParser.RenpyOneLinePython &&
                    ((RenpyParser.RenpyOneLinePython)lines[0]).Code ==
                        "$ " + prefix + "_compound_gate = True" &&
                    lines[1].GetType().Name == "VariableIndexExpressionLine" &&
                    lines[2] is RenpyParser.RenpyGoToLineUnless &&
                    lines[3] is RenpyParser.RenpyOneLinePython &&
                    ((RenpyParser.RenpyOneLinePython)lines[3]).Code ==
                        "$ score = score + (" + prefix + "_compound_rhs_index_0)",
                "Indexed RHS values should be resolved and guarded before the assignment.");

            Check(VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "coins /= 2", prefix, sourcePath, 15, 150, out lines) &&
                    ((RenpyParser.RenpyOneLinePython)lines[0]).Code == "$ coins = coins / (2)",
                "/= should translate to division.");
            Check(VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "coins %= 3", prefix, sourcePath, 16, 160, out lines) &&
                    lines.Count == 2 &&
                    lines[0] is RenpyParser.RenpyOneLinePython &&
                    ((RenpyParser.RenpyOneLinePython)lines[0]).Code ==
                        "$ " + prefix + "_compound_rhs_value = (3)" &&
                    lines[1].GetType().Name == "ScalarModuloAssignmentLine",
                "%= should evaluate its RHS and calculate the modulo in StoryAPI.");
            Check(!VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "coins **= 2", prefix, sourcePath, 17, 170, out lines),
                "Unsupported exponent compound assignments must remain rejected.");
            Check(!VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "inventory[0] += 1", prefix, sourcePath, 16, 160, out lines),
                "Indexed compound-assignment targets remain unsupported.");
            Check(!VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "coins += ", prefix, sourcePath, 17, 170, out lines),
                "A compound assignment must have a right-hand expression.");
            Check(!VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "coins += 5; coins += 1", prefix, sourcePath, 18, 180, out lines),
                "Multiple statements must not be accepted as one compound assignment.");
            Check(!VariableIndexExpressionLine.TryCreateCompoundAssignmentLines(
                    "coins == 5", prefix, sourcePath, 19, 190, out lines),
                "Comparison expressions must not be interpreted as compound assignments.");

            var statements = Parse(
                "label compound_test:\n" +
                "    $ coins += 5\n" +
                "    $ coins -= 2\n" +
                "    $ coins *= 3\n" +
                "    $ coins /= 2\n" +
                "    $ coins %= 3\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing compound assignments.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The += statement should parse as Python code.").Code == "coins += 5",
                "The parser should preserve += syntax.");
            Check(Require<PythonStatement>(label.Body[1],
                    "The -= statement should parse as Python code.").Code == "coins -= 2",
                "The parser should preserve -= syntax.");
            Check(Require<PythonStatement>(label.Body[2],
                    "The *= statement should parse as Python code.").Code == "coins *= 3",
                "The parser should preserve *= syntax.");
            Check(Require<PythonStatement>(label.Body[3],
                    "The /= statement should parse as Python code.").Code == "coins /= 2",
                "The parser should preserve /= syntax.");
            Check(Require<PythonStatement>(label.Body[4],
                    "The %= statement should parse as Python code.").Code == "coins %= 3",
                "The parser should preserve %= syntax.");
        }

        private static void TestRepeatedIndexedTemporaryReads()
        {
            const string sourcePath = "repeated-indexed-read-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var settings = new Dictionary<string, object>
            {
                { "alpha", 1f },
                { "beta", 2f }
            };
            context.SetVariable("settings", new SimpleExpressionEngine.DataValue((object)settings));
            context.SetVariable("key", new SimpleExpressionEngine.DataValue("alpha"));

            VariableIndexExpressionLine line;
            Check(VariableIndexExpressionLine.TryCreateTemporary(
                    "temporary_value", "settings[key]", sourcePath, 10,
                    new VariableInitializationState(), "_repeated_read_gate",
                    "assignment_target", out line),
                "A dynamic dictionary read should produce a temporary runtime line.");

            ((RenPyParser.Images.IApply)line).Apply(context);
            object first;
            Check(GameBridge.TryGetLastTestLiteral("temporary_value", out first) &&
                    Math.Abs(Convert.ToSingle(first) - 1f) < 0.001f,
                "The first execution should read the value for key 'alpha'.");

            // The same instruction object is revisited when the assignment is inside a loop.
            // It must use the current key rather than a one-time cached value.
            context.SetVariable("key", new SimpleExpressionEngine.DataValue("beta"));
            ((RenPyParser.Images.IApply)line).Apply(context);
            object second;
            Check(GameBridge.TryGetLastTestLiteral("temporary_value", out second) &&
                    Math.Abs(Convert.ToSingle(second) - 2f) < 0.001f,
                "A repeated execution should re-read the value for the updated key 'beta'.");
        }

        private static void TestIndexedCollectionAssignments()
        {
            const string prefix = "_storyapi_assignment_test";
            List<RenPyParser.Line> lines;

            Check(VariableIndexExpressionLine.TryCreateAssignmentLines(
                    "inventory[1] = 99", prefix, "assignment-test.rpy", 2, 4, out lines),
                "A literal list element assignment should be recognized.");
            Check(lines.Count == 4 &&
                lines.Last().GetType().Name == "CollectionMutationLine",
                "The assignment should emit a guarded block ending in a collection mutation line.");
            Check(lines.OfType<RenpyParser.RenpyGoToLineUnless>().Single().Target == 8,
                "The guard jump should target the instruction after the assignment block.");

            var context = new RenpyParser.DynamicIndexTestContext();
            var inventory = new List<object> { 10f, 20f, 30f };
            context.SetVariable("inventory", new SimpleExpressionEngine.DataValue((object)inventory));
            context.SetVariable(prefix + "_value", new SimpleExpressionEngine.DataValue(99f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(Convert.ToSingle(inventory[1]) == 99f,
                "The mutation line should replace the existing list element.");

            const string dynamicPrefix = "_storyapi_dynamic_assignment_test";
            Check(VariableIndexExpressionLine.TryCreateAssignmentLines(
                    "inventory[target_index + 1] = 55", dynamicPrefix,
                    "assignment-test.rpy", 3, 10, out lines),
                "A list assignment with a computed target index should be recognized.");
            context.SetVariable(dynamicPrefix + "_value", new SimpleExpressionEngine.DataValue(55f));
            context.SetVariable(dynamicPrefix + "_target_key_0", new SimpleExpressionEngine.DataValue(1f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(Convert.ToSingle(inventory[1]) == 55f,
                "A computed index should mutate the selected list element.");

            const string dictionaryPrefix = "_storyapi_dictionary_assignment_test";
            Check(VariableIndexExpressionLine.TryCreateAssignmentLines(
                    "settings[\"count\"] = 7", dictionaryPrefix,
                    "assignment-test.rpy", 4, 20, out lines),
                "A dictionary string-key assignment should be recognized.");
            var settings = new Dictionary<string, object> { { "count", 2f } };
            context.SetVariable("settings", new SimpleExpressionEngine.DataValue((object)settings));
            context.SetVariable(dictionaryPrefix + "_value", new SimpleExpressionEngine.DataValue(7f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(Convert.ToSingle(settings["count"]) == 7f,
                "A dictionary assignment should replace the value at a string key.");

            const string nestedPrefix = "_storyapi_nested_assignment_test";
            Check(VariableIndexExpressionLine.TryCreateAssignmentLines(
                    "settings[\"meta\"][\"count\"] = settings[\"count\"] + 1",
                    nestedPrefix, "assignment-test.rpy", 5, 30, out lines),
                "A nested target and RHS collection read should be recognized.");
            Check(lines.OfType<VariableIndexExpressionLine>().Any(),
                "A collection read in the RHS should become a runtime index-reader line.");
            Check(lines.OfType<RenpyParser.RenpyOneLinePython>().Any(
                    line => line.Code.Contains(nestedPrefix + "_value = " + nestedPrefix + "_rhs_index_0 + 1")),
                "The RHS collection value should be rewritten before the final assignment.");
            Check(lines.Last().GetType().Name == "CollectionMutationLine",
                "A nested assignment should end with the collection mutation line.");

            Check(!VariableIndexExpressionLine.TryCreateAssignmentLines(
                    "inventory[0] == 99", prefix, "assignment-test.rpy", 6, 40, out lines),
                "Comparison expressions must not be mistaken for collection assignments.");
            Check(!VariableIndexExpressionLine.TryCreateAssignmentLines(
                    "inventory[0] += 1", prefix, "assignment-test.rpy", 7, 40, out lines),
                "Compound assignments remain outside the initial mutation subset.");
        }

        private static void TestIndexedCollectionConditions()
        {
            const string prefix = "_storyapi_condition_test";
            const string condition =
                "stage5_probe_list[1] == 99 and stage5_probe_dict[\"count\"] == 5 and " +
                "stage5_probe_dict[\"meta\"][\"count\"] == 6";

            List<RenPyParser.Line> lines;
            string preparedCondition;
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    condition, prefix, "condition-test.rpy", 8, out lines, out preparedCondition),
                "An if condition containing multiple indexed collection reads should be prepared.");

            Check(lines.Count == 4 &&
                lines[0] is RenpyParser.RenpyOneLinePython &&
                ((RenpyParser.RenpyOneLinePython)lines[0]).Code == "$ " + prefix + "_gate = True",
                "Condition preparation should initialize a failure gate before resolving reads.");
            Check(lines.OfType<VariableIndexExpressionLine>().Count() == 3,
                "Each indexed value in a conditional expression should be resolved to a temporary.");
            Check(preparedCondition ==
                    prefix + "_gate and (" + prefix + "_index_0 == 99 and " +
                    prefix + "_index_1 == 5 and " + prefix + "_index_2 == 6)",
                "The native conditional expression should contain temporary names and a failure gate.");

            List<RenPyParser.Line> noIndexLines;
            string unchangedCondition;
            Check(!VariableIndexExpressionLine.TryPrepareCondition(
                    "score > 0", prefix, "condition-test.rpy", 9,
                    out noIndexLines, out unchangedCondition) &&
                noIndexLines.Count == 0 && unchangedCondition == "score > 0",
                "Ordinary scalar conditions should remain untouched.");

            const string nonePrefix = "_storyapi_condition_none_test";
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "optional_value != None and label == \"None\"",
                    nonePrefix, "condition-test.rpy", 10, out noIndexLines, out unchangedCondition),
                "A Python None literal in a condition should be prepared for the game evaluator.");
            Check(unchangedCondition == nonePrefix + "_gate and (" +
                    nonePrefix + "_none_compare_0 and label == \"None\")",
                "The None comparison should be rewritten, while quoted text remains unchanged.");
            Check(noIndexLines.Any(line => line.GetType().Name == "NoneComparisonLine"),
                "Condition preparation should emit a dedicated runtime None comparison.");
        }

        private static void TestListMethodMutations()
        {
            const string appendPrefix = "_storyapi_append_test";
            var inventory = new List<object> { "key", "flashlight" };
            var context = new RenpyParser.DynamicIndexTestContext();
            context.SetVariable("inventory",
                new SimpleExpressionEngine.DataValue((object)inventory));

            List<RenPyParser.Line> lines;
            Check(VariableIndexExpressionLine.TryCreateListMethodMutationLines(
                    @"inventory.append(""battery"")", appendPrefix,
                    "list-method-test.rpy", 2, 10, out lines),
                "A single-string-argument append call should be recognized.");
            Check(lines.Count == 4 &&
                lines.OfType<RenpyParser.RenpyGoToLineUnless>().Single().Target == 14,
                "Append should emit a guarded scalar-evaluation block with the correct jump target.");
            Check(lines.OfType<RenpyParser.RenpyOneLinePython>().Any(
                    line => line.Code.Contains(appendPrefix + "_value = ")),
                "Append's scalar argument should be evaluated by the game's native expression runner.");

            context.SetVariable(appendPrefix + "_value",
                new SimpleExpressionEngine.DataValue("battery"));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(inventory.Count == 3 && (string)inventory[2] == "battery",
                "append(value) should add one scalar value to the list.");

            const string removePrefix = "_storyapi_remove_test";
            inventory = new List<object> { "key", "key", "flashlight" };
            context.SetVariable("inventory",
                new SimpleExpressionEngine.DataValue((object)inventory));
            Check(VariableIndexExpressionLine.TryCreateListMethodMutationLines(
                    @"inventory.remove(""key"")", removePrefix,
                    "list-method-test.rpy", 3, 20, out lines),
                "A single-string-argument remove call should be recognized.");
            context.SetVariable(removePrefix + "_value",
                new SimpleExpressionEngine.DataValue("key"));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(inventory.Count == 2 &&
                (string)inventory[0] == "key" && (string)inventory[1] == "flashlight",
                "remove(value) should remove only the first matching list item.");

            const string numericPrefix = "_storyapi_numeric_remove_test";
            inventory = new List<object> { 1f, 2f, 1f };
            context.SetVariable("inventory",
                new SimpleExpressionEngine.DataValue((object)inventory));
            Check(VariableIndexExpressionLine.TryCreateListMethodMutationLines(
                    "inventory.remove(1)", numericPrefix,
                    "list-method-test.rpy", 4, 30, out lines),
                "A numeric remove argument should be recognized.");
            context.SetVariable(numericPrefix + "_value",
                new SimpleExpressionEngine.DataValue(1f));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(inventory.Count == 2 && Convert.ToSingle(inventory[0]) == 2f &&
                Convert.ToSingle(inventory[1]) == 1f,
                "remove(value) should remove the first matching numeric item.");

            Check(!VariableIndexExpressionLine.TryCreateListMethodMutationLines(
                    "inventory.append(1, 2)", "_storyapi_invalid_method_test",
                    "list-method-test.rpy", 5, 40, out lines),
                "Calls with multiple positional arguments should remain unsupported.");
            Check(!VariableIndexExpressionLine.TryCreateListMethodMutationLines(
                    "inventory.extend(1)", "_storyapi_invalid_method_test",
                    "list-method-test.rpy", 6, 40, out lines),
                "Methods other than append and remove should remain unsupported.");
        }

        private static void TestCollectionMembershipConditions()
        {
            const string prefix = "_storyapi_membership_test";
            var inventory = new List<object> { "key", "flashlight", 2f };
            var context = new RenpyParser.DynamicIndexTestContext();
            context.SetVariable("inventory",
                new SimpleExpressionEngine.DataValue((object)inventory));

            List<RenPyParser.Line> lines;
            string prepared;
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "\"key\" in inventory", prefix, "membership-test.rpy", 10,
                    out lines, out prepared),
                "A string literal membership check against a list should be prepared.");
            Check(prepared == prefix + "_gate and (" + prefix + "_membership_result_0)",
                "The membership result should replace the native in expression.");
            context.SetVariable(prefix + "_membership_value_0",
                new SimpleExpressionEngine.DataValue("key"));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            object result;
            Check(GameBridge.TryGetLastTestLiteral(prefix + "_membership_result_0", out result) &&
                result is bool && (bool)result,
                "A list membership check should find an existing string item.");

            const string notPrefix = "_storyapi_not_membership_test";
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "\"battery\" not in inventory", notPrefix, "membership-test.rpy", 11,
                    out lines, out prepared),
                "A not-in check against a list should be prepared.");
            Check(prepared == notPrefix + "_gate and (not " +
                    notPrefix + "_membership_result_0)",
                "not in should negate the computed membership result.");
            context.SetVariable(notPrefix + "_membership_value_0",
                new SimpleExpressionEngine.DataValue("battery"));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral(notPrefix + "_membership_result_0", out result) &&
                result is bool && !(bool)result,
                "An absent list item should produce a false membership result.");

            const string dictionaryPrefix = "_storyapi_dictionary_membership_test";
            var settings = new Dictionary<string, object>
            {
                { "count", 5f },
                { "theme", "dark" }
            };
            context.SetVariable("settings",
                new SimpleExpressionEngine.DataValue((object)settings));
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "\"count\" in settings", dictionaryPrefix, "membership-test.rpy", 12,
                    out lines, out prepared),
                "A string key membership check against a dictionary should be prepared.");
            context.SetVariable(dictionaryPrefix + "_membership_value_0",
                new SimpleExpressionEngine.DataValue("count"));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral(dictionaryPrefix + "_membership_result_0", out result) &&
                result is bool && (bool)result,
                "Dictionary membership should test keys, not values.");

            context.SetVariable("selected_item", new SimpleExpressionEngine.DataValue("flashlight"));
            const string variablePrefix = "_storyapi_variable_membership_test";
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "selected_item in inventory", variablePrefix, "membership-test.rpy", 13,
                    out lines, out prepared),
                "A scalar variable should be accepted as the left membership operand.");
            context.SetVariable(variablePrefix + "_membership_value_0",
                new SimpleExpressionEngine.DataValue("flashlight"));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral(variablePrefix + "_membership_result_0", out result) &&
                result is bool && (bool)result,
                "A variable left operand should be compared against list items.");

            const string combinedPrefix = "_storyapi_combined_membership_test";
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "\"key\" in inventory and \"battery\" not in inventory",
                    combinedPrefix, "membership-test.rpy", 14, out lines, out prepared),
                "Multiple membership checks joined by and should be prepared.");
            Check(prepared == combinedPrefix + "_gate and (" +
                    combinedPrefix + "_membership_result_0 and not " +
                    combinedPrefix + "_membership_result_1)",
                "Multiple in/not-in checks should retain their boolean structure.");
            context.SetVariable(combinedPrefix + "_membership_value_0",
                new SimpleExpressionEngine.DataValue("key"));
            context.SetVariable(combinedPrefix + "_membership_value_1",
                new SimpleExpressionEngine.DataValue("battery"));
            foreach (RenPyParser.Line line in lines.Where(
                line => line.GetType().Name == "CollectionMembershipLine"))
                ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral(combinedPrefix + "_membership_result_0", out result) &&
                result is bool && (bool)result,
                "The first check in a combined membership condition should be true.");
            Check(GameBridge.TryGetLastTestLiteral(combinedPrefix + "_membership_result_1", out result) &&
                result is bool && !(bool)result,
                "The not-in check in a combined condition should report the absent item.");

            const string mixedPrefix = "_storyapi_mixed_membership_test";
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "\"key\" in inventory and inventory[0] == \"key\"",
                    mixedPrefix, "membership-test.rpy", 15, out lines, out prepared),
                "Membership tests should coexist with indexed reads in the same condition.");
            Check(lines.OfType<VariableIndexExpressionLine>().Count() == 1 &&
                prepared.Contains(mixedPrefix + "_membership_result_0") &&
                prepared.Contains(mixedPrefix + "_index_0"),
                "A mixed condition should rewrite both membership and collection-index expressions.");

            string literalCondition = "\"The phrase 'key' in inventory is just text\"";
            Check(!VariableIndexExpressionLine.TryPrepareCondition(
                    literalCondition, prefix, "membership-test.rpy", 16,
                    out lines, out prepared) && prepared == literalCondition,
                "Text containing the words in and not in must not be rewritten as an operator.");
        }

        private static void TestCollectionLengthConditions()
        {
            const string prefix = "_storyapi_length_test";
            var inventory = new List<object> { "key", "flashlight", "battery" };
            var settings = new Dictionary<string, object>
            {
                { "count", 5f },
                { "theme", "dark" }
            };

            var context = new RenpyParser.DynamicIndexTestContext();
            context.SetVariable("inventory",
                new SimpleExpressionEngine.DataValue((object)inventory));
            context.SetVariable("settings",
                new SimpleExpressionEngine.DataValue((object)settings));

            List<RenPyParser.Line> lines;
            string prepared;
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "len(inventory) >= 3", prefix, "length-test.rpy", 20,
                    out lines, out prepared),
                "len(list) should be prepared as a runtime scalar in a condition.");
            Check(prepared == prefix + "_gate and (" +
                    prefix + "_length_result_0 >= 3)",
                "The len call should be rewritten to a scalar temporary.");
            Check(lines.OfType<RenPyParser.Line>().Count(line =>
                    line.GetType().Name == "CollectionLengthLine") == 1,
                "A single len call should emit one collection-length reader.");

            var lengthLine = lines.Single(line => line.GetType().Name == "CollectionLengthLine");
            ((RenPyParser.Images.IApply)lengthLine).Apply(context);
            object result;
            Check(GameBridge.TryGetLastTestLiteral(prefix + "_length_result_0", out result) &&
                Convert.ToInt32(result) == 3,
                "len(list) should return the number of list items.");

            const string dictPrefix = "_storyapi_dictionary_length_test";
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "len(settings) == 2", dictPrefix, "length-test.rpy", 21,
                    out lines, out prepared),
                "len(dictionary) should be prepared as a runtime scalar in a condition.");
            ((RenPyParser.Images.IApply)lines.Single(
                line => line.GetType().Name == "CollectionLengthLine")).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral(dictPrefix + "_length_result_0", out result) &&
                Convert.ToInt32(result) == 2,
                "len(dictionary) should return the number of dictionary keys.");

            const string multiPrefix = "_storyapi_multiple_length_test";
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "len(inventory) == 3 and len(settings) == 2", multiPrefix,
                    "length-test.rpy", 22, out lines, out prepared),
                "Multiple len calls in one condition should be prepared.");
            Check(lines.Count(line => line.GetType().Name == "CollectionLengthLine") == 2 &&
                prepared.Contains(multiPrefix + "_length_result_0") &&
                prepared.Contains(multiPrefix + "_length_result_1"),
                "Multiple len calls should receive distinct temporary values.");
            foreach (RenPyParser.Line line in lines.Where(
                line => line.GetType().Name == "CollectionLengthLine"))
                ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral(multiPrefix + "_length_result_0", out result) &&
                    Convert.ToInt32(result) == 3 &&
                GameBridge.TryGetLastTestLiteral(multiPrefix + "_length_result_1", out result) &&
                    Convert.ToInt32(result) == 2,
                "Each len call should resolve to its own collection count.");

            const string mixedPrefix = "_storyapi_mixed_length_index_test";
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "len(inventory) >= 3 and inventory[0] == \"key\"", mixedPrefix,
                    "length-test.rpy", 23, out lines, out prepared),
                "len calls should coexist with indexed collection reads.");
            Check(lines.OfType<VariableIndexExpressionLine>().Count() == 1 &&
                lines.Count(line => line.GetType().Name == "CollectionLengthLine") == 1 &&
                prepared.Contains(mixedPrefix + "_length_result_0") &&
                prepared.Contains(mixedPrefix + "_index_0"),
                "A mixed condition should rewrite length calls and indexed reads.");

            const string emptyPrefix = "_storyapi_empty_length_test";
            context.SetVariable("empty_inventory",
                new SimpleExpressionEngine.DataValue((object)new List<object>()));
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "len(empty_inventory) == 0", emptyPrefix, "length-test.rpy", 24,
                    out lines, out prepared),
                "len(empty_list) should be supported.");
            ((RenPyParser.Images.IApply)lines.Single(
                line => line.GetType().Name == "CollectionLengthLine")).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral(emptyPrefix + "_length_result_0", out result) &&
                Convert.ToInt32(result) == 0,
                "The length of an empty list should be zero.");

            const string ordinary = "score > 0";
            Check(!VariableIndexExpressionLine.TryPrepareCondition(
                    ordinary, prefix, "length-test.rpy", 25, out lines, out prepared) &&
                prepared == ordinary,
                "Ordinary scalar conditions should remain untouched.");

            const string stringCondition = "\"The text len(inventory) is literal\"";
            Check(!VariableIndexExpressionLine.TryPrepareCondition(
                    stringCondition, prefix, "length-test.rpy", 26, out lines, out prepared) &&
                prepared == stringCondition,
                "Text containing len(collection) inside a string must not be rewritten.");
        }

        private static void TestNoneComparisonConditions()
        {
            const string sourcePath = "none-condition-test.rpy";
            const string prefix = "_storyapi_none_comparison_test";
            var context = new RenpyParser.DynamicIndexTestContext();
            context.SetVariable("optional_value", default(SimpleExpressionEngine.DataValue));

            List<RenPyParser.Line> lines;
            string prepared;
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "optional_value != None", prefix, sourcePath, 31,
                    out lines, out prepared),
                "A simple variable != None condition should be rewritten.");
            Check(prepared == prefix + "_gate and (" + prefix + "_none_compare_0)",
                "A None comparison should use a boolean temporary rather than the native scalar evaluator.");
            RenPyParser.Line noneComparison = lines.Single(
                line => line.GetType().Name == "NoneComparisonLine");
            ((RenPyParser.Images.IApply)noneComparison).Apply(context);

            object observed;
            Check(GameBridge.TryGetLastTestLiteral(prefix + "_none_compare_0", out observed) &&
                    observed is bool && !(bool)observed,
                "An Unknown/default DataValue should compare equal to None.");

            context.SetVariable("optional_value", new SimpleExpressionEngine.DataValue("present"));
            ((RenPyParser.Images.IApply)noneComparison).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral(prefix + "_none_compare_0", out observed) &&
                    observed is bool && (bool)observed,
                "A string DataValue should compare unequal to None.");

            const string reversePrefix = "_storyapi_reverse_none_comparison_test";
            context.SetVariable("optional_value", default(SimpleExpressionEngine.DataValue));
            Check(VariableIndexExpressionLine.TryPrepareCondition(
                    "None == optional_value", reversePrefix, sourcePath, 32,
                    out lines, out prepared),
                "A None == variable condition should be rewritten.");
            ((RenPyParser.Images.IApply)lines.Single(
                line => line.GetType().Name == "NoneComparisonLine")).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral(
                    reversePrefix + "_none_compare_0", out observed) &&
                    observed is bool && (bool)observed,
                "The reverse operand order should also compare None correctly.");

            const string literalCondition = "\"The word None is literal text\"";
            Check(!VariableIndexExpressionLine.TryPrepareCondition(
                    literalCondition, prefix, sourcePath, 33, out lines, out prepared) &&
                    prepared == literalCondition,
                "None inside a quoted string must remain untouched.");
        }

        private static void TestCollectionClearMethods()
        {
            const string sourcePath = "collection-clear-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var inventory = new List<object> { "key", "flashlight", "battery" };
            var settings = new Dictionary<string, object>
            {
                { "count", 5f },
                { "theme", "dark" }
            };
            context.SetVariable("inventory",
                new SimpleExpressionEngine.DataValue((object)inventory));
            context.SetVariable("settings",
                new SimpleExpressionEngine.DataValue((object)settings));

            RenPyParser.Line line;
            Check(VariableIndexExpressionLine.TryCreateCollectionClearLine(
                    "inventory.clear()", sourcePath, 3, out line),
                "inventory.clear() should be recognized.");
            Check(line.GetType().Name == "CollectionClearLine",
                "clear() should emit the dedicated collection-clear instruction.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(inventory.Count == 0,
                "clear() should remove every element from the original list.");

            Check(VariableIndexExpressionLine.TryCreateCollectionClearLine(
                    "settings.clear()", sourcePath, 4, out line),
                "settings.clear() should be recognized for dictionaries.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(settings.Count == 0,
                "clear() should remove every key/value pair from the original dictionary.");

            Check(!VariableIndexExpressionLine.TryCreateCollectionClearLine(
                    "inventory.clear(\"key\")", sourcePath, 5, out line),
                "clear() must reject positional arguments.");
            Check(!VariableIndexExpressionLine.TryCreateCollectionClearLine(
                    "inventory.clear(value)", sourcePath, 6, out line),
                "clear() must reject variable arguments.");
            Check(!VariableIndexExpressionLine.TryCreateCollectionClearLine(
                    "inventory.append(\"battery\")", sourcePath, 7, out line),
                "The clear recognizer must not capture append calls.");
            Check(!VariableIndexExpressionLine.TryCreateCollectionClearLine(
                    "inventory[0].clear()", sourcePath, 8, out line),
                "Nested targets are not supported by clear().");
            Check(!VariableIndexExpressionLine.TryCreateCollectionClearLine(
                    "inventory.clear(); inventory.append(\"key\")", sourcePath, 9, out line),
                "Multiple statements must not be accepted as one clear call.");

            var statements = Parse(
                "label clear_test:\n" +
                "    $ inventory.clear()\n" +
                "    $ settings.clear()\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing clear() calls.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The first clear call should parse as a Python statement.").Code == "inventory.clear()",
                "The parser should preserve the list clear() call.");
            Check(Require<PythonStatement>(label.Body[1],
                    "The second clear call should parse as a Python statement.").Code == "settings.clear()",
                "The parser should preserve the dictionary clear() call.");
        }

        private static void TestDictionaryUpdateAndPopMethods()
        {
            const string sourcePath = "dictionary-update-pop-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var settings = new Dictionary<string, object>
            {
                { "count", 5f },
                { "theme", "dark" }
            };
            var overrides = new Dictionary<string, object>
            {
                { "theme", "light" },
                { "sound", "on" }
            };
            context.SetVariable("settings",
                new SimpleExpressionEngine.DataValue((object)settings));
            context.SetVariable("overrides",
                new SimpleExpressionEngine.DataValue((object)overrides));

            RenPyParser.Line line;
            Check(VariableIndexExpressionLine.TryCreateDictionaryUpdateLine(
                    "settings.update(overrides)", sourcePath, 10, out line),
                "settings.update(overrides) should be recognized.");
            Check(line.GetType().Name == "DictionaryUpdateLine",
                "update() should emit the dedicated dictionary-update instruction.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(settings.Count == 3 && (float)settings["count"] == 5f &&
                    (string)settings["theme"] == "light" && (string)settings["sound"] == "on",
                "update() should add missing keys and overwrite matching keys.");
            Check(overrides.Count == 2 && (string)overrides["theme"] == "light" &&
                    (string)overrides["sound"] == "on",
                "update() must not change its source dictionary.");

            Check(VariableIndexExpressionLine.TryCreateDictionaryUpdateLine(
                    "settings.update(settings)", sourcePath, 11, out line),
                "A dictionary should be allowed to update itself.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(settings.Count == 3,
                "Self-update should preserve all entries.");

            Check(!VariableIndexExpressionLine.TryCreateDictionaryUpdateLine(
                    "settings.update(\"theme\")", sourcePath, 12, out line),
                "update() should reject literal arguments.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryUpdateLine(
                    "settings[\"nested\"].update(overrides)", sourcePath, 13, out line),
                "update() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryUpdateLine(
                    "settings.update(overrides, settings)", sourcePath, 14, out line),
                "update() should reject multiple arguments.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryUpdateLine(
                    "settings.update(overrides); settings.clear()", sourcePath, 15, out line),
                "update() should reject multiple statements.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryUpdateLine(
                    "inventory.extend(overrides)", sourcePath, 16, out line),
                "The update recognizer must not capture other method calls.");

            List<RenPyParser.Line> lines;
            Check(VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "old_theme = settings.pop(\"theme\")", sourcePath, 20, out lines),
                "Assignment from dictionary pop() should be recognized.");
            Check(lines.Count == 1 && lines[0].GetType().Name == "DictionaryPopLine",
                "pop() should emit the dedicated dictionary-pop instruction.");
            ((RenPyParser.Images.IApply)lines[0]).Apply(context);
            object removed;
            Check(GameBridge.TryGetLastTestLiteral("old_theme", out removed) &&
                    string.Equals(removed as string, "light", StringComparison.Ordinal),
                "Dictionary pop() should assign the removed scalar value.");
            Check(!settings.ContainsKey("theme") && settings.ContainsKey("sound"),
                "Dictionary pop() should remove only the requested key.");

            Check(VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings.pop(\"count\")", sourcePath, 21, out lines),
                "Dictionary pop() without assignment should be recognized.");
            ((RenPyParser.Images.IApply)lines[0]).Apply(context);
            Check(!settings.ContainsKey("count"),
                "Dictionary pop() without assignment should still remove the key.");

            Check(!VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings.pop()", sourcePath, 22, out lines),
                "dict.pop() should reject a missing key.");
            Check(VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings.pop(\"sound\", \"off\")", sourcePath, 23, out lines),
                "dict.pop() should accept a literal default argument.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings.pop(key_name)", sourcePath, 24, out lines),
                "dict.pop() should require a quoted string key.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings[\"nested\"].pop(\"sound\")", sourcePath, 25, out lines),
                "dict.pop() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings.pop(\"sound\"); settings.clear()", sourcePath, 26, out lines),
                "dict.pop() should reject multiple statements.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings.clear()", sourcePath, 27, out lines),
                "The pop recognizer must not capture clear().");

            var statements = Parse(
                "label dictionary_update_pop_test:\n" +
                "    $ settings.update(overrides)\n" +
                "    $ old_theme = settings.pop(\"theme\")\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing dictionary update/pop calls.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The update call should parse as a Python statement.").Code ==
                    "settings.update(overrides)",
                "The parser should preserve dictionary update() calls.");
            Check(Require<PythonStatement>(label.Body[1],
                    "The pop assignment should parse as a Python statement.").Code ==
                    "old_theme = settings.pop(\"theme\")",
                "The parser should preserve dictionary pop() assignments.");
        }

        private static void TestDictionaryPopDefaultArgument()
        {
            const string sourcePath = "dictionary-pop-default-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var settings = new Dictionary<string, object>
            {
                { "theme", "dark" },
                { "count", 5f }
            };
            context.SetVariable("settings",
                new SimpleExpressionEngine.DataValue((object)settings));

            List<RenPyParser.Line> lines;
            Check(VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "theme_value = settings.pop(\"theme\", \"fallback\")",
                    sourcePath, 10, out lines),
                "pop() with a default should be recognized.");
            Check(lines.Count == 1 && lines[0].GetType().Name == "DictionaryPopLine",
                "pop() with a default should emit the dictionary-pop instruction.");
            ((RenPyParser.Images.IApply)lines[0]).Apply(context);

            object observed;
            Check(GameBridge.TryGetLastTestLiteral("theme_value", out observed) &&
                    Equals(observed, "dark") && !settings.ContainsKey("theme"),
                "When the key exists, pop() should return and remove its value instead of the default.");

            Check(VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "missing_value = settings.pop(\"missing\", \"fallback\")",
                    sourcePath, 11, out lines),
                "pop() should recognize a string default for a missing key.");
            ((RenPyParser.Images.IApply)lines[0]).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("missing_value", out observed) &&
                    Equals(observed, "fallback"),
                "pop() should assign its default when the key is missing.");
            Check(!settings.ContainsKey("missing"),
                "Using pop() with a default must not insert the missing key.");

            Check(VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "count_value = settings.pop(\"missing_count\", 7)",
                    sourcePath, 12, out lines),
                "pop() should accept a numeric default.");
            ((RenPyParser.Images.IApply)lines[0]).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("count_value", out observed) &&
                    Math.Abs(Convert.ToSingle(observed) - 7f) < 0.001f,
                "pop() should return a numeric default when the key is missing.");

            Check(VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "nullable_value = settings.pop(\"missing_nullable\", None)",
                    sourcePath, 13, out lines),
                "pop() should accept None as a default.");
            ((RenPyParser.Images.IApply)lines[0]).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("nullable_value", out observed) && observed == null,
                "pop() should assign None when the key is missing and its default is None.");

            Check(!VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings.pop(\"missing\", fallback)", sourcePath, 20, out lines),
                "pop() should reject variable defaults in this restricted implementation.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings.pop(\"missing\", \"a\", \"b\")", sourcePath, 21, out lines),
                "pop() should reject more than two arguments.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings.pop(key_name, \"fallback\")", sourcePath, 22, out lines),
                "pop() should continue to require a quoted string key.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings[\"nested\"].pop(\"missing\", \"fallback\")",
                    sourcePath, 23, out lines),
                "pop() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryPopLines(
                    "settings.pop(\"missing\", \"fallback\"); settings.clear()",
                    sourcePath, 24, out lines),
                "pop() should reject multiple statements.");

            var statements = Parse(
                "label dictionary_pop_default_test:\n" +
                "    $ missing_value = settings.pop(\"missing\", \"fallback\")\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing a dictionary pop() with default.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The pop() with default should parse as a Python statement.").Code ==
                    "missing_value = settings.pop(\"missing\", \"fallback\")",
                "The parser should preserve dictionary pop() defaults.");
        }

        private static void TestDictionaryGetMethod()
        {
            const string sourcePath = "dictionary-get-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var settings = new Dictionary<string, object>
            {
                { "theme", "dark" },
                { "count", 5f },
                { "enabled", true }
            };
            context.SetVariable("settings",
                new SimpleExpressionEngine.DataValue((object)settings));

            RenPyParser.Line line;
            Check(VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "theme = settings.get(\"theme\")", sourcePath, 10, out line),
                "dict.get() without a default should be recognized.");
            Check(line.GetType().Name == "DictionaryGetLine",
                "dict.get() should emit the dedicated dictionary-get instruction.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            object observed;
            Check(GameBridge.TryGetLastTestLiteral("theme", out observed) &&
                    Equals(observed, "dark"),
                "dict.get() should assign the existing value.");
            Check(settings.Count == 3 && (string)settings["theme"] == "dark",
                "dict.get() must not mutate the source dictionary.");

            Check(VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "missing = settings.get(\"missing\", \"fallback\")", sourcePath, 11, out line),
                "dict.get() with a quoted-string default should be recognized.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("missing", out observed) &&
                    Equals(observed, "fallback"),
                "dict.get() should assign the default when the key is absent.");

            Check(VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "missing_none = settings.get(\"absent\")", sourcePath, 12, out line),
                "dict.get() without a default should still support absent keys.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("missing_none", out observed) && observed == null,
                "An absent key without a default should assign None/null.");

            Check(VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "count_default = settings.get(\"absent\", 7)", sourcePath, 13, out line),
                "dict.get() should accept a numeric literal default.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("count_default", out observed) &&
                    Math.Abs(Convert.ToSingle(observed) - 7f) < 0.001f,
                "dict.get() should preserve a numeric default.");

            Check(VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "enabled_default = settings.get(\"absent\", True)", sourcePath, 14, out line),
                "dict.get() should accept a boolean default.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("enabled_default", out observed) &&
                    Equals(observed, true),
                "dict.get() should preserve a boolean default.");

            Check(!VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "theme = settings.get(key_name)", sourcePath, 15, out line),
                "dict.get() should require a quoted string key in this subset.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "theme = settings.get(\"theme\", fallback)", sourcePath, 16, out line),
                "dict.get() should reject non-literal defaults in this subset.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "theme = settings.get(\"theme\", \"fallback\", \"extra\")",
                    sourcePath, 17, out line),
                "dict.get() should reject more than two arguments.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "theme = settings[\"nested\"].get(\"theme\")", sourcePath, 18, out line),
                "dict.get() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "theme = settings.get(\"theme\"); settings.clear()", sourcePath, 19, out line),
                "dict.get() should reject multiple statements.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryGetLine(
                    "settings.get(\"theme\")", sourcePath, 20, out line),
                "dict.get() must require an assignment target.");

            var statements = Parse(
                "label dictionary_get_test:\n" +
                "    $ theme = settings.get(\"theme\")\n" +
                "    $ fallback = settings.get(\"missing\", \"off\")\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing dictionary get() calls.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The first get() should parse as a Python statement.").Code ==
                    "theme = settings.get(\"theme\")",
                "The parser should preserve dictionary get() assignments.");
            Check(Require<PythonStatement>(label.Body[1],
                    "The second get() should parse as a Python statement.").Code ==
                    "fallback = settings.get(\"missing\", \"off\")",
                "The parser should preserve dict.get() calls with a default.");
        }

        private static void TestDictionarySetDefaultMethod()
        {
            const string sourcePath = "dictionary-setdefault-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var settings = new Dictionary<string, object>
            {
                { "theme", "dark" },
                { "count", 5f },
                { "enabled", true }
            };
            context.SetVariable("settings",
                new SimpleExpressionEngine.DataValue((object)settings));

            RenPyParser.Line line;
            Check(VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "existing = settings.setdefault(\"theme\", \"light\")",
                    sourcePath, 10, out line),
                "setdefault() with a string default should be recognized.");
            Check(line.GetType().Name == "DictionarySetDefaultLine",
                "setdefault() should emit the dedicated setdefault instruction.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            object observed;
            Check(GameBridge.TryGetLastTestLiteral("existing", out observed) &&
                    Equals(observed, "dark"),
                "setdefault() should return the existing value.");
            Check((string)settings["theme"] == "dark" && settings.Count == 3,
                "setdefault() must not replace or add anything when the key already exists.");

            Check(VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "inserted = settings.setdefault(\"accent\", \"blue\")",
                    sourcePath, 11, out line),
                "setdefault() should accept a quoted string default.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("inserted", out observed) &&
                    Equals(observed, "blue"),
                "setdefault() should return the default for a missing key.");
            Check(settings.ContainsKey("accent") && Equals(settings["accent"], "blue"),
                "setdefault() should insert the default for a missing key.");

            Check(VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "again = settings.setdefault(\"accent\", \"red\")",
                    sourcePath, 12, out line),
                "A repeated setdefault() call should be recognized.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("again", out observed) &&
                    Equals(observed, "blue") && Equals(settings["accent"], "blue"),
                "A repeated setdefault() must return the existing value and preserve it.");

            Check(VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "number = settings.setdefault(\"new_count\", 7)",
                    sourcePath, 13, out line),
                "setdefault() should accept a numeric literal default.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("number", out observed) &&
                    Math.Abs(Convert.ToSingle(observed) - 7f) < 0.001f &&
                    Math.Abs(Convert.ToSingle(settings["new_count"]) - 7f) < 0.001f,
                "A numeric default should be assigned and stored.");

            Check(VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "enabled_default = settings.setdefault(\"new_enabled\", True)",
                    sourcePath, 14, out line),
                "setdefault() should accept a boolean literal default.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("enabled_default", out observed) &&
                    Equals(observed, true) && Equals(settings["new_enabled"], true),
                "A boolean default should be assigned and stored.");

            Check(VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "no_default = settings.setdefault(\"nullable\")",
                    sourcePath, 15, out line),
                "setdefault() without a default should be recognized.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("no_default", out observed) && observed == null &&
                    settings.ContainsKey("nullable") && settings["nullable"] == null,
                "Without a default, setdefault() should insert and return None.");

            Check(!VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "value = settings.setdefault(key_name)", sourcePath, 20, out line),
                "setdefault() should require a quoted string key in this subset.");
            Check(!VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "value = settings.setdefault(\"key\", fallback)", sourcePath, 21, out line),
                "setdefault() should reject non-literal defaults in this subset.");
            Check(!VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "value = settings.setdefault(\"key\", \"one\", \"two\")",
                    sourcePath, 22, out line),
                "setdefault() should reject more than two arguments.");
            Check(!VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "value = settings[\"nested\"].setdefault(\"key\")",
                    sourcePath, 23, out line),
                "setdefault() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "value = settings.setdefault(\"key\"); settings.clear()",
                    sourcePath, 24, out line),
                "setdefault() should reject multiple statements.");
            Check(!VariableIndexExpressionLine.TryCreateDictionarySetDefaultLine(
                    "settings.setdefault(\"theme\")", sourcePath, 25, out line),
                "This restricted setdefault() feature must require an assignment target.");

            var statements = Parse(
                "label dictionary_setdefault_test:\n" +
                "    $ existing = settings.setdefault(\"theme\", \"light\")\n" +
                "    $ inserted = settings.setdefault(\"accent\", \"blue\")\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing dictionary setdefault() assignments.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The existing-key call should parse as a Python statement.").Code ==
                    "existing = settings.setdefault(\"theme\", \"light\")",
                "The parser should preserve setdefault() assignments.");
            Check(Require<PythonStatement>(label.Body[1],
                    "The missing-key call should parse as a Python statement.").Code ==
                    "inserted = settings.setdefault(\"accent\", \"blue\")",
                "The parser should preserve setdefault() assignments with defaults.");
        }

        private static void TestDictionaryCopyMethod()
        {
            const string sourcePath = "dictionary-copy-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var settings = new Dictionary<string, object>
            {
                { "theme", "dark" },
                { "count", 5f },
                { "enabled", true }
            };
            context.SetVariable("settings",
                new SimpleExpressionEngine.DataValue((object)settings));

            RenPyParser.Line line;
            Check(VariableIndexExpressionLine.TryCreateDictionaryCopyLine(
                    "copied = settings.copy()", sourcePath, 10, out line),
                "settings.copy() assignment should be recognized.");
            Check(line.GetType().Name == "DictionaryCopyLine",
                "copy() should emit the dedicated dictionary-copy instruction.");
            ((RenPyParser.Images.IApply)line).Apply(context);

            object observed;
            Check(GameBridge.TryGetLastTestLiteral("copied", out observed) &&
                    observed is Dictionary<string, object>,
                "copy() should assign a dictionary object.");
            var copied = (Dictionary<string, object>)observed;
            Check(!object.ReferenceEquals(settings, copied),
                "copy() must create a distinct dictionary instance.");
            Check(copied.Count == settings.Count &&
                    Equals(copied["theme"], "dark") &&
                    Math.Abs(Convert.ToSingle(copied["count"]) - 5f) < 0.001f &&
                    Equals(copied["enabled"], true),
                "The copy should preserve all source key/value pairs.");
            copied["theme"] = "light";
            copied["accent"] = "blue";
            Check(Equals(settings["theme"], "dark") && !settings.ContainsKey("accent"),
                "Mutating the copied dictionary must not mutate the original.");
            Check(settings.Count == 3,
                "copy() must leave the original dictionary unchanged.");

            var empty = new Dictionary<string, object>();
            context.SetVariable("empty_settings",
                new SimpleExpressionEngine.DataValue((object)empty));
            Check(VariableIndexExpressionLine.TryCreateDictionaryCopyLine(
                    "empty_copy = empty_settings.copy()", sourcePath, 11, out line),
                "copy() should work for an empty dictionary.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("empty_copy", out observed) &&
                    observed is Dictionary<string, object> emptyCopy &&
                    emptyCopy.Count == 0 && !object.ReferenceEquals(empty, emptyCopy),
                "Copying an empty dictionary should produce an independent empty dictionary.");

            Check(!VariableIndexExpressionLine.TryCreateDictionaryCopyLine(
                    "copied = settings.copy(extra)", sourcePath, 20, out line),
                "copy() should reject arguments.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryCopyLine(
                    "copied = settings.copy().clear()", sourcePath, 21, out line),
                "copy() should reject chained calls.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryCopyLine(
                    "settings.copy()", sourcePath, 22, out line),
                "The restricted copy() feature must require an assignment target.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryCopyLine(
                    "copied = settings[\"nested\"].copy()", sourcePath, 23, out line),
                "copy() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateDictionaryCopyLine(
                    "copied = settings.copy(); settings.clear()", sourcePath, 24, out line),
                "copy() should reject multiple statements.");

            var statements = Parse(
                "label dictionary_copy_test:\n" +
                "    $ copied = settings.copy()\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing a dictionary copy assignment.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The copy call should parse as a Python statement.").Code ==
                    "copied = settings.copy()",
                "The parser should preserve dictionary copy assignments.");
        }

        private static void TestListCountMethod()
        {
            const string sourcePath = "list-count-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var inventory = new List<object>
            {
                "battery", "key", "battery", "battery", 2f, 2f, true, null
            };
            context.SetVariable("inventory",
                new SimpleExpressionEngine.DataValue((object)inventory));

            RenPyParser.Line line;
            Check(VariableIndexExpressionLine.TryCreateListCountLine(
                    "battery_count = inventory.count(\"battery\")", sourcePath, 3, out line),
                "count() with a string literal should be recognized.");
            Check(line.GetType().Name == "ListCountLine",
                "count() should emit a dedicated list-count instruction.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            object observed;
            Check(GameBridge.TryGetLastTestLiteral("battery_count", out observed) &&
                    Convert.ToInt32(observed) == 3,
                "count() should count every matching string occurrence.");

            Check(VariableIndexExpressionLine.TryCreateListCountLine(
                    "number_count = inventory.count(2)", sourcePath, 4, out line),
                "count() should accept a numeric literal.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("number_count", out observed) &&
                    Convert.ToInt32(observed) == 2,
                "count() should compare numeric scalars by value.");

            Check(VariableIndexExpressionLine.TryCreateListCountLine(
                    "true_count = inventory.count(True)", sourcePath, 5, out line),
                "count() should accept a boolean literal.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("true_count", out observed) &&
                    Convert.ToInt32(observed) == 1,
                "count() should distinguish booleans from numeric values.");

            Check(VariableIndexExpressionLine.TryCreateListCountLine(
                    "none_count = inventory.count(None)", sourcePath, 6, out line),
                "count() should accept None.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("none_count", out observed) &&
                    Convert.ToInt32(observed) == 1,
                "count(None) should count null entries.");

            Check(VariableIndexExpressionLine.TryCreateListCountLine(
                    "absent_count = inventory.count(\"absent\")", sourcePath, 7, out line),
                "count() should recognize a value not present in the list.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("absent_count", out observed) &&
                    Convert.ToInt32(observed) == 0,
                "count() should return zero when no elements match.");

            Check(!VariableIndexExpressionLine.TryCreateListCountLine(
                    "value_count = inventory.count(value_name)", sourcePath, 20, out line),
                "This restricted count() implementation should reject variable arguments.");
            Check(!VariableIndexExpressionLine.TryCreateListCountLine(
                    "value_count = inventory.count(\"battery\", \"key\")", sourcePath, 21, out line),
                "count() should reject more than one argument.");
            Check(!VariableIndexExpressionLine.TryCreateListCountLine(
                    "value_count = inventory[0].count(\"battery\")", sourcePath, 22, out line),
                "count() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateListCountLine(
                    "inventory.count(\"battery\")", sourcePath, 23, out line),
                "This restricted count() feature should require an assignment target.");
            Check(!VariableIndexExpressionLine.TryCreateListCountLine(
                    "value_count = inventory.count(\"battery\"); inventory.reverse()", sourcePath, 24, out line),
                "count() should reject multiple statements.");

            var statements = Parse(
                "label list_count_test:\n" +
                "    $ battery_count = inventory.count(\"battery\")\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing list count().");
            Check(Require<PythonStatement>(label.Body[0],
                    "The count() call should parse as a Python statement.").Code ==
                    "battery_count = inventory.count(\"battery\")",
                "The parser should preserve list count assignments.");
        }

        private static void TestListReverseMethod()
        {
            const string sourcePath = "list-reverse-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var inventory = new List<object> { "key", "flashlight", "battery", "medkit" };
            context.SetVariable("inventory",
                new SimpleExpressionEngine.DataValue((object)inventory));

            RenPyParser.Line line;
            Check(VariableIndexExpressionLine.TryCreateListReverseLine(
                    "inventory.reverse()", sourcePath, 3, out line),
                "inventory.reverse() should be recognized.");
            Check(line.GetType().Name == "ListReverseLine",
                "reverse() should emit the dedicated list-reverse instruction.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(inventory.SequenceEqual(new object[] { "medkit", "battery", "flashlight", "key" }),
                "reverse() should reverse the list in place.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(inventory.SequenceEqual(new object[] { "key", "flashlight", "battery", "medkit" }),
                "Calling reverse() a second time should restore the original order.");

            var empty = new List<object>();
            context.SetVariable("empty_inventory",
                new SimpleExpressionEngine.DataValue((object)empty));
            Check(VariableIndexExpressionLine.TryCreateListReverseLine(
                    "empty_inventory.reverse()", sourcePath, 4, out line),
                "reverse() should recognize an empty-list call.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(empty.Count == 0, "Reversing an empty list should leave it empty.");

            Check(!VariableIndexExpressionLine.TryCreateListReverseLine(
                    "inventory.reverse(1)", sourcePath, 10, out line),
                "reverse() should reject arguments.");
            Check(!VariableIndexExpressionLine.TryCreateListReverseLine(
                    "inventory[0].reverse()", sourcePath, 11, out line),
                "reverse() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateListReverseLine(
                    "inventory.reverse().clear()", sourcePath, 12, out line),
                "reverse() should reject chained calls.");
            Check(!VariableIndexExpressionLine.TryCreateListReverseLine(
                    "inventory.reverse(); inventory.clear()", sourcePath, 13, out line),
                "reverse() should reject multiple statements.");
            Check(!VariableIndexExpressionLine.TryCreateListReverseLine(
                    "settings.clear()", sourcePath, 14, out line),
                "The reverse recognizer must not capture other method names.");

            var statements = Parse(
                "label list_reverse_test:\n" +
                "    $ inventory.reverse()\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing a list reverse call.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The reverse call should parse as a Python statement.").Code == "inventory.reverse()",
                "The parser should preserve list reverse calls.");
        }

        private static void TestListExtendMethod()
        {
            const string sourcePath = "list-extend-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var inventory = new List<object> { "key", "flashlight" };
            var rewards = new List<object> { "battery", "medkit" };
            context.SetVariable("inventory",
                new SimpleExpressionEngine.DataValue((object)inventory));
            context.SetVariable("rewards",
                new SimpleExpressionEngine.DataValue((object)rewards));

            RenPyParser.Line line;
            Check(VariableIndexExpressionLine.TryCreateListExtendLine(
                    "inventory.extend(rewards)", sourcePath, 10, out line),
                "inventory.extend(rewards) should be recognized.");
            Check(line.GetType().Name == "ListExtendLine",
                "extend() should emit the dedicated list-extend instruction.");
            ((RenPyParser.Images.IApply)line).Apply(context);

            Check(inventory.Count == 4,
                "extend() should append all source items to the target list.");
            Check((string)inventory[0] == "key" &&
                    (string)inventory[1] == "flashlight" &&
                    (string)inventory[2] == "battery" &&
                    (string)inventory[3] == "medkit",
                "extend() should preserve the order of the original and appended items.");
            Check(rewards.Count == 2 && (string)rewards[0] == "battery" &&
                    (string)rewards[1] == "medkit",
                "extend() must not change the source list.");

            var selfExtending = new List<object> { "a", "b" };
            context.SetVariable("self_extending",
                new SimpleExpressionEngine.DataValue((object)selfExtending));
            Check(VariableIndexExpressionLine.TryCreateListExtendLine(
                    "self_extending.extend(self_extending)", sourcePath, 20, out line),
                "A list should be allowed to extend itself.");
            ((RenPyParser.Images.IApply)line).Apply(context);
            Check(selfExtending.Count == 4 &&
                    (string)selfExtending[0] == "a" &&
                    (string)selfExtending[1] == "b" &&
                    (string)selfExtending[2] == "a" &&
                    (string)selfExtending[3] == "b",
                "Self-extension should append a snapshot of the original items exactly once.");

            Check(!VariableIndexExpressionLine.TryCreateListExtendLine(
                    "inventory.extend(\"battery\")", sourcePath, 30, out line),
                "extend() should reject literal arguments.");
            Check(!VariableIndexExpressionLine.TryCreateListExtendLine(
                    "inventory.extend(rewards[0])", sourcePath, 31, out line),
                "extend() should reject indexed arguments.");
            Check(!VariableIndexExpressionLine.TryCreateListExtendLine(
                    "inventory[0].extend(rewards)", sourcePath, 32, out line),
                "extend() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateListExtendLine(
                    "inventory.extend(rewards, self_extending)", sourcePath, 33, out line),
                "extend() should reject multiple arguments.");
            Check(!VariableIndexExpressionLine.TryCreateListExtendLine(
                    "inventory.extend(rewards); inventory.clear()", sourcePath, 34, out line),
                "extend() should reject multiple statements.");
            Check(!VariableIndexExpressionLine.TryCreateListExtendLine(
                    "inventory.append(\"battery\")", sourcePath, 35, out line),
                "The extend recognizer must not capture append calls.");

            var statements = Parse(
                "label extend_test:\n" +
                "    $ inventory.extend(rewards)\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing an extend() call.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The extend call should parse as a Python statement.").Code == "inventory.extend(rewards)",
                "The parser should preserve the list extend() call.");
        }

        private static void TestListInsertAndPopMethods()
        {
            const string sourcePath = "list-insert-pop-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var inventory = new List<object> { "key", "medkit" };
            context.SetVariable("inventory",
                new SimpleExpressionEngine.DataValue((object)inventory));

            const string insertPrefix = "_storyapi_insert_test";
            context.SetVariable(insertPrefix + "_insert_index",
                new SimpleExpressionEngine.DataValue(1f));
            context.SetVariable(insertPrefix + "_insert_value",
                new SimpleExpressionEngine.DataValue("flashlight"));

            List<RenPyParser.Line> lines;
            Check(VariableIndexExpressionLine.TryCreateListInsertLines(
                    "inventory.insert(1, \"flashlight\")", insertPrefix, sourcePath, 10, 100,
                    out lines),
                "inventory.insert(index, value) should be recognized.");
            Check(lines.Last().GetType().Name == "ListInsertLine",
                "insert() should emit the dedicated list-insert instruction.");
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(inventory.Count == 3 && (string)inventory[0] == "key" &&
                    (string)inventory[1] == "flashlight" && (string)inventory[2] == "medkit",
                "insert() should place the value at the requested index.");

            context.SetVariable(insertPrefix + "_insert_index",
                new SimpleExpressionEngine.DataValue(-1f));
            context.SetVariable(insertPrefix + "_insert_value",
                new SimpleExpressionEngine.DataValue("battery"));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(inventory.Count == 4 && (string)inventory[2] == "battery",
                "insert() should clamp a negative index relative to the end.");

            context.SetVariable(insertPrefix + "_insert_index",
                new SimpleExpressionEngine.DataValue(100f));
            context.SetVariable(insertPrefix + "_insert_value",
                new SimpleExpressionEngine.DataValue("map"));
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check((string)inventory[inventory.Count - 1] == "map",
                "insert() should clamp an index above Count to the end.");

            Check(!VariableIndexExpressionLine.TryCreateListInsertLines(
                    "inventory.insert(1)", insertPrefix, sourcePath, 11, 100, out lines),
                "insert() should reject a missing value argument.");
            Check(!VariableIndexExpressionLine.TryCreateListInsertLines(
                    "inventory.insert(1, \"a\", \"b\")", insertPrefix, sourcePath, 12, 100, out lines),
                "insert() should reject more than two arguments.");
            Check(!VariableIndexExpressionLine.TryCreateListInsertLines(
                    "inventory[0].insert(1, \"a\")", insertPrefix, sourcePath, 13, 100, out lines),
                "insert() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateListInsertLines(
                    "inventory.insert(inventory[0], \"a\")", insertPrefix, sourcePath, 14, 100, out lines),
                "insert() should reject collection reads in the index expression.");

            // pop() both removes the final item and can assign its scalar result.
            var popInventory = new List<object> { "key", "battery" };
            context.SetVariable("inventory", new SimpleExpressionEngine.DataValue((object)popInventory));

            const string popPrefix = "_storyapi_pop_test";
            Check(VariableIndexExpressionLine.TryCreateListPopLines(
                    "last_item = inventory.pop()", popPrefix, sourcePath, 20, out lines),
                "Assignment from pop() should be recognized.");
            Check(lines.Last().GetType().Name == "ListPopLine",
                "pop() should emit the dedicated list-pop instruction.");
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            object removed;
            Check(GameBridge.TryGetLastTestLiteral("last_item", out removed) &&
                    string.Equals(removed as string, "battery", StringComparison.Ordinal),
                "Assignment from pop() should preserve the popped scalar value.");
            Check(popInventory.Count == 1 && (string)popInventory[0] == "key",
                "pop() with no index should remove the final item.");

            context.SetVariable(popPrefix + "_pop_index",
                new SimpleExpressionEngine.DataValue(0f));
            Check(VariableIndexExpressionLine.TryCreateListPopLines(
                    "first_item = inventory.pop(0)", popPrefix, sourcePath, 21, out lines),
                "pop(index) assignment should be recognized.");
            ((RenPyParser.Images.IApply)lines.Last()).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("first_item", out removed) &&
                    string.Equals(removed as string, "key", StringComparison.Ordinal),
                "pop(index) should assign the value at the requested index.");
            Check(popInventory.Count == 0,
                "pop(index) should remove the requested item.");

            Check(!VariableIndexExpressionLine.TryCreateListPopLines(
                    "item = inventory.pop(0, 1)", popPrefix, sourcePath, 22, out lines),
                "pop() should reject multiple arguments.");
            Check(!VariableIndexExpressionLine.TryCreateListPopLines(
                    "inventory[0].pop()", popPrefix, sourcePath, 23, out lines),
                "pop() should reject indexed targets.");
            Check(!VariableIndexExpressionLine.TryCreateListPopLines(
                    "item = inventory.pop(inventory[0])", popPrefix, sourcePath, 24, out lines),
                "pop() should reject collection reads in the index expression.");

            var statements = Parse(
                "label insert_pop_test:\n" +
                "    $ inventory.insert(1, \"flashlight\")\n" +
                "    $ item = inventory.pop()\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0],
                "Expected a label containing insert() and pop() calls.");
            Check(Require<PythonStatement>(label.Body[0],
                    "The insert call should parse as a Python statement.").Code ==
                    "inventory.insert(1, \"flashlight\")",
                "The parser should preserve insert() calls.");
            Check(Require<PythonStatement>(label.Body[1],
                    "The pop assignment should parse as a Python statement.").Code == "item = inventory.pop()",
                "The parser should preserve pop() assignments.");
        }

        private static void TestConditionalBranches()
        {
            var statements = Parse(
                "label branch_test:\n" +
                "    if score > 10:\n" +
                "        _ \"high\"\n" +
                "    elif score > 0:\n" +
                "        _ \"low\"\n" +
                "    else:\n" +
                "        _ \"zero\"\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0], "Expected label.");
            var branch = Require<IfStatement>(label.Body[0], "Expected if statement.");

            Check(branch.Branches.Count == 3, "Expected if, elif and else branches.");
            Check(branch.Branches[0].Condition == "score > 10", "if condition mismatch.");
            Check(branch.Branches[1].Condition == "score > 0", "elif condition mismatch.");
            Check(branch.Branches[2].Condition == null, "else branch should have no condition.");
            Check(branch.Branches.All(b => b.Body.Count == 1), "Each branch should contain one statement.");
        }

        private static void TestMenuChoices()
        {
            var statements = Parse(
                "label menu_test:\n" +
                "    menu \"Choose one\":\n" +
                "        \"Continue\":\n" +
                "            _ \"Continuing.\"\n" +
                "        \"Leave\":\n" +
                "            jump goodbye\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0], "Expected label.");
            var menu = Require<MenuStatement>(label.Body[0], "Expected menu.");

            Check(menu.Caption == "Choose one", "Menu caption mismatch.");
            Check(menu.Choices.Count == 2, "Expected two menu choices.");
            Check(menu.Choices[0].Caption == "Continue", "First choice caption mismatch.");
            Check(menu.Choices[0].Body.Count == 1 &&
                Require<SayStatement>(menu.Choices[0].Body[0], "Expected dialogue in first choice.").Text == "Continuing.",
                "First choice body mismatch.");
            Require<JumpStatement>(menu.Choices[1].Body[0], "Second choice should jump.");
        }

        private static void TestWhileAndLoopControl()
        {
            var statements = Parse(
                "label loop_test:\n" +
                "    while points < 3:\n" +
                "        $ points += 1\n" +
                "        if points == 2:\n" +
                "            continue\n" +
                "        else:\n" +
                "            break\n" +
                "        pass\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0], "Expected label.");
            var loop = Require<WhileStatement>(label.Body[0], "Expected while.");
            Check(loop.Condition == "points < 3", "While condition mismatch.");
            Require<PythonStatement>(loop.Body[0], "Expected assignment in loop.");
            var branch = Require<IfStatement>(loop.Body[1], "Expected if inside loop.");
            Check(Require<LoopControlStatement>(branch.Branches[0].Body[0],
                "Expected continue.").IsContinue, "First loop control should be continue.");
            Check(!Require<LoopControlStatement>(branch.Branches[1].Body[0],
                "Expected break.").IsContinue, "Else loop control should be break.");
            Require<PassStatement>(loop.Body[2], "Expected pass.");
        }

        private static void TestForListIteration()
        {
            var statements = Parse(
                "label for_test:\n" +
                "    for item in inventory:\n" +
                "        if item == \"skip\":\n" +
                "            continue\n" +
                "        if item == \"stop\":\n" +
                "            break\n" +
                "        _ \"[item]\"\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0], "Expected label.");
            var loop = Require<ForStatement>(label.Body[0], "Expected for loop.");
            Check(loop.Variable == "item", "for-loop variable mismatch.");
            Check(loop.Collection == "inventory", "for-loop collection mismatch.");
            Check(loop.Body.Count == 3, "Expected if/if/dialogue in the for-loop body.");
            var continueBranch = Require<IfStatement>(loop.Body[0], "Expected first if.");
            Check(Require<LoopControlStatement>(continueBranch.Branches[0].Body[0],
                "Expected continue.").IsContinue, "The first branch should continue.");
            var breakBranch = Require<IfStatement>(loop.Body[1], "Expected second if.");
            Check(!Require<LoopControlStatement>(breakBranch.Branches[0].Body[0],
                "Expected break.").IsContinue, "The second branch should break.");
            Require<SayStatement>(loop.Body[2], "Expected dialogue.");

            var empty = Parse(
                "label empty_for_test:\n" +
                "    for item in inventory:\n" +
                "        pass\n" +
                "    return\n");
            var emptyLabel = Require<LabelStatement>(empty[0], "Expected empty-for label.");
            var emptyLoop = Require<ForStatement>(emptyLabel.Body[0], "Expected empty for loop.");
            Check(emptyLoop.Body.Count == 1 && emptyLoop.Body[0] is PassStatement,
                "An empty-list loop body should parse normally.");
            ExpectSyntaxError(
                "label malformed_for:\n" +
                "    for item inventory:\n" +
                "        pass\n",
                "Expected 'in'");
        }

        private static void TestForDictionaryIteration()
        {
            var statements = Parse(
                "label for_dict_test:\n" +
                "    for key in settings:\n" +
                "        if key in settings:\n" +
                "            pass\n" +
                "        _ \"[key]\"\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0], "Expected dictionary-loop label.");
            var loop = Require<ForStatement>(label.Body[0], "Expected dictionary for loop.");
            Check(loop.Variable == "key", "Dictionary loop variable mismatch.");
            Check(loop.Collection == "settings", "Dictionary loop collection mismatch.");
            Check(!loop.IsRange, "Dictionary loop should not parse as a range.");
            Check(loop.Body.Count == 2, "Expected membership check and dialogue in dictionary loop.");
            Require<IfStatement>(loop.Body[0], "Expected dictionary membership condition.");
            Require<SayStatement>(loop.Body[1], "Expected dialogue in dictionary loop.");
        }

        private static void TestForDictionaryItemsIteration()
        {
            var statements = Parse(
                "label for_dict_items_test:\n" +
                "    for key, value in settings.items():\n" +
                "        if value > 0:\n" +
                "            pass\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0], "Expected dictionary-items label.");
            var loop = Require<ForStatement>(label.Body[0], "Expected dictionary-items loop.");
            Check(loop.Variable == "key" && loop.ValueVariable == "value",
                "Dictionary.items() should preserve both loop variable names.");
            Check(loop.Collection == "settings" && loop.IsDictionaryItems && !loop.IsRange,
                "Dictionary.items() should be represented as a dictionary entry loop.");
            Check(loop.Body.Count == 1 && loop.Body[0] is IfStatement,
                "The dictionary-items loop body should parse normally.");

            ExpectSyntaxError(
                "label dict_items_missing_value:\n" +
                "    for key in settings.items():\n" +
                "        pass\n",
                "requires 'for key, value");
            ExpectSyntaxError(
                "label dict_items_two_names_list:\n" +
                "    for key, value in settings:\n" +
                "        pass\n",
                "require dictionary.items()");
            ExpectSyntaxError(
                "label dict_items_three_names:\n" +
                "    for key, value, extra in settings.items():\n" +
                "        pass\n",
                "Only two loop variables");

            const string sourcePath = "dictionary-items-runtime-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var settings = new Dictionary<string, object>
            {
                { "alpha", 2f },
                { "beta", 4f }
            };
            context.SetVariable("settings", new SimpleExpressionEngine.DataValue((object)settings));

            RenPyParser.Line preparation;
            Check(VariableIndexExpressionLine.TryCreateForEachPreparationLine(
                    "settings", "_stage19_items", true, sourcePath, 12, out preparation),
                "The runtime should create a dictionary-items preparation line.");
            ((RenPyParser.Images.IApply)preparation).Apply(context);

            object preparedValue;
            Check(GameBridge.TryGetLastTestLiteral("_stage19_items", out preparedValue) &&
                    preparedValue is List<object>,
                "Dictionary-items preparation should snapshot entries into an iterable list.");
            var prepared = (List<object>)preparedValue;
            Check(prepared.Count == 2 &&
                    prepared.All(item => item is object[] pair && pair.Length == 2),
                "Each snapshot item should contain exactly one key and one value.");
            context.SetVariable("_stage19_items",
                new SimpleExpressionEngine.DataValue((object)prepared));

            RenPyParser.Line itemLine;
            Check(VariableIndexExpressionLine.TryCreateForEachItemLine(
                    "key", "value", "_stage19_items", "_stage19_index", "_stage19_gate",
                    sourcePath, 13, out itemLine),
                "The runtime should create a key/value assignment line.");
            context.SetVariable("_stage19_index", new SimpleExpressionEngine.DataValue(0f));
            ((RenPyParser.Images.IApply)itemLine).Apply(context);
            object observedKey, observedValue;
            Check(GameBridge.TryGetLastTestLiteral("key", out observedKey) &&
                    (string)observedKey == "alpha",
                "The first iteration should assign the first dictionary key.");
            Check(GameBridge.TryGetLastTestLiteral("value", out observedValue) &&
                    Math.Abs(Convert.ToSingle(observedValue) - 2f) < 0.001f,
                "The first iteration should assign the corresponding dictionary value.");

            context.SetVariable("_stage19_index", new SimpleExpressionEngine.DataValue(1f));
            ((RenPyParser.Images.IApply)itemLine).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("key", out observedKey) &&
                    (string)observedKey == "beta",
                "The second iteration should advance to the next dictionary key.");
            Check(GameBridge.TryGetLastTestLiteral("value", out observedValue) &&
                    Math.Abs(Convert.ToSingle(observedValue) - 4f) < 0.001f,
                "The second iteration should assign the matching dictionary value.");
        }

        private static void TestForDictionaryValuesIteration()
        {
            var statements = Parse(
                "label for_dict_values_test:\n" +
                "    for value in settings.values():\n" +
                "        if value > 0:\n" +
                "            pass\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0], "Expected dictionary-values label.");
            var loop = Require<ForStatement>(label.Body[0], "Expected dictionary-values loop.");
            Check(loop.Variable == "value" && loop.ValueVariable == null,
                "Dictionary.values() should use a single loop variable.");
            Check(loop.Collection == "settings" && loop.IsDictionaryValues &&
                    !loop.IsDictionaryItems && !loop.IsRange,
                "Dictionary.values() should be represented as a values-only loop.");
            Check(loop.Body.Count == 1 && loop.Body[0] is IfStatement,
                "The dictionary-values loop body should parse normally.");

            ExpectSyntaxError(
                "label dict_values_two_names:\n" +
                "    for key, value in settings.values():\n" +
                "        pass\n",
                "accepts only one loop variable");
            ExpectSyntaxError(
                "label dict_values_items_mismatch:\n" +
                "    for value in settings.items():\n" +
                "        pass\n",
                "requires 'for key, value");

            const string sourcePath = "dictionary-values-runtime-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var settings = new Dictionary<string, object>
            {
                { "alpha", 2f },
                { "beta", 4f }
            };
            context.SetVariable("settings", new SimpleExpressionEngine.DataValue((object)settings));

            RenPyParser.Line preparation;
            Check(VariableIndexExpressionLine.TryCreateForEachPreparationLine(
                    "settings", "_stage20_values", false, true, sourcePath, 22, out preparation),
                "The runtime should create a dictionary-values preparation line.");
            ((RenPyParser.Images.IApply)preparation).Apply(context);

            object preparedValue;
            Check(GameBridge.TryGetLastTestLiteral("_stage20_values", out preparedValue) &&
                    preparedValue is List<object>,
                "Dictionary.values() should snapshot values into an iterable list.");
            var prepared = (List<object>)preparedValue;
            Check(prepared.Count == 2 && prepared.Contains(2f) && prepared.Contains(4f),
                "The values snapshot should contain every dictionary value exactly once.");
            context.SetVariable("_stage20_values",
                new SimpleExpressionEngine.DataValue((object)prepared));

            RenPyParser.Line itemLine;
            Check(VariableIndexExpressionLine.TryCreateForEachItemLine(
                    "value", "_stage20_values", "_stage20_index", "_stage20_gate",
                    sourcePath, 23, out itemLine),
                "The runtime should create a single-value assignment line.");
            context.SetVariable("_stage20_index", new SimpleExpressionEngine.DataValue(0f));
            ((RenPyParser.Images.IApply)itemLine).Apply(context);
            object observedValue;
            Check(GameBridge.TryGetLastTestLiteral("value", out observedValue) &&
                    Equals(observedValue, prepared[0]),
                "The first iteration should assign the first snapshotted value.");

            context.SetVariable("_stage20_index", new SimpleExpressionEngine.DataValue(1f));
            ((RenPyParser.Images.IApply)itemLine).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("value", out observedValue) &&
                    Equals(observedValue, prepared[1]),
                "The second iteration should assign the next snapshotted value.");
        }

        private static void TestForDictionaryKeysMethodIteration()
        {
            var statements = Parse(
                "label for_dict_keys_method_test:\n" +
                "    for key in settings.keys():\n" +
                "        pass\n" +
                "    return\n");
            var label = Require<LabelStatement>(statements[0], "Expected dictionary-keys label.");
            var loop = Require<ForStatement>(label.Body[0], "Expected dictionary.keys() loop.");
            Check(loop.Variable == "key" && loop.ValueVariable == null,
                "Dictionary.keys() should use a single loop variable.");
            Check(loop.Collection == "settings" && loop.IsDictionaryKeys &&
                    !loop.IsDictionaryItems && !loop.IsDictionaryValues && !loop.IsRange,
                "Dictionary.keys() should be represented as an explicit keys-only loop.");

            ExpectSyntaxError(
                "label dict_keys_two_names:\n" +
                "    for key, value in settings.keys():\n" +
                "        pass\n",
                "accepts only one loop variable");

            const string sourcePath = "dictionary-keys-runtime-test.rpy";
            var context = new RenpyParser.DynamicIndexTestContext();
            var settings = new Dictionary<string, object>
            {
                { "alpha", 2f },
                { "beta", 4f }
            };
            context.SetVariable("settings", new SimpleExpressionEngine.DataValue((object)settings));

            RenPyParser.Line preparation;
            Check(VariableIndexExpressionLine.TryCreateForEachPreparationLine(
                    "settings", "_stage21_keys", false, false, true, sourcePath, 30, out preparation),
                "The runtime should create a dictionary-keys preparation line.");
            ((RenPyParser.Images.IApply)preparation).Apply(context);

            object preparedValue;
            Check(GameBridge.TryGetLastTestLiteral("_stage21_keys", out preparedValue) &&
                    preparedValue is List<object>,
                "Dictionary.keys() should snapshot keys into an iterable list.");
            var prepared = (List<object>)preparedValue;
            Check(prepared.Count == 2 && prepared.Contains("alpha") && prepared.Contains("beta"),
                "The key snapshot should contain each dictionary key exactly once.");

            settings["gamma"] = 6f;
            Check(prepared.Count == 2 && !prepared.Contains("gamma"),
                "Keys added after preparation must not enter the active snapshot.");

            context.SetVariable("settings",
                new SimpleExpressionEngine.DataValue((object)new List<object> { "not", "a", "dictionary" }));
            ((RenPyParser.Images.IApply)preparation).Apply(context);
            Check(GameBridge.TryGetLastTestLiteral("_stage21_keys", out preparedValue) &&
                    preparedValue is List<object> rejected && rejected.Count == 0,
                "dictionary.keys() must not silently iterate a list source.");
        }

        private static void TestForRangeIteration()
        {
            var oneArgument = Parse(
                "label range_one:\n" +
                "    for i in range(5):\n" +
                "        pass\n" +
                "    return\n");
            var one = Require<ForStatement>(
                Require<LabelStatement>(oneArgument[0], "Expected one-arg range label.").Body[0],
                "Expected one-arg range loop.");
            Check(one.IsRange && one.RangeStart == "0" && one.RangeStop == "5" && one.RangeStep == "1",
                "range(stop) should normalize to start=0 and step=1.");

            var twoArgument = Parse(
                "label range_two:\n" +
                "    for i in range(2, 8):\n" +
                "        pass\n" +
                "    return\n");
            var two = Require<ForStatement>(
                Require<LabelStatement>(twoArgument[0], "Expected two-arg range label.").Body[0],
                "Expected two-arg range loop.");
            Check(two.IsRange && two.RangeStart == "2" && two.RangeStop == "8" && two.RangeStep == "1",
                "range(start, stop) arguments mismatch.");

            var threeArgument = Parse(
                "label range_three:\n" +
                "    for i in range(8, 1, -2):\n" +
                "        pass\n" +
                "    return\n");
            var three = Require<ForStatement>(
                Require<LabelStatement>(threeArgument[0], "Expected three-arg range label.").Body[0],
                "Expected three-arg range loop.");
            Check(three.IsRange && three.RangeStart == "8" && three.RangeStop == "1" &&
                three.RangeStep == "-2", "range(start, stop, negative_step) arguments mismatch.");

            var expressions = Parse(
                "label range_expressions:\n" +
                "    for i in range(base + 1, limit * 2, -stride):\n" +
                "        pass\n" +
                "    return\n");
            var expressionLoop = Require<ForStatement>(
                Require<LabelStatement>(expressions[0], "Expected expression range label.").Body[0],
                "Expected expression range loop.");
            Check(expressionLoop.RangeStart == "base + 1" && expressionLoop.RangeStop == "limit * 2" &&
                expressionLoop.RangeStep == "-stride", "Scalar range expressions should be preserved.");

            var emptyRange = Parse(
                "label range_empty:\n" +
                "    for i in range(0):\n" +
                "        pass\n" +
                "    return\n");
            var empty = Require<ForStatement>(
                Require<LabelStatement>(emptyRange[0], "Expected empty range label.").Body[0],
                "Expected empty range loop.");
            Check(empty.RangeStart == "0" && empty.RangeStop == "0",
                "range(0) should preserve the empty [0, 0) interval.");

            ExpectSyntaxError(
                "label range_zero_args:\n" +
                "    for i in range():\n" +
                "        pass\n",
                "requires one, two, or three");
            ExpectSyntaxError(
                "label range_four_args:\n" +
                "    for i in range(1, 2, 3, 4):\n" +
                "        pass\n",
                "requires one, two, or three");
            ExpectSyntaxError(
                "label range_missing_arg:\n" +
                "    for i in range(1, , 3):\n" +
                "        pass\n",
                "cannot be empty");
        }

        private static void TestAssetPaths()
        {
            var statements = Parse(
                "image room = images/room.png\n" +
                "audio theme = audio/theme.ogg\n" +
                "label assets:\n" +
                "    return\n");
            var image = Require<ImageStatement>(statements[0], "Expected image declaration.");
            var audio = Require<AudioStatement>(statements[1], "Expected audio declaration.");

            Check(image.Name == "room" && image.File == "images/room.png",
                "Image path should be preserved.");
            Check(audio.Name == "theme" && audio.File == "audio/theme.ogg",
                "Audio path should be preserved.");
        }

        private static void TestCommentsAndBlankLines()
        {
            var statements = Parse(
                "# first comment\n" +
                "\n" +
                "label comments:\n" +
                "    # inner comment\n" +
                "    _ \"visible\" # trailing comment\n" +
                "\n" +
                "    return\n");

            var label = Require<LabelStatement>(statements[0], "Expected label after comments.");
            Check(label.Body.Count == 2, "Comments/blank lines should not create AST nodes.");
        }

        private static void TestMalformedScripts()
        {
            ExpectSyntaxError("label missing_colon\n    return\n", "Expected ':'");
            ExpectSyntaxError("menu_test:\n", "Only");
            ExpectSyntaxError(
                "label bad_menu:\n" +
                "    menu:\n" +
                "        jump nowhere\n",
                "menu choice must start with quoted text");
            ExpectSyntaxError(
                "label trailing_token:\n" +
                "    show monika at left unexpected\n",
                "Unexpected");
            ExpectSyntaxError("label unterminated:\n    _ \"no end\n", "Unterminated string.");

            try
            {
                Parse("label indentation:\n    _ \"a\"\n  return\n");
                throw new InvalidOperationException("Expected inconsistent indentation to fail.");
            }
            catch (ScriptSyntaxException e)
            {
                Check(e.Message.IndexOf("indentation", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Expected an inconsistent indentation diagnostic.");
            }
        }

        private static void TestExampleScripts()
        {
            string root = Path.Combine(Environment.CurrentDirectory, "mods", "example");
            Check(Directory.Exists(root), "Run tests from the repository root; missing " + root + ".");

            string[] files = Directory.GetFiles(root, "*.rpy", SearchOption.AllDirectories);
            Check(files.Length > 0, "No example .rpy scripts were found.");

            var failures = new List<string>();
            foreach (string file in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Parse(File.ReadAllText(file), file);
                }
                catch (Exception e)
                {
                    failures.Add(Path.GetFileName(file) + ": " + e.Message);
                }
            }

            Check(failures.Count == 0,
                "Some checked-in examples no longer parse:\n" + string.Join("\n", failures));
        }
    }
}

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
            Run("indexed collection assignment", TestIndexedCollectionAssignments);
            Run("list append and remove methods", TestListMethodMutations);
            Run("indexed collection conditions", TestIndexedCollectionConditions);
            Run("list and dictionary membership conditions", TestCollectionMembershipConditions);
            Run("list and dictionary length conditions", TestCollectionLengthConditions);
            Run("list and dictionary clear methods", TestCollectionClearMethods);
            Run("if/elif/else branches", TestConditionalBranches);
            Run("menu choices and nested statements", TestMenuChoices);
            Run("while and loop-control statements", TestWhileAndLoopControl);
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

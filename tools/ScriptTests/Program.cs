using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StoryAPI.Scripting;

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
                "Index expressions should allow scalar arithmetic.");

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

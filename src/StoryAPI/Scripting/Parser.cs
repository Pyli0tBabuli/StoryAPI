using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StoryAPI.Scripting
{
    /// <summary>
    /// Reads mod script into the <see cref="Statement"/> tree.
    ///
    /// Only syntax lives here: nothing is validated against the game. A mod that
    /// references a label or an image that does not exist still parses cleanly and
    /// fails later with a proper message from the compiler, which keeps the two kinds
    /// of error ("you mistyped" vs "that asset is missing") distinguishable.
    /// </summary>
    public sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly string _source;
        private int _index;

        public Parser(string source, string path)
        {
            _source = source ?? string.Empty;
            Path = path;
            _tokens = new Lexer(_source, path).Tokenize();
        }

        public string Path { get; }

        private Token Current => _tokens[_index];

        private Token Ahead(int offset) =>
            _tokens[Math.Min(_index + offset, _tokens.Count - 1)];

        private bool AtEnd => Current.Type == TokenType.EndOfFile;

        private Token Advance() => _tokens[_index++];

        private bool MatchKeyword(string keyword)
        {
            if (Current.Type == TokenType.Keyword && Current.Text == keyword)
            {
                _index++;
                return true;
            }
            return false;
        }

        private bool MatchIdentifier(string identifier)
        {
            if ((Current.Type == TokenType.Identifier || Current.Type == TokenType.Keyword)
                && Current.Text == identifier)
            {
                _index++;
                return true;
            }
            return false;
        }

        private void ExpectColon()
        {
            if (Current.Type != TokenType.Colon)
                throw new ScriptSyntaxException("Expected ':'.", Current.Line);
            _index++;
        }

        private void SkipNewlines()
        {
            while (Current.Type == TokenType.Newline) _index++;
        }

        private void ExpectIndent()
        {
            if (Current.Type != TokenType.Indent)
                throw new ScriptSyntaxException("Expected an indented block.", Current.Line);
            _index++;
        }

        private void ExpectDedent()
        {
            if (Current.Type != TokenType.Dedent)
                throw new ScriptSyntaxException("Expected the end of a block.", Current.Line);
            _index++;
        }

        public List<Statement> ParseFile()
        {
            var result = new List<Statement>();
            SkipNewlines();

            while (!AtEnd)
            {
                if (Current.Type == TokenType.Dedent) { _index++; continue; }
                result.Add(ParseTopLevel());
                SkipNewlines();
            }

            return result;
        }

        private Statement ParseTopLevel()
        {
            int line = Current.Line;

            if (MatchKeyword("label"))
            {
                string name = ReadName("label");
                ExpectColon();
                return ParseLabelBody(name, line);
            }

            if (MatchKeyword("define")) return ParseVariableDeclaration(false, line);
            if (MatchKeyword("default")) return ParseVariableDeclaration(true, line);
            if (MatchKeyword("character")) return ParseCharacter();
            if (MatchKeyword("image")) return ParseImage();
            if (MatchKeyword("audio")) return ParseAudio();
            if (MatchKeyword("override")) return ParseOverride();
            if (MatchKeyword("hook")) return ParseHook();

            throw new ScriptSyntaxException(
                "Only 'label', 'define', 'default', 'character', 'image', 'audio', 'override' and 'hook' may " +
                "appear at the top level. Found '" + Describe(Current) + "'.", line);
        }

        /// <summary>
        /// Reads <c>hook some_block after 12345:</c> and the indented body under it.
        /// </summary>
        private Statement ParseHook()
        {
            int line = Current.Line;

            var hook = new HookStatement { Block = ReadName("block name"), Line = line };

            if (!MatchIdentifier("after"))
                throw new ScriptSyntaxException("Expected 'after' and a dialogue id after the block name.", Current.Line);

            if (Current.Type != TokenType.Number)
                throw new ScriptSyntaxException(
                    "Expected a dialogue id after 'after'. Ids are the hashes listed in the " +
                    "script reference.", Current.Line);

            hook.AfterTextId = (long)double.Parse(Advance().Text, CultureInfo.InvariantCulture);
            ExpectColon();

            SkipNewlines();
            ExpectIndent();
            SkipNewlines();

            while (Current.Type != TokenType.Dedent && !AtEnd)
            {
                hook.Body.Add(ParseStatement());
                SkipNewlines();
            }

            if (Current.Type == TokenType.Dedent) _index++;
            return hook;
        }

        private LabelStatement ParseLabelBody(string name, int line)
        {
            var label = new LabelStatement { Name = name, Line = line };

            SkipNewlines();
            ExpectIndent();
            SkipNewlines();

            while (Current.Type != TokenType.Dedent && !AtEnd)
            {
                label.Body.Add(ParseStatement());
                SkipNewlines();
            }

            if (Current.Type == TokenType.Dedent) _index++;
            return label;
        }

        private Statement ParseStatement()
        {
            int line = Current.Line;

            if (Current.Type == TokenType.Dollar)
            {
                _index++;
                return new PythonStatement { Code = ReadRestOfLine(), Line = line };
            }

            if (Current.Type == TokenType.Keyword)
            {
                switch (Current.Text)
                {
                    case "scene": return ParseScene();
                    case "show": return ParseShow();
                    case "hide": return ParseHide();
                    case "with": return ParseWith();
                    case "play": return ParsePlay();
                    case "stop": return ParseStop();
                    case "jump": return ParseJump();
                    case "call": return ParseCall();
                    case "return": _index++; return new ReturnStatement { Line = line };
                    case "pause": return ParsePause();
                    case "move": return ParseMove();
                    case "zoom": return ParseZoom();
                    case "fade": return ParseFade();
                    case "turn": return ParseTurn();
                    case "pose": return ParsePose();
                    case "layer": return ParseLayer();
                    case "while": return ParseWhile();
                    case "for": return ParseFor();
                    case "break": return ParseLoopControl(false);
                    case "continue": return ParseLoopControl(true);
                    case "pass": _index++; FinishLine(); return new PassStatement { Line = line };
                    case "if": return ParseIf();
                    case "menu": return ParseMenu();
                    case "python": return ParsePythonBlock();
                    case "define":
                    case "default":
                        throw new ScriptSyntaxException("'define' and 'default' must be declared at the top level of a file.", line);
                    case "override": return ParseOverride();
                }
            }

            // `<tag> "text"` is a spoken line. `_` means narration with no speaker.
            if (Current.Type == TokenType.Identifier || Current.Type == TokenType.Keyword)
            {
                return ParseSay();
            }

            throw new ScriptSyntaxException("Unexpected '" + Describe(Current) + "'.", line);
        }

        // ------------------------------------------------------------ statements

        private Statement ParseSay()
        {
            int line = Current.Line;
            string tag = Advance().Text;

            string variant = null;
            // `speaker happy "text"` picks a sprite pose without showing it again.
            if (Current.Type == TokenType.Identifier && Ahead(1).Type == TokenType.String)
            {
                variant = Advance().Text;
            }

            if (Current.Type != TokenType.String)
            {
                throw new ScriptSyntaxException(
                    "Expected dialogue text after '" + tag + "'.", Current.Line);
            }

            string text = Advance().Text;
            FinishLine();

            return new SayStatement
            {
                Speaker = tag == "_" ? string.Empty : tag,
                Text = text,
                Variant = variant,
                Line = line,
            };
        }

        private Statement ParseScene()
        {
            int line = Current.Line;
            _index++;   // scene

            // The whole rest of the line is taken as written. The game names a
            // background in two words - "bg club_day" - and may end the line with a
            // transition - "with Dissolve(0.5)" - so the line is split rather than
            // read word by word.
            string rest = ReadRestOfLine();

            string image;
            string transition = null;

            int at = IndexOfWord(rest, "with");
            if (at >= 0)
            {
                image = rest.Substring(0, at).Trim();
                transition = rest.Substring(at + "with".Length).Trim();
                if (string.IsNullOrEmpty(transition))
                    throw new ScriptSyntaxException("Expected a transition after 'with'.", line);
            }
            else
            {
                image = rest.Trim();
            }

            if (string.IsNullOrEmpty(image))
                throw new ScriptSyntaxException("Expected a scene name.", line);

            FinishLine();
            return new SceneStatement { Image = image, Transition = transition, Line = line };
        }

        /// <summary>
        /// Where a word stands in a piece of text, or -1 when it is not there.
        ///
        /// Only a whole word counts, so a name that merely contains the letters is
        /// not mistaken for the word itself.
        /// </summary>
        private static int IndexOfWord(string text, string word)
        {
            if (string.IsNullOrEmpty(text)) return -1;

            int at = 0;
            while (at < text.Length)
            {
                int found = text.IndexOf(word, at, StringComparison.Ordinal);
                if (found < 0) return -1;

                bool startOk = found == 0 || !char.IsLetterOrDigit(text[found - 1]);
                int after = found + word.Length;
                bool endOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);

                if (startOk && endOk) return found;
                at = found + 1;
            }
            return -1;
        }


        private Statement ParseShow()
        {
            int line = Current.Line;
            _index++;   // show
            string image = ReadName("image name");

            var statement = new ShowStatement { Image = image, Line = line };

            // `show monika 1` is an image plus a pose. A pose is any bare name that is
            // not one of the keywords that introduce an option, so it can be told
            // apart without reserving words.
            if (Current.Type == TokenType.Number ||
                (Current.Type == TokenType.Identifier && !IsShowOption(Current.Text)))
            {
                // Ren'Py image attributes may start with digits and continue with
                // letters without whitespace, e.g. "show monika 2a". The lexer emits
                // "2" and "a" as separate tokens, so join only when they were adjacent
                // in the original source. A separated option such as "at left" remains
                // a separate clause.
                statement.Variant = ReadPosePart();
            }

            if (MatchIdentifier("as"))
            {
                // `show s_kill as the_kill` names the slot. It has to be read as its own
                // clause, otherwise "as" is swallowed as a pose name and the line is left
                // with a stray word on it.
                statement.Alias = ReadName("a name to file this sprite under");
            }

            if (MatchIdentifier("at"))
            {
                statement.At = ReadName("position");
            }

            if (MatchIdentifier("with"))
            {
                statement.Transform = ReadName("transform");
            }

            if (MatchIdentifier("zorder") || MatchIdentifier("z"))
            {
                if (Current.Type != TokenType.Number)
                    throw new ScriptSyntaxException("Expected a number after 'zorder'.", Current.Line);
                statement.ZOrder = (int)double.Parse(Advance().Text, CultureInfo.InvariantCulture);
            }

            FinishLine();
            return statement;
        }

        private static bool IsShowOption(string word)
        {
            // "as" has to be listed here as well, because anything not listed is taken to
            // be a pose name. Without it, "show x as y" reads "as" as the pose and leaves
            // "y" stranded, which is a syntax error.
            return word == "as" || word == "at" || word == "with" || word == "zorder" ||
                   word == "z" || word == "onlayer" || word == "behind";
        }

        private Statement ParseHide()
        {
            int line = Current.Line;
            _index++;   // hide
            string image = ReadName("image name");
            FinishLine();
            return new HideStatement { Image = image, Line = line };
        }

        private Statement ParseWith()
        {
            int line = Current.Line;
            _index++;   // with

            // Everything after "with" is the transition. The base game writes
            // transitions as expressions - "Dissolve(2)", "ImageDissolve(...)" - and
            // they have to arrive at the game's expression engine unchanged, so the
            // text is taken from the source rather than rebuilt from tokens.
            string transition = ReadRestOfLine();
            if (string.IsNullOrEmpty(transition))
                throw new ScriptSyntaxException("Expected a transition after 'with'.", line);

            FinishLine();
            return new WithStatement { Transition = transition, Line = line };
        }


        private Statement ParsePlay()
        {
            int line = Current.Line;
            _index++;   // play

            // `play music1 fadein 2` is accepted as shorthand for the music channel;
            // a mod author supplying their own track should not have to name a channel
            // that means nothing for a file in their own folder.
            string channel = "music";
            string name;

            string first = ReadName("channel or track name");
            if (IsChannelName(first))
            {
                channel = first;
                name = ReadName("track name");
            }
            else
            {
                name = first;
            }

            var statement = new PlayStatement
            {
                Channel = NormalizeChannel(channel, line),
                Name = name,
                Line = line,
            };

            ReadChannelOptions(statement);
            FinishLine();
            return statement;
        }

        private static bool IsChannelName(string word)
        {
            return word == "music" || word == "sound" || word == "music_poem";
        }

        private Statement ParseStop()
        {
            int line = Current.Line;
            _index++;   // stop
            string channel = ReadName("channel");

            var statement = new StopStatement
            {
                Channel = NormalizeChannel(channel, line),
                Line = line,
            };

            // `stop music fadeout 1.5`
            while (Current.Type == TokenType.Identifier)
            {
                string option = Current.Text;
                _index++;
                if (option == "fadeout" || option == "fade")
                {
                    statement.FadeOut = ReadNumber();
                }
                else if (option == "loop" || option == "noloop")
                {
                    // stopping has no loop state; accept and ignore for symmetry
                }
                else
                {
                    throw new ScriptSyntaxException("Unknown stop option '" + option + "'.", Current.Line);
                }
            }

            FinishLine();
            return statement;
        }

        private void ReadChannelOptions(PlayStatement statement)
        {
            while (Current.Type == TokenType.Identifier)
            {
                string option = Current.Text;
                _index++;
                switch (option)
                {
                    case "fadein": statement.FadeIn = ReadNumber(); break;
                    case "fadeout": statement.FadeOut = ReadNumber(); break;
                    case "loop": statement.Looping = true; break;
                    case "noloop": statement.Looping = false; break;
                    default:
                        throw new ScriptSyntaxException("Unknown play option '" + option + "'.", Current.Line);
                }
            }
        }

        private Statement ParseJump()
        {
            int line = Current.Line;
            _index++;   // jump
            string target = ReadName("label to jump to");
            FinishLine();
            return new JumpStatement { Target = target, Line = line };
        }

        private Statement ParseCall()
        {
            int line = Current.Line;
            _index++;   // call
            string target = ReadName("label to call");

            var statement = new CallStatement { Target = target, Line = line };

            if (Current.Type == TokenType.Identifier && Current.Text == "with")
            {
                _index++;
            }

            if (Current.Type == TokenType.Identifier && Ahead(1).Type == TokenType.Number)
            {
                // Positional arguments, e.g. call scene(1, 2)
                _index++;   // name
                statement.Arguments.Add(ReadNumber().ToString(CultureInfo.InvariantCulture));
                while (MatchKeyword("and") || (Current.Type == TokenType.Identifier && Current.Text == ","))
                {
                    if (Current.Type == TokenType.Identifier && Current.Text == ",") _index++;
                    statement.Arguments.Add(ReadNumber().ToString(CultureInfo.InvariantCulture));
                }
            }

            FinishLine();
            return statement;
        }

        private Statement ParsePause()
        {
            int line = Current.Line;
            _index++;   // pause
            float seconds = 1f;
            if (Current.Type == TokenType.Number) seconds = ReadNumber();
            FinishLine();
            return new PauseStatement { Seconds = seconds, Line = line };
        }

        private Statement ParseWhile()
        {
            int line = Current.Line;
            _index++;   // while
            string condition = ReadRestOfLine();
            if (string.IsNullOrWhiteSpace(condition))
                throw new ScriptSyntaxException("Expected a condition after 'while'.", line);
            ExpectColon();

            var statement = new WhileStatement { Condition = condition, Line = line };
            statement.Body.AddRange(ParseBranchBody());
            return statement;
        }

        private Statement ParseFor()
        {
            int line = Current.Line;
            _index++; // for

            if (Current.Type != TokenType.Identifier)
                throw new ScriptSyntaxException("Expected a loop variable after 'for'.", line);
            string variable = Advance().Text;
            string valueVariable = null;

            // A two-name target is accepted only for dictionary .items() iteration.
            if (Current.Text == ",")
            {
                _index++;
                if (Current.Type != TokenType.Identifier)
                    throw new ScriptSyntaxException("Expected a second loop variable after ','.", Current.Line);
                valueVariable = Advance().Text;
                if (Current.Text == ",")
                    throw new ScriptSyntaxException("Only two loop variables are supported for dictionary items.", Current.Line);
            }

            if (!MatchIdentifier("in"))
                throw new ScriptSyntaxException("Expected 'in' after the loop variable.", Current.Line);

            ForStatement statement;
            if (valueVariable != null &&
                Current.Type == TokenType.Identifier && Current.Text == "range" &&
                Ahead(1).Text == "(")
                throw new ScriptSyntaxException("range() loops accept only one loop variable.", line);

            if (Current.Type == TokenType.Identifier && Current.Text == "range" &&
                Ahead(1).Text == "(")
            {
                string rangeSource = ReadRestOfLine();
                string[] rangeArguments = ParseRangeArguments(rangeSource, line);
                ExpectColon();

                string start = rangeArguments.Length == 1 ? "0" : rangeArguments[0];
                string stop = rangeArguments.Length == 1 ? rangeArguments[0] : rangeArguments[1];
                string step = rangeArguments.Length < 3 ? "1" : rangeArguments[2];
                statement = new ForStatement
                {
                    Variable = variable,
                    IsRange = true,
                    RangeStart = start,
                    RangeStop = stop,
                    RangeStep = step,
                    Line = line,
                };
            }
            else
            {
                bool isReversed = false;
                string collection;
                if (Current.Type == TokenType.Identifier && Current.Text == "reversed" &&
                    Ahead(1).Text == "(")
                {
                    if (valueVariable != null)
                        throw new ScriptSyntaxException(
                            "reversed() iteration accepts only one loop variable.", Current.Line);
                    _index += 2; // reversed(
                    if (Current.Type != TokenType.Identifier)
                        throw new ScriptSyntaxException(
                            "reversed() requires a named list variable.", Current.Line);
                    collection = Advance().Text;
                    if (Current.Text != ")")
                        throw new ScriptSyntaxException(
                            "reversed() accepts exactly one named list variable.", Current.Line);
                    _index++; // )
                    isReversed = true;
                }
                else
                {
                    if (Current.Type != TokenType.Identifier)
                        throw new ScriptSyntaxException(
                            "Expected a named list, reversed(list), or range(start, stop[, step]) after 'in'.",
                            Current.Line);
                    collection = Advance().Text;
                }
                bool isDictionaryItems = false;
                bool isDictionaryValues = false;
                bool isDictionaryKeys = false;
                if (!isReversed && Current.Text == "." &&
                    (Ahead(1).Text == "items" || Ahead(1).Text == "values" ||
                     Ahead(1).Text == "keys") &&
                    Ahead(2).Text == "(" && Ahead(3).Text == ")")
                {
                    string dictionaryMethod = Ahead(1).Text;
                    if (dictionaryMethod == "items")
                    {
                        if (valueVariable == null)
                            throw new ScriptSyntaxException(
                                "dictionary.items() iteration requires 'for key, value in dictionary.items():'.",
                                Current.Line);
                        isDictionaryItems = true;
                    }
                    else
                    {
                        if (valueVariable != null)
                            throw new ScriptSyntaxException(
                                "dictionary." + dictionaryMethod +
                                "() iteration accepts only one loop variable.", Current.Line);
                        if (dictionaryMethod == "values")
                            isDictionaryValues = true;
                        else
                            isDictionaryKeys = true;
                    }
                    _index += 4;
                }
                else if (valueVariable != null)
                {
                    throw new ScriptSyntaxException(
                        "Two loop variables require dictionary.items() iteration.", Current.Line);
                }

                if (Current.Type != TokenType.Colon)
                    throw new ScriptSyntaxException(
                        "Only a named list, reversed(list), dictionary.keys(), dictionary.items(), or dictionary.values() is supported after 'in'.",
                        Current.Line);

                ExpectColon();
                statement = new ForStatement
                {
                    Variable = variable,
                    ValueVariable = valueVariable,
                    Collection = collection,
                    IsReversed = isReversed,
                    IsDictionaryItems = isDictionaryItems,
                    IsDictionaryValues = isDictionaryValues,
                    IsDictionaryKeys = isDictionaryKeys,
                    Line = line,
                };
            }

            statement.Body.AddRange(ParseBranchBody());
            return statement;
        }

        private static string[] ParseRangeArguments(string source, int line)
        {
            string text = (source ?? string.Empty).Trim();
            int open = text.IndexOf('(');
            if (open < 0 || text.Substring(0, open).Trim() != "range" ||
                text.Length == 0 || text[text.Length - 1] != ')')
                throw new ScriptSyntaxException(
                    "Expected range(start), range(start, stop), or range(start, stop, step).", line);

            string inner = text.Substring(open + 1, text.Length - open - 2);
            var arguments = new List<string>();
            int depth = 0;
            int start = 0;
            char quote = '\0';
            bool escaped = false;

            for (int i = 0; i < inner.Length; i++)
            {
                char c = inner[i];
                if (quote != '\0')
                {
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') { escaped = true; continue; }
                    if (c == quote) quote = '\0';
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    quote = c;
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth < 0)
                        throw new ScriptSyntaxException("Unbalanced parentheses in range() arguments.", line);
                }
                else if (c == ',' && depth == 0)
                {
                    string argument = inner.Substring(start, i - start).Trim();
                    if (argument.Length == 0)
                        throw new ScriptSyntaxException("range() arguments cannot be empty.", line);
                    arguments.Add(argument);
                    start = i + 1;
                }
            }

            if (depth != 0 || quote != '\0')
                throw new ScriptSyntaxException("Unbalanced parentheses or quotes in range() arguments.", line);

            string finalArgument = inner.Substring(start).Trim();
            if (finalArgument.Length == 0 && inner.Trim().Length > 0)
                throw new ScriptSyntaxException("range() arguments cannot be empty.", line);
            if (finalArgument.Length > 0) arguments.Add(finalArgument);

            if (arguments.Count < 1 || arguments.Count > 3)
                throw new ScriptSyntaxException("range() requires one, two, or three arguments.", line);

            return arguments.ToArray();
        }

        private Statement ParseLoopControl(bool isContinue)
        {
            int line = Current.Line;
            _index++;
            FinishLine();
            return new LoopControlStatement { IsContinue = isContinue, Line = line };
        }

        private Statement ParseIf()
        {
            int line = Current.Line;
            var statement = new IfStatement { Line = line };

            _index++;   // if
            string condition = ReadRestOfLine();
            ExpectColon();

            statement.Branches.Add(new Branch
            {
                Condition = condition,
                Body = ParseBranchBody(),
            });

            SkipNewlines();
            while (true)
            {
                if (Current.Type == TokenType.Keyword && Current.Text == "elif")
                {
                    int elifLine = Current.Line;
                    _index++;
                    string elifCondition = ReadRestOfLine();
                    ExpectColon();
                    statement.Branches.Add(new Branch
                    {
                        Condition = elifCondition,
                        Body = ParseBranchBody(),
                    });
                    SkipNewlines();
                    continue;
                }

                if (Current.Type == TokenType.Keyword && (Current.Text == "else" || Current.Text == "otherwise"))
                {
                    int elseLine = Current.Line;
                    _index++;
                    if (Current.Type != TokenType.Colon) ReadRestOfLine();
                    ExpectColon();
                    statement.Branches.Add(new Branch
                    {
                        Condition = null,
                        Body = ParseBranchBody(),
                    });
                    SkipNewlines();
                    continue;
                }

                break;
            }

            return statement;
        }

        private List<Statement> ParseBranchBody()
        {
            var body = new List<Statement>();
            SkipNewlines();
            ExpectIndent();
            SkipNewlines();

            while (Current.Type != TokenType.Dedent && !AtEnd)
            {
                body.Add(ParseStatement());
                SkipNewlines();
            }

            if (Current.Type == TokenType.Dedent) _index++;
            return body;
        }

        private Statement ParseMenu()
        {
            int line = Current.Line;
            _index++;   // menu
            var statement = new MenuStatement { Line = line };

            // A prompt may follow on the same line, e.g. menu "What now?"
            if (Current.Type == TokenType.String)
            {
                statement.Caption = Advance().Text;
            }
            ExpectColon();

            SkipNewlines();
            ExpectIndent();
            SkipNewlines();

            while (Current.Type != TokenType.Dedent && !AtEnd)
            {
                int choiceLine = Current.Line;
                if (Current.Type != TokenType.String)
                {
                    throw new ScriptSyntaxException(
                        "A menu choice must start with quoted text.", Current.Line);
                }

                string caption = Advance().Text;
                ExpectColon();

                var choice = new MenuChoice { Caption = caption, Line = choiceLine };

                SkipNewlines();
                ExpectIndent();
                SkipNewlines();

                while (Current.Type != TokenType.Dedent && !AtEnd)
                {
                    choice.Body.Add(ParseStatement());
                    SkipNewlines();
                }
                if (Current.Type == TokenType.Dedent) _index++;

                statement.Choices.Add(choice);
                SkipNewlines();
            }

            if (Current.Type == TokenType.Dedent) _index++;
            return statement;
        }

        private Statement ParsePythonBlock()
        {
            int line = Current.Line;
            _index++;   // python
            ExpectColon();

            var group = new GroupStatement { Line = line };
            SkipNewlines();
            ExpectIndent();
            SkipNewlines();

            while (Current.Type != TokenType.Dedent && !AtEnd)
            {
                int innerLine = Current.Line;
                if (Current.Type == TokenType.Dollar)
                {
                    _index++;
                    group.Body.Add(new PythonStatement { Code = ReadRestOfLine(), Line = innerLine });
                }
                else
                {
                    group.Body.Add(ParseStatement());
                }
                SkipNewlines();
            }

            if (Current.Type == TokenType.Dedent) _index++;
            return group;
        }

        // ----------------------------------------------------------- declarations

        /// <summary>
        /// Parse a Ren'Py-style define/default declaration. The current runtime does not
        /// expose Ren'Py's full initialization/Python phase, so values are initialized
        /// from scalar literals. The common Character("Name") form maps to StoryAPI's
        /// own character registry.
        /// </summary>
        private Statement ParseVariableDeclaration(bool isDefault, int line)
        {
            string name = ReadName("variable name");
            ExpectEquals();

            int expressionStart = _index;
            string expression = ReadRestOfLine();
            int expressionEnd = _index;
            if (string.IsNullOrWhiteSpace(expression))
                throw new ScriptSyntaxException("Expected an initializer after '='.", line);
            FinishLine();

            CharacterStatement character;
            if (!isDefault && TryReadCharacterDefinition(expressionStart, expressionEnd, name, line, out character))
                return character;

            var declaration = new VariableDeclarationStatement
            {
                Name = name,
                Expression = expression,
                IsDefault = isDefault,
                Line = line,
            };

            // Only simple literal initializers are evaluated during declaration loading.
            bool oneToken = expressionEnd == expressionStart + 1;
            if (oneToken)
            {
                Token token = _tokens[expressionStart];
                if (token.Type == TokenType.String)
                {
                    declaration.HasLiteral = true;
                    declaration.LiteralValue = token.Text;
                }
                else if (token.Type == TokenType.Number)
                {
                    string numberText = expression.Trim();
                    bool isInteger = numberText.IndexOf('.') < 0 && numberText.IndexOf('e') < 0 &&
                        numberText.IndexOf('E') < 0;
                    int integer;
                    double number;
                    if (isInteger && int.TryParse(numberText, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out integer))
                    {
                        declaration.HasLiteral = true;
                        declaration.LiteralValue = integer;
                    }
                    else if (double.TryParse(numberText, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out number))
                    {
                        declaration.HasLiteral = true;
                        declaration.LiteralValue = number;
                    }
                }
                else if (token.Type == TokenType.Keyword && token.Text == "True")
                {
                    declaration.HasLiteral = true;
                    declaration.LiteralValue = true;
                }
                else if (token.Type == TokenType.Keyword && token.Text == "False")
                {
                    declaration.HasLiteral = true;
                    declaration.LiteralValue = false;
                }
            }

            if (!declaration.HasLiteral)
            {
                object containerLiteral;
                if (TryReadContainerLiteral(expressionStart, expressionEnd, out containerLiteral))
                {
                    declaration.HasLiteral = true;
                    declaration.LiteralValue = containerLiteral;
                }
                else
                {
                    declaration.HasRuntimeExpression =
                        IsSupportedRuntimeScalarExpression(expressionStart, expressionEnd);
                }
            }

            return declaration;
        }

        /// <summary>
        /// Delegate a conservative scalar expression to the game's own $ evaluator.
        /// Container literals are handled separately as inert values. Index reads are
        /// allowed so a scalar define/default may read list or dictionary entries, but
        /// function calls, slices, comma-separated expressions and arbitrary Python remain
        /// outside this path.
        /// </summary>
        private bool IsSupportedRuntimeScalarExpression(int start, int end)
        {
            if (start >= end) return false;

            int indexDepth = 0;
            for (int i = start; i < end; i++)
            {
                Token token = _tokens[i];

                // Permit only subscription reads rooted in a variable or another subscription.
                // An empty index, unmatched bracket, or standalone list-like bracket is rejected.
                if (token.Type == TokenType.Identifier && token.Text == "[")
                {
                    if (i == start || i + 1 >= end ||
                        (_tokens[i + 1].Type == TokenType.Identifier && _tokens[i + 1].Text == "]"))
                        return false;

                    Token previous = _tokens[i - 1];
                    bool hasVariableBase = previous.Type == TokenType.Identifier &&
                        previous.Text.Length > 0 &&
                        (char.IsLetter(previous.Text[0]) || previous.Text[0] == '_');
                    bool hasSubscriptionBase = previous.Type == TokenType.Identifier &&
                        previous.Text == "]";
                    if (!hasVariableBase && !hasSubscriptionBase)
                        return false;

                    indexDepth++;
                    continue;
                }

                if (token.Type == TokenType.Identifier && token.Text == "]")
                {
                    if (indexDepth == 0 || i == start ||
                        (_tokens[i - 1].Type == TokenType.Identifier && _tokens[i - 1].Text == "["))
                        return false;

                    indexDepth--;
                    continue;
                }

                if (token.Type == TokenType.String || token.Type == TokenType.Number)
                    continue;

                if (token.Type == TokenType.Keyword)
                {
                    if (token.Text == "True" || token.Text == "False" || token.Text == "None" ||
                        token.Text == "and" || token.Text == "or" || token.Text == "not")
                        continue;

                    return false;
                }

                if (token.Type != TokenType.Identifier)
                    return false;

                // Ordinary variable names are allowed, but function calls are not.
                if (token.Text.Length > 0 &&
                    (char.IsLetter(token.Text[0]) || token.Text[0] == '_'))
                {
                    if (i + 1 < end && _tokens[i + 1].Text == "(")
                        return false;
                    continue;
                }

                // Only scalar arithmetic, comparison and grouping operators are allowed.
                if (token.Text != "(" && token.Text != ")" &&
                    token.Text != "+" && token.Text != "-" &&
                    token.Text != "*" && token.Text != "/" &&
                    token.Text != "%" && token.Text != "<" &&
                    token.Text != ">" && token.Text != "=" &&
                    token.Text != "!")
                    return false;

                if (token.Text == "!")
                {
                    if (i + 1 >= end || _tokens[i + 1].Text != "=" ||
                        token.Start + token.Length != _tokens[i + 1].Start)
                        return false;
                }

                if (token.Text == "=")
                {
                    bool joinedToPrevious = i > start &&
                        token.Start == _tokens[i - 1].Start + _tokens[i - 1].Length &&
                        (_tokens[i - 1].Text == "=" || _tokens[i - 1].Text == "<" ||
                         _tokens[i - 1].Text == ">" || _tokens[i - 1].Text == "!");
                    bool joinedToNext = i + 1 < end &&
                        token.Start + token.Length == _tokens[i + 1].Start &&
                        _tokens[i + 1].Text == "=";
                    if (!joinedToPrevious && !joinedToNext) return false;
                }
            }

            return indexDepth == 0;
        }

        /// <summary>
        /// Read a deliberately small, side-effect-free subset of container literals.
        /// Values are kept as ordinary CLR objects so GameBridge can pass the completed
        /// collection through DDLC+'s DataValue(System.Object) constructor.
        /// </summary>
        private bool TryReadContainerLiteral(int start, int end, out object value)
        {
            value = null;
            if (start >= end || (_tokens[start].Text != "[" && _tokens[start].Text != "{"))
                return false;

            int cursor = start;
            if (!TryReadLiteralValue(ref cursor, end, out value) || cursor != end)
            {
                value = null;
                return false;
            }

            return value is List<object> || value is Dictionary<string, object>;
        }

        private bool TryReadLiteralValue(ref int cursor, int end, out object value)
        {
            value = null;
            if (cursor >= end) return false;

            Token token = _tokens[cursor];
            if (token.Type == TokenType.String)
            {
                value = token.Text;
                cursor++;
                return true;
            }

            if (token.Type == TokenType.Number)
            {
                int integer;
                double number;
                if (int.TryParse(token.Text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out integer))
                {
                    value = integer;
                    cursor++;
                    return true;
                }

                if (double.TryParse(token.Text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out number))
                {
                    value = number;
                    cursor++;
                    return true;
                }

                return false;
            }

            if (token.Type == TokenType.Keyword)
            {
                if (token.Text == "True") value = true;
                else if (token.Text == "False") value = false;
                else if (token.Text == "None") value = null;
                else return false;

                cursor++;
                return true;
            }

            if (token.Text == "[")
            {
                cursor++;
                var items = new List<object>();
                if (cursor < end && _tokens[cursor].Text == "]")
                {
                    cursor++;
                    value = items;
                    return true;
                }

                while (cursor < end)
                {
                    object item;
                    if (!TryReadLiteralValue(ref cursor, end, out item)) return false;
                    items.Add(item);

                    if (cursor < end && _tokens[cursor].Text == ",")
                    {
                        cursor++;
                        if (cursor < end && _tokens[cursor].Text == "]")
                        {
                            cursor++;
                            value = items;
                            return true;
                        }
                        continue;
                    }

                    if (cursor < end && _tokens[cursor].Text == "]")
                    {
                        cursor++;
                        value = items;
                        return true;
                    }

                    return false;
                }

                return false;
            }

            if (token.Text == "{")
            {
                cursor++;
                var items = new Dictionary<string, object>(StringComparer.Ordinal);
                if (cursor < end && _tokens[cursor].Text == "}")
                {
                    cursor++;
                    value = items;
                    return true;
                }

                while (cursor < end)
                {
                    if (_tokens[cursor].Type != TokenType.String) return false;
                    string key = _tokens[cursor].Text;
                    cursor++;

                    if (cursor >= end || _tokens[cursor].Type != TokenType.Colon)
                        return false;
                    cursor++;

                    object item;
                    if (!TryReadLiteralValue(ref cursor, end, out item)) return false;
                    items[key] = item;

                    if (cursor < end && _tokens[cursor].Text == ",")
                    {
                        cursor++;
                        if (cursor < end && _tokens[cursor].Text == "}")
                        {
                            cursor++;
                            value = items;
                            return true;
                        }
                        continue;
                    }

                    if (cursor < end && _tokens[cursor].Text == "}")
                    {
                        cursor++;
                        value = items;
                        return true;
                    }

                    return false;
                }

                return false;
            }

            return false;
        }

        private bool TryReadCharacterDefinition(int start, int end, string tag, int line,
            out CharacterStatement character)
        {
            character = null;
            // The supported subset is exactly Character("Display name").
            if (end - start != 4) return false;
            if (_tokens[start].Text != "Character" || _tokens[start + 1].Text != "(" ||
                _tokens[start + 2].Type != TokenType.String || _tokens[start + 3].Text != ")")
                return false;

            character = new CharacterStatement
            {
                Tag = tag,
                DisplayName = _tokens[start + 2].Text,
                Image = tag,
                Line = line,
            };
            return true;
        }

        /// <summary>
        /// Reads a trailing `over &lt;seconds&gt;`, which every animation statement accepts.
        /// </summary>
        private float ReadDuration(float fallback)
        {
            if (MatchIdentifier("over"))
            {
                return ReadNumber();
            }
            return fallback;
        }

        private Statement ParseMove()
        {
            int line = Current.Line;
            _index++;   // move

            var statement = new MoveStatement { Name = ReadPoseName("sprite name"), Line = line };

bool hasX = false;
            while (Current.Type == TokenType.Identifier || Current.Type == TokenType.Number)
            {
                string axis = Advance().Text;

                // `over` ends the position list and starts the duration.
                if (axis == "over")
                {
                    if (Current.Type != TokenType.Number)
                        throw new ScriptSyntaxException("Expected a duration after 'over'.", Current.Line);
                    statement.Seconds = ReadNumber();
                    break;
                }

                if (Current.Type != TokenType.Number)
                    throw new ScriptSyntaxException("Expected a position after '" + axis + "'.", Current.Line);

                float value = ReadNumber();
                if (axis == "xpos" || axis == "x") { statement.XPos = value; hasX = true; }
                else if (axis == "ypos" || axis == "y") { statement.YPos = value; statement.HasY = true; }
                else
                {
                    throw new ScriptSyntaxException("Unknown position '" + axis + "'. Use xpos or ypos.", Current.Line);
                }
            }

            if (!hasX)
                throw new ScriptSyntaxException("'move' needs an xpos, e.g. move june xpos 0.8.", line);

            if (statement.Seconds <= 0f) statement.Seconds = 0.5f;
            FinishLine();
            return statement;
        }

        private Statement ParseZoom()
        {
            int line = Current.Line;
            _index++;   // zoom

            var statement = new ZoomStatement { Name = ReadPoseName("sprite name"), Line = line };
            MatchIdentifier("to");
            
            if (Current.Type != TokenType.Number)
                throw new ScriptSyntaxException("Expected a scale after 'zoom'.", Current.Line);
            statement.Scale = ReadNumber();

            statement.Seconds = ReadDuration(0.5f);
            FinishLine();
            return statement;
        }

/// <summary>
        /// Read a name the game spells with a space in it, such as "monika 1".
        ///
        /// The game splits this into an image and a pose, exactly as <c>show</c> does, and
        /// the two halves are joined back together because that is the name the game uses.
        /// </summary>
        /// <summary>
        /// Words that follow a sprite name in an animation statement, and so must never be
        /// mistaken for part of that name.
        ///
        /// A pose is written right after the name with no keyword, as in "monika 1", which
        /// makes it indistinguishable from an option by position alone. Listing the words
        /// that can appear there is what keeps "move june xpos 0.5" from reading the name
        /// as "june xpos".
        /// </summary>
        private static bool IsAnimationOption(string word)
        {
            return word == "xpos" || word == "ypos" || word == "x" || word == "y" ||
                   word == "over" || word == "to" || word == "alpha" || word == "zoom" ||
                   word == "rotate" || word == "degree" || word == "at" || word == "with";
        }

        private string ReadPoseName(string what)
        {
            string first = ReadName(what);

            if (Current.Type == TokenType.Number ||
                (Current.Type == TokenType.Identifier && !IsShowOption(Current.Text) &&
                 !IsAnimationOption(Current.Text)))
            {
                return first + " " + ReadPosePart();
            }

            return first;
        }

        /// <summary>
        /// Reads one pose/image attribute. In shipped DDLC scripts, attributes such as
        /// "2a" and "1f" contain digits followed immediately by letters. The lexer splits
        /// those into separate tokens; rejoin them only if there was no whitespace in the
        /// original source, so a numeric attribute like "1" followed by an option is safe.
        /// </summary>
        private string ReadPosePart()
        {
            Token first = Advance();
            if (first.Type == TokenType.Number &&
                (Current.Type == TokenType.Identifier || Current.Type == TokenType.Keyword) &&
                first.Start + first.Length == Current.Start)
            {
                return first.Text + Advance().Text;
            }

            return first.Text;
        }

        /// <summary>
        /// Read a single art name.
        ///
        /// Some layer names begin with a digit, as "1l" does, which the scanner splits
        /// into a number and a word. Putting the two back together is what lets those
        /// names be written plainly instead of needing quotes.
        /// </summary>
        private string ReadArtName(string what)
        {
            if (Current.Type == TokenType.Number)
            {
                string digits = Advance().Text;
                if (Current.Type == TokenType.Identifier) return digits + Advance().Text;
                return digits;
            }

            return ReadName(what);
        }

        /// <summary>
        /// Reads the rest of a layer command as one art/resource name.
        /// Game asset names can contain spaces and digits, for example "monika 5a".
        /// The lexer splits those names into multiple tokens, so consuming only one token
        /// leaves a stray number at the end of the line. Since the asset is the final
        /// argument of a layer command, the remaining source text belongs to that name.
        /// </summary>
        private string ReadRemainingArtName(string what)
        {
            if (Current.Type == TokenType.Newline || Current.Type == TokenType.EndOfFile ||
                Current.Type == TokenType.Dedent)
            {
                throw new ScriptSyntaxException("Expected " + what + ".", Current.Line);
            }

            int firstIndex = _index;
            int endIndex = firstIndex;
            while (endIndex < _tokens.Count &&
                   _tokens[endIndex].Type != TokenType.Newline &&
                   _tokens[endIndex].Type != TokenType.EndOfFile &&
                   _tokens[endIndex].Type != TokenType.Dedent)
            {
                endIndex++;
            }

            if (endIndex == firstIndex)
                throw new ScriptSyntaxException("Expected " + what + ".", Current.Line);

            // Quoted names should be returned without their quotes and with escapes decoded.
            if (endIndex == firstIndex + 1 && _tokens[firstIndex].Type == TokenType.String)
            {
                string quoted = _tokens[firstIndex].Text;
                _index = endIndex;
                return quoted;
            }

            Token first = _tokens[firstIndex];
            Token last = _tokens[endIndex - 1];
            int start = first.Start;
            int end = last.Start + last.Length;
            string name = _source.Substring(start, end - start).Trim();
            _index = endIndex;
            return name;
        }

        private Statement ParsePose()
        {
            int line = Current.Line;
            _index++;   // pose

            var statement = new PoseStatement { Name = ReadPoseName("pose name"), Line = line };

            // "at left" is optional; without it the pose goes to the middle.
            if (MatchIdentifier("at"))
            {
                statement.At = ReadName("a position such as left or right").ToLowerInvariant();
            }

            FinishLine();
            return statement;
        }

        private Statement ParseLayer()
        {
            int line = Current.Line;
            _index++;   // layer

            var statement = new LayerStatement { Pose = ReadPoseName("pose name"), Line = line };
            statement.Layer = ReadArtName("layer name, for example an eye or mouth layer");
            // The replacement asset is the final argument and can itself contain spaces
            // and digits (for example: layer monika 1 a monika 5a).
            statement.Asset = ReadRemainingArtName("the name of the art to show there instead");

            FinishLine();
            return statement;
        }

        private Statement ParseFade()
        {
            int line = Current.Line;
            _index++;   // fade

            var statement = new FadeStatement { Name = ReadPoseName("sprite name"), Line = line };
            MatchIdentifier("to");
            
            if (Current.Type != TokenType.Number)
                throw new ScriptSyntaxException("Expected an opacity after 'fade', 0 to 1.", Current.Line);
            statement.Alpha = ReadNumber();

            statement.Seconds = ReadDuration(0.5f);
            FinishLine();
            return statement;
        }

        private Statement ParseTurn()
        {
            int line = Current.Line;
            _index++;   // turn

            var statement = new TurnStatement { Name = ReadPoseName("sprite name"), Line = line };
            MatchIdentifier("to");
            
            if (Current.Type != TokenType.Number)
                throw new ScriptSyntaxException("Expected an angle after 'turn'.", Current.Line);
            statement.Degrees = ReadNumber();

            statement.Seconds = ReadDuration(0.5f);
            FinishLine();
            return statement;
}

        // Note: unlike the Parse*Statement methods, which are dispatched by switching on
        // the current token and therefore consume it themselves, these four are reached
        // through MatchKeyword, which has already stepped past the keyword.
        private Statement ParseCharacter()
        {
            int line = Current.Line;

            string tag = ReadName("character tag");
            string displayName = tag;
            string image = null;

            // Both a bare quoted name and `name="..."` style are accepted.
            if (Current.Type == TokenType.String)
            {
                displayName = Advance().Text;
            }

            // Option keywords like `image` are lexed as keywords, so keywords have to be
            // accepted here as well as plain identifiers.
            while (Current.Type == TokenType.Identifier || Current.Type == TokenType.Keyword ||
                   Current.Type == TokenType.String)
            {
                string key = Advance().Text;
                string value = null;
                if (Current.Type == TokenType.String || Current.Type == TokenType.Identifier ||
                    Current.Type == TokenType.Keyword || Current.Type == TokenType.Number)
                {
                    value = Advance().Text;
                }
                if (key == "name") displayName = value ?? displayName;
                else if (key == "image") image = value;
            }

            FinishLine();
            return new CharacterStatement
            {
                Tag = tag,
                DisplayName = displayName,
                Image = image ?? tag,
                Line = line,
            };
        }

        private Statement ParseImage()
        {
            int line = Current.Line;
            string name = ReadName("image name");
            ExpectEquals();
            string file = ReadPath();
            FinishLine();
            return new ImageStatement { Name = name, File = file, Line = line };
        }

        private Statement ParseAudio()
        {
            int line = Current.Line;
            string name = ReadName("audio name");
            ExpectEquals();
            string file = ReadPath();
            FinishLine();
            return new AudioStatement { Name = name, File = file, Line = line };
        }

        private Statement ParseOverride()
        {
            int line = Current.Line;

            string label = ReadName("label name");
            ExpectEquals();

            if (Current.Type != TokenType.Number)
                throw new ScriptSyntaxException("Expected a line id after '='.", Current.Line);

            int id = (int)double.Parse(Advance().Text, CultureInfo.InvariantCulture);

            string text = null;
            if (Current.Type == TokenType.String)
            {
                text = Advance().Text;
                FinishLine();
            }
            else
            {
                FinishLine();
                SkipNewlines();
                ExpectIndent();
                SkipNewlines();
                text = Advance().Text;
                SkipNewlines();
                if (Current.Type == TokenType.Dedent) _index++;
                FinishLine();
            }

            return new OverrideStatement { Label = label, TextId = id, Text = text, Line = line };
        }

        // ---------------------------------------------------------------- helpers

        private void ExpectEquals()
        {
            if (Current.Type == TokenType.Identifier && Current.Text == "=")
            {
                _index++;
                return;
            }
            throw new ScriptSyntaxException("Expected '='.", Current.Line);
        }

        private string ReadName(string what)
        {
            if (Current.Type == TokenType.Identifier || Current.Type == TokenType.Keyword ||
                Current.Type == TokenType.String || Current.Type == TokenType.Number)
            {
                return Advance().Text;
            }
            throw new ScriptSyntaxException("Expected " + what + ".", Current.Line);
        }

        private float ReadNumber()
        {
            if (Current.Type != TokenType.Number)
                throw new ScriptSyntaxException("Expected a number.", Current.Line);
            return float.Parse(Advance().Text, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads an asset path such as <c>rooms/lab.png</c>.
        ///
        /// File names are lexed as a run of identifiers separated by dots and slashes,
        /// so they have to be stitched back together here. This is deliberately only
        /// used for asset paths: inside expressions the same punctuation is real
        /// syntax and must stay separate.
        /// </summary>
        private string ReadPath()
        {
            var builder = new StringBuilder(ReadName("file name"));

            while ((Current.Type == TokenType.Identifier || Current.Type == TokenType.Number) &&
                   (Current.Text == "." || Current.Text == "/") &&
                   (Ahead(1).Type == TokenType.Identifier || Ahead(1).Type == TokenType.Number))
            {
                builder.Append(Advance().Text);
                builder.Append(Advance().Text);
            }

            return builder.ToString();
        }

        private string NormalizeChannel(string channel, int line)
        {
            switch (channel)
            {
                case "music": return "music";
                case "sound": return "sound";
                case "music_poem": return "music_poem";
                default:
                    throw new ScriptSyntaxException(
                        "Unknown audio channel '" + channel + "'. Use music or sound.", line);
            }
        }

        /// <summary>
        /// Slices the untouched source text from the current token to the end of the
        /// line. Expressions must reach the game's expression engine exactly as the
        /// author wrote them, so they are never rebuilt from tokens.
        /// </summary>
        private string ReadRestOfLine()
        {
            if (Current.Type == TokenType.Newline || Current.Type == TokenType.EndOfFile)
            {
                return string.Empty;
            }

            int start = Current.Start;

            // Find the end of the logical line.
            int probe = _index;
            while (_tokens[probe].Type != TokenType.Newline &&
                   _tokens[probe].Type != TokenType.EndOfFile)
            {
                probe++;
            }

            // A block header ends in a colon, and the caller still needs to consume
            // that colon itself, so stop the slice short of it.
            int end = probe;
            if (end - 1 >= _index && _tokens[end - 1].Type == TokenType.Colon)
            {
                end = end - 1;
            }

            if (end <= _index) return string.Empty;

            var last = _tokens[end - 1];
            string text = _source.Substring(start, last.Start + last.Length - start).Trim();
            _index = end;
            return text;
        }

        /// <summary>Consumes the newline that closes the current logical line.</summary>
        private void FinishLine()
        {
            if (Current.Type == TokenType.Newline)
            {
                _index++;
                return;
            }
            if (Current.Type == TokenType.EndOfFile || Current.Type == TokenType.Dedent) return;
            throw new ScriptSyntaxException(
                "Unexpected '" + Describe(Current) + "' at the end of the line.", Current.Line);
        }

        private static string Describe(Token token)
        {
            switch (token.Type)
            {
                case TokenType.EndOfFile: return "end of file";
                case TokenType.Newline: return "end of line";
                case TokenType.Indent: return "an indented block";
                case TokenType.Dedent: return "end of block";
                case TokenType.String: return "quoted text";
                default: return token.Text;
            }
        }
    }
}
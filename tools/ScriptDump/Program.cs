using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using StoryAPI.Scripting;

namespace StoryAPI.Tools
{
    /// <summary>
    /// Parses mod scripts outside the game and prints the tree.
    ///
    /// The lexer and parser deliberately carry no Unity or game dependencies, so the
    /// whole front end can be exercised with a plain "dotnet run". That keeps syntax
    /// work out of the much slower launch-the-game-and-read-the-log loop.
    /// </summary>
    public static class DumpTool
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("usage: dump <file.rpy> [...]");
                return 2;
            }

            int failures = 0;
            bool showTokens = false;
            var files = new List<string>();

            foreach (string arg in args)
            {
                if (arg == "--tokens") showTokens = true;
                else files.Add(arg);
            }

            foreach (string path in files)
            {
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine("missing: " + path);
                    failures++;
                    continue;
                }

                failures += Dump(path, showTokens);
            }

            return failures == 0 ? 0 : 1;
        }

        private static int Dump(string path, bool showTokens)
        {
            Console.WriteLine("=== " + path + " ===");

            string source = File.ReadAllText(path);

            if (showTokens)
            {
                var tokens = new Lexer(source, path).Tokenize();
                foreach (var token in tokens)
                {
                    Console.WriteLine("  " + token.Line.ToString().PadLeft(4) + "  " +
                                      token.Type + " \"" + token.Text + "\" @" + token.Start);
                }
                return 0;
            }

            List<Statement> statements;
            try
            {
                statements = new Parser(source, path).ParseFile();
            }
            catch (ScriptSyntaxException e)
            {
                Console.Error.WriteLine("  SYNTAX ERROR line " + e.Line + ": " + e.Message);
                return 1;
            }

            foreach (var statement in statements) Print(statement, 1);
            Console.WriteLine();
            return 0;
        }

        private static void Print(Statement statement, int depth)
        {
            string pad = new string(' ', depth * 2);
            int line = statement.Line;

            switch (statement)
            {
                case LabelStatement label:
                    Console.WriteLine(pad + "label " + label.Name + "  (line " + line + ")");
                    foreach (var inner in label.Body) Print(inner, depth + 1);
                    break;

                case SayStatement say:
                    Console.WriteLine(pad + "say " + (say.Speaker.Length == 0 ? "<narrator>" : say.Speaker) +
                                      (say.Variant == null ? "" : " @" + say.Variant) +
                                      ": \"" + Shorten(say.Text) + "\"");
                    break;

                case SceneStatement scene:
                    Console.WriteLine(pad + "scene " + scene.Image +
                                      (scene.Transition == null ? "" : " with " + scene.Transition));
                    break;

                case ShowStatement show:
                    Console.WriteLine(pad + "show " + show.Image +
                                      (show.Variant == null ? "" : " " + show.Variant) +
                                      (show.At == null ? "" : " at " + show.At) +
                                      (show.Transform == null ? "" : " with " + show.Transform) +
                                      (show.ZOrder.HasValue ? " zorder " + show.ZOrder : ""));
                    break;

                case HideStatement hide:
                    Console.WriteLine(pad + "hide " + hide.Image);
                    break;

                case WithStatement with:
                    Console.WriteLine(pad + "with " + with.Transition);
                    break;

                case PlayStatement play:
                    Console.WriteLine(pad + "play " + play.Channel + " " + play.Name +
                                      " fadein " + play.FadeIn + " loop " + play.Looping);
                    break;

                case StopStatement stop:
                    Console.WriteLine(pad + "stop " + stop.Channel);
                    break;

                case JumpStatement jump:
                    Console.WriteLine(pad + "jump " + jump.Target);
                    break;

                case CallStatement call:
                    Console.WriteLine(pad + "call " + call.Target);
                    break;

                case ReturnStatement _:
                    Console.WriteLine(pad + "return");
                    break;

                case PauseStatement pause:
                    Console.WriteLine(pad + "pause " + pause.Seconds);
                    break;

                case PythonStatement python:
                    Console.WriteLine(pad + "$ " + python.Code);
                    break;

                case IfStatement branch:
                    Console.WriteLine(pad + "if:");
                    foreach (var b in branch.Branches)
                    {
                        Console.WriteLine(pad + "  [" + (b.Condition ?? "otherwise") + "]");
                        foreach (var inner in b.Body) Print(inner, depth + 2);
                    }
                    break;

                case MenuStatement menu:
                    Console.WriteLine(pad + "menu" + (menu.Caption == null ? "" : " \"" + menu.Caption + "\"") + ":");
                    foreach (var choice in menu.Choices)
                    {
                        Console.WriteLine(pad + "  \"" + choice.Caption + "\":");
                        foreach (var inner in choice.Body) Print(inner, depth + 2);
                    }
                    break;

                case CharacterStatement character:
                    Console.WriteLine(pad + "character " + character.Tag +
                                      " name=" + character.DisplayName +
                                      " image=" + character.Image);
                    break;

                case ImageStatement image:
                    Console.WriteLine(pad + "image " + image.Name + " = " + image.File);
                    break;

                case AudioStatement audio:
                    Console.WriteLine(pad + "audio " + audio.Name + " = " + audio.File);
                    break;

                case OverrideStatement replace:
                    Console.WriteLine(pad + "override " + replace.Label + " = " + replace.TextId +
                                      " -> \"" + Shorten(replace.Text) + "\"");
                    break;

                case GroupStatement group:
                    foreach (var inner in group.Body) Print(inner, depth);
                    break;

                default:
                    Console.WriteLine(pad + "?" + statement.GetType().Name);
                    break;
            }
        }

        private static string Shorten(string text)
        {
            string single = text.Replace("\n", "\\n");
            return single.Length <= 60 ? single : single.Substring(0, 57) + "...";
        }
    }
}
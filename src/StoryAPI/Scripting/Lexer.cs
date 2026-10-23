using System;
using System.Collections.Generic;
using System.Text;

namespace StoryAPI.Scripting
{
    public enum TokenType
    {
        EndOfFile,
        Identifier,
        String,
        Number,
        Colon,
        Indent,
        Dedent,
        Newline,
        Dollar,
        Keyword,
    }

    public sealed class Token
    {
        public TokenType Type { get; set; }
        public string Text { get; set; }
        public int Line { get; set; }

        /// <summary>Offset of the token in the original source, for re-slicing raw text.</summary>
        public int Start { get; set; }

        /// <summary>Length of the token in the original source.</summary>
        public int Length { get; set; }

        /// <summary>True for the bare narration tag.</summary>
        public bool IsNarrationTag => Type == TokenType.Identifier && Text == "_";

        public override string ToString() => Type + "(" + Text + ")";
    }

    /// <summary>A parse failure with the position that caused it.</summary>
    public sealed class ScriptSyntaxException : Exception
    {
        public ScriptSyntaxException(string message, int line)
            : base(message)
        {
            Line = line;
        }

        public int Line { get; }
    }

    /// <summary>
    /// Turns mod script text into a token stream.
    ///
    /// The language is line-oriented, so indentation is meaningful: it is what marks
    /// the body of a label, an if-branch or a menu choice. Indentation is emitted as
    /// explicit Indent/Dedent tokens so the parser never has to look at raw spaces.
    /// </summary>
    public sealed class Lexer
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "label", "scene", "show", "hide", "with", "play", "stop", "queue",
            "jump", "call", "return", "menu", "if", "elif", "else", "otherwise",
            "python", "define", "character", "image", "audio", "override",
            "pause", "move", "zoom", "fade", "turn", "pose", "layer", "hook",
            "and", "or", "not", "True", "False", "None", "pass",
        };

        private readonly string _text;
        private readonly string _path;
        private int _pos;
        private int _line = 1;

        public Lexer(string text, string path)
        {
            _text = text ?? string.Empty;
            _path = path;
        }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();
            var indents = new Stack<int>();
            indents.Push(0);

            while (true)
            {
                int column = MeasureIndent();
                if (column < 0) break;   // blank or comment-only line

                if (column > indents.Peek())
                {
                    indents.Push(column);
                    tokens.Add(new Token { Type = TokenType.Indent, Line = _line });
                }
                else
                {
                    while (column < indents.Peek())
                    {
                        indents.Pop();
                        tokens.Add(new Token { Type = TokenType.Dedent, Line = _line });
                    }
                    if (column != indents.Peek())
                    {
                        throw new ScriptSyntaxException(
                            "Inconsistent indentation.", _line);
                    }
                }

                ReadLineContent(tokens);
                tokens.Add(new Token { Type = TokenType.Newline, Line = _line });
            }

            // Close every open block at end of file.
            if (_pos < _text.Length) ReadLineContent(tokens);

            while (indents.Count > 1)
            {
                indents.Pop();
                tokens.Add(new Token { Type = TokenType.Dedent, Line = _line });
            }

            tokens.Add(new Token { Type = TokenType.EndOfFile, Line = _line });
            return tokens;
        }

        /// <summary>
        /// Returns the indentation width of the next content line, or -1 for a line
        /// that holds nothing but whitespace or a comment.
        /// </summary>
        private int MeasureIndent()
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];

                if (c == '\n')
                {
                    _pos++;
                    _line++;
                    continue;
                }

                if (c == '\r') { _pos++; continue; }

                if (c == ' ' || c == '\t')
                {
                    _pos++;
                    continue;
                }
if (c == '#')
                {
                    SkipToEndOfLine();
                    continue;
                }

                // Leading blanks of this line have been consumed already, so the
                // indentation is whatever whitespace sits immediately behind the
                // cursor. Scanning backwards stops at the previous line break, which
                // keeps blank lines and comment-only lines from counting.
                int column = 0;
                int scan = _pos - 1;
                while (scan >= 0)
                {
                    char s = _text[scan];
                    if (s == ' ') { column++; scan--; }
                    else if (s == '\t') { column += 4; scan--; }
                    else break;
                }

                return column;
            }
            return -1;
        }

        private void SkipToEndOfLine()
        {
            while (_pos < _text.Length && _text[_pos] != '\n') _pos++;
        }

        private void ReadLineContent(List<Token> tokens)
        {
            while (_pos < _text.Length)
            {
                char c = _text[_pos];

                if (c == '\n' || c == '\r') return;
                if (c == '#') { SkipToEndOfLine(); return; }

                if (c == ' ' || c == '\t') { _pos++; continue; }

                if (c == ':')
                {
                    AddChar(tokens, TokenType.Colon, c);
                    continue;
                }

                if (c == '$')
                {
                    AddChar(tokens, TokenType.Dollar, c);
                    continue;
                }

                if (c == '"')
                {
                    int stringStart = _pos;
                    string value = ReadQuoted();
                    tokens.Add(new Token
                    {
                        Type = TokenType.String,
                        Text = value,
                        Line = _line,
                        Start = stringStart,
                        Length = _pos - stringStart,
                    });
                    continue;
                }

                if (char.IsDigit(c) || (c == '-' && _pos + 1 < _text.Length && char.IsDigit(_text[_pos + 1])))
                {
                    Add(tokens, TokenType.Number, ReadNumber());
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    string word = ReadIdentifier();
                    Add(tokens, Keywords.Contains(word) ? TokenType.Keyword : TokenType.Identifier, word);
                    continue;
                }

                // Anything else is punctuation that belongs to an expression. The
                // expression engine parses those, so keep it as an opaque token and
                // let the parser slice the untouched source when it needs the text.
                Add(tokens, TokenType.Identifier, ReadOperator());
            }
        }

        /// <summary>
        /// Records a token whose text has already been consumed from the source.
        /// </summary>
        private void Add(List<Token> tokens, TokenType type, string text)
        {
            tokens.Add(new Token
            {
                Type = type,
                Text = text,
                Line = _line,
                Start = _pos - text.Length,
                Length = text.Length,
            });
        }

        /// <summary>
        /// Records a single-character token and consumes it. Punctuation is matched
        /// character by character, so the cursor has to be moved here; a variant that
        /// forgot to would spin forever on the same character.
        /// </summary>
        private void AddChar(List<Token> tokens, TokenType type, char c)
        {
            int start = _pos;
            _pos++;
            tokens.Add(new Token
            {
                Type = type,
                Text = c.ToString(),
                Line = _line,
                Start = start,
                Length = 1,
            });
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

        private string ReadIdentifier()
        {
            int start = _pos;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) _pos++;
            return _text.Substring(start, _pos - start);
        }

        private string ReadNumber()
        {
            int start = _pos;
            if (_text[_pos] == '-') _pos++;
            while (_pos < _text.Length && (char.IsDigit(_text[_pos]) || _text[_pos] == '.')) _pos++;
            if (_pos < _text.Length && (_text[_pos] == 'f' || _text[_pos] == 'F'))
            {
                _pos++;
                return _text.Substring(start, _pos - start - 1);
            }
            return _text.Substring(start, _pos - start);
        }

        private string ReadOperator()
        {
            int start = _pos;
            _pos++;
            return _text.Substring(start, 1);
        }

        /// <summary>
        /// Reads a quoted string. Quotes may span lines, which is how a long speech
        /// can be wrapped in the source file without inserting a stray newline.
        /// </summary>
        private string ReadQuoted()
        {
            _pos++;   // opening quote
            var builder = new StringBuilder();

            while (_pos < _text.Length)
            {
                char c = _text[_pos];

                if (c == '\\' && _pos + 1 < _text.Length)
                {
                    char next = _text[_pos + 1];
                    switch (next)
                    {
                        case 'n': builder.Append('\n'); _pos += 2; continue;
                        case 't': builder.Append('\t'); _pos += 2; continue;
                        case '"': builder.Append('"'); _pos += 2; continue;
                        case '\\': builder.Append('\\'); _pos += 2; continue;
                        default:
                            builder.Append(c);
                            builder.Append(next);
                            _pos += 2;
                            continue;
                    }
                }

                if (c == '"')
                {
                    _pos++;
                    return builder.ToString();
                }

                if (c == '\n') _line++;
                builder.Append(c);
                _pos++;
            }

            throw new ScriptSyntaxException("Unterminated string.", _line);
        }
    }
}
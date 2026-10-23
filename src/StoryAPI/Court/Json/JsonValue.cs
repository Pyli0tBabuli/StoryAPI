using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StoryAPI.Court.Json
{
    /// <summary>
    /// Вид значения в файле контента.
    /// </summary>
    public enum JsonKind
    {
        Null,
        Bool,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>
    /// Ошибка разбора: где именно и почему.
    /// </summary>
    public class JsonException : Exception
    {
        public int Line { get; private set; }
        public int Column { get; private set; }

        public JsonException(string message, int line, int column)
            : base(message + " (строка " + line + ", столбец " + column + ")")
        {
            Line = line;
            Column = column;
        }
    }

    /// <summary>
    /// Значение в файле контента.
    ///
    /// Свой парсер нужен потому, что в игре нет ни Newtonsoft, ни терпимого к комментариям
    /// <c>JsonUtility</c>: в конфигурации дела используются комментарии и вложенные объекты,
    /// которые штатный сериализатор Unity не читает.
    /// </summary>
    public class JsonValue
    {
        private static readonly JsonValue s_Null = new JsonValue();

        private bool _bool;
        private double _number;
        private string _string;
        private List<JsonValue> _array;
        private Dictionary<string, JsonValue> _object;

        public JsonKind Kind { get; private set; }

        private JsonValue()
        {
            Kind = JsonKind.Null;
        }

        public JsonValue(bool value)
        {
            Kind = JsonKind.Bool;
            _bool = value;
        }

        public JsonValue(double value)
        {
            Kind = JsonKind.Number;
            _number = value;
        }

        public JsonValue(string value)
        {
            Kind = JsonKind.String;
            _string = value;
        }

        public static JsonValue Null
        {
            get { return s_Null; }
        }

        public bool IsNull
        {
            get { return Kind == JsonKind.Null; }
        }

        public bool AsBool
        {
            get
            {
                switch (Kind)
                {
                    case JsonKind.Bool: return _bool;
                    case JsonKind.Number: return Math.Abs(_number) > double.Epsilon;
                    case JsonKind.String: return !string.IsNullOrEmpty(_string) && _string != "false";
                    default: return false;
                }
            }
        }

        public double AsNumber
        {
            get
            {
                switch (Kind)
                {
                    case JsonKind.Number: return _number;
                    case JsonKind.Bool: return _bool ? 1.0 : 0.0;
                    case JsonKind.String:
                        double parsed;
                        if (double.TryParse(_string, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                        {
                            return parsed;
                        }
                        return 0.0;
                    default: return 0.0;
                }
            }
        }

        public float AsFloat
        {
            get { return (float)AsNumber; }
        }

        public int AsInt
        {
            get { return (int)Math.Round(AsNumber); }
        }

        public string AsString
        {
            get
            {
                switch (Kind)
                {
                    case JsonKind.String: return _string;
                    case JsonKind.Bool: return _bool ? "true" : "false";
                    case JsonKind.Number: return _number.ToString(CultureInfo.InvariantCulture);
                    default: return string.Empty;
                }
            }
        }

        public List<JsonValue> AsArray
        {
            get { return _array ?? (_array = new List<JsonValue>()); }
        }

        public Dictionary<string, JsonValue> AsObject
        {
            get { return _object ?? (_object = new Dictionary<string, JsonValue>()); }
        }

        public bool Has(string key)
        {
            return _object != null && _object.ContainsKey(key);
        }

        public JsonValue Get(string key)
        {
            JsonValue found;
            if (_object != null && _object.TryGetValue(key, out found)) return found;
            return s_Null;
        }

        public JsonValue Get(int index)
        {
            if (_array == null || index < 0 || index >= _array.Count) return s_Null;
            return _array[index];
        }

        public int Count
        {
            get
            {
                if (_array != null) return _array.Count;
                if (_object != null) return _object.Count;
                return 0;
            }
        }

        /// <summary>
        /// Список строк из массива. Нестроки пропускаются, а не считаются ошибкой:
        /// так автор может оставить в списке закомментированный элемент.
        /// </summary>
        public List<string> AsStringList()
        {
            var result = new List<string>();
            if (_array == null) return result;
            for (int i = 0; i < _array.Count; i++)
            {
                if (_array[i].Kind == JsonKind.String) result.Add(_array[i].AsString);
            }
            return result;
        }

        public List<float> AsFloatList()
        {
            var result = new List<float>();
            if (_array == null) return result;
            for (int i = 0; i < _array.Count; i++)
            {
                if (_array[i].Kind == JsonKind.Number) result.Add(_array[i].AsFloat);
            }
            return result;
        }

        // ------------------------------------------------------------- разбор

        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            var reader = new Reader(text);
            reader.SkipWhitespace();
            JsonValue value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw reader.Error("после значения остался лишний текст");
            }
            return value;
        }

        private class Reader
        {
            private readonly string _text;
            private int _position;

            public Reader(string text)
            {
                _text = text;
            }

            public bool AtEnd
            {
                get { return _position >= _text.Length; }
            }

            public JsonException Error(string message)
            {
                int line = 1;
                int column = 1;
                for (int i = 0; i < _position && i < _text.Length; i++)
                {
                    if (_text[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }
                return new JsonException(message, line, column);
            }

            public void SkipWhitespace()
            {
                while (_position < _text.Length)
                {
                    char c = _text[_position];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    {
                        _position++;
                        continue;
                    }
                    // Комментарии: и // до конца строки, и /* ... */.
                    if (c == '/' && _position + 1 < _text.Length)
                    {
                        char next = _text[_position + 1];
                        if (next == '/')
                        {
                            _position += 2;
                            while (_position < _text.Length && _text[_position] != '\n') _position++;
                            continue;
                        }
                        if (next == '*')
                        {
                            _position += 2;
                            while (_position + 1 < _text.Length &&
                                   !(_text[_position] == '*' && _text[_position + 1] == '/')) _position++;
                            _position = Math.Min(_position + 2, _text.Length);
                            continue;
                        }
                    }
                    break;
                }
            }

            private char Current
            {
                get { return _position < _text.Length ? _text[_position] : '\0'; }
            }

            public JsonValue ReadValue()
            {
                SkipWhitespace();
                if (AtEnd) throw Error("ожидалось значение, а файл кончился");
                char c = Current;
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return new JsonValue(ReadString());
                    case 't':
                        Expect("true");
                        return new JsonValue(true);
                    case 'f':
                        Expect("false");
                        return new JsonValue(false);
                    case 'n':
                        Expect("null");
                        return s_Null;
                    default:
                        return ReadNumber();
                }
            }

            private void Expect(string word)
            {
                if (_position + word.Length > _text.Length ||
                    string.CompareOrdinal(_text, _position, word, 0, word.Length) != 0)
                {
                    throw Error("ожидалось '" + word + "'");
                }
                _position += word.Length;
            }

            private JsonValue ReadObject()
            {
                var result = new JsonValue();
                result.Kind = JsonKind.Object;
                _position++; // '{'
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd) throw Error("объект не закрыт");
                    if (Current == '}')
                    {
                        _position++;
                        return result;
                    }
                    string key;
                    if (Current == '"')
                    {
                        key = ReadString();
                    }
                    else
                    {
                        key = ReadBareKey();
                    }
                    SkipWhitespace();
                    if (Current != ':') throw Error("после ключа '" + key + "' ожидалось ':'");
                    _position++;
                    result.AsObject[key] = ReadValue();
                    SkipWhitespace();
                    if (Current == ',')
                    {
                        _position++;
                        continue;
                    }
                    if (Current == '}')
                    {
                        _position++;
                        return result;
                    }
                    throw Error("после значения ожидалась ',' или '}'");
                }
            }

            private JsonValue ReadArray()
            {
                var result = new JsonValue();
                result.Kind = JsonKind.Array;
                _position++; // '['
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd) throw Error("массив не закрыт");
                    if (Current == ']')
                    {
                        _position++;
                        return result;
                    }
                    result.AsArray.Add(ReadValue());
                    SkipWhitespace();
                    if (Current == ',')
                    {
                        _position++;
                        continue;
                    }
                    if (Current == ']')
                    {
                        _position++;
                        return result;
                    }
                    throw Error("после элемента ожидалась ',' или ']'");
                }
            }

            private string ReadBareKey()
            {
                int start = _position;
                while (_position < _text.Length)
                {
                    char c = _text[_position];
                    if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') _position++;
                    else break;
                }
                if (_position == start) throw Error("ожидался ключ");
                return _text.Substring(start, _position - start);
            }

            private string ReadString()
            {
                _position++; // '"'
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("строка не закрыта");
                    char c = _text[_position++];
                    if (c == '"') return builder.ToString();
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (AtEnd) throw Error("строка не закрыта");
                    char escape = _text[_position++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (_position + 4 > _text.Length) throw Error("неполный \\u");
                            int code;
                            if (!int.TryParse(_text.Substring(_position, 4), NumberStyles.HexNumber,
                                             CultureInfo.InvariantCulture, out code))
                            {
                                throw Error("неверный код \\u");
                            }
                            builder.Append((char)code);
                            _position += 4;
                            break;
                        default:
                            throw Error("неизвестный escape '\\" + escape + "'");
                    }
                }
            }

            private JsonValue ReadNumber()
            {
                int start = _position;
                if (Current == '-' || Current == '+') _position++;
                while (_position < _text.Length)
                {
                    char c = _text[_position];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '-' || c == '+')
                    {
                        _position++;
                        continue;
                    }
                    break;
                }
                string text = _text.Substring(start, _position - start);
                double value;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    _position = start;
                    throw Error("неверное число");
                }
                return new JsonValue(value);
            }
        }
    }
}

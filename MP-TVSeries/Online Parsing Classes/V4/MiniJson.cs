using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WindowPlugins.GUITVSeries.Online_Parsing_Classes.V4
{
    /// <summary>
    /// Minimal JSON parser. The project targets .NET Framework 4.0 with no JSON library
    /// referenced (no Newtonsoft.Json / System.Text.Json), so rather than add a new binary
    /// dependency to a very old-style .csproj, we parse into a plain object graph:
    ///   object  -> Dictionary&lt;string, object&gt;
    ///   array   -> List&lt;object&gt;
    ///   string  -> string
    ///   number  -> double
    ///   true/false -> bool
    ///   null    -> null
    /// Use the JsonHelpers class alongside this to pull fields out defensively.
    /// </summary>
    static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = 0;
            var result = ParseValue(json, ref i);
            return result;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't':
                    Expect(s, ref i, "true");
                    return true;
                case 'f':
                    Expect(s, ref i, "false");
                    return false;
                case 'n':
                    Expect(s, ref i, "null");
                    return null;
                default:
                    return ParseNumber(s, ref i);
            }
        }

        static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || s.Substring(i, literal.Length) != literal)
                throw new FormatException("Invalid JSON literal at position " + i);
            i += literal.Length;
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++; // skip {
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return dict; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (s[i] != ':') throw new FormatException("Expected ':' at position " + i);
                i++;
                object value = ParseValue(s, ref i);
                dict[key] = value;
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unexpected end of JSON in object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("Expected ',' or '}' at position " + i);
            }
            return dict;
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // skip [
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }

            while (true)
            {
                object value = ParseValue(s, ref i);
                list.Add(value);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unexpected end of JSON in array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("Expected ',' or ']' at position " + i);
            }
            return list;
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected '\"' at position " + i);
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i];
                if (c == '\\')
                {
                    i++;
                    char esc = s[i];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            string hex = s.Substring(i + 1, 4);
                            sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default:
                            throw new FormatException("Invalid escape sequence at position " + i);
                    }
                    i++;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            i++; // closing quote
            return sb.ToString();
        }

        static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
                i++;
            string numStr = s.Substring(start, i - start);
            return double.Parse(numStr, CultureInfo.InvariantCulture);
        }

        static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }

    /// <summary>
    /// Defensive accessors for the object graph produced by MiniJson.Parse, since we can't
    /// be 100% certain of TVDB v4's exact field casing/naming for every endpoint without
    /// live-testing against the real API. Each getter accepts several candidate key names
    /// and returns the first that matches, so a naming mismatch degrades to "field missing"
    /// (logged) rather than a crash.
    /// </summary>
    static class JsonHelpers
    {
        public static Dictionary<string, object> AsObject(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static List<object> AsArray(object o)
        {
            return o as List<object>;
        }

        public static object Get(Dictionary<string, object> obj, params string[] candidateKeys)
        {
            if (obj == null) return null;
            foreach (string key in candidateKeys)
            {
                object value;
                if (obj.TryGetValue(key, out value) && value != null)
                    return value;
            }
            return null;
        }

        public static string GetString(Dictionary<string, object> obj, params string[] candidateKeys)
        {
            object v = Get(obj, candidateKeys);
            if (v == null) return string.Empty;
            if (v is double) return ((double)v).ToString(CultureInfo.InvariantCulture);
            if (v is bool) return ((bool)v) ? "1" : "0";
            return v.ToString();
        }

        public static int GetInt(Dictionary<string, object> obj, params string[] candidateKeys)
        {
            object v = Get(obj, candidateKeys);
            if (v == null) return 0;
            if (v is double) return (int)(double)v;
            int result;
            int.TryParse(v.ToString(), out result);
            return result;
        }

        public static List<object> GetArray(Dictionary<string, object> obj, params string[] candidateKeys)
        {
            object v = Get(obj, candidateKeys);
            return v as List<object>;
        }

        /// <summary>
        /// Joins a JSON array of strings (e.g. genres, aliases) with the given separator,
        /// matching the old v3 "|Genre1|Genre2|" style used throughout the local DB.
        /// </summary>
        public static string JoinStringArray(List<object> arr, string separator)
        {
            if (arr == null || arr.Count == 0) return string.Empty;
            var parts = new List<string>();
            foreach (object item in arr)
            {
                if (item == null) continue;
                if (item is Dictionary<string, object>)
                {
                    // some arrays are objects with a "name" field (e.g. genres in some responses)
                    string name = GetString((Dictionary<string, object>)item, "name");
                    if (!string.IsNullOrEmpty(name)) parts.Add(name);
                }
                else
                {
                    parts.Add(item.ToString());
                }
            }
            return parts.Count == 0 ? string.Empty : (separator + string.Join(separator, parts.ToArray()) + separator);
        }
    }
}

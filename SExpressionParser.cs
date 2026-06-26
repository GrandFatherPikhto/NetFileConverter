using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NetFileConverter
{
    public static class SExpressionParser
    {
        /// <summary>
        /// Разбирает S-выражение нетлиста KiCad в дерево вложенных списков.
        /// </summary>
        public static List<object>? Parse(string text)
        {
            var tokenRegex = new Regex(@"([()]|""[^""\\]*(?:\\.[^""\\]*)*""|[^\s()]+)");
            var matches = tokenRegex.Matches(text);

            var stack = new Stack<List<object>>();
            stack.Push(new List<object>());

            foreach (Match match in matches)
            {
                string token = match.Value;
                if (token == "(")
                {
                    var newList = new List<object>();
                    stack.Peek().Add(newList);
                    stack.Push(newList);
                }
                else if (token == ")")
                {
                    if (stack.Count > 1) stack.Pop();
                }
                else
                {
                    if (token.StartsWith("\"") && token.EndsWith("\""))
                    {
                        token = token.Substring(1, token.Length - 2).Replace("\\\"", "\"");
                    }
                    stack.Peek().Add(token);
                }
            }

            var root = stack.ToArray()[stack.Count - 1];
            if (root.Count > 0 && root[0] is List<object> res)
            {
                return res;
            }
            return null;
        }
    }
}

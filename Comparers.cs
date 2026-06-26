using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NetFileConverter
{
    /// <summary>
    /// Сортировка пинов по-человечески: числовые пины идут по порядку (9 перед 13).
    /// </summary>
    public class PinComparer : IComparer<string>
    {
        public int Compare(string? x, string? y) // Добавили знаки вопроса
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            bool xIsNum = int.TryParse(x, out int xNum);
            bool yIsNum = int.TryParse(y, out int yNum);

            if (xIsNum && yIsNum) return xNum.CompareTo(yNum);
            if (xIsNum) return -1;
            if (yIsNum) return 1;
            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }

    public class RefComparer : IComparer<string>
    {
        public int Compare(string? x, string? y) // Добавили знаки вопроса
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var matchX = Regex.Match(x, @"([A-Za-z]+)(\d+)");
            var matchY = Regex.Match(y, @"([A-Za-z]+)(\d+)");

            if (matchX.Success && matchY.Success)
            {
                string lettersX = matchX.Groups[1].Value;
                string lettersY = matchY.Groups[1].Value;
                int compLetters = string.Compare(lettersX, lettersY, StringComparison.Ordinal);
                if (compLetters != 0) return compLetters;

                int numX = int.Parse(matchX.Groups[2].Value);
                int numY = int.Parse(matchY.Groups[2].Value);
                return numX.CompareTo(numY);
            }
            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }

}

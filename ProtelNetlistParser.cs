using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NetFileConverter
{
    /// <summary>
    /// Парсер формата Protel Netlist 2.0.
    ///
    /// Структура файла:
    ///   PROTEL NETLIST 2.0          ← заголовок
    ///   [                           ← начало блока компонента
    ///   DESIGNATOR                  ← ключ
    ///   C1                          ← значение
    ///   FOOTPRINT
    ///   CAPC1608X90N
    ///   PARTTYPE
    ///   100nF
    ///   ...
    ///   ]                           ← конец блока компонента
    ///   (                           ← начало блока цепи
    ///   +3V3                        ← имя цепи
    ///   C1-2 100nF- Passive         ← узел: REF-PIN Value- ...
    ///   )                           ← конец блока цепи
    /// </summary>
    public static class ProtelNetlistParser
    {
        // Разбираем узел вида "C1-2 100nF- Passive" или "B1-104 10CL006YE144C8G- Passive"
        // REF содержит только буквы+цифры, PIN — всё после последнего дефиса перед пробелом
        // Надёжнее: берём первое слово, делим по ПОСЛЕДНЕМУ дефису
        private static readonly Regex NodeRegex =
            new Regex(@"^(\S+?)-(\S+?)\s+", RegexOptions.Compiled);

        public static (Dictionary<string, ComponentInfo> components,
                       SortedDictionary<string, Dictionary<string, SortedSet<string>>> nets,
                       bool hasWarnings)
            Parse(string filePath)
        {
            FileLogger.Log($"[Protel] Старт парсинга: {Path.GetFileName(filePath)}");

            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath, DetectEncoding(filePath));
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[Protel] Ошибка чтения файла: {ex.Message}");
                return (new Dictionary<string, ComponentInfo>(),
                        new SortedDictionary<string, Dictionary<string, SortedSet<string>>>(),
                        false);
            }

            var components = new Dictionary<string, ComponentInfo>(StringComparer.OrdinalIgnoreCase);
            var nets = new SortedDictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.Ordinal);
            bool hasWarnings = false;

            int i = 0;

            // Пропускаем заголовок
            if (lines.Length > 0 && lines[0].StartsWith("PROTEL NETLIST", StringComparison.OrdinalIgnoreCase))
                i = 1;

            while (i < lines.Length)
            {
                string raw = lines[i].Trim();

                // ── Блок компонента ──────────────────────────────────────────
                if (raw == "[")
                {
                    i++;
                    var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string? lastKey = null;

                    while (i < lines.Length && lines[i].Trim() != "]")
                    {
                        string line = lines[i].Trim();

                        // Строки "*" — признак пина; в данном контексте нас не интересуют
                        if (line == "*" || line == "")
                        {
                            lastKey = null; // следующая строка уже не значение
                            i++;
                            continue;
                        }

                        // Если прошлый ключ ещё не получил значение — это значение
                        if (lastKey != null)
                        {
                            if (!fields.ContainsKey(lastKey))
                                fields[lastKey] = line;
                            lastKey = null;
                        }
                        else
                        {
                            // Это ключ
                            lastKey = line;
                        }
                        i++;
                    }
                    i++; // пропускаем "]"

                    if (fields.TryGetValue("DESIGNATOR", out string? designator) && !string.IsNullOrWhiteSpace(designator))
                    {
                        string refName = designator.Trim();
                        string value = fields.TryGetValue("PARTTYPE", out string? pt) && !string.IsNullOrWhiteSpace(pt)
                            ? pt.Trim() : "~";
                        string footprint = fields.TryGetValue("FOOTPRINT", out string? fp) && !string.IsNullOrWhiteSpace(fp)
                            ? fp.Trim() : "";

                        bool isMissing = string.IsNullOrWhiteSpace(footprint);
                        if (isMissing) hasWarnings = true;

                        if (!components.ContainsKey(refName))
                        {
                            components[refName] = new ComponentInfo
                            {
                                Value = value,
                                Footprint = isMissing ? "!!! НЕТ ФУТПРИНТА !!!" : footprint,
                                IsMissingFp = isMissing
                            };
                        }
                    }
                    continue;
                }

                // ── Блок цепи ────────────────────────────────────────────────
                if (raw == "(")
                {
                    i++;
                    if (i >= lines.Length) break;

                    string netName = lines[i].Trim();
                    i++;

                    // Фильтруем пустые имена и unconnected
                    bool skip = string.IsNullOrWhiteSpace(netName)
                                || netName.IndexOf("unconnected", StringComparison.OrdinalIgnoreCase) >= 0;

                    var netNodes = skip
                        ? null
                        : nets.TryGetValue(netName, out var existing)
                            ? existing
                            : nets[netName] = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

                    while (i < lines.Length && lines[i].Trim() != ")")
                    {
                        string nodeLine = lines[i].Trim();
                        i++;

                        if (string.IsNullOrEmpty(nodeLine) || skip) continue;

                        // Формат: "REF-PIN Value- Passive"
                        // Находим первый дефис, отделяющий ref от pin
                        int dashIdx = nodeLine.IndexOf('-');
                        if (dashIdx <= 0) continue;

                        // PIN — всё от дефиса+1 до первого пробела
                        string refPart = nodeLine.Substring(0, dashIdx).Trim();
                        string rest = nodeLine.Substring(dashIdx + 1);
                        int spaceIdx = rest.IndexOf(' ');
                        string pinPart = spaceIdx > 0 ? rest.Substring(0, spaceIdx).Trim() : rest.Trim();

                        if (string.IsNullOrEmpty(refPart) || string.IsNullOrEmpty(pinPart)) continue;

                        if (!netNodes!.ContainsKey(refPart))
                            netNodes[refPart] = new SortedSet<string>(new PinComparer());
                        netNodes[refPart].Add(pinPart);
                    }
                    i++; // пропускаем ")"
                    continue;
                }

                i++;
            }

            FileLogger.Log($"[Protel] Готово: {components.Count} компонентов, {nets.Count} цепей.");
            return (components, nets, hasWarnings);
        }

        /// <summary>
        /// Определяем кодировку файла по BOM или возвращаем Windows-1252 как типичную для Protel/Altium.
        /// </summary>
        private static Encoding DetectEncoding(string path)
        {
            byte[] bom = new byte[4];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            int read = fs.Read(bom, 0, 4);

            if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return Encoding.UTF8;
            if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
                return Encoding.Unicode;
            if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            // Altium традиционно пишет в Windows-1252 / CP1252
            try { return Encoding.GetEncoding(1252); }
            catch { return Encoding.Latin1; }
        }
    }
}

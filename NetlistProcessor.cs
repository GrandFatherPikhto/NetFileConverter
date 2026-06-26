using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NetFileConverter
{
    public class NetlistProcessor
    {
        // ── Вспомогательные методы для KiCad ─────────────────────────────

        private string CleanRef(string refName)
        {
            var match = Regex.Match(refName, @"^([A-Z]+\d+)[A-Z]?$");
            return match.Success ? match.Groups[1].Value : refName;
        }

        private bool IsTag(object item, string tag)
        {
            return item is List<object> list && list.Count > 0 && list[0] is string s && s == tag;
        }

        private string GetValueForTag(List<object> block, string tag)
        {
            foreach (var subItem in block)
            {
                if (IsTag(subItem, tag))
                {
                    var list = (List<object>)subItem;
                    if (list.Count > 1 && list[1] is string val) return val;
                }
            }
            return "~";
        }

        // ── Точка входа: определяем формат и диспетчеризируем ─────────────

        /// <summary>
        /// Определяет формат нетлиста по содержимому файла (не по расширению).
        /// </summary>
        private static NetlistFormat DetectFormat(string filePath)
        {
            try
            {
                // Читаем только первые 64 байта — достаточно для детекции
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] buf = new byte[64];
                int n = fs.Read(buf, 0, buf.Length);
                string header = Encoding.ASCII.GetString(buf, 0, n);

                if (header.StartsWith("PROTEL NETLIST", StringComparison.OrdinalIgnoreCase))
                    return NetlistFormat.Protel2;

                // KiCad нетлист начинается с "(export" или пробелов перед ним
                if (header.TrimStart().StartsWith("(export", StringComparison.OrdinalIgnoreCase))
                    return NetlistFormat.KiCad;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"[Detect] Ошибка определения формата: {ex.Message}");
            }

            return NetlistFormat.Unknown;
        }

        public void ParseAndSimplify(string netlistPath)
        {
            FileLogger.Log($"--- Старт обработки: {Path.GetFileName(netlistPath)} ---");

            var format = DetectFormat(netlistPath);
            FileLogger.Log($"[Detect] Формат: {format}");

            switch (format)
            {
                case NetlistFormat.Protel2:
                    ParseProtel(netlistPath);
                    break;
                case NetlistFormat.KiCad:
                    ParseKiCad(netlistPath);
                    break;
                default:
                    FileLogger.Log($"[Detect] Неизвестный формат файла: {Path.GetFileName(netlistPath)}. Пропуск.");
                    break;
            }
        }

        // ── Protel 2.0 ────────────────────────────────────────────────────

        private void ParseProtel(string netlistPath)
        {
            var (components, nets, hasWarnings) = ProtelNetlistParser.Parse(netlistPath);
            WriteOutputFiles(netlistPath, components, nets, hasWarnings);
        }

        // ── KiCad ─────────────────────────────────────────────────────────

        private void ParseKiCad(string netlistPath)
        {
            string content;
            try
            {
                content = File.ReadAllText(netlistPath, Encoding.UTF8);
            }
            catch (Exception e)
            {
                FileLogger.Log($"Критическая ошибка чтения файла: {e.Message}");
                return;
            }

            List<object>? tree = SExpressionParser.Parse(content);
            if (tree == null || tree.Count == 0 || !(tree[0] is string rootTag) || rootTag != "export")
            {
                FileLogger.Log("Ошибка: Неверный формат нетлиста KiCad (отсутствует export).");
                return;
            }

            var components = new Dictionary<string, ComponentInfo>();
            var simplifiedNets = new SortedDictionary<string, Dictionary<string, SortedSet<string>>>();
            bool hasWarnings = false;

            for (int i = 1; i < tree.Count; i++)
            {
                if (!(tree[i] is List<object> block)) continue;

                if (IsTag(block, "components"))
                {
                    for (int j = 1; j < block.Count; j++)
                    {
                        if (block[j] is List<object> comp && IsTag(comp, "comp"))
                        {
                            string refName = GetValueForTag(comp, "ref");
                            string value = GetValueForTag(comp, "value");
                            string footprint = GetValueForTag(comp, "footprint");

                            if (footprint.Contains(":"))
                                footprint = footprint.Substring(footprint.LastIndexOf(':') + 1);

                            string baseRef = CleanRef(refName);
                            if (!components.ContainsKey(baseRef))
                            {
                                bool isMissing = string.IsNullOrWhiteSpace(footprint) || footprint == "~";
                                if (isMissing) hasWarnings = true;

                                components[baseRef] = new ComponentInfo
                                {
                                    Value = value,
                                    Footprint = isMissing ? "!!! НЕТ ФУТПРИНТА !!!" : footprint,
                                    IsMissingFp = isMissing
                                };
                            }
                        }
                    }
                }
                else if (IsTag(block, "nets"))
                {
                    for (int j = 1; j < block.Count; j++)
                    {
                        if (block[j] is List<object> net && IsTag(net, "net"))
                        {
                            string netName = GetValueForTag(net, "name");

                            if (string.IsNullOrWhiteSpace(netName) ||
                                netName.IndexOf("unconnected", StringComparison.OrdinalIgnoreCase) >= 0)
                                continue;

                            if (!simplifiedNets.ContainsKey(netName))
                                simplifiedNets[netName] = new Dictionary<string, SortedSet<string>>();

                            foreach (var item in net)
                            {
                                if (item is List<object> node && IsTag(node, "node"))
                                {
                                    string nRef = GetValueForTag(node, "ref");
                                    string nPin = GetValueForTag(node, "pin");
                                    string baseRef = CleanRef(nRef);

                                    if (!simplifiedNets[netName].ContainsKey(baseRef))
                                        simplifiedNets[netName][baseRef] = new SortedSet<string>(new PinComparer());
                                    simplifiedNets[netName][baseRef].Add(nPin);
                                }
                            }
                        }
                    }
                }
            }

            WriteOutputFiles(netlistPath, components, simplifiedNets, hasWarnings);
        }

        // ── Запись результатов (общая для обоих форматов) ─────────────────

        private void WriteOutputFiles(
            string netlistPath,
            Dictionary<string, ComponentInfo> components,
            SortedDictionary<string, Dictionary<string, SortedSet<string>>> simplifiedNets,
            bool hasWarnings)
        {
            string? dirName = Path.GetDirectoryName(netlistPath);
            string outDir = Path.Combine(dirName ?? AppContext.BaseDirectory, "out");
            Directory.CreateDirectory(outDir);
            string baseName = Path.GetFileNameWithoutExtension(netlistPath);

            // Нетлист
            string netOutPath = Path.Combine(outDir, $"{baseName}_net.txt");
            using (var writer = new StreamWriter(netOutPath, false, Encoding.UTF8))
            {
                writer.WriteLine($"=== Упрощённый нетлист для {Path.GetFileName(netlistPath)} ===");
                writer.WriteLine("(Изолированные и unconnected цепи отфильтрованы)\n");

                foreach (var netKvp in simplifiedNets)
                {
                    writer.WriteLine($"Сеть: {netKvp.Key}");
                    foreach (var compKvp in netKvp.Value)
                    {
                        string pinsStr = string.Join(", ", compKvp.Value);
                        components.TryGetValue(compKvp.Key, out var compInfo);
                        string valStr = compInfo != null ? compInfo.Value : "?";
                        string alertStr = (compInfo != null && compInfo.IsMissingFp) ? " [⚠️ ВНИМАНИЕ: НЕТ КОРПУСА]" : "";
                        writer.WriteLine($"  └─ {compKvp.Key} ({valStr}){alertStr} -> пины: {pinsStr}");
                    }
                    writer.WriteLine();
                }
            }

            // BOM
            string bomOutPath = Path.Combine(outDir, $"{baseName}_bom.txt");
            using (var writer = new StreamWriter(bomOutPath, false, Encoding.UTF8))
            {
                writer.WriteLine($"=== BOM (Список компонентов) для {Path.GetFileName(netlistPath)} ===\n");

                if (hasWarnings)
                {
                    writer.WriteLine("⚠️⚠️⚠️ ВНИМАНИЕ! НАЙДЕНЫ КОМПОНЕНТЫ БЕЗ ПОСАДОЧНЫХ МЕСТ (FOOTPRINT): ⚠️⚠️⚠️");
                    foreach (var kvp in components)
                        if (kvp.Value.IsMissingFp)
                            writer.WriteLine($"  - {kvp.Key} (Номинал: {kvp.Value.Value})");
                    writer.WriteLine($"\n{new string('-', 73)}\n");
                }

                writer.WriteLine($"{"Компонент",-12} | {"Номинал",-25} | {"Корпус (Footprint)",-30}");
                writer.WriteLine(new string('-', 73));

                var sortedRefs = new List<string>(components.Keys);
                sortedRefs.Sort(new RefComparer());

                foreach (var baseRef in sortedRefs)
                {
                    var info = components[baseRef];
                    writer.WriteLine($"{baseRef,-12} | {info.Value,-25} | {info.Footprint,-30}");
                }
            }

            FileLogger.Log($"Успешно сгенерированы отчёты в /out/ для {baseName}");
        }
    }

    public enum NetlistFormat
    {
        Unknown,
        KiCad,
        Protel2
    }
}
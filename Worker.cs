using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace NetFileConverter
{
    public class Worker : BackgroundService
    {
        // Удаляет суффиксы секций KiCad (U1A -> U1)
        private string CleanRef(string refName)
        {
            var match = Regex.Match(refName, @"^([A-Z]+\d+)[A-Z]?$");
            return match.Success ? match.Groups[1].Value : refName;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Здесь должна быть логика запуска первоначального анализа и включения watchdog,
            // которую мы писали в Python-версии. Например:

            Console.WriteLine("Фоновая служба парсера KiCad успешно запущена.");

            // Держим службу запущенной, пока приложение не закроют
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }

        // Проверяет, является ли элемент списком, который начинается с нужного тега
        private bool IsTag(object item, string tag)
        {
            return item is List<object> list && list.Count > 0 && list[0] is string s && s == tag;
        }

        // Безопасное извлечение текстового значения для тега из блока
        private string GetValueForTag(List<object> block, string tag)
        {
            foreach (var subItem in block)
            {
                if (IsTag(subItem, tag))
                {
                    var list = (List<object>)subItem;
                    if (list.Count > 1 && list[1] is string val)
                    {
                        return val;
                    }
                }
            }
            return "~";
        }

        public void ParseAndSimplify(string netlistPath)
        {
            // Инициализируем наш новый логгер рядом с целевым файлом
            FileLogger.Initialize(netlistPath);
            FileLogger.Log($"Начало обработки файла: {Path.GetFileName(netlistPath)}");

            string content;

            try
            {
                content = File.ReadAllText(netlistPath, Encoding.UTF8);
                FileLogger.Log($"Файл успешно прочитан. Размер: {content.Length} символов.");
            }
            catch (Exception e)
            {
                FileLogger.Log($"КРИТИЧЕСКАЯ ОШИБКА ЧТЕНИЯ ФАЙЛА: {e.Message}");
                return;
            }

            // Вызываем парсер S-выражений
            List<object>? tree = SExpressionParser.Parse(content);
            
            if (tree == null)
            {
                FileLogger.Log("ОШИБКА: Парсер SExpressionParser.Parse вернул null!");
                return;
            }

            FileLogger.Log($"Парсер вернул дерево. Количество элементов на верхнем уровне: {tree.Count}");
            
            if (tree.Count > 0)
            {
                FileLogger.Log($"Тип первого элемента дерева: {tree[0]?.GetType().Name}");
                FileLogger.Log($"Значение первого элемента дерева: '{tree[0]}'");
            }

            // Проверяем корень на "export"
            if (tree.Count == 0 || !(tree[0] is string rootTag) || rootTag != "export")
            {
                FileLogger.Log("ОШИБКА ФОРМАТА: Первый элемент дерева НЕ является строкой 'export'. Выход из парсера.");
                return;
            }

            FileLogger.Log("Проверка корня 'export' ПРОЙДЕНА успешно. Начинаем обход блоков...");

            var components = new Dictionary<string, ComponentInfo>();
            var simplifiedNets = new SortedDictionary<string, Dictionary<string, SortedSet<string>>>();
            bool hasWarnings = false;

            // Обходим блоки внутри "export"
            for (int i = 1; i < tree.Count; i++)
            {
                if (!(tree[i] is List<object> block))
                {
                    FileLogger.Log($"Строка {i}: Элемент не является списком (пропускаем). Тип: {tree[i]?.GetType().Name}");
                    continue;
                }

                if (block.Count == 0 || !(block[0] is string blockTag))
                {
                    FileLogger.Log($"Строка {i}: Список пустой или не начинается с текстового тега.");
                    continue;
                }

                FileLogger.Log($"Строка {i}: Обнаружен блок '{blockTag}' с количеством подэлементов: {block.Count}");

                // 1. Сбор данных для BOM
                if (blockTag == "components")
                {
                    FileLogger.Log(" -> Заходим в разбор компонентов...");
                    int compCount = 0;
                    for (int j = 1; j < block.Count; j++)
                    {
                        if (block[j] is List<object> comp && comp.Count > 0 && comp[0] is string compTag && compTag == "comp")
                        {
                            compCount++;
                            string refName = GetValueForTag(comp, "ref");
                            string value = GetValueForTag(comp, "value");
                            string footprint = GetValueForTag(comp, "footprint");

                            if (footprint.Contains(":"))
                            {
                                footprint = footprint.Substring(footprint.LastIndexOf(':') + 1);
                            }

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
                    FileLogger.Log($" -> Завершен разбор компонентов. Успешно обработано 'comp': {compCount}");
                }
                // 2. Сбор данных для Нетлиста
                else if (blockTag == "nets")
                {
                    FileLogger.Log(" -> Заходим в разбор цепей (nets)...");
                    int netCount = 0;
                    for (int j = 1; j < block.Count; j++)
                    {
                        if (block[j] is List<object> net && net.Count > 0 && net[0] is string netTag && netTag == "net")
                        {
                            netCount++;
                            string netName = GetValueForTag(net, "name");

                            if (string.IsNullOrWhiteSpace(netName) || netName.ToLower().Contains("unconnected"))
                            {
                                continue;
                            }

                            if (!simplifiedNets.ContainsKey(netName))
                            {
                                simplifiedNets[netName] = new Dictionary<string, SortedSet<string>>();
                            }

                            foreach (var item in net)
                            {
                                if (item is List<object> node && node.Count > 0 && node[0] is string nodeTag && nodeTag == "node")
                                {
                                    string nRef = GetValueForTag(node, "ref");
                                    string nPin = GetValueForTag(node, "pin");
                                    string baseRef = CleanRef(nRef);

                                    if (!simplifiedNets[netName].ContainsKey(baseRef))
                                    {
                                        simplifiedNets[netName][baseRef] = new SortedSet<string>(new PinComparer());
                                    }
                                    simplifiedNets[netName][baseRef].Add(nPin);
                                }
                            }
                        }
                    }
                    FileLogger.Log($" -> Завершен разбор цепей. Успешно обработано 'net': {netCount}");
                }
            }

            FileLogger.Log($"Сбор данных завершен. Найдено уникальных компонентов: {components.Count}, цепей: {simplifiedNets.Count}");
            FileLogger.Log("Переходим к вызову метода WriteOutputFiles...");

            // Выгружаем результаты в файлы (этот метод у вас остается без изменений)
            WriteOutputFiles(netlistPath, components, simplifiedNets, hasWarnings);
            
            FileLogger.Log("=== КОНЕЦ ОТЛАДКИ: Все файлы успешно сгенерированы! ===");
        }


        private void WriteOutputFiles(string netlistPath, Dictionary<string, ComponentInfo> components, 
            SortedDictionary<string, Dictionary<string, SortedSet<string>>> simplifiedNets, bool hasWarnings)
        {
            string outDir = Path.Combine(Path.GetDirectoryName(netlistPath), "out");
            Directory.CreateDirectory(outDir);
            string baseName = Path.GetFileNameWithoutExtension(netlistPath);

            // Запись упрощенного нетлиста
            string netOutPath = Path.Combine(outDir, $"{baseName}_net.txt");
            using (var writer = new StreamWriter(netOutPath, false, Encoding.UTF8))
            {
                writer.WriteLine($"=== Упрощенный нетлист для {Path.GetFileName(netlistPath)} ===");
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

            // Запись BOM
            string bomOutPath = Path.Combine(outDir, $"{baseName}_bom.txt");
            using (var writer = new StreamWriter(bomOutPath, false, Encoding.UTF8))
            {
                writer.WriteLine($"=== BOM (Список компонентов) для {Path.GetFileName(netlistPath)} ===\n");

                if (hasWarnings)
                {
                    writer.WriteLine("⚠️⚠️⚠️ ВНИМАНИЕ! НАЙДЕНЫ КОМПОНЕНТЫ БЕЗ ПОСАДОЧНЫХ МЕСТ (FOOTPRINT): ⚠️⚠️⚠️");
                    foreach (var kvp in components)
                    {
                        if (kvp.Value.IsMissingFp) writer.WriteLine($"  - {kvp.Key} (Номинал: {kvp.Value.Value})");
                    }
                    writer.WriteLine($"\n{new string('-', 73)}\n");
                }

                writer.WriteLine($"{"Компонент",-12} | {"Номинал",-25} | {"Корпус (Footprint)",-30}");
                writer.WriteLine(new string('-', 73));

                var sortedComponents = new List<string>(components.Keys);
                // Используем наш вынесенный RefComparer
                sortedComponents.Sort(new RefComparer());

                foreach (var baseRef in sortedComponents)
                {
                    var info = components[baseRef];
                    writer.WriteLine($"{baseRef,-12} | {info.Value,-25} | {info.Footprint,-30}");
                }
            }

            Console.WriteLine($" Успешно созданы файлы в out/:\n   - {Path.GetFileName(netOutPath)}\n   - {Path.GetFileName(bomOutPath)}\n");
        }
    }
}

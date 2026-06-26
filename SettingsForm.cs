using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;

namespace NetFileConverter
{
    /// <summary>
    /// Форма настроек. UI-разметка находится в SettingsForm.Designer.cs.
    /// Здесь — только логика.
    /// </summary>
    public partial class SettingsForm : Form
    {
        // ── Модель строки ────────────────────────────────────────────────────
        private sealed class WatchedEntry
        {
            public string Path   { get; set; } = "";
            public string Format { get; set; } = "Protel2";
        }

        // ── Поля ─────────────────────────────────────────────────────────────
        private readonly string _configPath;

        // ── Конструктор ──────────────────────────────────────────────────────
        public SettingsForm()
        {
            InitializeComponent();   // из Designer.cs
            _configPath = GetConfigPath();
            SetupListView();
        }

        // ── Настройка ListView ───────────────────────────────────────────────
        private void SetupListView()
        {
            listViewWatchedFolders.View = View.Details;
            listViewWatchedFolders.FullRowSelect = true;
            listViewWatchedFolders.GridLines = true;
            listViewWatchedFolders.MultiSelect = false;
            listViewWatchedFolders.Font = new Font("Consolas", 9f);

            listViewWatchedFolders.Columns.Add("Формат", 70, HorizontalAlignment.Center);
            listViewWatchedFolders.Columns.Add("Путь к папке", 360, HorizontalAlignment.Left);
        }

        // ── Загрузка формы ───────────────────────────────────────────────────
        private void SettingsForm_Load(object sender, EventArgs e)
        {
            LoadSettings();
        }

        // ── Загрузка конфигурации ────────────────────────────────────────────
        private void LoadSettings()
        {
            listViewWatchedFolders.Items.Clear();

            if (!File.Exists(_configPath)) return;

            var builder = new ConfigurationBuilder()
                .SetBasePath(System.IO.Path.GetDirectoryName(_configPath)!)
                .AddJsonFile(System.IO.Path.GetFileName(_configPath), optional: true);
            var config = builder.Build();

            var section = config.GetSection("WatcherSettings:SourceDirectories");
            var children = section.GetChildren().ToList();

            if (children.Any())
            {
                // Новый формат: массив объектов { Path, Format }
                foreach (var child in children)
                {
                    string? path   = child["Path"];
                    string? format = child["Format"];
                    if (!string.IsNullOrWhiteSpace(path))
                        AddRow(new WatchedEntry { Path = NormalizePath(path), Format = format ?? "Protel2" });
                }
            }
            else
            {
                // Старый формат: массив строк
                var old = section.Get<string[]>();
                if (old != null)
                    foreach (var p in old)
                        AddRow(new WatchedEntry { Path = NormalizePath(p), Format = "Protel2" });
            }

            AutoResizePathColumn();
        }

        // ── Вспомогательные методы ───────────────────────────────────────────
        private void AddRow(WatchedEntry entry)
        {
            var item = new ListViewItem(entry.Format) { Tag = entry };
            item.SubItems.Add(entry.Path);

            // Цветовая маркировка форматов
            item.ForeColor = entry.Format == "KiCad" ? Color.DarkBlue : Color.DarkGreen;

            listViewWatchedFolders.Items.Add(item);
        }

        private void AutoResizePathColumn()
        {
            if (listViewWatchedFolders.Items.Count > 0)
                listViewWatchedFolders.Columns[1].AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            path = path.TrimEnd('\\', '/');
            try   { return System.IO.Path.GetFullPath(path); }
            catch { return path; }
        }

        private static string GetConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder  = System.IO.Path.Combine(appData, "NetFileConverter");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return System.IO.Path.Combine(folder, "appsettings.json");
        }

        // ── Кнопка «Добавить» ────────────────────────────────────────────────
        private void btnAddFolder_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description  = "Выберите папку для отслеживания",
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            string norm = NormalizePath(dlg.SelectedPath);

            // Проверяем дубликат
            bool exists = listViewWatchedFolders.Items
                .Cast<ListViewItem>()
                .Any(it => ((WatchedEntry)it.Tag!).Path.Equals(norm, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                MessageBox.Show("Эта папка уже добавлена.", "Предупреждение",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Диалог выбора формата
            string format = AskFormat(norm);
            if (format == null!) return;  // пользователь закрыл окно

            AddRow(new WatchedEntry { Path = norm, Format = format });
            AutoResizePathColumn();
        }

        /// <summary>Небольшое всплывающее окошко выбора формата нетлиста.</summary>
        private string AskFormat(string folderPath)
        {
            using var dlg = new Form
            {
                Text = "Формат нетлиста",
                Width = 360,
                Height = 160,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lbl = new Label
            {
                Text = $"Папка: {System.IO.Path.GetFileName(folderPath)}\nВыберите формат нетлиста:",
                Left = 12, Top = 12, Width = 320, Height = 40,
                Font = new Font("Segoe UI", 9f)
            };

            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Left = 12, Top = 60, Width = 200,
                Font = new Font("Segoe UI", 10f)
            };
            combo.Items.AddRange(new object[] { "Protel2", "KiCad" });
            combo.SelectedIndex = 0;

            var btnOk = new Button
            {
                Text = "OK", DialogResult = DialogResult.OK,
                Left = 225, Top = 58, Width = 100, Height = 28,
                Font = new Font("Segoe UI", 9f)
            };
            dlg.AcceptButton = btnOk;

            dlg.Controls.AddRange(new Control[] { lbl, combo, btnOk });

            return dlg.ShowDialog(this) == DialogResult.OK
                ? combo.SelectedItem!.ToString()!
                : null!;
        }

        // ── Кнопка «Удалить» ─────────────────────────────────────────────────
        private void btnRemoveFolder_Click(object sender, EventArgs e)
        {
            if (listViewWatchedFolders.SelectedItems.Count == 0)
            {
                MessageBox.Show("Выберите строку для удаления.", "Подсказка",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            listViewWatchedFolders.Items.Remove(listViewWatchedFolders.SelectedItems[0]);
        }

        // ── Кнопка «Формат (K/P)» ────────────────────────────────────────────
        private void btnToggleFormat_Click(object sender, EventArgs e)
        {
            if (listViewWatchedFolders.SelectedItems.Count == 0)
            {
                MessageBox.Show("Выберите строку для смены формата.", "Подсказка",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var item  = listViewWatchedFolders.SelectedItems[0];
            var entry = (WatchedEntry)item.Tag!;

            entry.Format   = entry.Format == "KiCad" ? "Protel2" : "KiCad";
            item.Text      = entry.Format;
            item.ForeColor = entry.Format == "KiCad" ? Color.DarkBlue : Color.DarkGreen;
        }

        // ── Кнопка «Сохранить» ───────────────────────────────────────────────
        private void btnSave_Click(object sender, EventArgs e)
        {
            try
            {
                var entries = listViewWatchedFolders.Items
                    .Cast<ListViewItem>()
                    .Select(it => (WatchedEntry)it.Tag!)
                    .ToList();

                // Читаем существующий JSON, сохраняем остальные секции (Logging и т.д.)
                string json = File.Exists(_configPath)
                    ? File.ReadAllText(_configPath)
                    : "{}";

                using var doc = JsonDocument.Parse(json);
                using var mem = new MemoryStream();
                using var w   = new Utf8JsonWriter(mem, new JsonWriterOptions { Indented = true });

                w.WriteStartObject();

                bool watcherWritten = false;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "WatcherSettings")
                    {
                        WriteWatcherSection(w, entries);
                        watcherWritten = true;
                    }
                    else
                    {
                        prop.WriteTo(w);
                    }
                }

                // Если секции WatcherSettings ещё не было
                if (!watcherWritten)
                    WriteWatcherSection(w, entries);

                w.WriteEndObject();
                w.Flush();

                File.WriteAllBytes(_configPath, mem.ToArray());

                MessageBox.Show(
                    "Настройки сохранены.\nСлужба применит их автоматически через несколько секунд.",
                    "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения:\n{ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void WriteWatcherSection(Utf8JsonWriter w, List<WatchedEntry> entries)
        {
            w.WriteStartObject("WatcherSettings");
            w.WriteStartArray("SourceDirectories");
            foreach (var e in entries)
            {
                w.WriteStartObject();
                w.WriteString("Path",   e.Path);
                w.WriteString("Format", e.Format);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }

        // ── Кнопка «Отмена» ──────────────────────────────────────────────────
        private void btnCancel_Click(object sender, EventArgs e) => Close();
    }
}
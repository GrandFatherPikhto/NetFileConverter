using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace FolderWatcher.Service
{
    public partial class SettingsForm : Form
    {
        private readonly string _configPath;
        private ListBox _listBox = null!;
        private Button _addButton = null!;
        private Button _removeButton = null!;
        private Button _saveButton = null!;
        private IConfigurationRoot _configuration = null!;

        // Модель для элемента списка
        private class SourceDirectory
        {
            public string Path { get; set; } = "";
            public string Format { get; set; } = "Protel2";
            public override string ToString() => $"[{Format}] {Path}";
        }

        private static string GetConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "NetFileConverter");
            if (!Directory.Exists(appFolder))
                Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, "appsettings.json");
        }

        public SettingsForm()
        {
            InitializeComponent();
            _configPath = GetConfigPath();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            this.Text = "Настройки Folder Watcher";
            this.Size = new Size(550, 400);
            this.StartPosition = FormStartPosition.CenterScreen;

            _listBox = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };

            _addButton = new Button { Text = "Добавить папку", Dock = DockStyle.Bottom, Height = 30 };
            _removeButton = new Button { Text = "Удалить", Dock = DockStyle.Bottom, Height = 30 };
            _saveButton = new Button { Text = "Сохранить", Dock = DockStyle.Bottom, Height = 30 };

            var panel = new Panel { Dock = DockStyle.Bottom, Height = 95 };
            panel.Controls.Add(_addButton);
            panel.Controls.Add(_removeButton);
            panel.Controls.Add(_saveButton);
            _addButton.Top = 0;
            _removeButton.Top = 30;
            _saveButton.Top = 60;

            this.Controls.Add(_listBox);
            this.Controls.Add(panel);

            _addButton.Click += AddButton_Click;
            _removeButton.Click += RemoveButton_Click;
            _saveButton.Click += SaveButton_Click;
        }

        private void LoadSettings()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(_configPath))
                .AddJsonFile(Path.GetFileName(_configPath), optional: true, reloadOnChange: true);
            _configuration = builder.Build();

            var directories = new List<SourceDirectory>();
            var section = _configuration.GetSection("WatcherSettings:SourceDirectories");

            // Новый формат: массив объектов
            var children = section.GetChildren();
            if (children.Any())
            {
                foreach (var child in children)
                {
                    string path = child["Path"];
                    string format = child["Format"];
                    if (!string.IsNullOrEmpty(path))
                    {
                        directories.Add(new SourceDirectory
                        {
                            Path = path,
                            Format = string.IsNullOrEmpty(format) ? "Protel2" : format
                        });
                    }
                }
            }
            else
            {
                // Старый формат: массив строк
                var old = section.Get<string[]>();
                if (old != null)
                {
                    foreach (var path in old)
                        directories.Add(new SourceDirectory { Path = path, Format = "Protel2" });
                }
            }

            _listBox.Items.Clear();
            foreach (var dir in directories)
                _listBox.Items.Add(dir);
        }

        private void AddButton_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Выберите папку для отслеживания";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                using var fmtForm = new Form()
                {
                    Text = "Выберите формат нетлиста",
                    Width = 300,
                    Height = 150,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false
                };
                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Left = 20,
                    Top = 20,
                    Width = 240
                };
                combo.Items.AddRange(new[] { "Protel2", "KiCad" });
                combo.SelectedIndex = 0;

                var btnOk = new Button { Text = "OK", Left = 100, Top = 70, Width = 80, DialogResult = DialogResult.OK };
                btnOk.Click += (_, _) => fmtForm.Close();
                fmtForm.Controls.Add(combo);
                fmtForm.Controls.Add(btnOk);

                if (fmtForm.ShowDialog() == DialogResult.OK)
                {
                    var newDir = new SourceDirectory
                    {
                        Path = dialog.SelectedPath,
                        Format = combo.SelectedItem?.ToString() ?? "Protel2"
                    };
                    // Проверка на дубликат
                    bool exists = _listBox.Items.Cast<SourceDirectory>().Any(d => d.Path.Equals(newDir.Path, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                        _listBox.Items.Add(newDir);
                    else
                        MessageBox.Show("Эта папка уже добавлена.", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void RemoveButton_Click(object? sender, EventArgs e)
        {
            if (_listBox.SelectedItem != null)
                _listBox.Items.Remove(_listBox.SelectedItem);
        }

        private void SaveButton_Click(object? sender, EventArgs e)
        {
            try
            {
                var directories = _listBox.Items.Cast<SourceDirectory>().ToList();

                // Читаем существующий JSON, чтобы сохранить другие секции (Logging и т.д.)
                string json = File.ReadAllText(_configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                using var stream = new MemoryStream();
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

                writer.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Name == "WatcherSettings")
                    {
                        writer.WriteStartObject("WatcherSettings");
                        writer.WriteStartArray("SourceDirectories");
                        foreach (var d in directories)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("Path", d.Path);
                            writer.WriteString("Format", d.Format);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();

                        // Копируем остальные поля из WatcherSettings (если появятся)
                        foreach (var innerProp in property.Value.EnumerateObject())
                        {
                            if (innerProp.Name != "SourceDirectories")
                                innerProp.WriteTo(writer);
                        }
                        writer.WriteEndObject();
                    }
                    else
                    {
                        property.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
                writer.Flush();

                File.WriteAllBytes(_configPath, stream.ToArray());

                MessageBox.Show("Настройки сохранены. Сервис автоматически применит их через несколько секунд.",
                    "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
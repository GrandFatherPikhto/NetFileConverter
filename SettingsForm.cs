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

        // Возвращает тот же путь, что и в Program.cs
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
            // ... (без изменений, как в исходном коде)
            this.Text = "Настройки Folder Watcher";
            this.Size = new Size(400, 300);
            this.StartPosition = FormStartPosition.CenterScreen;

            _listBox = new ListBox { Dock = DockStyle.Fill };
            _addButton = new Button { Text = "Добавить папку", Dock = DockStyle.Bottom, Height = 30 };
            _removeButton = new Button { Text = "Удалить", Dock = DockStyle.Bottom, Height = 30 };
            _saveButton = new Button { Text = "Сохранить", Dock = DockStyle.Bottom, Height = 30 };

            var panel = new Panel { Dock = DockStyle.Bottom, Height = 90 };
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

            var directories = _configuration.GetSection("WatcherSettings:SourceDirectories").Get<string[]>() ?? Array.Empty<string>();
            _listBox.Items.Clear();
            _listBox.Items.AddRange(directories);
        }

        private void AddButton_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog();
            dialog.Description = "Выберите папку для отслеживания";
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (!_listBox.Items.Contains(dialog.SelectedPath))
                    _listBox.Items.Add(dialog.SelectedPath);
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
                var directories = _listBox.Items.Cast<string>().ToArray();
                string json = File.ReadAllText(_configPath);
                var doc = JsonDocument.Parse(json);
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
                        foreach (var dir in directories)
                            writer.WriteStringValue(dir);
                        writer.WriteEndArray();
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

                MessageBox.Show("Настройки сохранены. Сервис автоматически применит их через несколько секунд.", "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
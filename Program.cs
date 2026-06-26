using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using NetFileConverter;
using NetFileConverter.Core.Interfaces;
using NetFileConverter.Core.Serialization;
using NetFileConverter.Infrastructure.Generators;
using NetFileConverter.Infrastructure.Parsers;

namespace FolderWatcher
{
    internal static class Program
    {
        private static IHost? _host;
        private static NotifyIcon? _trayIcon;
        private static SettingsForm? _settingsForm;

        /// <summary>Возвращает путь к appsettings.json в папке пользователя</summary>
        private static string GetConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "NetFileConverter");
            if (!Directory.Exists(appFolder))
                Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, "appsettings.json");
        }

        /// <summary>Переносит старый конфиг (из папки с exe) или создаёт новый по умолчанию</summary>
        private static void EnsureConfigExists()
        {
            string configPath = GetConfigPath();
            if (File.Exists(configPath))
                return;

            string exePath = System.Environment.ProcessPath ?? AppContext.BaseDirectory;
            string exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            string oldConfigPath = Path.Combine(exeDir, "appsettings.json");

            if (File.Exists(oldConfigPath))
            {
                File.Copy(oldConfigPath, configPath);
                return;
            }

            var defaultConfig = new
            {
                Logging = new
                {
                    LogLevel = new
                    {
                        Default = "Information",
                        Microsoft_Hosting_Lifetime = "Information"
                    }
                },
                WatcherSettings = new
                {
                    SourceDirectories = Array.Empty<object>()
                }
            };
            string json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }

        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();

            EnsureConfigExists();
            string configPath = GetConfigPath();

            string exePath = System.Environment.ProcessPath ?? AppContext.BaseDirectory;
            string exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            Directory.SetCurrentDirectory(exeDir);

            var builder = new HostBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    // Исправлено предупреждение CS8604 проверкой на null через оператор ??
                    config.SetBasePath(Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory);
                    config.AddJsonFile(Path.GetFileName(configPath), optional: false, reloadOnChange: true);

                    string localConfig = Path.Combine(exeDir, "appsettings.json");
                    if (File.Exists(localConfig))
                        config.AddJsonFile(localConfig, optional: true, reloadOnChange: false);
                })
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddConsole();
                    logging.AddDebug();
                })
                .ConfigureServices((context, services) =>
                {
                    // Объединили все ваши регистрации в один чистый DI-контейнер
                    services.AddSingleton<INetlistSerializer, JsonNetlistSerializer>();

                    // Регистрируем ОБА парсера (чтобы Worker мог выбирать нужный)
                    services.AddSingleton<INetlistParser, KiCadParser>();
                    services.AddSingleton<INetlistParser, Protel2Parser>();

                    // Регистрируем все генераторы
                    services.AddSingleton<IOutputGenerator, NetlistGenerator>();
                    services.AddSingleton<IOutputGenerator, BomGenerator>();
                    services.AddSingleton<IOutputGenerator, DotGenerator>();
                    services.AddSingleton<IOutputGenerator, MermaidGenerator>();

                    // Добавляем фоновую службу и форму настроек
                    services.AddHostedService<Worker>();
                    services.AddTransient<SettingsForm>();
                });

            _host = builder.Build();
            _host.StartAsync().GetAwaiter().GetResult();

            _trayIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(System.Environment.ProcessPath ?? AppContext.BaseDirectory),
                Visible = true,
                Text = "NetList Folder Watcher Service"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Настройки", null, ShowSettings);
            contextMenu.Items.Add("Выход", null, ExitApplication);
            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += ShowSettings;

            Application.Run();

            _host?.StopAsync().GetAwaiter().GetResult();
            _host?.Dispose();
            _trayIcon?.Dispose();
        }

        public static void ShowNotification(string title, string text, ToolTipIcon iconType = ToolTipIcon.Info)
        {
            if (_trayIcon != null && _trayIcon.Visible)
                _trayIcon.ShowBalloonTip(3000, title, text, iconType);
        }

        private static void ShowSettings(object? sender, EventArgs e)
        {
            if (_settingsForm == null || _settingsForm.IsDisposed)
                _settingsForm = _host?.Services.GetRequiredService<SettingsForm>() ?? new SettingsForm();
            _settingsForm.Show();
            _settingsForm.Activate();
        }

        private static void ExitApplication(object? sender, EventArgs e) => Application.Exit();
    }
}

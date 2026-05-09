using FolderWatcher.Service;

var builder = Host.CreateApplicationBuilder(args);

// ВОТ ЭТА СТРОКА ВАЖНА:
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "NetFilesConverterService";
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

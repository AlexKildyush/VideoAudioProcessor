using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoAudioProcessor.Infrastructure.Hosting;
using VideoAudioProcessor.Infrastructure.Theming;
using VideoAudioProcessor.View;

namespace VideoAudioProcessor;

public partial class App : Application
{
    private readonly IHost _host = AppHost.CreateHostBuilder().Build();

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var themeService = _host.Services.GetRequiredService<IThemeService>();
        themeService.ApplyTheme(themeService.CurrentTheme);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}

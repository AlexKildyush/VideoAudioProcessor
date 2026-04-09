using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VideoAudioProcessor.Infrastructure.DependencyInjection;
using VideoAudioProcessor.View;
using VideoAudioProcessor.ViewModel;

namespace VideoAudioProcessor.Infrastructure.Hosting;

public static class AppHost
{
    public static IHostBuilder CreateHostBuilder()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            })
            .ConfigureServices(services =>
            {
                services.AddVideoAudioProcessor();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            });
    }
}

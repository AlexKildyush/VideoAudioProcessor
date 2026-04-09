using Microsoft.Extensions.DependencyInjection;
using VideoAudioProcessor.Infrastructure.Configuration;
using VideoAudioProcessor.Infrastructure.Theming;
using VideoAudioProcessor.Services;

namespace VideoAudioProcessor.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVideoAudioProcessor(this IServiceCollection services)
    {
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IThemeService, ThemeService>();

        services.AddSingleton<FfmpegCommandRunner>();
        services.AddSingleton<InformationIndexStorageService>();
        services.AddSingleton<InformationSearchService>();

        services.AddTransient<MediaProbeService>();
        services.AddTransient<BatchQueueRunner>();

        return services;
    }
}

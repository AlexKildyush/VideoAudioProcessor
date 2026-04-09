using System.Configuration;

namespace VideoAudioProcessor.Infrastructure.Configuration;

public sealed class AppSettingsService : IAppSettingsService
{
    private const string RootPathKey = "RootPath";
    private const string ThemeKey = "Theme";

    public string RootPath
    {
        get => ConfigurationManager.AppSettings[RootPathKey] ?? string.Empty;
        set
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = config.AppSettings.Settings;

            if (settings[RootPathKey] == null)
            {
                settings.Add(RootPathKey, value);
            }
            else
            {
                settings[RootPathKey]!.Value = value;
            }

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }

    public string Theme
    {
        get => ConfigurationManager.AppSettings[ThemeKey] ?? "Light";
        set
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = config.AppSettings.Settings;

            if (settings[ThemeKey] == null)
            {
                settings.Add(ThemeKey, value);
            }
            else
            {
                settings[ThemeKey]!.Value = value;
            }

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }
}

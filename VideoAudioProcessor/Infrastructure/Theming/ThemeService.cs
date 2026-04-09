using System.Windows;
using VideoAudioProcessor.Infrastructure.Configuration;

namespace VideoAudioProcessor.Infrastructure.Theming;

public sealed class ThemeService(IAppSettingsService appSettings) : IThemeService
{
    private const string LightPalettePath = "/Resources/Styles/Palette.Light.xaml";
    private const string DarkPalettePath = "/Resources/Styles/Palette.Dark.xaml";

    public AppTheme CurrentTheme { get; private set; } = ParseTheme(appSettings.Theme);
    public bool IsDarkTheme => CurrentTheme == AppTheme.Dark;

    public void ToggleTheme()
    {
        ApplyTheme(IsDarkTheme ? AppTheme.Light : AppTheme.Dark);
    }

    public void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;
        appSettings.Theme = theme.ToString();

        if (Application.Current.Resources is not ResourceDictionary resources)
        {
            return;
        }

        var dictionaries = resources.MergedDictionaries;
        var existingPalette = dictionaries.FirstOrDefault(IsPaletteDictionary);
        if (existingPalette != null)
        {
            dictionaries.Remove(existingPalette);
        }

        var paletteDictionary = new ResourceDictionary
        {
            Source = new Uri(theme == AppTheme.Dark ? DarkPalettePath : LightPalettePath, UriKind.Relative)
        };
        dictionaries.Insert(0, paletteDictionary);
    }

    private static AppTheme ParseTheme(string? value)
    {
        return Enum.TryParse<AppTheme>(value, true, out var parsed) ? parsed : AppTheme.Light;
    }

    private static bool IsPaletteDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return string.Equals(source, LightPalettePath, StringComparison.OrdinalIgnoreCase)
               || string.Equals(source, DarkPalettePath, StringComparison.OrdinalIgnoreCase);
    }
}

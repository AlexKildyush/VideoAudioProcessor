namespace VideoAudioProcessor.Infrastructure.Theming;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    bool IsDarkTheme { get; }
    void ApplyTheme(AppTheme theme);
    void ToggleTheme();
}

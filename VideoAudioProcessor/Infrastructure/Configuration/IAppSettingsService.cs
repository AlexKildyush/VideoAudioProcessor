namespace VideoAudioProcessor.Infrastructure.Configuration;

public interface IAppSettingsService
{
    string RootPath { get; set; }
    string Theme { get; set; }
}

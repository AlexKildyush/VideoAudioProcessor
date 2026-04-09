using VideoAudioProcessor.Infrastructure.Configuration;
using VideoAudioProcessor.Infrastructure.Theming;
using VideoAudioProcessor.Services;
using VideoAudioProcessor.ViewModel;

namespace VideoAudioProcessor.View;

public partial class MainWindow
{
    private readonly IAppSettingsService _appSettings;
    private readonly FfmpegCommandRunner _commandRunner;
    private readonly InformationSearchService _informationSearchService;
    private readonly IThemeService _themeService;
    private readonly MainWindowViewModel _viewModel;

    private TrackStorageService CreateStorageService() => new(RootPath);

    private MediaProbeService CreateMediaProbeService() => new(_commandRunner);

    private MediaProcessingService CreateMediaProcessingService() => new(CreateStorageService(), _commandRunner);

    private BatchQueueRunner CreateBatchQueueRunner() => new(_commandRunner);

    private ProjectRenderService CreateProjectRenderService() => new(CreateStorageService(), CreateMediaProbeService(), _commandRunner);

    private ProcessingOptions BuildProcessingOptionsFromUi(string inputPath)
    {
        var useCustomCommand = !string.IsNullOrWhiteSpace(CustomCommandTextBox.Text);
        var resolutionEnabled = !string.IsNullOrWhiteSpace(OutputWidthTextBox.Text) && !string.IsNullOrWhiteSpace(OutputHeightTextBox.Text);
        var cropResizeEnabled = !string.IsNullOrWhiteSpace(CropTextBox.Text) || !string.IsNullOrWhiteSpace(ScaleTextBox.Text);

        return new ProcessingOptions
        {
            RootPath = RootPath,
            InputPath = inputPath,
            OutputFileName = FileNameTextBox.Text.Trim(),
            OutputFormat = _selectedFormat,
            UseCustomCommand = useCustomCommand,
            CustomCommandTemplate = CustomCommandTextBox.Text.Trim(),
            SubtitlePath = string.Empty,
            SubtitleMode = SubtitleMode.None,
            HardwareAccelerationMode = GetSelectedHardwareMode(),
            HardwareDecodeEnabled = HardwareDecodeCheckBox.IsChecked == true,
            LosslessCopy = LosslessCopyCheckBox.IsChecked == true,
            ExtractOpus = ExtractOpusCheckBox.IsChecked == true,
            CropResizeEnabled = cropResizeEnabled,
            CropValue = CropTextBox.Text.Trim(),
            ScaleValue = ScaleTextBox.Text.Trim(),
            AlphaChannelEnabled = AlphaChannelCheckBox.IsChecked == true,
            FpsChangeEnabled = !string.IsNullOrWhiteSpace(FpsTextBox.Text),
            FpsValue = FpsTextBox.Text.Trim(),
            Vp9Enabled = !string.IsNullOrWhiteSpace(Vp9CrfTextBox.Text),
            Vp9CrfValue = Vp9CrfTextBox.Text.Trim(),
            TwoPassEnabled = !string.IsNullOrWhiteSpace(TwoPassBitrateTextBox.Text),
            TwoPassBitrate = TwoPassBitrateTextBox.Text.Trim(),
            FastPresetEnabled = FastCheckBox.IsChecked == true,
            RemoveAudio = RemoveAudioCheckBox.IsChecked == true,
            TrimStart = TrimStartTextBox.Text.Trim(),
            TrimEnd = TrimEndTextBox.Text.Trim(),
            OutputWidth = resolutionEnabled ? ParseIntOrDefault(OutputWidthTextBox.Text, 0) : 0,
            OutputHeight = resolutionEnabled ? ParseIntOrDefault(OutputHeightTextBox.Text, 0) : 0
        };
    }
}

using VideoAudioProcessor.Services;

namespace VideoAudioProcessor;

public partial class MainWindow
{
    private readonly FfmpegCommandRunner _commandRunner = new();

    private TrackStorageService CreateStorageService() => new(RootPath);

    private MediaProbeService CreateMediaProbeService() => new(_commandRunner);

    private MediaProcessingService CreateMediaProcessingService() => new(CreateStorageService(), _commandRunner);

    private BatchQueueRunner CreateBatchQueueRunner() => new(_commandRunner);

    private ProjectRenderService CreateProjectRenderService() => new(CreateStorageService(), CreateMediaProbeService(), _commandRunner);

    private ProcessingOptions BuildProcessingOptionsFromUi(string inputPath)
    {
        return new ProcessingOptions
        {
            RootPath = RootPath,
            InputPath = inputPath,
            OutputFileName = FileNameTextBox.Text.Trim(),
            OutputFormat = _selectedFormat,
            SubtitlePath = SubtitlePathTextBox.Text.Trim(),
            SubtitleMode = GetSelectedSubtitleMode(),
            HardwareAccelerationMode = GetSelectedHardwareMode(),
            HardwareDecodeEnabled = HardwareDecodeCheckBox.IsChecked == true,
            LosslessCopy = LosslessCopyCheckBox.IsChecked == true,
            ExtractOpus = ExtractOpusCheckBox.IsChecked == true,
            CropResizeEnabled = CropResizeCheckBox.IsChecked == true,
            CropValue = CropTextBox.Text.Trim(),
            ScaleValue = ScaleTextBox.Text.Trim(),
            AlphaChannelEnabled = AlphaChannelCheckBox.IsChecked == true,
            FpsChangeEnabled = FpsChangeCheckBox.IsChecked == true,
            FpsValue = FpsTextBox.Text.Trim(),
            Vp9Enabled = Vp9CheckBox.IsChecked == true,
            Vp9CrfValue = Vp9CrfTextBox.Text.Trim(),
            TwoPassEnabled = TwoPassCheckBox.IsChecked == true,
            TwoPassBitrate = TwoPassBitrateTextBox.Text.Trim(),
            FastPresetEnabled = FastCheckBox.IsChecked == true,
            RemoveAudio = RemoveAudioCheckBox.IsChecked == true,
            TrimStart = TrimStartTextBox.Text.Trim(),
            TrimEnd = TrimEndTextBox.Text.Trim(),
            OutputWidth = ParseIntOrDefault(OutputWidthTextBox.Text, 1920),
            OutputHeight = ParseIntOrDefault(OutputHeightTextBox.Text, 1080)
        };
    }
}

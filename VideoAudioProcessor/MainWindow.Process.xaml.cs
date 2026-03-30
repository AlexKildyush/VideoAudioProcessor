using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoAudioProcessor.Services;

namespace VideoAudioProcessor;

public partial class MainWindow : Window
{
    private DispatcherTimer? _previewTimer;
    private bool _isUpdatingPreviewProgress;
    private string _selectedFormat = "mp4";

    private void InitializePreviewTimer()
    {
        _previewTimer = new DispatcherTimer();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(200);
        _previewTimer.Tick += PreviewTimer_Tick;
    }

    private void InitializeProcessingOptions()
    {
        PresetComboBox.ItemsSource = ProcessingPresets.BuiltIn;
        PresetComboBox.SelectedIndex = 0;

        HardwareAccelerationComboBox.ItemsSource = new[]
        {
            new ComboBoxItem { Content = "Без ускорения", Tag = HardwareAccelerationMode.None },
            new ComboBoxItem { Content = "Auto", Tag = HardwareAccelerationMode.Auto },
            new ComboBoxItem { Content = "NVIDIA NVENC", Tag = HardwareAccelerationMode.NvidiaNvenc },
            new ComboBoxItem { Content = "Intel QSV", Tag = HardwareAccelerationMode.IntelQsv },
            new ComboBoxItem { Content = "AMD AMF", Tag = HardwareAccelerationMode.AmdAmf }
        };
        HardwareAccelerationComboBox.SelectedIndex = 0;

        SubtitleModeComboBox.ItemsSource = new[]
        {
            new ComboBoxItem { Content = "Без субтитров", Tag = SubtitleMode.None },
            new ComboBoxItem { Content = "Вшить в видео", Tag = SubtitleMode.BurnIn },
            new ComboBoxItem { Content = "Вложить как дорожку", Tag = SubtitleMode.Embed }
        };
        SubtitleModeComboBox.SelectedIndex = 0;

        Mp4RadioButton.IsChecked = true;
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (PreviewMediaPlayer.NaturalDuration.HasTimeSpan)
        {
            _isUpdatingPreviewProgress = true;
            PreviewSlider.Value = PreviewMediaPlayer.Position.TotalSeconds /
                                  PreviewMediaPlayer.NaturalDuration.TimeSpan.TotalSeconds * 100;
            _isUpdatingPreviewProgress = false;
            PreviewCurrentTime.Text = PreviewMediaPlayer.Position.ToString(@"hh\:mm\:ss");
        }
    }

    private void PreviewPlay_Click(object sender, RoutedEventArgs e)
    {
        PreviewMediaPlayer.Play();
        _previewTimer?.Start();
    }

    private void PreviewPause_Click(object sender, RoutedEventArgs e)
    {
        PreviewMediaPlayer.Pause();
        _previewTimer?.Stop();
    }

    private void PreviewStop_Click(object sender, RoutedEventArgs e)
    {
        PreviewMediaPlayer.Stop();
        _previewTimer?.Stop();
        PreviewSlider.Value = 0;
        PreviewCurrentTime.Text = "00:00";
    }

    private void PreviewMediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (PreviewMediaPlayer.NaturalDuration.HasTimeSpan)
        {
            PreviewTotalTime.Text = PreviewMediaPlayer.NaturalDuration.TimeSpan.ToString(@"hh\:mm\:ss");
        }
    }

    private void PreviewMediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        PreviewMediaPlayer.Stop();
        _previewTimer?.Stop();
    }

    private void PreviewSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingPreviewProgress || !PreviewMediaPlayer.NaturalDuration.HasTimeSpan) return;

        var newPosition = TimeSpan.FromSeconds(PreviewSlider.Value / 100 *
            PreviewMediaPlayer.NaturalDuration.TimeSpan.TotalSeconds);
        PreviewMediaPlayer.Position = newPosition;
        PreviewCurrentTime.Text = newPosition.ToString(@"hh\:mm\:ss");
    }

    private void OnOutputFormatChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            _selectedFormat = tag;
        }
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetComboBox.SelectedItem is not ProcessingPreset preset)
        {
            return;
        }

        PresetDescriptionText.Text = preset.Description;
        OutputWidthTextBox.Text = (preset.Width ?? 1920).ToString(CultureInfo.InvariantCulture);
        OutputHeightTextBox.Text = (preset.Height ?? 1080).ToString(CultureInfo.InvariantCulture);

        if (preset.Fps.HasValue)
        {
            FpsTextBox.Text = preset.Fps.Value.ToString(CultureInfo.InvariantCulture);
            FpsChangeCheckBox.IsChecked = true;
        }

        SetOutputFormatRadio(preset.OutputFormat);
    }

    private void SetOutputFormatRadio(string format)
    {
        _selectedFormat = format;
        Mp4RadioButton.IsChecked = format == "mp4";
        AviRadioButton.IsChecked = format == "avi";
        MkvRadioButton.IsChecked = format == "mkv";
    }

    private void BrowseSubtitle_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Субтитры|*.srt;*.ass;*.ssa;*.vtt|Все файлы|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            SubtitlePathTextBox.Text = dialog.FileName;
        }
    }

    private async void ExecuteProcessing_Click(object sender, RoutedEventArgs e)
    {
        ProcessingRequest request;
        try
        {
            request = BuildProcessingRequest() ?? throw new InvalidOperationException("Не удалось создать задачу обработки.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            var processingService = CreateMediaProcessingService();
            await RunWithWaitDialogAsync("Обработка", "Выполняется ffmpeg...", async () =>
            {
                await processingService.ExecuteProcessingAsync(request);
            });

            RefreshProcessedList();
            MessageBox.Show("Файл успешно обработан!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка ffmpeg: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddProcessingToQueue_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var request = BuildProcessingRequest();
            if (request == null)
            {
                return;
            }

            AddProcessingJob(request, $"Обработка {Path.GetFileNameWithoutExtension(request.OutputPath)}");
            MessageBox.Show("Задача добавлена в очередь.", "Очередь", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private ProcessingRequest? BuildProcessingRequest()
    {
        if (!TryGetPreviewInputPath(out var inputPath))
        {
            return null;
        }

        return CreateMediaProcessingService().BuildProcessingRequest(BuildProcessingOptionsFromUi(inputPath));
    }

    private string BuildStandardArguments(string inputPath, string outputPath, string subtitlePath, SubtitleMode subtitleMode, bool lossless, bool extractOpus)
    {
        var options = new ProcessingOptions
        {
            RootPath = RootPath,
            InputPath = inputPath,
            OutputFileName = FileNameTextBox.Text.Trim(),
            OutputFormat = _selectedFormat,
            SubtitlePath = subtitlePath,
            SubtitleMode = subtitleMode,
            HardwareAccelerationMode = GetSelectedHardwareMode(),
            HardwareDecodeEnabled = HardwareDecodeCheckBox.IsChecked == true,
            LosslessCopy = lossless,
            ExtractOpus = extractOpus,
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

        return CreateMediaProcessingService().BuildStandardArguments(inputPath, outputPath, options);
    }

    private SubtitleMode GetSelectedSubtitleMode()
    {
        return SubtitleModeComboBox.SelectedItem is ComboBoxItem { Tag: SubtitleMode mode }
            ? mode
            : SubtitleMode.None;
    }

    private HardwareAccelerationMode GetSelectedHardwareMode()
    {
        return HardwareAccelerationComboBox.SelectedItem is ComboBoxItem { Tag: HardwareAccelerationMode mode }
            ? mode
            : HardwareAccelerationMode.None;
    }

    private async void ExecuteCustomCommand_Click(object sender, RoutedEventArgs e)
    {
        var fileName = FileNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(fileName))
        {
            MessageBox.Show("Введите название файла.");
            return;
        }

        if (string.IsNullOrWhiteSpace(RootPath))
        {
            MessageBox.Show("Пожалуйста, сначала установите корневую папку.");
            return;
        }

        if (!TryGetPreviewInputPath(out var inputPath))
        {
            return;
        }

        var outputPath = Path.Combine(ProcessedPath, $"{fileName}.{_selectedFormat}");
        if (File.Exists(outputPath))
        {
            MessageBox.Show("Файл с таким названием уже существует.");
            return;
        }

        try
        {
            var processingService = CreateMediaProcessingService();
            var command = processingService.BuildCustomCommand(CustomCommandTextBox.Text, inputPath, outputPath);
            await processingService.ExecuteCustomCommandAsync(command);
            RefreshProcessedList();
            MessageBox.Show("Команда выполнена успешно!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка выполнения команды: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool TryGetPreviewInputPath(out string inputPath)
    {
        inputPath = string.Empty;
        if (PreviewMediaPlayer.Source == null)
        {
            MessageBox.Show("Сначала выберите файл для обработки.");
            return false;
        }

        inputPath = PreviewMediaPlayer.Source.LocalPath;
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            MessageBox.Show("Не удалось определить путь к файлу.");
            return false;
        }

        return true;
    }

    private ProcessingRequest? BuildLosslessMergeRequest(IReadOnlyList<string> inputPaths, string outputPath)
    {
        return CreateMediaProcessingService().BuildLosslessMergeRequest(inputPaths, outputPath);
    }

    private async Task<(int ExitCode, string ErrorOutput)> RunFfmpegAsync(string arguments)
    {
        return await _commandRunner.RunFfmpegAsync(arguments);
    }
}

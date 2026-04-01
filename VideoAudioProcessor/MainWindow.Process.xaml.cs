using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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

        Mp4RadioButton.IsChecked = true;
        ClearOptionalProcessingFields();
        UpdateProcessingFunctionState();
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
        OutputWidthTextBox.Text = preset.Width?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        OutputHeightTextBox.Text = preset.Height?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        FpsTextBox.Text = preset.Fps?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        SetOutputFormatRadio(preset.OutputFormat);
        UpdateProcessingFunctionState();
    }

    private void SetOutputFormatRadio(string format)
    {
        _selectedFormat = format;
        Mp4RadioButton.IsChecked = format == "mp4";
        AviRadioButton.IsChecked = format == "avi";
        MkvRadioButton.IsChecked = format == "mkv";
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

        ValidateProcessingUiState();
        return CreateMediaProcessingService().BuildProcessingRequest(BuildProcessingOptionsFromUi(inputPath));
    }

    private HardwareAccelerationMode GetSelectedHardwareMode()
    {
        return HardwareAccelerationComboBox.SelectedItem is ComboBoxItem { Tag: HardwareAccelerationMode mode }
            ? mode
            : HardwareAccelerationMode.None;
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

    private void ProcessingOptionInput_Changed(object sender, TextChangedEventArgs e)
    {
        UpdateProcessingFunctionState();
    }

    private void ProcessingOptionToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateProcessingFunctionState();
    }

    private void ProcessingOptionSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateProcessingFunctionState();
    }

    private void ResetResolution_Click(object sender, RoutedEventArgs e)
    {
        OutputWidthTextBox.Clear();
        OutputHeightTextBox.Clear();
        UpdateProcessingFunctionState();
    }

    private void ResetTrim_Click(object sender, RoutedEventArgs e)
    {
        TrimStartTextBox.Clear();
        TrimEndTextBox.Clear();
        UpdateProcessingFunctionState();
    }

    private void ResetCropScale_Click(object sender, RoutedEventArgs e)
    {
        CropTextBox.Clear();
        ScaleTextBox.Clear();
        UpdateProcessingFunctionState();
    }

    private void ResetVp9_Click(object sender, RoutedEventArgs e)
    {
        Vp9CrfTextBox.Clear();
        UpdateProcessingFunctionState();
    }

    private void ResetTwoPass_Click(object sender, RoutedEventArgs e)
    {
        TwoPassBitrateTextBox.Clear();
        UpdateProcessingFunctionState();
    }

    private void ResetFps_Click(object sender, RoutedEventArgs e)
    {
        FpsTextBox.Clear();
        UpdateProcessingFunctionState();
    }

    private void ResetCustomCommand_Click(object sender, RoutedEventArgs e)
    {
        CustomCommandTextBox.Clear();
        UpdateProcessingFunctionState();
    }

    private void UpdateProcessingFunctionState()
    {
        var hasCustomCommand = HasText(CustomCommandTextBox);
        var resolutionActive = HasText(OutputWidthTextBox) || HasText(OutputHeightTextBox);
        var trimActive = HasText(TrimStartTextBox) || HasText(TrimEndTextBox);
        var cropScaleActive = HasText(CropTextBox) || HasText(ScaleTextBox);
        var vp9Active = HasText(Vp9CrfTextBox);
        var twoPassActive = HasText(TwoPassBitrateTextBox);
        var fpsActive = HasText(FpsTextBox);
        var hardwareActive = GetSelectedHardwareMode() != HardwareAccelerationMode.None || HardwareDecodeCheckBox.IsChecked == true;
        var standardActive = resolutionActive || trimActive || cropScaleActive || vp9Active || twoPassActive || fpsActive ||
                             LosslessCopyCheckBox.IsChecked == true || FastCheckBox.IsChecked == true ||
                             ExtractOpusCheckBox.IsChecked == true || AlphaChannelCheckBox.IsChecked == true ||
                             RemoveAudioCheckBox.IsChecked == true || hardwareActive;
        var losslessActive = LosslessCopyCheckBox.IsChecked == true;

        ResolutionResetButton.Visibility = resolutionActive ? Visibility.Visible : Visibility.Collapsed;
        TrimResetButton.Visibility = trimActive ? Visibility.Visible : Visibility.Collapsed;
        CropScaleResetButton.Visibility = cropScaleActive ? Visibility.Visible : Visibility.Collapsed;
        Vp9ResetButton.Visibility = vp9Active ? Visibility.Visible : Visibility.Collapsed;
        TwoPassResetButton.Visibility = twoPassActive ? Visibility.Visible : Visibility.Collapsed;
        FpsResetButton.Visibility = fpsActive ? Visibility.Visible : Visibility.Collapsed;
        CustomCommandResetButton.Visibility = hasCustomCommand ? Visibility.Visible : Visibility.Collapsed;

        var disableStandardInputs = hasCustomCommand;
        OutputWidthTextBox.IsEnabled = !disableStandardInputs;
        OutputHeightTextBox.IsEnabled = !disableStandardInputs;
        TrimStartTextBox.IsEnabled = !disableStandardInputs;
        TrimEndTextBox.IsEnabled = !disableStandardInputs;
        LosslessCopyCheckBox.IsEnabled = !disableStandardInputs;
        HardwareAccelerationComboBox.IsEnabled = !disableStandardInputs;
        HardwareDecodeCheckBox.IsEnabled = !disableStandardInputs;
        CropTextBox.IsEnabled = !disableStandardInputs;
        ScaleTextBox.IsEnabled = !disableStandardInputs;
        Vp9CrfTextBox.IsEnabled = !disableStandardInputs;
        TwoPassBitrateTextBox.IsEnabled = !disableStandardInputs;
        FastCheckBox.IsEnabled = !disableStandardInputs;
        ExtractOpusCheckBox.IsEnabled = !disableStandardInputs;
        AlphaChannelCheckBox.IsEnabled = !disableStandardInputs;
        FpsTextBox.IsEnabled = !disableStandardInputs;
        RemoveAudioCheckBox.IsEnabled = !disableStandardInputs;

        CustomCommandTextBox.IsEnabled = !standardActive || hasCustomCommand;

        ResolutionStatusText.Text = hasCustomCommand
            ? "Недоступно с ручной командой"
            : resolutionActive && !(HasText(OutputWidthTextBox) && HasText(OutputHeightTextBox))
                ? "Нужно указать и ширину, и высоту"
                : losslessActive && resolutionActive
                    ? "Недоступно с Lossless cut"
                    : string.Empty;

        TrimStatusText.Text = hasCustomCommand
            ? "Недоступно с ручной командой"
            : string.Empty;

        LosslessStatusText.Text = hasCustomCommand
            ? "Недоступно с ручной командой"
            : string.Empty;

        CropScaleStatusText.Text = hasCustomCommand
            ? "Недоступно с ручной командой"
            : cropScaleActive && !(HasText(CropTextBox) && HasText(ScaleTextBox))
                ? "Нужно указать и crop, и scale"
                : losslessActive && cropScaleActive
                    ? "Недоступно с Lossless cut"
                    : string.Empty;

        Vp9StatusText.Text = hasCustomCommand
            ? "Недоступно с ручной командой"
            : losslessActive && vp9Active
                ? "Недоступно с Lossless cut"
                : string.Empty;

        TwoPassStatusText.Text = hasCustomCommand
            ? "Недоступно с ручной командой"
            : losslessActive && twoPassActive
                ? "Недоступно с Lossless cut"
                : string.Empty;

        FpsStatusText.Text = hasCustomCommand
            ? "Недоступно с ручной командой"
            : losslessActive && fpsActive
                ? "Недоступно с Lossless cut"
                : string.Empty;

        CustomCommandStatusText.Text = hasCustomCommand && standardActive
            ? "Сбросьте другие функции, чтобы использовать ручную команду"
            : !hasCustomCommand && standardActive
                ? "Ручная команда недоступна, пока заданы другие функции"
                : string.Empty;
    }

    private void ValidateProcessingUiState()
    {
        var hasCustomCommand = HasText(CustomCommandTextBox);
        var resolutionActive = HasText(OutputWidthTextBox) || HasText(OutputHeightTextBox);
        var trimActive = HasText(TrimStartTextBox) || HasText(TrimEndTextBox);
        var cropScaleActive = HasText(CropTextBox) || HasText(ScaleTextBox);
        var vp9Active = HasText(Vp9CrfTextBox);
        var twoPassActive = HasText(TwoPassBitrateTextBox);
        var fpsActive = HasText(FpsTextBox);
        var hardwareActive = GetSelectedHardwareMode() != HardwareAccelerationMode.None || HardwareDecodeCheckBox.IsChecked == true;
        var standardActive = resolutionActive || trimActive || cropScaleActive || vp9Active || twoPassActive || fpsActive ||
                             LosslessCopyCheckBox.IsChecked == true || FastCheckBox.IsChecked == true ||
                             ExtractOpusCheckBox.IsChecked == true || AlphaChannelCheckBox.IsChecked == true ||
                             RemoveAudioCheckBox.IsChecked == true || hardwareActive;

        if (string.IsNullOrWhiteSpace(FileNameTextBox.Text))
        {
            throw new InvalidOperationException("Введите название файла.");
        }

        if (resolutionActive && !(HasText(OutputWidthTextBox) && HasText(OutputHeightTextBox)))
        {
            throw new InvalidOperationException("Для разрешения нужно указать и ширину, и высоту.");
        }

        if (cropScaleActive && !(HasText(CropTextBox) && HasText(ScaleTextBox)))
        {
            throw new InvalidOperationException("Для функции обрезки и масштабирования нужно указать и crop, и scale.");
        }

        if (hasCustomCommand && standardActive)
        {
            throw new InvalidOperationException("Ручную команду FFmpeg нельзя использовать вместе с другими функциями обработки.");
        }

        if (LosslessCopyCheckBox.IsChecked == true)
        {
            if (resolutionActive || cropScaleActive || vp9Active || twoPassActive || fpsActive || FastCheckBox.IsChecked == true ||
                ExtractOpusCheckBox.IsChecked == true || AlphaChannelCheckBox.IsChecked == true || RemoveAudioCheckBox.IsChecked == true)
            {
                throw new InvalidOperationException("Lossless cut нельзя использовать вместе с кодированием, фильтрами, изменением FPS или аудио-режимами.");
            }
        }

        if (ExtractOpusCheckBox.IsChecked == true && RemoveAudioCheckBox.IsChecked == true)
        {
            throw new InvalidOperationException("Нельзя одновременно извлекать аудио в Opus и удалять аудио.");
        }
    }

    private void ClearOptionalProcessingFields()
    {
        OutputWidthTextBox.Text = string.Empty;
        OutputHeightTextBox.Text = string.Empty;
        TrimStartTextBox.Text = string.Empty;
        TrimEndTextBox.Text = string.Empty;
        CropTextBox.Text = string.Empty;
        ScaleTextBox.Text = string.Empty;
        Vp9CrfTextBox.Text = string.Empty;
        TwoPassBitrateTextBox.Text = string.Empty;
        FpsTextBox.Text = string.Empty;
        CustomCommandTextBox.Text = string.Empty;
        LosslessCopyCheckBox.IsChecked = false;
        HardwareAccelerationComboBox.SelectedIndex = 0;
        HardwareDecodeCheckBox.IsChecked = false;
        FastCheckBox.IsChecked = false;
        ExtractOpusCheckBox.IsChecked = false;
        AlphaChannelCheckBox.IsChecked = false;
        RemoveAudioCheckBox.IsChecked = false;
        PresetComboBox.SelectedIndex = 0;
    }

    private static bool HasText(TextBox textBox)
    {
        return !string.IsNullOrWhiteSpace(textBox.Text);
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

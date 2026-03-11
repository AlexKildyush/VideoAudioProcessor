using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

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
        _previewTimer.Start();
    }

    private void PreviewPause_Click(object sender, RoutedEventArgs e)
    {
        PreviewMediaPlayer.Pause();
        _previewTimer.Stop();
    }

    private void PreviewStop_Click(object sender, RoutedEventArgs e)
    {
        PreviewMediaPlayer.Stop();
        _previewTimer.Stop();
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
        _previewTimer.Stop();
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
        var request = BuildProcessingRequest();
        if (request == null)
        {
            return;
        }

        try
        {
            await RunWithWaitDialogAsync("Обработка", "Выполняется ffmpeg...", async () =>
            {
                var (exitCode, errorOutput) = await RunFfmpegAsync(request.Arguments);
                if (exitCode != 0)
                {
                    throw new InvalidOperationException(errorOutput);
                }
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
        var request = BuildProcessingRequest();
        if (request == null)
        {
            return;
        }

        AddProcessingJob(request, $"Обработка {Path.GetFileNameWithoutExtension(request.OutputPath)}");
        MessageBox.Show("Задача добавлена в очередь.", "Очередь", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private ProcessingRequest? BuildProcessingRequest()
    {
        var fileName = FileNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            MessageBox.Show("Введите название файла.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(RootPath))
        {
            MessageBox.Show("Пожалуйста, сначала установите корневую папку.");
            return null;
        }

        if (!TryGetPreviewInputPath(out var inputPath))
        {
            return null;
        }

        if (!File.Exists(inputPath))
        {
            MessageBox.Show("Файл для обработки не найден.");
            return null;
        }

        Directory.CreateDirectory(ProcessedPath);
        var outputPath = Path.Combine(ProcessedPath, $"{fileName}.{_selectedFormat}");
        if (File.Exists(outputPath))
        {
            MessageBox.Show("Файл с таким названием уже существует.");
            return null;
        }

        var subtitleMode = GetSelectedSubtitleMode();
        var subtitlePath = SubtitlePathTextBox.Text.Trim();
        if (subtitleMode != SubtitleMode.None && string.IsNullOrWhiteSpace(subtitlePath))
        {
            MessageBox.Show("Выберите файл субтитров.");
            return null;
        }

        if (subtitleMode != SubtitleMode.None && !File.Exists(subtitlePath))
        {
            MessageBox.Show("Файл субтитров не найден.");
            return null;
        }

        var lossless = LosslessCopyCheckBox.IsChecked == true;
        var extractOpus = ExtractOpusCheckBox.IsChecked == true;
        if (lossless && (CropResizeCheckBox.IsChecked == true || FpsChangeCheckBox.IsChecked == true || Vp9CheckBox.IsChecked == true || subtitleMode != SubtitleMode.None))
        {
            MessageBox.Show("Lossless режим нельзя сочетать с фильтрами, VP9 или субтитрами.");
            return null;
        }

        if (extractOpus && subtitleMode != SubtitleMode.None)
        {
            MessageBox.Show("Субтитры недоступны для аудио-only режима.");
            return null;
        }

        if (subtitleMode == SubtitleMode.Embed && _selectedFormat == "avi")
        {
            MessageBox.Show("Вложенные субтитры для AVI не поддерживаются.");
            return null;
        }

        var arguments = BuildStandardArguments(inputPath, outputPath, subtitlePath, subtitleMode, lossless, extractOpus);
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        return new ProcessingRequest
        {
            InputPaths = [inputPath],
            OutputPath = outputPath,
            Arguments = arguments,
            Summary = $"{Path.GetFileName(inputPath)} -> {Path.GetFileName(outputPath)}"
        };
    }

    private string BuildStandardArguments(string inputPath, string outputPath, string subtitlePath, SubtitleMode subtitleMode, bool lossless, bool extractOpus)
    {
        var start = TrimStartTextBox.Text.Trim();
        var end = TrimEndTextBox.Text.Trim();
        var builder = new StringBuilder("-y");

        AppendHardwareDecodeArgs(builder);

        if (lossless)
        {
            if (!string.IsNullOrWhiteSpace(start))
            {
                builder.Append($" -ss {start}");
            }

            if (!string.IsNullOrWhiteSpace(end))
            {
                builder.Append($" -to {end}");
            }

            builder.Append($" -i \"{inputPath}\" -c copy \"{outputPath}\"");
            return builder.ToString();
        }

        builder.Append($" -i \"{inputPath}\"");

        string videoCodec;
        string audioCodec;
        switch (_selectedFormat)
        {
            case "avi":
                videoCodec = "mpeg4";
                audioCodec = "libmp3lame";
                break;
            case "mkv":
                videoCodec = "libx264";
                audioCodec = "aac";
                break;
            default:
                videoCodec = "libx264";
                audioCodec = "aac";
                break;
        }

        var videoFilters = new List<string>();
        var outputWidth = ParseIntOrDefault(OutputWidthTextBox.Text, 1920);
        var outputHeight = ParseIntOrDefault(OutputHeightTextBox.Text, 1080);

        if (!string.IsNullOrWhiteSpace(start))
        {
            builder.Append($" -ss {start}");
        }

        if (!string.IsNullOrWhiteSpace(end))
        {
            builder.Append($" -to {end}");
        }

        if (Vp9CheckBox.IsChecked == true)
        {
            videoCodec = "libvpx-vp9";
        }
        else
        {
            videoCodec = GetVideoCodecForHardware(videoCodec);
        }

        if (TwoPassCheckBox.IsChecked == true)
        {
            builder.Append($" -b:v {TwoPassBitrateTextBox.Text.Trim()}");
        }

        if (FastCheckBox.IsChecked == true)
        {
            builder.Append(" -preset ultrafast");
        }

        if (CropResizeCheckBox.IsChecked == true)
        {
            videoFilters.Add($"crop={CropTextBox.Text.Trim()}");
            videoFilters.Add($"scale={ScaleTextBox.Text.Trim()}");
        }
        else if (outputWidth > 0 && outputHeight > 0)
        {
            videoFilters.Add($"scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=decrease");
            videoFilters.Add($"pad={outputWidth}:{outputHeight}:(ow-iw)/2:(oh-ih)/2");
        }

        if (AlphaChannelCheckBox.IsChecked == true)
        {
            videoFilters.Add("colorkey=0x000000:0.1:0.1");
            videoFilters.Add("format=yuva420p");
        }

        if (FpsChangeCheckBox.IsChecked == true)
        {
            videoFilters.Add($"fps={FpsTextBox.Text.Trim()}");
        }

        if (subtitleMode == SubtitleMode.BurnIn)
        {
            videoFilters.Add($"subtitles='{EscapeFilterPath(subtitlePath)}'");
        }

        if (ExtractOpusCheckBox.IsChecked == true)
        {
            audioCodec = "libopus";
            builder.Append(" -vn");
        }
        else if (videoFilters.Count > 0)
        {
            builder.Append($" -vf \"{string.Join(',', videoFilters)}\"");
        }

        if (RemoveAudioCheckBox.IsChecked == true)
        {
            builder.Append(" -an");
        }
        else
        {
            builder.Append($" -c:a {audioCodec}");
        }

        if (!extractOpus)
        {
            builder.Append($" -c:v {videoCodec}");
            if (Vp9CheckBox.IsChecked == true)
            {
                builder.Append($" -crf {Vp9CrfTextBox.Text.Trim()} -b:v 0");
            }
        }

        if (subtitleMode == SubtitleMode.Embed)
        {
            builder.Append($" -i \"{subtitlePath}\" -c:s {GetSubtitleCodec()} -map 0:v? -map 0:a? -map 1:0");
        }

        builder.Append($" \"{outputPath}\"");
        return builder.ToString();
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

    private void AppendHardwareDecodeArgs(StringBuilder builder)
    {
        if (HardwareDecodeCheckBox.IsChecked != true)
        {
            return;
        }

        switch (GetSelectedHardwareMode())
        {
            case HardwareAccelerationMode.Auto:
                builder.Append(" -hwaccel auto");
                break;
            case HardwareAccelerationMode.NvidiaNvenc:
                builder.Append(" -hwaccel cuda");
                break;
            case HardwareAccelerationMode.IntelQsv:
                builder.Append(" -hwaccel qsv");
                break;
            case HardwareAccelerationMode.AmdAmf:
                builder.Append(" -hwaccel d3d11va");
                break;
        }
    }

    private string GetVideoCodecForHardware(string fallbackCodec)
    {
        if (_selectedFormat == "avi")
        {
            return fallbackCodec;
        }

        return GetSelectedHardwareMode() switch
        {
            HardwareAccelerationMode.NvidiaNvenc => "h264_nvenc",
            HardwareAccelerationMode.IntelQsv => "h264_qsv",
            HardwareAccelerationMode.AmdAmf => "h264_amf",
            _ => fallbackCodec
        };
    }

    private string GetSubtitleCodec()
    {
        return _selectedFormat == "mp4" ? "mov_text" : "srt";
    }

    private static string EscapeFilterPath(string path)
    {
        return path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
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
            var command = CustomCommandTextBox.Text
                .Replace("{input}", inputPath)
                .Replace("{output}", outputPath);

            var (exitCode, errorOutput) = await RunFfmpegAsync(command);
            if (exitCode == 0)
            {
                RefreshProcessedList();
                MessageBox.Show("Команда выполнена успешно!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Ошибка выполнения команды:\n{errorOutput}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        if (inputPaths.Count < 2)
        {
            return null;
        }

        var concatListPath = Path.Combine(Path.GetTempPath(), $"vap_merge_{Guid.NewGuid():N}.txt");
        var listContent = string.Join(Environment.NewLine, inputPaths.Select(path => $"file '{path.Replace("'", "''")}'"));
        File.WriteAllText(concatListPath, listContent);

        return new ProcessingRequest
        {
            InputPaths = inputPaths,
            OutputPath = outputPath,
            Arguments = $"-y -f concat -safe 0 -i \"{concatListPath}\" -c copy \"{outputPath}\"",
            Summary = $"Merge {inputPaths.Count} файлов",
            IsMerge = true
        };
    }

    private static async Task<(int ExitCode, string ErrorOutput)> RunFfmpegAsync(string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        var errorOutput = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, errorOutput);
    }
}


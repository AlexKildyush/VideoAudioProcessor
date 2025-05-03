using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VideoAudioProcessor;

public partial class MainWindow : Window
{
    private DispatcherTimer _previewTimer;
    private bool _isUpdatingPreviewProgress;

    private void InitializePreviewTimer()
    {
        _previewTimer = new DispatcherTimer();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(200);
        _previewTimer.Tick += PreviewTimer_Tick;
    }

    private void PreviewTimer_Tick(object sender, EventArgs e)
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
    
    private string _selectedFormat = "mp4"; // значение по умолчанию

    private void OnOutputFormatChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            _selectedFormat = tag;
        }
    }

    private async void ExecuteProcessing_Click(object sender, RoutedEventArgs e)
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

        var start = TrimStartTextBox.Text;
        var end = TrimEndTextBox.Text;
        if (!TryGetPreviewInputPath(out var inputPath))
        {
            return;
        }

        if (!File.Exists(inputPath))
        {
            MessageBox.Show("Файл для обработки не найден.");
            return;
        }

        var processedPath = Path.Combine(RootPath, "TrackManager", "Processed");
        Directory.CreateDirectory(processedPath);
        var outputPath = Path.Combine(processedPath, $"{fileName}.{_selectedFormat}");

        if (File.Exists(outputPath))
        {
            MessageBox.Show("Файл с таким названием уже существует.");
            return;
        }

        try
        {
            // Базовые параметры
            var args = new StringBuilder($"-y -i \"{inputPath}\"");
            if (!string.IsNullOrWhiteSpace(start))
            {
                args.Append($" -ss {start}");
            }

            if (!string.IsNullOrWhiteSpace(end))
            {
                args.Append($" -to {end}");
            }

            // Выбор кодеков в зависимости от формата
            string videoCodec;
            string audioCodec;

            switch (_selectedFormat)
            {
                case "mp4":
                    videoCodec = "libx264";
                    audioCodec = "aac";
                    break;
                case "avi":
                    videoCodec = "mpeg4";
                    audioCodec = "libmp3lame";
                    break;
                case "mkv":
                    videoCodec = "libx264";
                    audioCodec = "aac";
                    break;
                default:
                    throw new InvalidOperationException("Неизвестный формат.");
            }

            var videoFilters = new List<string>();

            var videoCodecArgs = string.Empty;

            // Обработка VP9
            if (Vp9CheckBox.IsChecked == true)
            {
                videoCodec = "libvpx-vp9";
                videoCodecArgs = $"-c:v {videoCodec} -crf {Vp9CrfTextBox.Text} -b:v 0";
            }
            else
            {
                videoCodecArgs = $"-c:v {videoCodec}";
            }

            // 2-pass кодирование
            var isTwoPass = TwoPassCheckBox.IsChecked == true;
            if (isTwoPass)
            {
                args.Append($" -b:v {TwoPassBitrateTextBox.Text}");
            }

            // Ускоренное кодирование
            if (FastCheckBox.IsChecked == true)
            {
                args.Append(" -preset ultrafast");
            }

            // Обрезка и изменение разрешения
            if (CropResizeCheckBox.IsChecked == true)
            {
                videoFilters.Add($"crop={CropTextBox.Text}");
                videoFilters.Add($"scale={ScaleTextBox.Text}");
            }

            // Извлечение аудио в Opus
            if (ExtractOpusCheckBox.IsChecked == true)
            {
                audioCodec = "libopus";
            }

            // Альфа-канал
            if (AlphaChannelCheckBox.IsChecked == true)
            {
                videoFilters.Add("colorkey=0x000000:0.1:0.1");
                videoFilters.Add("format=yuva420p");
            }

            // Изменение FPS
            if (FpsChangeCheckBox.IsChecked == true)
            {
                args.Append($" -r {FpsTextBox.Text}");
            }

            var removeAudio = RemoveAudioCheckBox.IsChecked == true;
            var extractOpus = ExtractOpusCheckBox.IsChecked == true && !removeAudio;

            if (extractOpus)
            {
                args.Append(" -vn");
            }

            if (!extractOpus && videoFilters.Count > 0)
            {
                args.Append($" -vf \"{string.Join(",", videoFilters)}\"");
            }

            // Удаление аудио
            if (removeAudio)
            {
                args.Append(" -an");
            }
            else if (extractOpus)
            {
                args.Append($" -c:a {audioCodec}");
            }
            else
            {
                args.Append($" -c:a {audioCodec}");
            }

            var ffmpegArgs = args.ToString();

            if (!extractOpus)
            {
                ffmpegArgs += $" {videoCodecArgs}";
            }

            if (isTwoPass)
            {
                var pass1Args = $"{ffmpegArgs} -pass 1 -f null NUL";
                var (pass1ExitCode, pass1Error) = await RunFfmpegAsync(pass1Args);
                if (pass1ExitCode != 0)
                {
                    MessageBox.Show($"Ошибка ffmpeg (pass 1):\n{pass1Error}", "Ошибка", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                var pass2Args = $"{ffmpegArgs} -pass 2 \"{outputPath}\"";
                var (pass2ExitCode, pass2Error) = await RunFfmpegAsync(pass2Args);
                if (pass2ExitCode == 0)
                {
                    MessageBox.Show("Файл успешно обработан!", "Успех", MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Ошибка ffmpeg (pass 2):\n{pass2Error}", "Ошибка", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                return;
            }

            ffmpegArgs += $" \"{outputPath}\"";
            var (exitCode, errorOutput) = await RunFfmpegAsync(ffmpegArgs);
            if (exitCode == 0)
            {
                MessageBox.Show("Файл успешно обработан!", "Успех", MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Ошибка ffmpeg:\n{errorOutput}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка обработки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        var processedPath = Path.Combine(RootPath, "TrackManager", "Processed");
        Directory.CreateDirectory(processedPath);
        var outputPath = Path.Combine(processedPath, $"{fileName}.{_selectedFormat}");

        if (File.Exists(outputPath))
        {
            MessageBox.Show("Файл с таким названием уже существует.");
            return;
        }

        try
        {
            // Заменяем плейсхолдеры в команде
            string command = CustomCommandTextBox.Text
                .Replace("{input}", inputPath)
                .Replace("{output}", outputPath);

            var (exitCode, errorOutput) = await RunFfmpegAsync(command);

            if (exitCode == 0)
            {
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

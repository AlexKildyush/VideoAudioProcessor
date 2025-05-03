using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VideoAudioProcessor;

public partial class MainWindow : Window
{
    private DispatcherTimer _processedTimer;
    private bool _isUpdatingProcessedProgress;

    private void InitializeProcessedTimer()
    {
        _processedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _processedTimer.Tick += ProcessedTimer_Tick;
    }

    private void ProcessedTimer_Tick(object? sender, EventArgs e)
    {
        if (!ProcessedMediaPlayer.NaturalDuration.HasTimeSpan || ProcessedMediaPlayer.Source == null) return;
        _isUpdatingProcessedProgress = true;
        ProcessedProgressSlider.Value = ProcessedMediaPlayer.Position.TotalSeconds /
                                        ProcessedMediaPlayer.NaturalDuration.TimeSpan.TotalSeconds * 100;
        _isUpdatingProcessedProgress = false;
        ProcessedCurrentTimeText.Text = ProcessedMediaPlayer.Position.ToString(@"mm\:ss");
    }

    private void ProcessedListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessedListBox.SelectedItem == null)
        {
            return;
        }

        var selectedFile = Path.Combine(ProcessedPath, ProcessedListBox.SelectedItem.ToString()!);

        try
        {
            ProcessedMediaPlayer.Source = new Uri(selectedFile, UriKind.Absolute);
            ProcessedPlayerStatus.Visibility = Visibility.Collapsed;
            ProcessedMediaPlayer.Play();
            _processedTimer.Start();
        }
        catch (Exception ex)
        {
            ProcessedPlayerStatus.Text = $"Ошибка воспроизведения: {ex.Message}";
            ProcessedPlayerStatus.Visibility = Visibility.Visible;
        }
    }

    private void ProcessedMediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (ProcessedMediaPlayer.NaturalDuration.HasTimeSpan)
        {
            ProcessedTotalTimeText.Text = ProcessedMediaPlayer.NaturalDuration.TimeSpan.ToString(@"mm\:ss");
        }
    }

    private void ProcessedMediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        ProcessedMediaPlayer.Stop();
        ProcessedProgressSlider.Value = 0;
        ProcessedCurrentTimeText.Text = "00:00";
        _processedTimer.Stop();
    }

    private void ProcessedProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingProcessedProgress || ProcessedMediaPlayer.Source == null || !ProcessedMediaPlayer.NaturalDuration.HasTimeSpan) return;
        var newPosition = TimeSpan.FromSeconds(ProcessedProgressSlider.Value / 100 *
                                               ProcessedMediaPlayer.NaturalDuration.TimeSpan.TotalSeconds);
        ProcessedMediaPlayer.Position = newPosition;
        ProcessedCurrentTimeText.Text = newPosition.ToString(@"mm\:ss");
    }

    private void ProcessedPlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessedMediaPlayer.Source == null) return;
        ProcessedMediaPlayer.Play();
        _processedTimer.Start();
        ProcessedPlayerStatus.Visibility = Visibility.Collapsed;
    }

    private void ProcessedPauseButton_Click(object sender, RoutedEventArgs e)
    {
        ProcessedMediaPlayer.Pause();
        _processedTimer.Stop();
    }

    private void ProcessedStopButton_Click(object sender, RoutedEventArgs e)
    {
        ProcessedMediaPlayer.Stop();
        _processedTimer.Stop();
        ProcessedProgressSlider.Value = 0;
        ProcessedCurrentTimeText.Text = "00:00";
    }

    private void SendToQueueButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessedListBox.SelectedItem == null) return;
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            MessageBox.Show("Пожалуйста, сначала установите корневую папку", "Ошибка", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(QueuePath);
        var sourcePath = Path.Combine(ProcessedPath, ProcessedListBox.SelectedItem.ToString()!);
        var destinationName = GetUniqueQueueFileName(Path.GetFileName(sourcePath));
        var destinationPath = Path.Combine(QueuePath, destinationName);

        try
        {
            File.Copy(sourcePath, destinationPath);
            MessageBox.Show($"Файл добавлен в очередь: {destinationName}", "Успешно", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при добавлении файла в очередь: {ex.Message}", "Ошибка", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    private async void DeleteProcessedFile_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessedListBox.SelectedItem == null)
        {
            return;
        }

        var selectedName = ProcessedListBox.SelectedItem.ToString()!;
        var selectedFile = Path.Combine(ProcessedPath, selectedName);
        var result = MessageBox.Show($"Удалить файл {selectedName}?", "Подтверждение удаления", MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await RunWithWaitDialogAsync("Удаление", "Файл удаляется...", async () =>
            {
                await Task.Run(() =>
                {
                    if (File.Exists(selectedFile))
                    {
                        File.Delete(selectedFile);
                    }
                });
            });

            RefreshProcessedList();
            ProcessedMediaPlayer.Stop();
            ProcessedMediaPlayer.Source = null;
            ProcessedPlayerStatus.Text = "Выберите файл для воспроизведения";
            ProcessedPlayerStatus.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при удалении файла: {ex.Message}", "Ошибка", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string GetUniqueQueueFileName(string originalFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var candidateName = originalFileName;
        var version = 1;

        while (File.Exists(Path.Combine(QueuePath, candidateName)))
        {
            candidateName = $"{baseName}({version}){extension}";
            version++;
        }

        return candidateName;
    }

    private void RefreshProcessedList()
    {
        if (!Directory.Exists(ProcessedPath))
        {
            ProcessedListBox.ItemsSource = null;
            return;
        }

        var files = Directory.GetFiles(ProcessedPath)
            .Where(f => MediaFormats.Supported.Contains(Path.GetExtension(f).ToLower()))
            .Select(Path.GetFileName)
            .ToList();

        ProcessedListBox.ItemsSource = files;
    }
}

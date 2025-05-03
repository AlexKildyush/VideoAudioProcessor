using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VideoAudioProcessor;

public partial class MainWindow : Window
{
    private DispatcherTimer _progressTimer;
    private bool _isUpdatingQueueProgress;
    
    private void FilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilesListBox.SelectedItem == null) return;

        var selectedFile = Path.Combine(QueuePath, FilesListBox.SelectedItem.ToString()!);
        
        try
        {
            MediaPlayer.Source = new Uri(selectedFile, UriKind.Absolute);
            PlayerStatus.Visibility = Visibility.Collapsed;
            MediaPlayer.Play();
            _progressTimer.Start();
        }
        catch (Exception ex)
        {
            PlayerStatus.Text = $"Ошибка воспроизведения: {ex.Message}";
            PlayerStatus.Visibility = Visibility.Visible;
        }
    }
    
    private async void DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        if (FilesListBox.SelectedItem == null) return;
    
        var selectedFile = Path.Combine(QueuePath, FilesListBox.SelectedItem.ToString());
    
        var result = MessageBox.Show($"Вы точно хотите удалить файл {FilesListBox.SelectedItem}?", 
            "Подтверждение удаления", 
            MessageBoxButton.YesNo, 
            MessageBoxImage.Question);
    
        if (result == MessageBoxResult.Yes)
        {
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

                RefreshFileList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при удалении файла: {ex.Message}", 
                    "Ошибка", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            }
        }
    }
     
    private void RefreshFileList()
    {
        if (!Directory.Exists(QueuePath))
        {
            FilesListBox.ItemsSource = null;
            return;
        }

        var files = Directory.GetFiles(QueuePath)
            .Where(f => MediaFormats.Supported.Contains(Path.GetExtension(f).ToLower()))
            .Select(Path.GetFileName)
            .ToList();

        FilesListBox.ItemsSource = files;
    }
    
    private void ProcessFile_Click(object sender, RoutedEventArgs e)
    {
        if (FilesListBox.SelectedItem == null) return;
    
        var selectedFile = Path.Combine(QueuePath, FilesListBox.SelectedItem.ToString());
    
        try
        {
            _progressTimer.Stop();
            MediaPlayer.Pause();

            _previewTimer.Stop();
            PreviewMediaPlayer.Stop();
            PreviewMediaPlayer.Source = new Uri(selectedFile, UriKind.Absolute);
            PreviewSlider.Value = 0;
            PreviewCurrentTime.Text = "00:00";
            PreviewTotalTime.Text = "00:00";
        
            // Переключаемся на экран обработки
            HideAllScreens();
            ProcessScreen.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при открытии файла: {ex.Message}", 
                "Ошибка", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error);
        }
    }
    
    private void InitializeProgressTimer()
    {
        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _progressTimer.Tick += ProgressTimer_Tick;
    }

    private void ProgressTimer_Tick(object sender, EventArgs e)
    {
        if (!MediaPlayer.NaturalDuration.HasTimeSpan || MediaPlayer.Source == null) return;
        _isUpdatingQueueProgress = true;
        ProgressSlider.Value = MediaPlayer.Position.TotalSeconds / MediaPlayer.NaturalDuration.TimeSpan.TotalSeconds * 100;
        _isUpdatingQueueProgress = false;
        CurrentTimeText.Text = MediaPlayer.Position.ToString(@"mm\:ss");
    }
    
    private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (MediaPlayer.NaturalDuration.HasTimeSpan)
        {
            TotalTimeText.Text = MediaPlayer.NaturalDuration.TimeSpan.ToString(@"mm\:ss");
        }
    }

    private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        MediaPlayer.Stop();
        ProgressSlider.Value = 0;
        CurrentTimeText.Text = "00:00";
        _progressTimer.Stop();
    }

    private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingQueueProgress || MediaPlayer.Source == null || !MediaPlayer.NaturalDuration.HasTimeSpan) return;
        var newPosition = TimeSpan.FromSeconds(ProgressSlider.Value / 100 * 
                                               MediaPlayer.NaturalDuration.TimeSpan.TotalSeconds);
        MediaPlayer.Position = newPosition;
        CurrentTimeText.Text = newPosition.ToString(@"mm\:ss");
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (MediaPlayer.Source == null) return;
        
        MediaPlayer.Play();
        _progressTimer.Start();
        PlayerStatus.Visibility = Visibility.Collapsed;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        MediaPlayer.Pause();
        _progressTimer.Stop();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        MediaPlayer.Stop();
        _progressTimer.Stop();
        ProgressSlider.Value = 0;
        CurrentTimeText.Text = "00:00";
    }

    private void ToggleMuteButton_Click(object sender, RoutedEventArgs e)
    {
        MediaPlayer.IsMuted = !MediaPlayer.IsMuted;
        UpdateMuteButtonIcon();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MediaPlayer.Volume = VolumeSlider.Value;
        UpdateMuteButtonIcon();
    }
    
    private void UpdateMuteButtonIcon()
    {
        if (MediaPlayer.IsMuted || MediaPlayer.Volume == 0)
        {
            MuteButton.Content = "🔇";  
        }
        else
        {
            MuteButton.Content = "🔊";  
        }
    }
}

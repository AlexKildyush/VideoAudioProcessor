using System;
using System.Linq;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace VideoAudioProcessor;

public partial class MainWindow : Window
{
    private static string QueuePath => Path.Combine(RootPath, "TrackManager", "Queue");
    private static string ProcessedPath => Path.Combine(RootPath, "TrackManager", "Processed");

    private static string RootPath
    {
        get => ConfigurationManager.AppSettings["RootPath"] ?? string.Empty;
        set
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var settings = config.AppSettings.Settings;
            if (settings["RootPath"] == null)
            {
                settings.Add("RootPath", value);
            }
            else
            {
                settings["RootPath"].Value = value;
            }

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        InitializePreviewTimer();
        InitializeProgressTimer();
        InitializeProcessedTimer();
        InitializeBatchQueue();
        InitializeProcessingOptions();
        VolumeSlider.Value = 0.5;
    }

    private async void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(RootPath))
        {
            MessageBox.Show("Пожалуйста, сначала установите корневую папку", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Title = "Выберите корневую папку"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                RootPath = dialog.FileName;
            }

            return;
        }

        var openFileDialog = new OpenFileDialog
        {
            Filter = "Медиа файлы|*.mp3;*.wav;*.ogg;*.flac;*.mp4;*.avi;*.mkv;*.mov;*.wmv|Все файлы|*.*",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string destinationPath = string.Empty;

            await RunWithWaitDialogAsync("Загрузка", "Файл загружается...", async () =>
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(QueuePath);
                    destinationPath = GetUniqueQueueFilePath(Path.GetFileName(openFileDialog.FileName));
                    File.Copy(openFileDialog.FileName, destinationPath, false);
                });
            });

            RefreshFileList();
            MessageBox.Show($"Файл сохранен: {destinationPath}", "Успешно", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при загрузке файла: {ex.Message}", "Ошибка", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string GetUniqueQueueFilePath(string originalFileName)
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

        return Path.Combine(QueuePath, candidateName);
    }

    private void ShowQueue_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(RootPath))
        {
            MessageBox.Show("Пожалуйста, сначала установите корневую папку", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(QueuePath))
        {
            MessageBox.Show("Папка очереди не существует", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var files = Directory.GetFiles(QueuePath)
            .Where(f => MediaFormats.Supported.Contains(Path.GetExtension(f).ToLower()))
            .ToList();

        FilesListBox.ItemsSource = files.Select(Path.GetFileName);
        HideAllScreens();
        QueueScreen.Visibility = Visibility.Visible;
    }

    private void ShowProcessed_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(RootPath))
        {
            MessageBox.Show("Пожалуйста, сначала установите корневую папку", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(ProcessedPath))
        {
            MessageBox.Show("Папка обработанных файлов не существует", "Информация", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RefreshProcessedList();
        HideAllScreens();
        ProcessedScreen.Visibility = Visibility.Visible;
    }

    private void ShowAbout_Click(object sender, RoutedEventArgs e)
    {
        HideAllScreens();
        StartScreen.Visibility = Visibility.Visible;
    }

    private void HideAllScreens()
    {
        StartScreen.Visibility = Visibility.Collapsed;
        QueueScreen.Visibility = Visibility.Collapsed;
        ProcessedScreen.Visibility = Visibility.Collapsed;
        ProcessScreen.Visibility = Visibility.Collapsed;
        ProjectListScreen.Visibility = Visibility.Collapsed;
        ProjectEditorScreen.Visibility = Visibility.Collapsed;
        BatchScreen.Visibility = Visibility.Collapsed;
    }

    private async Task RunWithWaitDialogAsync(string title, string message, Func<Task> action)
    {
        var waitingWindow = new Window
        {
            Title = title,
            Width = 320,
            Height = 140,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow,
            Owner = this,
            Content = new Grid
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        Foreground = System.Windows.Media.Brushes.White,
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            },
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF1E1E1E")!
        };

        waitingWindow.Show();
        await Task.Yield();

        try
        {
            await action();
        }
        finally
        {
            waitingWindow.Close();
        }
    }
}


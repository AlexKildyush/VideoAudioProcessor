using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using VideoAudioProcessor.Infrastructure.Configuration;
using VideoAudioProcessor.Infrastructure.Theming;
using VideoAudioProcessor.Services;
using VideoAudioProcessor.ViewModel;

namespace VideoAudioProcessor.View;

public partial class MainWindow : Window
{
    private string QueuePath => Path.Combine(RootPath, "TrackManager", "Queue");
    private string ProcessedPath => Path.Combine(RootPath, "TrackManager", "Processed");

    private string RootPath
    {
        get => _appSettings.RootPath;
        set => _appSettings.RootPath = value;
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        IAppSettingsService appSettings,
        IThemeService themeService,
        FfmpegCommandRunner commandRunner,
        InformationSearchService informationSearchService)
    {
        _viewModel = viewModel;
        _appSettings = appSettings;
        _themeService = themeService;
        _commandRunner = commandRunner;
        _informationSearchService = informationSearchService;

        InitializeComponent();
        DataContext = _viewModel;

        InitializeInformation();
        InitializePreviewTimer();
        InitializeProgressTimer();
        InitializeProcessedTimer();
        InitializeBatchQueue();
        InitializeProcessingOptions();

        TimelineItemsListBox.SelectionChanged += (_, _) => NormalizeProjectEditorVisuals();
        VideoTimelinePanel.LayoutUpdated += (_, _) => NormalizeTimelinePreviewTheme();
        AudioTimelinePanel.LayoutUpdated += (_, _) => NormalizeTimelinePreviewTheme();
        SubtitleTimelinePanel.LayoutUpdated += (_, _) => NormalizeTimelinePreviewTheme();
    }

    private async void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(RootPath))
        {
            MessageBox.Show("Пожалуйста, сначала установите корневую папку", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (TrySelectRootFolder())
            {
                RefreshCurrentScreenAfterRootPathChange();
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
            var storage = CreateStorageService();
            string destinationPath = string.Empty;

            await RunWithWaitDialogAsync("Загрузка", "Файл загружается...", async () =>
            {
                await Task.Run(() =>
                {
                    destinationPath = storage.GetUniqueQueueFilePath(Path.GetFileName(openFileDialog.FileName));
                    File.Copy(openFileDialog.FileName, destinationPath, false);
                });
            });

            RefreshFileList();
            MessageBox.Show($"Файл сохранен: {destinationPath}", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при загрузке файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string GetUniqueQueueFilePath(string originalFileName)
    {
        return CreateStorageService().GetUniqueQueueFilePath(originalFileName);
    }

private void ShowQueue_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(RootPath))
        {
            MessageBox.Show("Пожалуйста, сначала установите корневую папку", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(QueuePath);

        FilesListBox.ItemsSource = CreateStorageService().GetSupportedQueueFiles();
        ShowScreen(AppScreen.Queue);
    }

private void ShowProcessed_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(RootPath))
        {
            MessageBox.Show("Пожалуйста, сначала установите корневую папку", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(ProcessedPath);

        RefreshProcessedList();
        ShowScreen(AppScreen.Processed);
    }

    private void ShowAbout_Click(object sender, RoutedEventArgs e)
    {
        ShowInformationScreen();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_themeService.CurrentTheme, RootPath)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() != true)
        {
            return;
        }

        _themeService.ApplyTheme(settingsWindow.SelectedTheme);

        if (!string.Equals(RootPath, settingsWindow.SelectedRootPath, StringComparison.Ordinal))
        {
            RootPath = settingsWindow.SelectedRootPath;
            RefreshCurrentScreenAfterRootPathChange();
        }

        ApplyThemeSensitiveVisuals();
    }

    private void HideAllScreens()
    {
        _viewModel.CurrentScreen = AppScreen.None;
    }

    private void ShowScreen(AppScreen screen)
    {
        _viewModel.CurrentScreen = screen;
        ApplyThemeSensitiveVisuals();
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
                        Foreground = ThemeBrush("TextPrimaryBrush"),
                        TextAlignment = TextAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            },
            Background = ThemeBrush("SurfaceStrongBrush")
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

    private async Task RunJobWithProgressDialogAsync(string title, ProcessingJob job, Func<Task> action)
    {
        var waitingWindow = new Window
        {
            Title = title,
            Width = 460,
            Height = 260,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.ToolWindow,
            Owner = this,
            Background = ThemeBrush("SurfaceStrongBrush"),
            Content = BuildJobProgressContent(job)
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

    private FrameworkElement BuildJobProgressContent(ProcessingJob job)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(18)
        };

        panel.Children.Add(CreateBoundTextBlock(nameof(ProcessingJob.Name), "TextPrimaryBrush", 18, FontWeights.Bold));
        panel.Children.Add(CreateBoundTextBlock(nameof(ProcessingJob.StageText), "StatusBrush", margin: new Thickness(0, 10, 0, 0)));

        var progressBar = new ProgressBar
        {
            Height = 12,
            Maximum = 100,
            Margin = new Thickness(0, 10, 0, 0)
        };
        progressBar.SetBinding(RangeBase.ValueProperty, new Binding(nameof(ProcessingJob.ProgressValue)) { Mode = BindingMode.OneWay });
        panel.Children.Add(progressBar);

        panel.Children.Add(CreateBoundTextBlock(nameof(ProcessingJob.ProgressText), "TextPrimaryBrush", margin: new Thickness(0, 8, 0, 0)));
        panel.Children.Add(CreateBoundTextBlock(nameof(ProcessingJob.TimingText), "TextSecondaryBrush", margin: new Thickness(0, 6, 0, 0)));
        panel.Children.Add(CreateBoundTextBlock(nameof(ProcessingJob.SpeedText), "TextSecondaryBrush", margin: new Thickness(0, 6, 0, 0), collapseWhenEmpty: true));
        panel.Children.Add(CreateBoundTextBlock(nameof(ProcessingJob.Summary), "TextSecondaryBrush", margin: new Thickness(0, 10, 0, 0), wrap: true, collapseWhenEmpty: true));

        panel.DataContext = job;
        return panel;
    }

    private TextBlock CreateBoundTextBlock(
        string path,
        string brushKey,
        double? fontSize = null,
        FontWeight? fontWeight = null,
        Thickness? margin = null,
        bool wrap = false,
        bool collapseWhenEmpty = false)
    {
        var textBlock = new TextBlock
        {
            Foreground = ThemeBrush(brushKey),
            Margin = margin ?? new Thickness(0),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
        };

        if (fontSize.HasValue)
        {
            textBlock.FontSize = fontSize.Value;
        }

        if (fontWeight.HasValue)
        {
            textBlock.FontWeight = fontWeight.Value;
        }

        textBlock.SetBinding(TextBlock.TextProperty, new Binding(path) { Mode = BindingMode.OneWay });

        if (collapseWhenEmpty)
        {
            textBlock.Style = new Style(typeof(TextBlock))
            {
                Setters =
                {
                    new Setter(VisibilityProperty, Visibility.Visible)
                },
                Triggers =
                {
                    new DataTrigger
                    {
                        Binding = new Binding(path),
                        Value = string.Empty,
                        Setters = { new Setter(VisibilityProperty, Visibility.Collapsed) }
                    }
                }
            };
        }

        return textBlock;
    }

    private Brush ThemeBrush(string key)
    {
        return (Brush)(TryFindResource(key) ?? Brushes.Transparent);
    }

    private bool TrySelectRootFolder()
    {
        var dialog = new CommonOpenFileDialog
        {
            IsFolderPicker = true,
            Title = "Выберите корневую папку"
        };

        if (!string.IsNullOrWhiteSpace(RootPath) && Directory.Exists(RootPath))
        {
            dialog.InitialDirectory = RootPath;
            dialog.DefaultDirectory = RootPath;
        }

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
        {
            return false;
        }

        RootPath = dialog.FileName;
        return true;
    }

    private void RefreshCurrentScreenAfterRootPathChange()
    {
        switch (_viewModel.CurrentScreen)
        {
            case AppScreen.Queue:
                if (Directory.Exists(QueuePath))
                {
                    RefreshFileList();
                }
                break;
            case AppScreen.Processed:
                if (Directory.Exists(ProcessedPath))
                {
                    RefreshProcessedList();
                }
                break;
            case AppScreen.Projects:
                RefreshProjectList();
                break;
        }
    }

    private void ApplyThemeSensitiveVisuals()
    {
        NormalizeProjectListVisuals();
        NormalizeProjectEditorVisuals();
        NormalizeTimelinePreviewTheme();
    }

    private void NormalizeProjectListVisuals()
    {
        ProjectListTitle.Text = _currentProjectType == ProjectType.SlideShow
            ? "Проекты слайдшоу"
            : "Проекты медиаколлажей";
    }

    private void NormalizeProjectEditorVisuals()
    {
        AddSubtitleButton.Content = GetSelectedSubtitleItem() == null
            ? "Добавить субтитр"
            : "Обновить субтитр";
    }

    private void NormalizeTimelinePreviewTheme()
    {
        if (TimelineScaleText == null)
        {
            return;
        }

        var totalDuration = _timelineItems.Sum(item => Math.Max(0.5, item.DurationSeconds));
        TimelineScaleText.Text = $"0 сек  |  Общая длительность: {totalDuration:0.##} сек";

        for (var index = 0; index < VideoTimelinePanel.Children.Count && index < _timelineItems.Count; index++)
        {
            if (VideoTimelinePanel.Children[index] is not Border border)
            {
                continue;
            }

            border.Background = _timelineItems[index].Kind == ProjectMediaKind.Image
                ? ThemeBrush("TimelineImageBrush")
                : ThemeBrush("TimelineVideoBrush");

            if (border.Child is TextBlock text)
            {
                text.Foreground = ThemeBrush("TimelineBlockTextBrush");
                text.Text = $"{Path.GetFileName(_timelineItems[index].Path)} ({_timelineItems[index].DurationSeconds:0.#}с)";
            }
        }

        var usesVideoAudio = _currentProject?.UseVideoAudio == true && _timelineItems.Any(item => item.Kind == ProjectMediaKind.Video);
        NormalizeTrackPanel(
            AudioTimelinePanel,
            usesVideoAudio
                ? ThemeBrush("TimelineAudioBrush")
                : _audioTimelineItems.Count == 0
                    ? ThemeBrush("TimelineEmptyBrush")
                    : ThemeBrush("TimelineAudioBrush"));
        NormalizeTrackPanel(
            SubtitleTimelinePanel,
            _subtitleTimelineItems.Count == 0
                ? ThemeBrush("TimelineEmptyBrush")
                : ThemeBrush("TimelineSubtitleBrush"));
    }

    private void NormalizeTrackPanel(Panel panel, Brush background)
    {
        foreach (var child in panel.Children)
        {
            if (child is not Border border)
            {
                continue;
            }

            border.Background = background;
            if (border.Child is not TextBlock text)
            {
                continue;
            }

            text.Foreground = ThemeBrush("TimelineBlockTextBrush");
            text.Text = text.Text.Replace("СЃ", "с");
        }
    }
}

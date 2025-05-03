using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.VisualBasic;

namespace VideoAudioProcessor;

public partial class MainWindow : Window
{
    private const string ProjectsFolderName = "Projects";
    private const int DefaultProjectFps = 30;
    private const double DefaultTransitionSeconds = 1;
    private const double DefaultMaxClipDurationSeconds = 0;
    private ProjectType _currentProjectType;
    private ProjectData? _currentProject;
    private readonly ObservableCollection<ProjectMediaItem> _timelineItems = new();
    private readonly ObservableCollection<ProjectAudioItem> _audioTimelineItems = new();
    private readonly ObservableCollection<TimelineListEntry> _timelineListItems = new();

    private string ProjectsRootPath => Path.Combine(RootPath, "TrackManager", ProjectsFolderName);

    private void ShowVideoCollageProjects_Click(object sender, RoutedEventArgs e)
    {
        ShowProjectList(ProjectType.VideoCollage);
    }


    private void ShowProjectList(ProjectType type)
    {
        if (string.IsNullOrEmpty(RootPath))
        {
            MessageBox.Show("Пожалуйста, сначала установите корневую папку", "Ошибка", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _currentProjectType = type;
        ProjectListTitle.Text = "Проекты медиаколлажей";
        RefreshProjectList();
        HideAllScreens();
        ProjectListScreen.Visibility = Visibility.Visible;
    }

    private void RefreshProjectList()
    {
        var projectsPath = GetProjectsPath(_currentProjectType);
        Directory.CreateDirectory(projectsPath);

        var names = Directory.GetFiles(projectsPath, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name)
            .ToList();

        ProjectsListBox.ItemsSource = names;
    }

    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        var name = Interaction.InputBox("Введите название проекта", "Новый проект", "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var sanitizedName = name.Trim();
        if (!IsValidProjectName(sanitizedName))
        {
            MessageBox.Show("Название проекта содержит недопустимые символы.");
            return;
        }

        var projectPath = GetProjectFilePath(_currentProjectType, sanitizedName);
        if (File.Exists(projectPath))
        {
            MessageBox.Show("Проект с таким названием уже существует.");
            return;
        }

        var project = new ProjectData
        {
            Name = sanitizedName,
            Type = _currentProjectType
        };

        SaveProjectToFile(project);
        OpenProjectEditor(project);
    }

    private void ProjectsListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenSelectedProject();
    }

    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedProject();
    }

    private void OpenSelectedProject()
    {
        if (ProjectsListBox.SelectedItem is not string projectName)
        {
            return;
        }

        var projectPath = GetProjectFilePath(_currentProjectType, projectName);
        if (!File.Exists(projectPath))
        {
            MessageBox.Show("Файл проекта не найден.");
            RefreshProjectList();
            return;
        }

        var json = File.ReadAllText(projectPath);
        var project = JsonSerializer.Deserialize<ProjectData>(json);
        if (project == null)
        {
            MessageBox.Show("Не удалось загрузить проект.");
            return;
        }

        OpenProjectEditor(project);
    }

    private void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsListBox.SelectedItem is not string projectName)
        {
            return;
        }

        var result = MessageBox.Show($"Удалить проект {projectName}?", "Подтверждение", MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var projectPath = GetProjectFilePath(_currentProjectType, projectName);
        if (File.Exists(projectPath))
        {
            File.Delete(projectPath);
        }

        RefreshProjectList();
    }

    private void EnsureAudioItemsMigrated(ProjectData project)
    {
        project.AudioItems ??= new List<ProjectAudioItem>();
        if (project.AudioItems.Count == 0 && !string.IsNullOrWhiteSpace(project.AudioPath))
        {
            project.AudioItems.Add(new ProjectAudioItem
            {
                Path = project.AudioPath,
                DurationSeconds = project.AudioDurationSeconds
            });
        }
    }

    private void SyncProjectAudioState(ProjectData project)
    {
        project.AudioItems = _audioTimelineItems.ToList();
        if (project.AudioItems.Count > 0)
        {
            project.AudioPath = project.AudioItems[0].Path;
            project.AudioDurationSeconds = project.AudioItems[0].DurationSeconds;
        }
        else
        {
            project.AudioPath = null;
            project.AudioDurationSeconds = 0;
        }
    }

    private void UpdateSelectedAudioText(ProjectData project)
    {
        if (project.UseVideoAudio && _timelineItems.Any(item => item.Kind == ProjectMediaKind.Video))
        {
            SelectedAudioText.Text = "Аудио берется из видео дорожки";
            return;
        }

        if (_audioTimelineItems.Count == 0)
        {
            SelectedAudioText.Text = "Аудио не выбрано";
            return;
        }

        var totalDuration = _audioTimelineItems.Sum(item => item.DurationSeconds > 0
            ? item.DurationSeconds
            : GetMediaDuration(item.Path));
        SelectedAudioText.Text = $"Выбрано аудио: {_audioTimelineItems.Count} (сумма: {totalDuration:0.##} сек.)";
    }

    private void OpenProjectEditor(ProjectData project)
    {
        _currentProject = project;
        EnsureAudioItemsMigrated(project);

        _timelineItems.Clear();
        foreach (var item in project.Items)
        {
            _timelineItems.Add(item);
        }

        _audioTimelineItems.Clear();
        foreach (var item in project.AudioItems)
        {
            _audioTimelineItems.Add(item);
        }

        TimelineItemsListBox.ItemsSource = _timelineListItems;
        ProjectEditorTitle.Text = "Форма редактирования медиаколлажа";
        ProjectNameTextBox.Text = project.Name;
        UpdateSelectedAudioText(project);
        UseVideoAudioCheckBox.IsChecked = project.UseVideoAudio;
        ProjectOutputFormatComboBox.SelectedIndex = project.OutputFormat switch
        {
            "mkv" => 1,
            "avi" => 2,
            _ => 0
        };
        project.Fps = DefaultProjectFps;
        project.TransitionSeconds = DefaultTransitionSeconds;
        project.MaxClipDurationSeconds = DefaultMaxClipDurationSeconds;

        ProjectWidthTextBox.Text = project.Width.ToString();
        ProjectHeightTextBox.Text = project.Height.ToString();
        SlideDurationTextBox.Text = project.SlideDurationSeconds.ToString();

        UseVideoAudioCheckBox.Visibility = Visibility.Visible;
        SlideDurationLabel.Visibility = Visibility.Collapsed;
        SlideDurationTextBox.Visibility = Visibility.Collapsed;

        RefreshTimelineList();
        RefreshTimelinePreview();

        HideAllScreens();
        ProjectEditorScreen.Visibility = Visibility.Visible;
    }

    private void AddVideoFromBase_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
        {
            return;
        }

        var selected = SelectFromTrackStorage("Выберите видео из треков", IsVideoFile);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        AddTimelineItem(selected, ProjectMediaKind.Video);
    }

    private void AddVideoFromComputer_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Видео файлы|*.mp4;*.avi;*.mkv;*.mov;*.wmv",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() != true)
        {
            return;
        }

        AddTimelineItem(openFileDialog.FileName, ProjectMediaKind.Video);
    }


    private void AddImageFromComputer_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp;*.gif",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() != true)
        {
            return;
        }

        AddTimelineItem(openFileDialog.FileName, ProjectMediaKind.Image);
    }

    private void AddTimelineItem(string path, ProjectMediaKind kind)
    {
        if (_currentProject == null)
        {
            return;
        }

        var duration = kind == ProjectMediaKind.Image
            ? PromptDurationSeconds(3)
            : GetTrimmedDuration(path, _currentProject.MaxClipDurationSeconds);

        if (kind == ProjectMediaKind.Image && duration == null)
        {
            return;
        }

        var item = new ProjectMediaItem
        {
            Path = path,
            DurationSeconds = duration ?? 0,
            Kind = kind
        };

        _timelineItems.Add(item);
        _currentProject.Items = _timelineItems.ToList();
        SaveProjectToFile(_currentProject);
        RefreshTimelineList();
        RefreshTimelinePreview();
    }

    private static double? PromptDurationSeconds(double defaultValue)
    {
        var input = Interaction.InputBox("Укажите длительность отображения в секундах", "Длительность",
            defaultValue.ToString("0.##"));

        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var duration = ParseDoubleOrDefault(input, defaultValue);
        return Math.Max(0.5, duration);
    }

    private void EditTimelineItemDuration_Click(object sender, RoutedEventArgs e)
    {
        if (TimelineItemsListBox.SelectedItem is not TimelineListEntry selectedEntry || _currentProject == null)
        {
            return;
        }

        if (selectedEntry.MediaItem != null)
        {
            var updatedDuration = PromptDurationSeconds(selectedEntry.MediaItem.DurationSeconds > 0
                ? selectedEntry.MediaItem.DurationSeconds
                : 3);
            if (updatedDuration == null)
            {
                return;
            }

            selectedEntry.MediaItem.DurationSeconds = updatedDuration.Value;
            _currentProject.Items = _timelineItems.ToList();
        }
        else if (selectedEntry.AudioItem != null)
        {
            var defaultDuration = selectedEntry.AudioItem.DurationSeconds > 0
                ? selectedEntry.AudioItem.DurationSeconds
                : GetMediaDuration(selectedEntry.AudioItem.Path);
            var updatedDuration = PromptDurationSeconds(defaultDuration);
            if (updatedDuration == null)
            {
                return;
            }

            selectedEntry.AudioItem.DurationSeconds = updatedDuration.Value;
            SyncProjectAudioState(_currentProject);
        }

        SaveProjectToFile(_currentProject);
        RefreshTimelineList();
        RefreshTimelinePreview();
        UpdateSelectedAudioText(_currentProject);
    }

    private void RemoveTimelineItem_Click(object sender, RoutedEventArgs e)
    {
        if (TimelineItemsListBox.SelectedItem is not TimelineListEntry selected)
        {
            return;
        }

        if (_currentProject != null)
        {
            if (selected.MediaItem != null)
            {
                _timelineItems.Remove(selected.MediaItem);
            }
            else if (selected.AudioItem != null)
            {
                _audioTimelineItems.Remove(selected.AudioItem);
                SyncProjectAudioState(_currentProject);
                if (_audioTimelineItems.Count == 0)
                {
                    _currentProject.UseVideoAudio = false;
                    UseVideoAudioCheckBox.IsChecked = false;
                }
            }

            _currentProject.Items = _timelineItems.ToList();
            SyncProjectAudioState(_currentProject);
            SaveProjectToFile(_currentProject);
            RefreshTimelineList();
            RefreshTimelinePreview();
            UpdateSelectedAudioText(_currentProject);
        }
    }

    private void SelectAudioFromBase_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectFromTrackStorage("Выберите аудио из треков", IsAudioFile);
        if (string.IsNullOrWhiteSpace(selected) || _currentProject == null)
        {
            return;
        }

        _audioTimelineItems.Add(new ProjectAudioItem
        {
            Path = selected,
            DurationSeconds = 0
        });
        _currentProject.UseVideoAudio = false;
        UseVideoAudioCheckBox.IsChecked = false;
        SyncProjectAudioState(_currentProject);
        SaveProjectToFile(_currentProject);
        RefreshTimelineList();
        RefreshTimelinePreview();
        UpdateSelectedAudioText(_currentProject);
    }

    private void SelectAudioFromComputer_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "Аудио файлы|*.mp3;*.wav;*.ogg;*.flac;*.aac;*.m4a",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() != true || _currentProject == null)
        {
            return;
        }

        _audioTimelineItems.Add(new ProjectAudioItem
        {
            Path = openFileDialog.FileName,
            DurationSeconds = 0
        });
        _currentProject.UseVideoAudio = false;
        UseVideoAudioCheckBox.IsChecked = false;
        SyncProjectAudioState(_currentProject);
        SaveProjectToFile(_currentProject);
        RefreshTimelineList();
        RefreshTimelinePreview();
        UpdateSelectedAudioText(_currentProject);
    }

    private void ClearAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
        {
            return;
        }

        _audioTimelineItems.Clear();
        SyncProjectAudioState(_currentProject);
        _currentProject.UseVideoAudio = false;
        UseVideoAudioCheckBox.IsChecked = false;
        SaveProjectToFile(_currentProject);
        RefreshTimelineList();
        RefreshTimelinePreview();
        UpdateSelectedAudioText(_currentProject);
    }

    private void RefreshTimelineList()
    {
        _timelineListItems.Clear();
        foreach (var item in _timelineItems)
        {
            _timelineListItems.Add(new TimelineListEntry(item));
        }

        if (_currentProject?.UseVideoAudio == true && _timelineItems.Any(item => item.Kind == ProjectMediaKind.Video))
        {
            _timelineListItems.Add(TimelineListEntry.CreateAudioFromVideo());
            return;
        }

        foreach (var audioItem in _audioTimelineItems)
        {
            _timelineListItems.Add(TimelineListEntry.CreateAudio(audioItem));
        }
    }

    private void RefreshTimelinePreview()
    {
        VideoTimelinePanel.Children.Clear();
        AudioTimelinePanel.Children.Clear();

        var totalDuration = _timelineItems.Sum(item => Math.Max(0.5, item.DurationSeconds));
        TimelineScaleText.Text = $"0 сек  |  Общая длительность: {totalDuration:0.##} сек";

        foreach (var item in _timelineItems)
        {
            var width = Math.Max(60, item.DurationSeconds * 24);
            var color = item.Kind == ProjectMediaKind.Image ? "#FF5B3A9E" : "#FF1565C0";
            var block = new Border
            {
                Width = width,
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                CornerRadius = new CornerRadius(3),
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(color)!,
                Child = new TextBlock
                {
                    Text = $"{Path.GetFileName(item.Path)} ({item.DurationSeconds:0.#}с)",
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                ToolTip = item.DisplayName
            };

            VideoTimelinePanel.Children.Add(block);
        }

        if (_currentProject?.UseVideoAudio == true && _timelineItems.Any(item => item.Kind == ProjectMediaKind.Video))
        {
            var videoAudioBlock = new Border
            {
                Width = Math.Max(120, totalDuration * 24),
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                CornerRadius = new CornerRadius(3),
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF2E7D32")!,
                Child = new TextBlock
                {
                    Text = "Аудио из видео дорожки",
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            AudioTimelinePanel.Children.Add(videoAudioBlock);
            return;
        }

        if (_audioTimelineItems.Count == 0)
        {
            var emptyAudioBlock = new Border
            {
                Width = Math.Max(120, totalDuration * 24),
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                CornerRadius = new CornerRadius(3),
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF424242")!,
                Child = new TextBlock
                {
                    Text = "Без аудио",
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            AudioTimelinePanel.Children.Add(emptyAudioBlock);
            return;
        }

        foreach (var item in _audioTimelineItems)
        {
            var audioDuration = item.DurationSeconds > 0 ? item.DurationSeconds : GetMediaDuration(item.Path);
            var audioBlock = new Border
            {
                Width = Math.Max(120, audioDuration * 24),
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                CornerRadius = new CornerRadius(3),
                Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF2E7D32")!,
                Child = new TextBlock
                {
                    Text = $"{Path.GetFileName(item.Path)} ({audioDuration:0.#}с)",
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                ToolTip = item.DisplayName
            };

            AudioTimelinePanel.Children.Add(audioBlock);
        }
    }

    private void UseVideoAudioCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
        {
            return;
        }

        _currentProject.UseVideoAudio = UseVideoAudioCheckBox.IsChecked == true;
        SyncProjectAudioState(_currentProject);
        SaveProjectToFile(_currentProject);
        RefreshTimelineList();
        RefreshTimelinePreview();
        UpdateSelectedAudioText(_currentProject);
    }

    private static readonly string[] VideoExtensions = [".mp4", ".avi", ".mkv", ".mov", ".wmv"];
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a"];

    private string? SelectFromTrackStorage(string title, Func<string, bool> predicate)
    {
        if (string.IsNullOrEmpty(RootPath))
        {
            return null;
        }

        var baseTracks = GetBaseTracks(predicate);
        if (baseTracks.Count == 0)
        {
            MessageBox.Show("В базе треков нет файлов.");
            return null;
        }

        var listBox = new ListBox
        {
            Margin = new Thickness(10),
            DisplayMemberPath = nameof(TrackStorageItem.DisplayName),
            ItemsSource = baseTracks
        };
        if (baseTracks.Count > 0)
        {
            listBox.SelectedIndex = 0;
        }

        var selectButton = new Button
        {
            Content = "Выбрать",
            Width = 90,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var cancelButton = new Button
        {
            Content = "Отмена",
            Width = 90,
            IsCancel = true
        };

        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 10, 10)
        };
        actionsPanel.Children.Add(selectButton);
        actionsPanel.Children.Add(cancelButton);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(listBox, 0);
        Grid.SetRow(actionsPanel, 1);
        layout.Children.Add(listBox);
        layout.Children.Add(actionsPanel);

        var pickerWindow = new Window
        {
            Title = title,
            Width = 640,
            Height = 420,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = layout,
            Background = (Brush)new BrushConverter().ConvertFromString("#FF1E1E1E")!
        };

        string? selectedPath = null;
        selectButton.Click += (_, _) =>
        {
            if (listBox.SelectedItem is TrackStorageItem selectedItem)
            {
                selectedPath = selectedItem.Path;
                pickerWindow.DialogResult = true;
            }
        };

        listBox.MouseDoubleClick += (_, _) =>
        {
            if (listBox.SelectedItem is TrackStorageItem selectedItem)
            {
                selectedPath = selectedItem.Path;
                pickerWindow.DialogResult = true;
            }
        };

        return pickerWindow.ShowDialog() == true ? selectedPath : null;
    }

    private List<TrackStorageItem> GetBaseTracks(Func<string, bool> predicate)
    {
        var results = new List<TrackStorageItem>();
        if (!string.IsNullOrEmpty(QueuePath) && Directory.Exists(QueuePath))
        {
            results.AddRange(Directory.GetFiles(QueuePath)
                .Where(predicate)
                .Select(path => new TrackStorageItem(path, "В очереди")));
        }

        if (!string.IsNullOrEmpty(ProcessedPath) && Directory.Exists(ProcessedPath))
        {
            results.AddRange(Directory.GetFiles(ProcessedPath)
                .Where(predicate)
                .Select(path => new TrackStorageItem(path, "Обработанные")));
        }

        return results.OrderBy(item => item.Source).ThenBy(item => item.FileName).ToList();
    }

    private static bool IsVideoFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return VideoExtensions.Contains(extension);
    }

    private static bool IsAudioFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return AudioExtensions.Contains(extension);
    }

    private void ListBoxItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null and not ListBoxItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        if (current is ListBoxItem listBoxItem)
        {
            listBoxItem.IsSelected = true;
        }
    }

    private async void SaveProjectVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
        {
            return;
        }

        if (!TryRenameCurrentProject())
        {
            return;
        }

        if (!ValidateProjectForRender(_currentProject))
        {
            return;
        }

        _currentProject.OutputFormat = GetSelectedOutputFormat();
        _currentProject.Width = ParseIntOrDefault(ProjectWidthTextBox.Text, 1920);
        _currentProject.Height = ParseIntOrDefault(ProjectHeightTextBox.Text, 1080);
        _currentProject.Fps = DefaultProjectFps;
        _currentProject.TransitionSeconds = DefaultTransitionSeconds;
        _currentProject.SlideDurationSeconds = ParseDoubleOrDefault(SlideDurationTextBox.Text, 3);
        _currentProject.MaxClipDurationSeconds = DefaultMaxClipDurationSeconds;
        _currentProject.UseVideoAudio = UseVideoAudioCheckBox.IsChecked == true;
        _currentProject.Items = _timelineItems.ToList();
        SyncProjectAudioState(_currentProject);

        SaveProjectToFile(_currentProject);
        SaveProjectButton.IsEnabled = false;
        SaveProgressPanel.Visibility = Visibility.Visible;
        try
        {
            await RenderProjectAsync(_currentProject);
        }
        finally
        {
            SaveProgressPanel.Visibility = Visibility.Collapsed;
            SaveProjectButton.IsEnabled = true;
        }
    }

    private sealed class TrackStorageItem(string path, string source)
    {
        public string Path { get; } = path;
        public string Source { get; } = source;
        public string FileName => System.IO.Path.GetFileName(Path);
        public string DisplayName => $"[{Source}] {FileName}";
    }

    private sealed class TimelineListEntry
    {
        private TimelineListEntry(ProjectMediaItem? mediaItem, ProjectAudioItem? audioItem, bool isAudioFromVideo, string? displayName = null)
        {
            MediaItem = mediaItem;
            AudioItem = audioItem;
            IsAudioFromVideo = isAudioFromVideo;
            _displayName = displayName;
        }

        private readonly string? _displayName;
        public ProjectMediaItem? MediaItem { get; }
        public ProjectAudioItem? AudioItem { get; }
        public bool IsAudioFromVideo { get; }

        public string DisplayName => MediaItem?.DisplayName ?? AudioItem?.DisplayName ?? _displayName ?? string.Empty;

        public static TimelineListEntry CreateAudio(ProjectAudioItem item) =>
            new(null, item, false);

        public static TimelineListEntry CreateAudioFromVideo() =>
            new(null, null, true, "[Аудио] Аудио берется из видео дорожки");

        public TimelineListEntry(ProjectMediaItem item) : this(item, null, false)
        {
        }
    }

    private void BackToProjectList_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject != null)
        {
            if (!TryRenameCurrentProject())
            {
                return;
            }
            SaveProjectToFile(_currentProject);
        }

        ShowProjectList(_currentProjectType);
    }

    private void BackToStart_Click(object sender, RoutedEventArgs e)
    {
        HideAllScreens();
        StartScreen.Visibility = Visibility.Visible;
    }

    private bool ValidateProjectForRender(ProjectData project)
    {
        if (project.Items.Count == 0)
        {
            MessageBox.Show("Добавьте элементы в таймлайн.");
            return false;
        }

        if (project.Items.Any(item => !File.Exists(item.Path)))
        {
            MessageBox.Show("Некоторые файлы в таймлайне не найдены.");
            return false;
        }

        EnsureAudioItemsMigrated(project);
        if (project.AudioItems.Any(item => !File.Exists(item.Path)))
        {
            MessageBox.Show("Один или несколько аудиофайлов не найдены.");
            return false;
        }

        var outputPath = Path.Combine(ProcessedPath, $"{project.Name}.{GetSelectedOutputFormat()}");
        if (File.Exists(outputPath))
        {
            MessageBox.Show("Файл с таким названием уже существует в обработанных.");
            return false;
        }

        return true;
    }

    private string GetSelectedOutputFormat()
    {
        if (ProjectOutputFormatComboBox.SelectedItem is ComboBoxItem item)
        {
            return item.Content.ToString() ?? "mp4";
        }

        return "mp4";
    }

    private void SaveProjectToFile(ProjectData project)
    {
        var projectPath = GetProjectFilePath(project.Type, project.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(projectPath, json);
    }

    private void RenameProject_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
        {
            return;
        }

        if (TryRenameCurrentProject())
        {
            SaveProjectToFile(_currentProject);
            MessageBox.Show("Название проекта обновлено.", "Успешно", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private bool TryRenameCurrentProject()
    {
        if (_currentProject == null)
        {
            return false;
        }

        var newName = ProjectNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show("Введите название проекта.");
            return false;
        }

        if (!IsValidProjectName(newName))
        {
            MessageBox.Show("Название проекта содержит недопустимые символы.");
            return false;
        }

        if (string.Equals(_currentProject.Name, newName, StringComparison.Ordinal))
        {
            return true;
        }

        var oldProjectPath = GetProjectFilePath(_currentProject.Type, _currentProject.Name);
        var newProjectPath = GetProjectFilePath(_currentProject.Type, newName);
        if (File.Exists(newProjectPath))
        {
            MessageBox.Show("Проект с таким названием уже существует.");
            return false;
        }

        _currentProject.Name = newName;

        if (File.Exists(oldProjectPath))
        {
            File.Move(oldProjectPath, newProjectPath);
        }

        return true;
    }

    private string GetProjectsPath(ProjectType type)
    {
        var folder = type == ProjectType.VideoCollage ? "VideoCollage" : "SlideShow";
        return Path.Combine(ProjectsRootPath, folder);
    }

    private string GetProjectFilePath(ProjectType type, string name)
    {
        return Path.Combine(GetProjectsPath(type), $"{name}.json");
    }

    private static bool IsValidProjectName(string name)
    {
        return name.All(ch => !Path.GetInvalidFileNameChars().Contains(ch));
    }

    private static int ParseIntOrDefault(string? value, int defaultValue)
    {
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static double ParseDoubleOrDefault(string? value, double defaultValue)
    {
        return double.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}

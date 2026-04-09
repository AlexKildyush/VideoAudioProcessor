using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using VideoAudioProcessor.Services;

namespace VideoAudioProcessor.View;

public partial class MainWindow : Window
{
    private const int DefaultProjectFps = 30;
    private const double DefaultTransitionSeconds = 1;
    private const double DefaultMaxClipDurationSeconds = 0;
    private ProjectType _currentProjectType;
    private ProjectData? _currentProject;
    private readonly ObservableCollection<ProjectMediaItem> _timelineItems = new();
    private readonly ObservableCollection<ProjectAudioItem> _audioTimelineItems = new();
    private readonly ObservableCollection<ProjectSubtitleItem> _subtitleTimelineItems = new();
    private readonly ObservableCollection<TimelineListEntry> _timelineListItems = new();

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
        ProjectListTitle.Text = type == ProjectType.SlideShow ? "Проекты слайдшоу" : "Проекты медиаколлажей";
        RefreshProjectList();
        ShowScreen(ViewModel.AppScreen.Projects);
    }

    private void RefreshProjectList()
    {
        ProjectsListBox.ItemsSource = CreateStorageService().ListProjectNames(_currentProjectType);
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

        var storage = CreateStorageService();
        var projectPath = storage.GetProjectFilePath(_currentProjectType, sanitizedName);
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

    private void ProjectsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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

        try
        {
            var project = CreateStorageService().LoadProject(_currentProjectType, projectName);
            OpenProjectEditor(project);
        }
        catch (FileNotFoundException)
        {
            MessageBox.Show("Файл проекта не найден.");
            RefreshProjectList();
        }
        catch (Exception)
        {
            MessageBox.Show("Не удалось загрузить проект.");
        }
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

        CreateStorageService().DeleteProject(_currentProjectType, projectName);
        RefreshProjectList();
    }

    private void EnsureAudioItemsMigrated(ProjectData project)
    {
        CreateProjectRenderService().EnsureAudioItemsMigrated(project);
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

    private void SyncProjectSubtitleState(ProjectData project)
    {
        project.SubtitleItems = _subtitleTimelineItems
            .OrderBy(item => item.StartSeconds)
            .ToList();
    }

    private void UpdateSelectedAudioText(ProjectData project)
    {
        if (project.UseVideoAudio && _timelineItems.Any(item => item.Kind == ProjectMediaKind.Video))
        {
            SelectedAudioText.Text = "Аудио берется из видео дорожки";
            return;
        }

        else if (_audioTimelineItems.Count == 0)
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
        project.SubtitleItems ??= [];

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

        _subtitleTimelineItems.Clear();
        foreach (var item in project.SubtitleItems.OrderBy(item => item.StartSeconds))
        {
            _subtitleTimelineItems.Add(item);
        }

        TimelineItemsListBox.ItemsSource = _timelineListItems;
        ProjectEditorTitle.Text = project.Type == ProjectType.SlideShow ? "Форма редактирования слайдшоу" : "Форма редактирования медиаколлажа";
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
        ClearSubtitleEditor();

        UseVideoAudioCheckBox.Visibility = Visibility.Visible;
        SlideDurationLabel.Visibility = project.Type == ProjectType.SlideShow ? Visibility.Visible : Visibility.Collapsed;
        SlideDurationTextBox.Visibility = project.Type == ProjectType.SlideShow ? Visibility.Visible : Visibility.Collapsed;

        RefreshTimelineList();
        RefreshTimelinePreview();

        ShowScreen(ViewModel.AppScreen.ProjectEditor);
    }

    private async void AddVideoFromBase_Click(object sender, RoutedEventArgs e)
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

        await AddTimelineItemAsync(selected, ProjectMediaKind.Video);
    }

    private async void AddVideoFromComputer_Click(object sender, RoutedEventArgs e)
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

        await AddTimelineItemAsync(openFileDialog.FileName, ProjectMediaKind.Video);
    }

    private async void AddImageFromComputer_Click(object sender, RoutedEventArgs e)
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

        await AddTimelineItemAsync(openFileDialog.FileName, ProjectMediaKind.Image);
    }

    private async Task AddTimelineItemAsync(string path, ProjectMediaKind kind)
    {
        if (_currentProject == null)
        {
            return;
        }

        double? duration;
        if (kind == ProjectMediaKind.Image)
        {
            duration = PromptDurationSeconds(_currentProject.Type == ProjectType.SlideShow ? _currentProject.SlideDurationSeconds : 3);
        }
        else
        {
            duration = 0;
            await RunWithWaitDialogAsync("Добавление в проект", "Подготавливаем медиа для таймлайна...", async () =>
            {
                duration = await Task.Run(() => GetTrimmedDuration(path, _currentProject.MaxClipDurationSeconds));
            });
        }

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
            else if (selected.SubtitleItem != null)
            {
                _subtitleTimelineItems.Remove(selected.SubtitleItem);
                SyncProjectSubtitleState(_currentProject);
                ClearSubtitleEditor();
            }

            _currentProject.Items = _timelineItems.ToList();
            SyncProjectAudioState(_currentProject);
            SyncProjectSubtitleState(_currentProject);
            SaveProjectToFile(_currentProject);
            RefreshTimelineList();
            RefreshTimelinePreview();
            UpdateSelectedAudioText(_currentProject);
        }
    }

    private async void SelectAudioFromBase_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectFromTrackStorage("Выберите аудио из треков", IsAudioFile);
        if (string.IsNullOrWhiteSpace(selected) || _currentProject == null)
        {
            return;
        }

        await AddAudioTimelineItemAsync(selected);
    }

    private async void SelectAudioFromComputer_Click(object sender, RoutedEventArgs e)
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

        await AddAudioTimelineItemAsync(openFileDialog.FileName);
    }

    private async Task AddAudioTimelineItemAsync(string path)
    {
        if (_currentProject == null)
        {
            return;
        }

        var duration = 0d;
        await RunWithWaitDialogAsync("Р”РѕР±Р°РІР»РµРЅРёРµ Р°СѓРґРёРѕ", "РџРѕРґРіРѕС‚Р°РІР»РёРІР°РµРј Р°СѓРґРёРѕ РґР»СЏ РїСЂРѕРµРєС‚Р°...", async () =>
        {
            duration = await Task.Run(() => GetMediaDuration(path));
        });

        _audioTimelineItems.Add(new ProjectAudioItem
        {
            Path = path,
            DurationSeconds = duration
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

    private void AddOrUpdateSubtitle_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null)
        {
            return;
        }

        var subtitle = BuildSubtitleFromEditor();
        if (subtitle == null)
        {
            return;
        }

        if (GetSelectedSubtitleItem() is { } selected)
        {
            selected.Text = subtitle.Text;
            selected.StartSeconds = subtitle.StartSeconds;
            selected.EndSeconds = subtitle.EndSeconds;
            selected.FadeSeconds = subtitle.FadeSeconds;
            selected.FontSize = subtitle.FontSize;
            selected.ColorHex = subtitle.ColorHex;
        }
        else
        {
            _subtitleTimelineItems.Add(subtitle);
        }

        SyncProjectSubtitleState(_currentProject);
        SaveProjectToFile(_currentProject);
        RefreshTimelineList();
        ClearSubtitleEditor();
        RefreshTimelinePreview();
    }

    private void RemoveSubtitle_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProject == null || GetSelectedSubtitleItem() is not { } item)
        {
            return;
        }

        _subtitleTimelineItems.Remove(item);
        SyncProjectSubtitleState(_currentProject);
        SaveProjectToFile(_currentProject);
        RefreshTimelineList();
        ClearSubtitleEditor();
        RefreshTimelinePreview();
    }

    private void ClearSubtitleEditor_Click(object sender, RoutedEventArgs e)
    {
        ClearSubtitleEditor();
    }

    private void ClearSubtitleEditor()
    {
        SubtitleTextBox.Text = string.Empty;
        SubtitleStartTextBox.Text = string.Empty;
        SubtitleEndTextBox.Text = string.Empty;
        SubtitleFadeTextBox.Text = "0.2";
        SubtitleColorTextBox.Text = "#FFFFFF";
        SubtitleSizeTextBox.Text = "42";
        if (TimelineItemsListBox.SelectedItem is TimelineListEntry { SubtitleItem: not null })
        {
            TimelineItemsListBox.SelectedItem = null;
        }
        AddSubtitleButton.Content = "Добавить субтитр";
    }

    private ProjectSubtitleItem? BuildSubtitleFromEditor()
    {
        var text = SubtitleTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show("Введите текст субтитра.");
            return null;
        }

        var startSeconds = ParseDoubleOrDefault(SubtitleStartTextBox.Text, -1);
        var endSeconds = ParseDoubleOrDefault(SubtitleEndTextBox.Text, -1);
        var fadeSeconds = Math.Max(0, ParseDoubleOrDefault(SubtitleFadeTextBox.Text, 0.2));
        var fontSize = Math.Max(12, ParseIntOrDefault(SubtitleSizeTextBox.Text, 42));
        var colorHex = NormalizeSubtitleColor(SubtitleColorTextBox.Text);

        if (startSeconds < 0 || endSeconds <= startSeconds)
        {
            MessageBox.Show("Укажите корректный интервал субтитра.");
            return null;
        }

        return new ProjectSubtitleItem
        {
            Text = text,
            StartSeconds = startSeconds,
            EndSeconds = endSeconds,
            FadeSeconds = fadeSeconds,
            FontSize = fontSize,
            ColorHex = colorHex
        };
    }

    private void RefreshTimelineList()
    {
        _timelineListItems.Clear();
        foreach (var item in _timelineItems)
        {
            _timelineListItems.Add(new TimelineListEntry(item));
        }

        foreach (var subtitleItem in _subtitleTimelineItems.OrderBy(item => item.StartSeconds))
        {
            _timelineListItems.Add(TimelineListEntry.CreateSubtitle(subtitleItem));
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

    private void TimelineItemsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TimelineItemsListBox.SelectedItem is not TimelineListEntry { SubtitleItem: { } item })
        {
            AddSubtitleButton.Content = "Добавить субтитр";
            return;
        }

        SubtitleTextBox.Text = item.Text;
        SubtitleStartTextBox.Text = item.StartSeconds.ToString("0.##");
        SubtitleEndTextBox.Text = item.EndSeconds.ToString("0.##");
        SubtitleFadeTextBox.Text = item.FadeSeconds.ToString("0.##");
        SubtitleColorTextBox.Text = item.ColorHex;
        SubtitleSizeTextBox.Text = item.FontSize.ToString();
        AddSubtitleButton.Content = "Обновить субтитр";
    }

    private ProjectSubtitleItem? GetSelectedSubtitleItem()
    {
        return TimelineItemsListBox.SelectedItem is TimelineListEntry { SubtitleItem: { } item }
            ? item
            : null;
    }

    private void RefreshTimelinePreview()
    {
        VideoTimelinePanel.Children.Clear();
        AudioTimelinePanel.Children.Clear();
        SubtitleTimelinePanel.Children.Clear();

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
                Background = (Brush)new BrushConverter().ConvertFromString(color)!,
                Child = new TextBlock
                {
                    Text = $"{Path.GetFileName(item.Path)} ({item.DurationSeconds:0.#}с)",
                    Foreground = Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                ToolTip = item.DisplayName
            };

            VideoTimelinePanel.Children.Add(block);
        }

        var usesVideoAudio = _currentProject?.UseVideoAudio == true && _timelineItems.Any(item => item.Kind == ProjectMediaKind.Video);
        if (usesVideoAudio)
        {
            var videoAudioBlock = new Border
            {
                Width = Math.Max(120, totalDuration * 24),
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                CornerRadius = new CornerRadius(3),
                Background = (Brush)new BrushConverter().ConvertFromString("#FF2E7D32")!,
                Child = new TextBlock
                {
                    Text = "Аудио из видео дорожки",
                    Foreground = Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            AudioTimelinePanel.Children.Add(videoAudioBlock);
        }

        if (!usesVideoAudio && _audioTimelineItems.Count == 0)
        {
            var emptyAudioBlock = new Border
            {
                Width = Math.Max(120, totalDuration * 24),
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                CornerRadius = new CornerRadius(3),
                Background = (Brush)new BrushConverter().ConvertFromString("#FF424242")!,
                Child = new TextBlock
                {
                    Text = "Без аудио",
                    Foreground = Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            AudioTimelinePanel.Children.Add(emptyAudioBlock);
        }

        else
        {
            foreach (var item in _audioTimelineItems)
            {
                var audioDuration = item.DurationSeconds > 0 ? item.DurationSeconds : GetMediaDuration(item.Path);
                var audioBlock = new Border
                {
                    Width = Math.Max(120, audioDuration * 24),
                    Height = 28,
                    Margin = new Thickness(3, 0, 3, 0),
                    CornerRadius = new CornerRadius(3),
                    Background = (Brush)new BrushConverter().ConvertFromString("#FF2E7D32")!,
                    Child = new TextBlock
                    {
                        Text = $"{Path.GetFileName(item.Path)} ({audioDuration:0.#}с)",
                        Foreground = Brushes.White,
                        Margin = new Thickness(6, 4, 6, 4),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    ToolTip = item.DisplayName
                };

                AudioTimelinePanel.Children.Add(audioBlock);
            }
        }

        if (_subtitleTimelineItems.Count == 0)
        {
            var emptySubtitleBlock = new Border
            {
                Width = Math.Max(120, totalDuration * 24),
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                CornerRadius = new CornerRadius(3),
                Background = (Brush)new BrushConverter().ConvertFromString("#FF424242")!,
                Child = new TextBlock
                {
                    Text = "Без субтитров",
                    Foreground = Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            SubtitleTimelinePanel.Children.Add(emptySubtitleBlock);
            return;
        }

        foreach (var item in _subtitleTimelineItems.OrderBy(x => x.StartSeconds))
        {
            var subtitleDuration = Math.Max(0.5, item.EndSeconds - item.StartSeconds);
            var subtitleBlock = new Border
            {
                Width = Math.Max(80, subtitleDuration * 24),
                Height = 28,
                Margin = new Thickness(3, 0, 3, 0),
                CornerRadius = new CornerRadius(3),
                Background = (Brush)new BrushConverter().ConvertFromString("#FFB76E00")!,
                Child = new TextBlock
                {
                    Text = $"{item.Text} ({item.StartSeconds:0.#}-{item.EndSeconds:0.#}с)",
                    Foreground = Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                ToolTip = item.DisplayName
            };

            SubtitleTimelinePanel.Children.Add(subtitleBlock);
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

        var baseTracks = CreateStorageService().GetBaseTracks(predicate);
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
        SyncProjectSubtitleState(_currentProject);

        try
        {
            CreateProjectRenderService().ValidateProjectForRender(_currentProject, _currentProject.OutputFormat);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
            return;
        }

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

    private sealed class TimelineListEntry
    {
        private TimelineListEntry(ProjectMediaItem? mediaItem, ProjectAudioItem? audioItem, ProjectSubtitleItem? subtitleItem, bool isAudioFromVideo, string? displayName = null)
        {
            MediaItem = mediaItem;
            AudioItem = audioItem;
            SubtitleItem = subtitleItem;
            IsAudioFromVideo = isAudioFromVideo;
            _displayName = displayName;
        }

        private TimelineListEntry(ProjectMediaItem? mediaItem, ProjectAudioItem? audioItem, bool isAudioFromVideo, string? displayName = null)
            : this(mediaItem, audioItem, null, isAudioFromVideo, displayName)
        {
        }

        private readonly string? _displayName;
        public ProjectMediaItem? MediaItem { get; }
        public ProjectAudioItem? AudioItem { get; }
        public ProjectSubtitleItem? SubtitleItem { get; }
        public bool IsAudioFromVideo { get; }

        public string DisplayName => MediaItem?.DisplayName ?? AudioItem?.DisplayName ?? SubtitleItem?.DisplayName ?? _displayName ?? string.Empty;

        public static TimelineListEntry CreateAudio(ProjectAudioItem item) =>
            new(null, item, null, false);

        public static TimelineListEntry CreateSubtitle(ProjectSubtitleItem item) =>
            new(null, null, item, false);

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
        ShowInformationScreen();
    }

    private bool ValidateProjectForRender(ProjectData project)
    {
        try
        {
            CreateProjectRenderService().ValidateProjectForRender(project, GetSelectedOutputFormat());
            return true;
        }
        catch
        {
            return false;
        }
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
        CreateStorageService().SaveProject(project);
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

        var oldName = _currentProject.Name;
        try
        {
            CreateStorageService().RenameProject(_currentProject.Type, oldName, newName);
            _currentProject.Name = newName;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message);
            return false;
        }
    }

    private string GetProjectsPath(ProjectType type)
    {
        return CreateStorageService().GetProjectsPath(type);
    }

    private string GetProjectFilePath(ProjectType type, string name)
    {
        return CreateStorageService().GetProjectFilePath(type, name);
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

    private static string NormalizeSubtitleColor(string? input)
    {
        var value = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "#FFFFFF";
        }

        if (!value.StartsWith("#"))
        {
            value = $"#{value}";
        }

        return value.Length == 7 ? value.ToUpperInvariant() : "#FFFFFF";
    }
}

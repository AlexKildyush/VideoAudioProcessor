using System.IO;
using System.Linq;
using System.Windows;

namespace VideoAudioProcessor;

public partial class MainWindow : Window
{
    private async Task RenderProjectAsync(ProjectData project)
    {
        try
        {
            var renderService = CreateProjectRenderService();
            ProcessingRequest? request = null;

            await RunWithWaitDialogAsync("Рендер", "Подготавливаем проект...", async () =>
            {
                request = await Task.Run(() => renderService.BuildRenderRequest(project));
            });

            await RunWithWaitDialogAsync("Рендер", "Выполняется ffmpeg...", async () =>
            {
                await CreateMediaProcessingService().ExecuteProcessingAsync(request!);
            });

            foreach (var path in request!.FilesToDeleteOnSuccess.Where(File.Exists))
            {
                File.Delete(path);
            }

            foreach (var path in request.TempFilesToDelete.Where(File.Exists))
            {
                File.Delete(path);
            }

            MessageBox.Show("Проект успешно сохранен в обработанные!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);

            RefreshProjectList();
            ShowProjectList(project.Type);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при сохранении проекта: {ex.Message}");
        }
    }

    private async void AddProjectRenderToQueue_Click(object sender, RoutedEventArgs e)
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
            SaveProjectToFile(_currentProject);

            ProcessingRequest? request = null;
            await RunWithWaitDialogAsync("Очередь", "Подготавливаем проект для очереди...", async () =>
            {
                request = await Task.Run(() => CreateProjectRenderService().BuildRenderRequest(_currentProject, deleteProjectFileAfterRender: false));
            });

            AddProcessingJob(request!, $"Рендер проекта {Path.GetFileNameWithoutExtension(request!.OutputPath)}");
            MessageBox.Show("Проект добавлен в очередь.", "Очередь", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private (string Arguments, List<string> TempFiles) BuildVideoCollageArguments(ProjectData project, string outputPath)
    {
        return CreateProjectRenderService().BuildVideoCollageArguments(project, outputPath);
    }

    private static int NormalizeEvenDimension(int value, int defaultValue)
    {
        return Services.ProjectRenderService.NormalizeEvenDimension(value, defaultValue);
    }

    private (string Arguments, List<string> TempFiles) BuildSlideShowArguments(ProjectData project, string outputPath)
    {
        return CreateProjectRenderService().BuildSlideShowArguments(project, outputPath);
    }

    private bool HasAudioStream(string path)
    {
        return CreateMediaProbeService().HasAudioStream(path);
    }

    private double GetMediaDuration(string path)
    {
        return CreateMediaProbeService().GetMediaDuration(path);
    }

    private double GetTrimmedDuration(string path, double maxDuration)
    {
        return CreateMediaProbeService().GetTrimmedDuration(path, maxDuration);
    }
}

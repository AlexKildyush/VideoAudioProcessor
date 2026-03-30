using System.Windows;

namespace VideoAudioProcessor;

public partial class MainWindow : Window
{
    private async Task RenderProjectAsync(ProjectData project)
    {
        try
        {
            var renderService = CreateProjectRenderService();
            await renderService.RenderProjectAsync(project);

            MessageBox.Show("Проект успешно сохранен в обработанные!", "Успех", MessageBoxButton.OK,
                MessageBoxImage.Information);

            RefreshProjectList();
            ShowProjectList(project.Type);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при сохранении проекта: {ex.Message}");
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

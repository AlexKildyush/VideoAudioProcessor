using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualBasic;

namespace VideoAudioProcessor;

public partial class MainWindow
{
    private readonly ObservableCollection<ProcessingJob> _processingJobs = new();

    private void InitializeBatchQueue()
    {
        BatchJobsListBox.ItemsSource = _processingJobs;
    }

    private void ShowJobs_Click(object sender, RoutedEventArgs e)
    {
        HideAllScreens();
        BatchScreen.Visibility = Visibility.Visible;
        RefreshBatchSummary();
    }

    private void RefreshBatchSummary()
    {
        var pending = _processingJobs.Count(job => job.Status == BatchJobStatus.Pending);
        var running = _processingJobs.Count(job => job.Status == BatchJobStatus.Running);
        var completed = _processingJobs.Count(job => job.Status == BatchJobStatus.Completed);
        var failed = _processingJobs.Count(job => job.Status == BatchJobStatus.Failed);
        BatchSummaryText.Text = $"Всего: {_processingJobs.Count} | Ожидают: {pending} | Выполняются: {running} | Готово: {completed} | Ошибки: {failed}";
    }

    private void AddProcessingJob(ProcessingRequest request, string jobName)
    {
        _processingJobs.Add(new ProcessingJob
        {
            Name = jobName,
            InputPaths = request.InputPaths.ToList(),
            OutputPath = request.OutputPath,
            Arguments = request.Arguments,
            Summary = request.Summary,
            IsMerge = request.IsMerge,
            Status = BatchJobStatus.Pending
        });

        RefreshBatchSummary();
    }

    private async void RunAllJobs_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in _processingJobs.Where(j => j.Status == BatchJobStatus.Pending || j.Status == BatchJobStatus.Failed).ToList())
        {
            await RunJobAsync(job);
        }

        RefreshBatchSummary();
    }

    private async void RunSelectedJob_Click(object sender, RoutedEventArgs e)
    {
        if (BatchJobsListBox.SelectedItem is not ProcessingJob job)
        {
            return;
        }

        await RunJobAsync(job);
        RefreshBatchSummary();
    }

    private void RemoveSelectedJob_Click(object sender, RoutedEventArgs e)
    {
        if (BatchJobsListBox.SelectedItem is not ProcessingJob job)
        {
            return;
        }

        _processingJobs.Remove(job);
        RefreshBatchSummary();
    }

    private void ClearCompletedJobs_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in _processingJobs.Where(j => j.Status == BatchJobStatus.Completed).ToList())
        {
            _processingJobs.Remove(job);
        }

        RefreshBatchSummary();
    }

    private async Task RunJobAsync(ProcessingJob job)
    {
        if (job.Status == BatchJobStatus.Running)
        {
            return;
        }

        job.Status = BatchJobStatus.Running;
        job.LastError = null;
        BatchJobsListBox.Items.Refresh();
        RefreshBatchSummary();

        try
        {
            if (File.Exists(job.OutputPath))
            {
                throw new InvalidOperationException("Выходной файл уже существует.");
            }

            var (exitCode, errorOutput) = await RunFfmpegAsync(job.Arguments);
            if (exitCode != 0)
            {
                job.Status = BatchJobStatus.Failed;
                job.LastError = errorOutput;
                MessageBox.Show($"Ошибка задачи '{job.Name}':\n{errorOutput}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                job.Status = BatchJobStatus.Completed;
                RefreshProcessedList();
            }
        }
        catch (Exception ex)
        {
            job.Status = BatchJobStatus.Failed;
            job.LastError = ex.Message;
            MessageBox.Show($"Ошибка задачи '{job.Name}': {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BatchJobsListBox.Items.Refresh();
            RefreshBatchSummary();
        }
    }

    private async void LosslessMergeSelectedFiles_Click(object sender, RoutedEventArgs e)
    {
        if (FilesListBox.SelectedItems.Count < 2)
        {
            MessageBox.Show("Выберите минимум два файла в очереди для merge.");
            return;
        }

        if (string.IsNullOrWhiteSpace(RootPath))
        {
            MessageBox.Show("Сначала укажите корневую папку.");
            return;
        }

        var selectedPaths = FilesListBox.SelectedItems
            .Cast<string>()
            .Select(name => Path.Combine(QueuePath, name))
            .ToList();

        var extension = Path.GetExtension(selectedPaths[0]);
        var outputName = Interaction.InputBox("Введите имя итогового файла без расширения", "Lossless merge", $"merged_{DateTime.Now:yyyyMMdd_HHmmss}");
        if (string.IsNullOrWhiteSpace(outputName))
        {
            return;
        }

        Directory.CreateDirectory(ProcessedPath);
        var outputPath = Path.Combine(ProcessedPath, $"{outputName.Trim()}{extension}");
        if (File.Exists(outputPath))
        {
            MessageBox.Show("Файл с таким именем уже существует.");
            return;
        }

        var request = BuildLosslessMergeRequest(selectedPaths, outputPath);
        if (request == null)
        {
            return;
        }

        var result = MessageBox.Show("Добавить merge в очередь задач? Нажмите 'Нет' для немедленного запуска.", "Lossless merge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel)
        {
            return;
        }

        if (result == MessageBoxResult.Yes)
        {
            AddProcessingJob(request, "Lossless merge");
            MessageBox.Show("Задача добавлена в очередь.");
            return;
        }

        await RunWithWaitDialogAsync("Merge", "Выполняется lossless merge...", async () =>
        {
            var (exitCode, errorOutput) = await RunFfmpegAsync(request.Arguments);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(errorOutput);
            }
        });

        RefreshProcessedList();
        MessageBox.Show("Файлы успешно объединены.");
    }
}

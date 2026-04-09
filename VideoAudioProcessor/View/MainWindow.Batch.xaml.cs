using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.VisualBasic;

namespace VideoAudioProcessor.View;

public partial class MainWindow
{
    private readonly ObservableCollection<ProcessingJob> _processingJobs = new();

    private void InitializeBatchQueue()
    {
        BatchJobsListBox.ItemsSource = _processingJobs;
    }

    private void ShowJobs_Click(object sender, RoutedEventArgs e)
    {
        ShowScreen(ViewModel.AppScreen.Batch);
        RefreshBatchSummary();
    }

    private void RefreshBatchSummary()
    {
        var pending = _processingJobs.Count(job => job.Status == BatchJobStatus.Pending);
        var running = _processingJobs.Count(job => job.Status == BatchJobStatus.Running);
        var failed = _processingJobs.Count(job => job.Status == BatchJobStatus.Failed);
        BatchSummaryText.Text = $"Всего: {_processingJobs.Count} | Ожидают: {pending} | Выполняются: {running} | Ошибки: {failed}";
    }

    private void AddProcessingJob(ProcessingRequest request, string jobName)
    {
        _processingJobs.Add(CreateBatchQueueRunner().CreateJob(request, jobName));
        RefreshBatchSummary();
    }

    private async void RunAllJobs_Click(object sender, RoutedEventArgs e)
    {
        var runner = CreateBatchQueueRunner();
        foreach (var job in _processingJobs.Where(j => j.Status == BatchJobStatus.Pending || j.Status == BatchJobStatus.Failed).ToList())
        {
            await RunJobAsync(job, runner);
        }

        RefreshBatchSummary();
    }

    private async void RunSelectedJob_Click(object sender, RoutedEventArgs e)
    {
        if (BatchJobsListBox.SelectedItem is not ProcessingJob job)
        {
            return;
        }

        await RunJobAsync(job, CreateBatchQueueRunner());
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

    private async Task RunJobAsync(ProcessingJob job, Services.BatchQueueRunner runner)
    {
        if (job.Status == BatchJobStatus.Running)
        {
            return;
        }

        BatchJobsListBox.Items.Refresh();
        RefreshBatchSummary();

        await runner.RunJobAsync(job);

        if (job.Status == BatchJobStatus.Completed)
        {
            RefreshProcessedList();
            if (job.IsProjectRender)
            {
                _processingJobs.Remove(job);
            }
        }
        else if (job.Status == BatchJobStatus.Failed && !string.IsNullOrWhiteSpace(job.LastError))
        {
            MessageBox.Show($"Ошибка задачи '{job.Name}': {job.LastError}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        BatchJobsListBox.Items.Refresh();
        RefreshBatchSummary();
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

        var storage = CreateStorageService();
        storage.EnsureProcessedDirectory();
        var outputPath = Path.Combine(ProcessedPath, $"{outputName.Trim()}{extension}");
        if (File.Exists(outputPath))
        {
            MessageBox.Show("Файл с таким именем уже существует.");
            return;
        }

        ProcessingRequest request;
        try
        {
            request = BuildLosslessMergeRequest(selectedPaths, outputPath) ?? throw new InvalidOperationException("Не удалось создать merge-задачу.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
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

        try
        {
            var runner = CreateBatchQueueRunner();
            var job = runner.CreateJob(request, "Lossless merge");
            await RunJobWithProgressDialogAsync("Merge", job, async () =>
            {
                await runner.RunJobAsync(job);
            });

            if (job.Status != BatchJobStatus.Completed)
            {
                throw new InvalidOperationException(job.LastError ?? "Не удалось выполнить merge.");
            }

            RefreshProcessedList();
            MessageBox.Show("Файлы успешно объединены.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка ffmpeg: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

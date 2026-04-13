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
    }

    private void AddProcessingJob(ProcessingRequest request, string jobName)
    {
        var job = CreateBatchQueueRunner().CreateJob(request, jobName);
        _processingJobs.Add(job);
    }

    private async void RunAllJobs_Click(object sender, RoutedEventArgs e)
    {
        var runner = CreateBatchQueueRunner();
        foreach (var job in _processingJobs.Where(j => j.Status == BatchJobStatus.Pending || j.Status == BatchJobStatus.Failed).ToList())
        {
            await RunJobAsync(job, runner);
        }
    }

    private async void RunSelectedJob_Click(object sender, RoutedEventArgs e)
    {
        if (BatchJobsListBox.SelectedItem is not ProcessingJob job)
        {
            return;
        }

        await RunJobAsync(job, CreateBatchQueueRunner());
    }

    private void RemoveSelectedJob_Click(object sender, RoutedEventArgs e)
    {
        if (BatchJobsListBox.SelectedItem is not ProcessingJob job)
        {
            return;
        }

        _processingJobs.Remove(job);
    }

    private void ClearCompletedJobs_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in _processingJobs.Where(j => j.Status == BatchJobStatus.Completed).ToList())
        {
            _processingJobs.Remove(job);
        }
    }

    private async Task RunJobAsync(ProcessingJob job, Services.BatchQueueRunner runner)
    {
        if (job.Status == BatchJobStatus.Running)
        {
            return;
        }

        BatchJobsListBox.Items.Refresh();

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
            MessageBox.Show($"РћС€РёР±РєР° Р·Р°РґР°С‡Рё '{job.Name}': {job.LastError}", "РћС€РёР±РєР°", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        BatchJobsListBox.Items.Refresh();
    }

    private async void LosslessMergeSelectedFiles_Click(object sender, RoutedEventArgs e)
    {
        if (FilesListBox.SelectedItems.Count < 2)
        {
            MessageBox.Show("Р’С‹Р±РµСЂРёС‚Рµ РјРёРЅРёРјСѓРј РґРІР° С„Р°Р№Р»Р° РІ РѕС‡РµСЂРµРґРё РґР»СЏ merge.");
            return;
        }

        if (string.IsNullOrWhiteSpace(RootPath))
        {
            MessageBox.Show("РЎРЅР°С‡Р°Р»Р° СѓРєР°Р¶РёС‚Рµ РєРѕСЂРЅРµРІСѓСЋ РїР°РїРєСѓ.");
            return;
        }

        var selectedPaths = FilesListBox.SelectedItems
            .Cast<string>()
            .Select(name => Path.Combine(QueuePath, name))
            .ToList();

        var extension = Path.GetExtension(selectedPaths[0]);
        var outputName = Interaction.InputBox("Р’РІРµРґРёС‚Рµ РёРјСЏ РёС‚РѕРіРѕРІРѕРіРѕ С„Р°Р№Р»Р° Р±РµР· СЂР°СЃС€РёСЂРµРЅРёСЏ", "Lossless merge", $"merged_{DateTime.Now:yyyyMMdd_HHmmss}");
        if (string.IsNullOrWhiteSpace(outputName))
        {
            return;
        }

        var storage = CreateStorageService();
        storage.EnsureProcessedDirectory();
        var outputPath = Path.Combine(ProcessedPath, $"{outputName.Trim()}{extension}");
        if (File.Exists(outputPath))
        {
            MessageBox.Show("Р¤Р°Р№Р» СЃ С‚Р°РєРёРј РёРјРµРЅРµРј СѓР¶Рµ СЃСѓС‰РµСЃС‚РІСѓРµС‚.");
            return;
        }

        ProcessingRequest request;
        try
        {
            request = BuildLosslessMergeRequest(selectedPaths, outputPath) ?? throw new InvalidOperationException("РќРµ СѓРґР°Р»РѕСЃСЊ СЃРѕР·РґР°С‚СЊ merge-Р·Р°РґР°С‡Сѓ.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
            return;
        }

        var result = MessageBox.Show("Р”РѕР±Р°РІРёС‚СЊ merge РІ РѕС‡РµСЂРµРґСЊ Р·Р°РґР°С‡? РќР°Р¶РјРёС‚Рµ 'РќРµС‚' РґР»СЏ РЅРµРјРµРґР»РµРЅРЅРѕРіРѕ Р·Р°РїСѓСЃРєР°.", "Lossless merge", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel)
        {
            return;
        }

        if (result == MessageBoxResult.Yes)
        {
            AddProcessingJob(request, "Lossless merge");
            MessageBox.Show("Р—Р°РґР°С‡Р° РґРѕР±Р°РІР»РµРЅР° РІ РѕС‡РµСЂРµРґСЊ.");
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
                throw new InvalidOperationException(job.LastError ?? "РќРµ СѓРґР°Р»РѕСЃСЊ РІС‹РїРѕР»РЅРёС‚СЊ merge.");
            }

            RefreshProcessedList();
            MessageBox.Show("Р¤Р°Р№Р»С‹ СѓСЃРїРµС€РЅРѕ РѕР±СЉРµРґРёРЅРµРЅС‹.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"РћС€РёР±РєР° ffmpeg: {ex.Message}", "РћС€РёР±РєР°", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

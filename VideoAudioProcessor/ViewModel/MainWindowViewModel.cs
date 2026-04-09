namespace VideoAudioProcessor.ViewModel;

public sealed class MainWindowViewModel : ObservableObject
{
    private AppScreen _currentScreen = AppScreen.Information;

    public AppScreen CurrentScreen
    {
        get => _currentScreen;
        set
        {
            if (!SetProperty(ref _currentScreen, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsInformationScreenVisible));
            OnPropertyChanged(nameof(IsQueueScreenVisible));
            OnPropertyChanged(nameof(IsProcessedScreenVisible));
            OnPropertyChanged(nameof(IsProjectsScreenVisible));
            OnPropertyChanged(nameof(IsProjectEditorScreenVisible));
            OnPropertyChanged(nameof(IsBatchScreenVisible));
            OnPropertyChanged(nameof(IsProcessScreenVisible));
        }
    }

    public bool IsInformationScreenVisible => CurrentScreen == AppScreen.Information;
    public bool IsQueueScreenVisible => CurrentScreen == AppScreen.Queue;
    public bool IsProcessedScreenVisible => CurrentScreen == AppScreen.Processed;
    public bool IsProjectsScreenVisible => CurrentScreen == AppScreen.Projects;
    public bool IsProjectEditorScreenVisible => CurrentScreen == AppScreen.ProjectEditor;
    public bool IsBatchScreenVisible => CurrentScreen == AppScreen.Batch;
    public bool IsProcessScreenVisible => CurrentScreen == AppScreen.Process;
}

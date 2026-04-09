using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VideoAudioProcessor;

public enum SubtitleMode
{
    None,
    BurnIn,
    Embed
}

public enum HardwareAccelerationMode
{
    None,
    Auto,
    NvidiaNvenc,
    IntelQsv,
    AmdAmf
}

public enum BatchJobStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public sealed class ProcessingPreset
{
    public string Name { get; init; } = string.Empty;
    public string OutputFormat { get; init; } = "mp4";
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? Fps { get; init; }
    public string Description { get; init; } = string.Empty;

    public override string ToString() => Name;
}

public sealed class ProcessingRequest
{
    public required IReadOnlyList<string> InputPaths { get; init; }
    public required string OutputPath { get; init; }
    public required string Arguments { get; init; }
    public required string Summary { get; init; }
    public double? ExpectedDurationSeconds { get; init; }
    public bool IsMerge { get; init; }
    public bool IsProjectRender { get; init; }
    public List<string> TempFilesToDelete { get; init; } = [];
    public List<string> FilesToDeleteOnSuccess { get; init; } = [];
}

public sealed class ProcessingJob : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private List<string> _inputPaths = [];
    private string _outputPath = string.Empty;
    private string _arguments = string.Empty;
    private string _summary = string.Empty;
    private bool _isMerge;
    private bool _isProjectRender;
    private List<string> _tempFilesToDelete = [];
    private List<string> _filesToDeleteOnSuccess = [];
    private BatchJobStatus _status = BatchJobStatus.Pending;
    private string? _lastError;
    private double? _expectedDurationSeconds;
    private double? _progressPercent;
    private string _stageText = "Ожидает";
    private TimeSpan _elapsed = TimeSpan.Zero;
    private TimeSpan? _estimatedRemaining;
    private string? _currentSpeed;
    private TimeSpan _processedDuration = TimeSpan.Zero;

    public Guid Id { get; init; } = Guid.NewGuid();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public List<string> InputPaths
    {
        get => _inputPaths;
        set => SetProperty(ref _inputPaths, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public string Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value);
    }

    public string Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }

    public bool IsMerge
    {
        get => _isMerge;
        set => SetProperty(ref _isMerge, value);
    }

    public bool IsProjectRender
    {
        get => _isProjectRender;
        set => SetProperty(ref _isProjectRender, value);
    }

    public List<string> TempFilesToDelete
    {
        get => _tempFilesToDelete;
        set => SetProperty(ref _tempFilesToDelete, value);
    }

    public List<string> FilesToDeleteOnSuccess
    {
        get => _filesToDeleteOnSuccess;
        set => SetProperty(ref _filesToDeleteOnSuccess, value);
    }

    public BatchJobStatus Status
    {
        get => _status;
        set => SetProperty(ref _status, value, nameof(DisplayName), nameof(StatusText));
    }

    public string? LastError
    {
        get => _lastError;
        set => SetProperty(ref _lastError, value);
    }

    public double? ExpectedDurationSeconds
    {
        get => _expectedDurationSeconds;
        set => SetProperty(ref _expectedDurationSeconds, value);
    }

    public double? ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value, nameof(ProgressValue), nameof(ProgressText), nameof(TimingText));
    }

    public string StageText
    {
        get => _stageText;
        set => SetProperty(ref _stageText, value);
    }

    public TimeSpan Elapsed
    {
        get => _elapsed;
        set => SetProperty(ref _elapsed, value, nameof(TimingText));
    }

    public TimeSpan? EstimatedRemaining
    {
        get => _estimatedRemaining;
        set => SetProperty(ref _estimatedRemaining, value, nameof(TimingText));
    }

    public string? CurrentSpeed
    {
        get => _currentSpeed;
        set => SetProperty(ref _currentSpeed, value, nameof(SpeedText));
    }

    public TimeSpan ProcessedDuration
    {
        get => _processedDuration;
        set => SetProperty(ref _processedDuration, value);
    }

    public string DisplayName
    {
        get
        {
            var fileName = System.IO.Path.GetFileName(OutputPath);
            return $"[{Status}] {Name} -> {fileName}";
        }
    }

    public string StatusText => Status switch
    {
        BatchJobStatus.Pending => "Ожидает",
        BatchJobStatus.Running => "Выполняется",
        BatchJobStatus.Completed => "Завершено",
        BatchJobStatus.Failed => "Ошибка",
        _ => Status.ToString()
    };

    public double ProgressValue => ProgressPercent ?? 0;

    public string ProgressText => ProgressPercent.HasValue
        ? $"{Math.Clamp(ProgressPercent.Value, 0, 100):0.#}%"
        : "Прогресс вычисляется";

    public string TimingText
    {
        get
        {
            var elapsedText = $"Прошло: {FormatTimeSpan(Elapsed)}";
            return EstimatedRemaining.HasValue
                ? $"{elapsedText} | Осталось: {FormatTimeSpan(EstimatedRemaining.Value)}"
                : $"{elapsedText} | Осталось: вычисляется";
        }
    }

    public string SpeedText => string.IsNullOrWhiteSpace(CurrentSpeed)
        ? string.Empty
        : $"Скорость: {CurrentSpeed}";

    private static string FormatTimeSpan(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? value.ToString(@"hh\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private void SetProperty<T>(ref T field, T value, params string[] additionalPropertyNames)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged();
        foreach (var propertyName in additionalPropertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public static class ProcessingPresets
{
    public static readonly ReadOnlyCollection<ProcessingPreset> BuiltIn = new(
    [
        new ProcessingPreset
        {
            Name = "Без пресета",
            OutputFormat = "mp4",
            Description = "Текущие ручные настройки"
        },
        new ProcessingPreset
        {
            Name = "YouTube 1080p",
            OutputFormat = "mp4",
            Width = 1920,
            Height = 1080,
            Fps = 30,
            Description = "Горизонтальное видео 1080p"
        },
        new ProcessingPreset
        {
            Name = "Instagram Reels 1080x1920",
            OutputFormat = "mp4",
            Width = 1080,
            Height = 1920,
            Fps = 30,
            Description = "Вертикальный формат для Reels"
        },
        new ProcessingPreset
        {
            Name = "TikTok 1080x1920",
            OutputFormat = "mp4",
            Width = 1080,
            Height = 1920,
            Fps = 30,
            Description = "Вертикальный формат для TikTok"
        },
        new ProcessingPreset
        {
            Name = "Telegram Video 720p",
            OutputFormat = "mp4",
            Width = 1280,
            Height = 720,
            Fps = 30,
            Description = "Сжатый ролик для отправки"
        },
        new ProcessingPreset
        {
            Name = "iPhone 1080p",
            OutputFormat = "mp4",
            Width = 1920,
            Height = 1080,
            Fps = 30,
            Description = "Совместимый MP4-профиль"
        }
    ]);
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
    public bool IsMerge { get; init; }
}

public sealed class ProcessingJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<string> InputPaths { get; set; } = [];
    public string OutputPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool IsMerge { get; set; }
    public BatchJobStatus Status { get; set; } = BatchJobStatus.Pending;
    public string? LastError { get; set; }

    public string DisplayName
    {
        get
        {
            var fileName = System.IO.Path.GetFileName(OutputPath);
            return $"[{Status}] {Name} -> {fileName}";
        }
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

using System;
using System.Collections.Generic;

namespace VideoAudioProcessor;

public enum ProjectType
{
    VideoCollage,
    SlideShow
}

public enum ProjectMediaKind
{
    Video,
    Image
}

public sealed class ProjectMediaItem
{
    public string Path { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public ProjectMediaKind Kind { get; set; } = ProjectMediaKind.Video;

    public string DisplayName
    {
        get
        {
            var fileName = System.IO.Path.GetFileName(Path);
            var mediaType = Kind == ProjectMediaKind.Image ? "Фото" : "Видео";
            return DurationSeconds > 0
                ? $"[{mediaType}] {fileName} ({DurationSeconds:0.##} сек.)"
                : $"[{mediaType}] {fileName}";
        }
    }
}

public sealed class ProjectData
{
    public string Name { get; set; } = string.Empty;
    public ProjectType Type { get; set; }
    public List<ProjectMediaItem> Items { get; set; } = new();
    public string? AudioPath { get; set; }
    public List<ProjectAudioItem> AudioItems { get; set; } = new();
    public bool UseVideoAudio { get; set; } = true;
    public double AudioDurationSeconds { get; set; }
    public string OutputFormat { get; set; } = "mp4";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 30;
    public double TransitionSeconds { get; set; } = 1;
    public double SlideDurationSeconds { get; set; } = 3;
    public double MaxClipDurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ProjectAudioItem
{
    public string Path { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }

    public string DisplayName
    {
        get
        {
            var fileName = System.IO.Path.GetFileName(Path);
            return DurationSeconds > 0
                ? $"[Аудио] {fileName} ({DurationSeconds:0.##} сек.)"
                : $"[Аудио] {fileName}";
        }
    }
}

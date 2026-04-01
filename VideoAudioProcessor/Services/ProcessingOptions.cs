namespace VideoAudioProcessor.Services;

public sealed class ProcessingOptions
{
    public string RootPath { get; init; } = string.Empty;
    public string InputPath { get; init; } = string.Empty;
    public string OutputFileName { get; init; } = string.Empty;
    public string OutputFormat { get; init; } = "mp4";
    public bool UseCustomCommand { get; init; }
    public string CustomCommandTemplate { get; init; } = string.Empty;
    public string SubtitlePath { get; init; } = string.Empty;
    public SubtitleMode SubtitleMode { get; init; } = SubtitleMode.None;
    public HardwareAccelerationMode HardwareAccelerationMode { get; init; } = HardwareAccelerationMode.None;
    public bool HardwareDecodeEnabled { get; init; }
    public bool LosslessCopy { get; init; }
    public bool ExtractOpus { get; init; }
    public bool CropResizeEnabled { get; init; }
    public string CropValue { get; init; } = string.Empty;
    public string ScaleValue { get; init; } = string.Empty;
    public bool AlphaChannelEnabled { get; init; }
    public bool FpsChangeEnabled { get; init; }
    public string FpsValue { get; init; } = string.Empty;
    public bool Vp9Enabled { get; init; }
    public string Vp9CrfValue { get; init; } = "32";
    public bool TwoPassEnabled { get; init; }
    public string TwoPassBitrate { get; init; } = string.Empty;
    public bool FastPresetEnabled { get; init; }
    public bool RemoveAudio { get; init; }
    public string TrimStart { get; init; } = string.Empty;
    public string TrimEnd { get; init; } = string.Empty;
    public int OutputWidth { get; init; } = 1920;
    public int OutputHeight { get; init; } = 1080;
}

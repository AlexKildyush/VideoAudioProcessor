using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VideoAudioProcessor.Services;

public sealed class MediaProcessingService(
    TrackStorageService storage,
    FfmpegCommandRunner commandRunner)
{
    public ProcessingRequest BuildProcessingRequest(ProcessingOptions options)
    {
        ValidateProcessingOptions(options);

        storage.EnsureProcessedDirectory();
        var outputPath = storage.GetProcessedOutputPath(options.OutputFileName.Trim(), options.OutputFormat);
        if (File.Exists(outputPath))
        {
            throw new InvalidOperationException("Файл с таким названием уже существует.");
        }

        var arguments = options.UseCustomCommand
            ? BuildCustomCommand(options.CustomCommandTemplate, options.InputPath, outputPath)
            : BuildStandardArguments(options.InputPath, outputPath, options);
        if (string.IsNullOrWhiteSpace(arguments))
        {
            throw new InvalidOperationException("Не удалось сформировать команду обработки.");
        }

        return new ProcessingRequest
        {
            InputPaths = [options.InputPath],
            OutputPath = outputPath,
            Arguments = arguments,
            Summary = options.UseCustomCommand
                ? $"Custom ffmpeg: {Path.GetFileName(options.InputPath)} -> {Path.GetFileName(outputPath)}"
                : $"{Path.GetFileName(options.InputPath)} -> {Path.GetFileName(outputPath)}"
        };
    }

    public async Task ExecuteProcessingAsync(ProcessingRequest request, CancellationToken cancellationToken = default)
    {
        var (exitCode, errorOutput) = await commandRunner.RunFfmpegAsync(request.Arguments, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(errorOutput);
        }
    }

    public async Task ExecuteCustomCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        var (exitCode, errorOutput) = await commandRunner.RunFfmpegAsync(command, cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(errorOutput);
        }
    }

    public string BuildCustomCommand(string template, string inputPath, string outputPath)
    {
        return template
            .Replace("{input}", inputPath)
            .Replace("{output}", outputPath);
    }

    public ProcessingRequest BuildLosslessMergeRequest(IReadOnlyList<string> inputPaths, string outputPath)
    {
        if (inputPaths.Count < 2)
        {
            throw new InvalidOperationException("Для merge требуется минимум два файла.");
        }

        var concatListPath = Path.Combine(Path.GetTempPath(), $"vap_merge_{Guid.NewGuid():N}.txt");
        var listContent = string.Join(Environment.NewLine, inputPaths.Select(path => $"file '{path.Replace("'", "''")}'"));
        File.WriteAllText(concatListPath, listContent);

        return new ProcessingRequest
        {
            InputPaths = inputPaths,
            OutputPath = outputPath,
            Arguments = $"-y -f concat -safe 0 -i \"{concatListPath}\" -c copy \"{outputPath}\"",
            Summary = $"Merge {inputPaths.Count} файлов",
            IsMerge = true
        };
    }

    public string BuildStandardArguments(string inputPath, string outputPath, ProcessingOptions options)
    {
        var builder = new StringBuilder("-y");
        AppendHardwareDecodeArgs(builder, options);

        if (options.LosslessCopy)
        {
            if (!string.IsNullOrWhiteSpace(options.TrimStart))
            {
                builder.Append($" -ss {options.TrimStart.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(options.TrimEnd))
            {
                builder.Append($" -to {options.TrimEnd.Trim()}");
            }

            builder.Append($" -i \"{inputPath}\" -c copy \"{outputPath}\"");
            return builder.ToString();
        }

        if (!string.IsNullOrWhiteSpace(options.TrimStart))
        {
            builder.Append($" -ss {options.TrimStart.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(options.TrimEnd))
        {
            builder.Append($" -to {options.TrimEnd.Trim()}");
        }

        builder.Append($" -i \"{inputPath}\"");
        if (options.SubtitleMode == SubtitleMode.Embed)
        {
            builder.Append($" -i \"{options.SubtitlePath}\"");
        }

        string videoCodec;
        string audioCodec;
        switch (options.OutputFormat)
        {
            case "avi":
                videoCodec = "mpeg4";
                audioCodec = "libmp3lame";
                break;
            case "mkv":
                videoCodec = "libx264";
                audioCodec = "aac";
                break;
            default:
                videoCodec = "libx264";
                audioCodec = "aac";
                break;
        }

        var videoFilters = new List<string>();

        if (options.Vp9Enabled)
        {
            videoCodec = "libvpx-vp9";
        }
        else
        {
            videoCodec = GetVideoCodecForHardware(options.OutputFormat, options.HardwareAccelerationMode, videoCodec);
        }

        if (options.TwoPassEnabled)
        {
            builder.Append($" -b:v {options.TwoPassBitrate.Trim()}");
        }

        if (options.FastPresetEnabled)
        {
            builder.Append(" -preset ultrafast");
        }

        if (options.CropResizeEnabled)
        {
            videoFilters.Add($"crop={options.CropValue.Trim()}");
            videoFilters.Add($"scale={options.ScaleValue.Trim()}");
        }
        else if (options.OutputWidth > 0 && options.OutputHeight > 0)
        {
            videoFilters.Add($"scale={options.OutputWidth}:{options.OutputHeight}:force_original_aspect_ratio=decrease");
            videoFilters.Add($"pad={options.OutputWidth}:{options.OutputHeight}:(ow-iw)/2:(oh-ih)/2");
        }

        if (options.AlphaChannelEnabled)
        {
            videoFilters.Add("colorkey=0x000000:0.1:0.1");
            videoFilters.Add("format=yuva420p");
        }

        if (options.FpsChangeEnabled)
        {
            videoFilters.Add($"fps={options.FpsValue.Trim()}");
        }

        if (options.SubtitleMode == SubtitleMode.BurnIn)
        {
            videoFilters.Add($"subtitles='{EscapeFilterPath(options.SubtitlePath)}'");
        }

        if (options.ExtractOpus)
        {
            audioCodec = "libopus";
            builder.Append(" -vn");
        }
        else if (videoFilters.Count > 0)
        {
            builder.Append($" -vf \"{string.Join(',', videoFilters)}\"");
        }

        if (options.RemoveAudio)
        {
            builder.Append(" -an");
        }
        else
        {
            builder.Append($" -c:a {audioCodec}");
        }

        if (!options.ExtractOpus)
        {
            builder.Append($" -c:v {videoCodec}");
            if (options.Vp9Enabled)
            {
                builder.Append($" -crf {options.Vp9CrfValue.Trim()} -b:v 0");
            }
        }

        if (options.SubtitleMode == SubtitleMode.Embed)
        {
            builder.Append($" -c:s {GetSubtitleCodec(options.OutputFormat)} -map 0:v? -map 0:a? -map 1:0");
        }

        builder.Append($" \"{outputPath}\"");
        return builder.ToString();
    }

    public static string EscapeFilterPath(string path)
    {
        return path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
    }

    private static void ValidateProcessingOptions(ProcessingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OutputFileName))
        {
            throw new InvalidOperationException("Введите название файла.");
        }

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            throw new InvalidOperationException("Пожалуйста, сначала установите корневую папку.");
        }

        if (string.IsNullOrWhiteSpace(options.InputPath))
        {
            throw new InvalidOperationException("Сначала выберите файл для обработки.");
        }

        if (!File.Exists(options.InputPath))
        {
            throw new InvalidOperationException("Файл для обработки не найден.");
        }

        if (options.UseCustomCommand)
        {
            if (string.IsNullOrWhiteSpace(options.CustomCommandTemplate))
            {
                throw new InvalidOperationException("Введите пользовательскую команду FFmpeg.");
            }

            if (!options.CustomCommandTemplate.Contains("{input}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Пользовательская команда должна содержать шаблон {input}.");
            }

            if (!options.CustomCommandTemplate.Contains("{output}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Пользовательская команда должна содержать шаблон {output}.");
            }

            return;
        }

        if (options.SubtitleMode != SubtitleMode.None && string.IsNullOrWhiteSpace(options.SubtitlePath))
        {
            throw new InvalidOperationException("Выберите файл субтитров.");
        }

        if (options.SubtitleMode != SubtitleMode.None && !File.Exists(options.SubtitlePath))
        {
            throw new InvalidOperationException("Файл субтитров не найден.");
        }

        if (options.LosslessCopy && (options.CropResizeEnabled || options.FpsChangeEnabled || options.Vp9Enabled || options.SubtitleMode != SubtitleMode.None))
        {
            throw new InvalidOperationException("Lossless режим нельзя сочетать с фильтрами, VP9 или субтитрами.");
        }

        if (options.ExtractOpus && options.SubtitleMode != SubtitleMode.None)
        {
            throw new InvalidOperationException("Субтитры недоступны для аудио-only режима.");
        }

        if (options.SubtitleMode == SubtitleMode.Embed && options.OutputFormat == "avi")
        {
            throw new InvalidOperationException("Вложенные субтитры для AVI не поддерживаются.");
        }
    }

    private static void AppendHardwareDecodeArgs(StringBuilder builder, ProcessingOptions options)
    {
        if (!options.HardwareDecodeEnabled)
        {
            return;
        }

        switch (options.HardwareAccelerationMode)
        {
            case HardwareAccelerationMode.Auto:
                builder.Append(" -hwaccel auto");
                break;
            case HardwareAccelerationMode.NvidiaNvenc:
                builder.Append(" -hwaccel cuda");
                break;
            case HardwareAccelerationMode.IntelQsv:
                builder.Append(" -hwaccel qsv");
                break;
            case HardwareAccelerationMode.AmdAmf:
                builder.Append(" -hwaccel d3d11va");
                break;
        }
    }

    private static string GetVideoCodecForHardware(string outputFormat, HardwareAccelerationMode mode, string fallbackCodec)
    {
        if (outputFormat == "avi")
        {
            return fallbackCodec;
        }

        return mode switch
        {
            HardwareAccelerationMode.NvidiaNvenc => "h264_nvenc",
            HardwareAccelerationMode.IntelQsv => "h264_qsv",
            HardwareAccelerationMode.AmdAmf => "h264_amf",
            _ => fallbackCodec
        };
    }

    private static string GetSubtitleCodec(string outputFormat)
    {
        return outputFormat == "mp4" ? "mov_text" : "srt";
    }
}

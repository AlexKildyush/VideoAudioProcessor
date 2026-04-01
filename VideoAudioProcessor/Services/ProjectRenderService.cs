using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VideoAudioProcessor.Services;

public sealed class ProjectRenderService(
    TrackStorageService storage,
    MediaProbeService mediaProbe,
    FfmpegCommandRunner commandRunner)
{
    public void EnsureAudioItemsMigrated(ProjectData project)
    {
        project.AudioItems ??= new List<ProjectAudioItem>();
        project.SubtitleItems ??= new List<ProjectSubtitleItem>();
        if (project.AudioItems.Count == 0 && !string.IsNullOrWhiteSpace(project.AudioPath))
        {
            project.AudioItems.Add(new ProjectAudioItem
            {
                Path = project.AudioPath,
                DurationSeconds = project.AudioDurationSeconds
            });
        }
    }

    public void ValidateProjectForRender(ProjectData project, string outputFormat)
    {
        if (project.Items.Count == 0)
        {
            throw new InvalidOperationException("Добавьте элементы в таймлайн.");
        }

        if (project.Items.Any(item => !File.Exists(item.Path)))
        {
            throw new InvalidOperationException("Некоторые файлы в таймлайне не найдены.");
        }

        EnsureAudioItemsMigrated(project);
        if (project.AudioItems.Any(item => !File.Exists(item.Path)))
        {
            throw new InvalidOperationException("Один или несколько аудиофайлов не найдены.");
        }

        foreach (var item in project.SubtitleItems)
        {
            if (string.IsNullOrWhiteSpace(item.Text))
            {
                throw new InvalidOperationException("У субтитров должен быть текст.");
            }

            if (item.StartSeconds < 0 || item.EndSeconds <= item.StartSeconds)
            {
                throw new InvalidOperationException("У субтитров некорректный интервал времени.");
            }
        }

        var outputPath = storage.GetProcessedOutputPath(project.Name, outputFormat);
        if (File.Exists(outputPath))
        {
            throw new InvalidOperationException("Файл с таким названием уже существует в обработанных.");
        }
    }

    public async Task<string> RenderProjectAsync(ProjectData project, bool deleteProjectFileAfterRender = true, CancellationToken cancellationToken = default)
    {
        var request = BuildRenderRequest(project, deleteProjectFileAfterRender);

        try
        {
            var (exitCode, errorOutput) = await commandRunner.RunFfmpegAsync(request.Arguments, cancellationToken);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(errorOutput);
            }

            foreach (var path in request.FilesToDeleteOnSuccess.Where(File.Exists))
            {
                File.Delete(path);
            }

            return request.OutputPath;
        }
        finally
        {
            foreach (var tempFile in request.TempFilesToDelete)
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }

    public ProcessingRequest BuildRenderRequest(ProjectData project, bool deleteProjectFileAfterRender = true)
    {
        storage.EnsureProcessedDirectory();
        var outputPath = storage.GetProcessedOutputPath(project.Name, project.OutputFormat);
        var (arguments, tempFiles) = project.Type == ProjectType.SlideShow
            ? BuildSlideShowArguments(project, outputPath)
            : BuildVideoCollageArguments(project, outputPath);

        if (string.IsNullOrWhiteSpace(arguments))
        {
            throw new InvalidOperationException("Не удалось сформировать команду обработки.");
        }

        var filesToDeleteOnSuccess = deleteProjectFileAfterRender
            ? new List<string> { storage.GetProjectFilePath(project.Type, project.Name) }
            : new List<string>();

        return new ProcessingRequest
        {
            InputPaths = project.Items.Select(item => item.Path).Concat(project.AudioItems.Select(item => item.Path)).Distinct().ToList(),
            OutputPath = outputPath,
            Arguments = arguments,
            Summary = $"Project render: {project.Name}",
            TempFilesToDelete = tempFiles,
            FilesToDeleteOnSuccess = filesToDeleteOnSuccess
        };
    }

    public (string Arguments, List<string> TempFiles) BuildVideoCollageArguments(ProjectData project, string outputPath)
    {
        var tempFiles = new List<string>();
        var inputBuilder = new StringBuilder();
        var filterBuilder = new StringBuilder();
        var outputWidth = NormalizeEvenDimension(project.Width, 1920);
        var outputHeight = NormalizeEvenDimension(project.Height, 1080);
        var videoLabels = new List<string>();
        var timelineAudioLabels = new List<string>();
        EnsureAudioItemsMigrated(project);

        for (var i = 0; i < project.Items.Count; i++)
        {
            var item = project.Items[i];
            var duration = item.Kind == ProjectMediaKind.Image
                ? Math.Max(0.5, item.DurationSeconds)
                : mediaProbe.GetTrimmedDuration(item.Path, project.MaxClipDurationSeconds);
            item.DurationSeconds = duration;

            if (item.Kind == ProjectMediaKind.Image)
            {
                inputBuilder.Append($" -loop 1 -t {duration.ToString(CultureInfo.InvariantCulture)} -i \"{item.Path}\"");
            }
            else
            {
                inputBuilder.Append($" -i \"{item.Path}\"");
            }

            var videoLabel = $"v{i}";
            filterBuilder.Append(
                $"[{i}:v]trim=0:{duration.ToString(CultureInfo.InvariantCulture)},setpts=PTS-STARTPTS," +
                $"scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=increase," +
                $"crop={outputWidth}:{outputHeight},fps={project.Fps},format=yuv420p,setsar=1[{videoLabel}];");
            videoLabels.Add(videoLabel);

            if (project.UseVideoAudio && item.Kind == ProjectMediaKind.Video && mediaProbe.HasAudioStream(item.Path))
            {
                var audioLabel = $"a{i}";
                filterBuilder.Append(
                    $"[{i}:a]atrim=0:{duration.ToString(CultureInfo.InvariantCulture)}," +
                    $"asetpts=PTS-STARTPTS,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[{audioLabel}];");
                timelineAudioLabels.Add(audioLabel);
            }
            else if (project.UseVideoAudio)
            {
                var silentLabel = $"asil{i}";
                filterBuilder.Append(
                    $"anullsrc=r=48000:cl=stereo,atrim=0:{duration.ToString(CultureInfo.InvariantCulture)}," +
                    $"asetpts=PTS-STARTPTS,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[{silentLabel}];");
                timelineAudioLabels.Add(silentLabel);
            }
        }

        var totalDuration = project.Items.Sum(item => item.DurationSeconds);

        var concatVideoInputs = string.Join(string.Empty, videoLabels.Select(label => $"[{label}]"));
        filterBuilder.Append($"{concatVideoInputs}concat=n={videoLabels.Count}:v=1:a=0[vcat];");

        string audioMap;
        if (project.UseVideoAudio && timelineAudioLabels.Count > 0)
        {
            if (timelineAudioLabels.Count == 1)
            {
                audioMap = $"-map \"[{timelineAudioLabels[0]}]\"";
            }
            else
            {
                var concatAudioInputs = string.Join(string.Empty, timelineAudioLabels.Select(label => $"[{label}]"));
                filterBuilder.Append($"{concatAudioInputs}concat=n={timelineAudioLabels.Count}:v=0:a=1[vaudio];");
                audioMap = "-map \"[vaudio]\"";
            }
        }
        else if (project.AudioItems.Count > 0)
        {
            var baseAudioIndex = project.Items.Count;
            for (var i = 0; i < project.AudioItems.Count; i++)
            {
                inputBuilder.Append($" -i \"{project.AudioItems[i].Path}\"");
            }

            var sequenceLabels = new List<string>();
            for (var i = 0; i < project.AudioItems.Count; i++)
            {
                var item = project.AudioItems[i];
                var duration = item.DurationSeconds > 0
                    ? item.DurationSeconds
                    : mediaProbe.GetMediaDuration(item.Path);
                item.DurationSeconds = Math.Max(0.5, duration);

                var inputIndex = baseAudioIndex + i;
                var audioLabel = $"aseq{i}";

                filterBuilder.Append(
                    $"[{inputIndex}:a]atrim=0:{item.DurationSeconds.ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS," +
                    $"aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[{audioLabel}];");
                sequenceLabels.Add(audioLabel);
            }

            if (sequenceLabels.Count == 1)
            {
                audioMap = $"-map \"[{sequenceLabels[0]}]\"";
            }
            else
            {
                var concatInputs = string.Join(string.Empty, sequenceLabels.Select(label => $"[{label}]"));
                filterBuilder.Append($"{concatInputs}concat=n={sequenceLabels.Count}:v=0:a=1[audio];");
                audioMap = "-map \"[audio]\"";
            }
        }
        else
        {
            var silentAudioIndex = project.Items.Count;
            var silentDuration = Math.Max(0.5, totalDuration).ToString(CultureInfo.InvariantCulture);
            inputBuilder.Append($" -f lavfi -t {silentDuration} -i anullsrc=channel_layout=stereo:sample_rate=48000");
            filterBuilder.Append($"[{silentAudioIndex}:a]atrim=0:{silentDuration},asetpts=PTS-STARTPTS,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[silent];");
            audioMap = "-map \"[silent]\"";
        }

        var currentVideoLabel = "vcat";
        if (project.SubtitleItems.Count > 0)
        {
            var subtitlePath = CreateAssSubtitleFile(project.SubtitleItems);
            tempFiles.Add(subtitlePath);
            filterBuilder.Append($"[vcat]subtitles='{EscapeFilterPath(subtitlePath)}'[vsub];");
            currentVideoLabel = "vsub";
        }

        filterBuilder.Append($"[{currentVideoLabel}]fps={project.Fps},format=yuv420p,setsar=1[vout];");

        var filterComplex = filterBuilder.ToString().TrimEnd(';');
        var arguments = $"-y {inputBuilder} -filter_complex \"{filterComplex}\" -map \"[vout]\" {audioMap}" +
                        $" -shortest -c:v libx264 -pix_fmt yuv420p -profile:v high -level 4.0 -preset medium -crf 20 -c:a aac -b:a 320k -movflags +faststart \"{outputPath}\"";

        return (arguments, tempFiles);
    }

    public (string Arguments, List<string> TempFiles) BuildSlideShowArguments(ProjectData project, string outputPath)
    {
        foreach (var item in project.Items.Where(item => item.Kind == ProjectMediaKind.Image && item.DurationSeconds <= 0))
        {
            item.DurationSeconds = Math.Max(1, project.SlideDurationSeconds);
        }

        return BuildVideoCollageArguments(project, outputPath);
    }

    public static int NormalizeEvenDimension(int value, int defaultValue)
    {
        var normalized = value > 0 ? value : defaultValue;
        if (normalized % 2 != 0)
        {
            normalized--;
        }

        return Math.Max(2, normalized);
    }

    private static string CreateAssSubtitleFile(IReadOnlyList<ProjectSubtitleItem> items)
    {
        var path = Path.Combine(Path.GetTempPath(), $"vap_subtitles_{Guid.NewGuid():N}.ass");
        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine("PlayResX: 1920");
        builder.AppendLine("PlayResY: 1080");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name,Fontname,Fontsize,PrimaryColour,SecondaryColour,OutlineColour,BackColour,Bold,Italic,Underline,StrikeOut,ScaleX,ScaleY,Spacing,Angle,BorderStyle,Outline,Shadow,Alignment,MarginL,MarginR,MarginV,Encoding");
        builder.AppendLine("Style: Default,Arial,42,&H00FFFFFF,&H00FFFFFF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,2,1,2,40,40,40,1");
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer,Start,End,Style,Name,MarginL,MarginR,MarginV,Effect,Text");

        foreach (var item in items.OrderBy(x => x.StartSeconds))
        {
            var fadeMs = Math.Max(0, (int)Math.Round(item.FadeSeconds * 1000));
            var text = EscapeAssText(item.Text);
            var color = ToAssColor(item.ColorHex);
            var fontSize = Math.Max(12, item.FontSize);
            builder.AppendLine(
                $"Dialogue: 0,{ToAssTime(item.StartSeconds)},{ToAssTime(item.EndSeconds)},Default,,0,0,40,,{{\\an2\\c{color}\\fs{fontSize}\\fad({fadeMs},{fadeMs})}}{text}");
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        return path;
    }

    private static string EscapeFilterPath(string path)
    {
        return path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
    }

    private static string EscapeAssText(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace(Environment.NewLine, "\\N")
            .Replace("\n", "\\N")
            .Replace("\r", string.Empty);
    }

    private static string ToAssTime(double seconds)
    {
        var safeSeconds = Math.Max(0, seconds);
        var span = TimeSpan.FromSeconds(safeSeconds);
        return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds / 10:00}";
    }

    private static string ToAssColor(string colorHex)
    {
        var hex = (colorHex ?? "#FFFFFF").Trim().TrimStart('#');
        if (hex.Length != 6)
        {
            hex = "FFFFFF";
        }

        var r = hex.Substring(0, 2);
        var g = hex.Substring(2, 2);
        var b = hex.Substring(4, 2);
        return $"&H00{b}{g}{r}";
    }
}

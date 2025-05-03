using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace VideoAudioProcessor;

public partial class MainWindow : Window
{
    private async Task RenderProjectAsync(ProjectData project)
    {
        try
        {
            Directory.CreateDirectory(ProcessedPath);
            var outputPath = Path.Combine(ProcessedPath, $"{project.Name}.{project.OutputFormat}");

            var (arguments, tempFiles) = BuildVideoCollageArguments(project, outputPath);

            if (string.IsNullOrWhiteSpace(arguments))
            {
                MessageBox.Show("Не удалось сформировать команду обработки.");
                return;
            }

            var (exitCode, errorOutput) = await RunFfmpegAsync(arguments);
            if (exitCode != 0)
            {
                MessageBox.Show($"Ошибка ffmpeg:\n{errorOutput}");
                return;
            }

            var projectPath = GetProjectFilePath(project.Type, project.Name);
            if (File.Exists(projectPath))
            {
                File.Delete(projectPath);
            }

            foreach (var tempFile in tempFiles)
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }

            MessageBox.Show("Проект успешно сохранен в обработанные!", "Успех", MessageBoxButton.OK,
                MessageBoxImage.Information);

            RefreshProjectList();
            ShowProjectList(project.Type);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при сохранении проекта: {ex.Message}");
        }
    }

    private (string Arguments, List<string> TempFiles) BuildVideoCollageArguments(ProjectData project, string outputPath)
    {
        var tempFiles = new List<string>();
        var inputBuilder = new StringBuilder();
        var filterBuilder = new StringBuilder();
        var outputWidth = NormalizeEvenDimension(project.Width, 1920);
        var outputHeight = NormalizeEvenDimension(project.Height, 1080);
        var videoLabels = new List<string>();
        var timelineAudioLabels = new List<string>();
        project.AudioItems ??= new List<ProjectAudioItem>();
        if (project.AudioItems.Count == 0 && !string.IsNullOrWhiteSpace(project.AudioPath))
        {
            project.AudioItems.Add(new ProjectAudioItem
            {
                Path = project.AudioPath,
                DurationSeconds = project.AudioDurationSeconds
            });
        }

        for (var i = 0; i < project.Items.Count; i++)
        {
            var item = project.Items[i];
            var duration = item.Kind == ProjectMediaKind.Image
                ? Math.Max(0.5, item.DurationSeconds)
                : GetTrimmedDuration(item.Path, project.MaxClipDurationSeconds);
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

            if (project.UseVideoAudio && item.Kind == ProjectMediaKind.Video && HasAudioStream(item.Path))
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
        var currentVideo = "vcat";

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
                    : GetMediaDuration(item.Path);
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
            inputBuilder.Append(
                $" -f lavfi -t {Math.Max(0.5, totalDuration).ToString(CultureInfo.InvariantCulture)} -i anullsrc=channel_layout=stereo:sample_rate=48000");
            filterBuilder.Append($"[{silentAudioIndex}:a]atrim=0:{Math.Max(0.5, totalDuration).ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo[silent];");
            audioMap = "-map \"[silent]\"";
        }

        var finalizedVideoLabel = "vout";
        filterBuilder.Append($"[{currentVideo}]fps={project.Fps},format=yuv420p,setsar=1[{finalizedVideoLabel}];");

        var filterComplex = filterBuilder.ToString().TrimEnd(';');
        var arguments = $"-y {inputBuilder} -filter_complex \"{filterComplex}\" -map \"[{finalizedVideoLabel}]\" {audioMap}" +
                        $" -shortest -c:v libx264 -pix_fmt yuv420p -profile:v high -level 4.0 -preset medium -crf 20 -c:a aac -b:a 320k -movflags +faststart \"{outputPath}\"";

        return (arguments, tempFiles);
    }

    private static int NormalizeEvenDimension(int value, int defaultValue)
    {
        var normalized = value > 0 ? value : defaultValue;
        if (normalized % 2 != 0)
        {
            normalized--;
        }

        return Math.Max(2, normalized);
    }

    private (string Arguments, List<string> TempFiles) BuildSlideShowArguments(ProjectData project, string outputPath)
    {
        foreach (var item in project.Items.Where(item => item.Kind == ProjectMediaKind.Image && item.DurationSeconds <= 0))
        {
            item.DurationSeconds = Math.Max(1, project.SlideDurationSeconds);
        }

        return BuildVideoCollageArguments(project, outputPath);
    }

    private bool HasAudioStream(string path)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -select_streams a -show_entries stream=codec_type -of csv=p=0 \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    private double GetMediaDuration(string path)
    {
        var duration = GetMediaDurationInternal(path);
        return duration > 0 ? duration : 1;
    }

    private double GetTrimmedDuration(string path, double maxDuration)
    {
        var duration = GetMediaDurationInternal(path);
        if (duration <= 0)
        {
            return Math.Max(1, maxDuration);
        }

        if (maxDuration > 0 && duration > maxDuration)
        {
            return maxDuration;
        }

        return duration;
    }

    private double GetMediaDurationInternal(string path)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var duration))
            {
                return duration;
            }
        }
        catch
        {
            return 0;
        }

        return 0;
    }
}

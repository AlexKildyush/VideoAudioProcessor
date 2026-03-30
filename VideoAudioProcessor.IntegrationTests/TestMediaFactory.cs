namespace VideoAudioProcessor.IntegrationTests;

internal static class TestMediaFactory
{
    public static async Task<MediaSampleSet> CreateAsync(TestWorkspace workspace)
    {
        var sourceDir = Path.Combine(workspace.RootPath, "media-src");
        Directory.CreateDirectory(sourceDir);

        var videoWithAudio = Path.Combine(sourceDir, "video_with_audio.mp4");
        var videoWithoutAudio = Path.Combine(sourceDir, "video_without_audio.mp4");
        var mergeVideo = Path.Combine(sourceDir, "video_with_audio_2.mp4");
        var audio = Path.Combine(sourceDir, "tone.wav");
        var imageOne = Path.Combine(sourceDir, "image1.png");
        var imageTwo = Path.Combine(sourceDir, "image2.jpg");
        var subtitle = Path.Combine(sourceDir, "sample.srt");

        await RunFfmpegAsync(workspace, $"-y -f lavfi -i testsrc=duration=2:size=320x240:rate=25 -f lavfi -i sine=frequency=1000:duration=2 -c:v libx264 -pix_fmt yuv420p -c:a aac \"{videoWithAudio}\"");
        await RunFfmpegAsync(workspace, $"-y -f lavfi -i color=c=blue:size=320x240:rate=24 -t 2 -c:v libx264 -pix_fmt yuv420p -an \"{videoWithoutAudio}\"");
        await RunFfmpegAsync(workspace, $"-y -f lavfi -i testsrc2=duration=2:size=320x240:rate=25 -f lavfi -i sine=frequency=700:duration=2 -c:v libx264 -pix_fmt yuv420p -c:a aac \"{mergeVideo}\"");
        await RunFfmpegAsync(workspace, $"-y -f lavfi -i sine=frequency=440:duration=3 \"{audio}\"");
        await RunFfmpegAsync(workspace, $"-y -f lavfi -i color=c=red:size=320x240 -frames:v 1 \"{imageOne}\"");
        await RunFfmpegAsync(workspace, $"-y -f lavfi -i color=c=green:size=320x240 -frames:v 1 \"{imageTwo}\"");

        await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:01,000\nHello\n\n2\n00:00:01,000 --> 00:00:02,000\nWorld\n");

        return new MediaSampleSet(videoWithAudio, videoWithoutAudio, mergeVideo, audio, imageOne, imageTwo, subtitle);
    }

    private static async Task RunFfmpegAsync(TestWorkspace workspace, string arguments)
    {
        var (exitCode, error) = await workspace.Runner.RunFfmpegAsync(arguments);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Failed to generate test media: {error}");
        }
    }
}

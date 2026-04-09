using NUnit.Framework;
using VideoAudioProcessor.Services;

namespace VideoAudioProcessor.IntegrationTests;

[TestFixture]
internal sealed class FfmpegProgressParsingTests
{
    [Test]
    public void ParseProgressState_ParsesOutTimeSpeedAndCompletion()
    {
        var info = FfmpegCommandRunner.ParseProgressState(new Dictionary<string, string>
        {
            ["out_time_ms"] = "1500000",
            ["speed"] = "1.25x",
            ["progress"] = "continue"
        });

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.ProcessedTime.TotalSeconds, Is.EqualTo(1.5).Within(0.001));
        Assert.That(info.Speed, Is.EqualTo("1.25x"));
        Assert.That(info.IsFinished, Is.False);
        Assert.That(info.RawProgressState, Is.EqualTo("continue"));
    }

    [Test]
    public void ParseProgressState_ReturnsFinishedSnapshot_ForProgressEnd()
    {
        var info = FfmpegCommandRunner.ParseProgressState(new Dictionary<string, string>
        {
            ["out_time_us"] = "2000000",
            ["progress"] = "end"
        });

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.ProcessedTime.TotalSeconds, Is.EqualTo(2).Within(0.001));
        Assert.That(info.IsFinished, Is.True);
    }

    [Test]
    public void CalculateProgressPercent_ClampsToValidRange()
    {
        var percent = BatchQueueRunner.CalculateProgressPercent(TimeSpan.FromSeconds(12), 10);

        Assert.That(percent, Is.EqualTo(100));
    }

    [Test]
    public void CalculateEstimatedRemaining_ReturnsEta_WhenSpeedAndDurationAreKnown()
    {
        var eta = BatchQueueRunner.CalculateEstimatedRemaining(TimeSpan.FromSeconds(3), 9, "2.0x");

        Assert.That(eta, Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void CalculateEstimatedRemaining_ReturnsNull_WhenSpeedIsMissing()
    {
        var eta = BatchQueueRunner.CalculateEstimatedRemaining(TimeSpan.FromSeconds(3), 9, null);

        Assert.That(eta, Is.Null);
    }
}

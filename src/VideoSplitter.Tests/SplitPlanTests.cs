using VideoSplitter.Core.Models;
using Xunit;

namespace VideoSplitter.Tests;

public class SplitPlanTests
{
    [Fact]
    public void CalculatesPartCountBasedOnSize()
    {
        var probe = new ProbeResult(durationSeconds: 1200, fileSizeBytes: 4L * 1024 * 1024 * 1024, bitRateBitsPerSecond: 0);

        var plan = SplitPlan.FromProbe(probe, maxGigabytes: 1.5, maxMinutes: 50);

        Assert.Equal(3, plan.PartCount);
        Assert.InRange(plan.SegmentLengthSeconds, 399, 402);
    }

    [Fact]
    public void CalculatesPartCountBasedOnDuration()
    {
        var probe = new ProbeResult(durationSeconds: 4 * 3600, fileSizeBytes: 500L * 1024 * 1024, bitRateBitsPerSecond: 0);

        var plan = SplitPlan.FromProbe(probe, maxGigabytes: 10, maxMinutes: 50);

        Assert.Equal(5, plan.PartCount);
        Assert.InRange(plan.SegmentLengthSeconds, 2870, 2890);
    }

    [Fact]
    public void MinimumPartCountIsOne()
    {
        var probe = new ProbeResult(durationSeconds: 30, fileSizeBytes: 100 * 1024 * 1024, bitRateBitsPerSecond: 0);

        var plan = SplitPlan.FromProbe(probe, maxGigabytes: 10, maxMinutes: 60);

        Assert.Equal(1, plan.PartCount);
        Assert.Equal(30, plan.SegmentLengthSeconds);
    }
}

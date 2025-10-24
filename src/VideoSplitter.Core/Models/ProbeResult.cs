using System;

namespace VideoSplitter.Core.Models;

public sealed record ProbeResult(double DurationSeconds, double FileSizeBytes, double BitRateBitsPerSecond)
{
    public TimeSpan Duration => TimeSpan.FromSeconds(DurationSeconds);
    public double FileSizeGigabytes => FileSizeBytes / (1024d * 1024d * 1024d);
}

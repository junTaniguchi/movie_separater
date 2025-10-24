using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoSplitter.Core.Models;

public sealed record SplitPlan(int PartCount, double SegmentLengthSeconds, double MaximumSizeBytes)
{
    public IReadOnlyList<SplitPart> Parts => Enumerable.Range(0, PartCount)
        .Select(index => new SplitPart(index + 1, SegmentLengthSeconds))
        .ToArray();

    public static SplitPlan FromProbe(ProbeResult probe, double maxGigabytes, double maxMinutes)
    {
        if (maxGigabytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGigabytes));
        }

        if (maxMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMinutes));
        }

        var maxSizeBytes = maxGigabytes * 1024d * 1024d * 1024d;
        var maxDurationSeconds = maxMinutes * 60d;

        var sizeBasedCount = Math.Ceiling(probe.FileSizeBytes / maxSizeBytes);
        var timeBasedCount = Math.Ceiling(probe.DurationSeconds / maxDurationSeconds);
        var requiredParts = (int)Math.Max(Math.Max(sizeBasedCount, timeBasedCount), 1);

        var segmentLength = probe.DurationSeconds / requiredParts;
        if (segmentLength <= 0)
        {
            segmentLength = probe.DurationSeconds;
        }

        return new SplitPlan(requiredParts, segmentLength, maxSizeBytes);
    }
}

public sealed record SplitPart(int Index, double NominalDurationSeconds)
{
    public string FileName => $"part_{Index:00}.mp4";
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoSplitter.Core.Models;

public sealed record SplitPlan(string BaseName, int PartCount, double SegmentLengthSeconds, double MaximumSizeBytes)
{
    public IReadOnlyList<SplitPart> Parts => Enumerable.Range(0, PartCount)
        .Select(index => new SplitPart(BaseName, index + 1, SegmentLengthSeconds))
        .ToArray();

    public static SplitPlan FromProbe(ProbeResult probe, double maxGigabytes, double maxMinutes, string? baseName = null)
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

        var safeBaseName = string.IsNullOrWhiteSpace(baseName) ? "part" : baseName;
        return new SplitPlan(safeBaseName, requiredParts, segmentLength, maxSizeBytes);
    }
}

public sealed record SplitPart(string BaseName, int Index, double NominalDurationSeconds)
{
<<<<<<< HEAD
    public string FileName => $"{BaseName}_part_{Index:00}.mp4";
=======
    public string GetFileName(string baseName, string extension)
    {
        if (string.IsNullOrWhiteSpace(baseName))
        {
            throw new ArgumentException("Base name must not be null or whitespace.", nameof(baseName));
        }

        if (string.IsNullOrEmpty(extension))
        {
            extension = ".mp4";
        }
        else if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        return $"{baseName}_{Index - 1:00}{extension}";
    }
>>>>>>> 6d95b371f18f26aeb47d9d6716fa88074f426bea
}

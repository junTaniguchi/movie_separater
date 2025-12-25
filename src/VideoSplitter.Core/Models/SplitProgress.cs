namespace VideoSplitter.Core.Models;

public enum SplitPhase
{
    Preparing,
    CopySplitting,
    Reencoding,
    AudioExtract,
    Completed
}

public sealed record SplitProgress(SplitPhase Phase, int CompletedParts, int TotalParts, string? Message = null)
{
    public double Percentage => TotalParts == 0 ? 0 : (double)CompletedParts / TotalParts * 100.0;
}

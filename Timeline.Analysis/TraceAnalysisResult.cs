using Timeline.Format;

namespace Timeline.Analysis
{
    public sealed record TraceAnalysisResult
    {
        public required TraceHeaderRecord Header { get; init; }
        public TraceSessionEndRecord? SessionEnd { get; init; }
        public required IReadOnlyList<TraceTrack> Tracks { get; init; }
        public required IReadOnlyList<TraceSpan> Spans { get; init; }
        public required IReadOnlyList<TraceAnalysisDiagnostic> Diagnostics { get; init; }
    }
}

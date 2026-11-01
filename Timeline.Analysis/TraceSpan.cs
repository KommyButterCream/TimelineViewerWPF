using System.Text.Json;

namespace Timeline.Analysis
{
    public sealed record TraceSpan
    {
        public required string Id { get; init; }
        public required string TrackId { get; init; }
        public required string Name { get; init; }
        public required long StartTimeUs { get; init; }
        public required long? EndTimeUs { get; init; }
        public required TraceSpanStatus Status { get; init; }
        public long? DurationUs => EndTimeUs is long end ? end - StartTimeUs : null;
        public string? OriginalParentId { get; init; }
        public string? ParentId { get; init; }
        public IReadOnlyDictionary<string, JsonElement>? Args { get; init; }
        public required long BeginLineNumber { get; init; }
        public long? EndLineNumber { get; init; }
    }
}

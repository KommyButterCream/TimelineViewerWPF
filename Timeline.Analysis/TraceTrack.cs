namespace Timeline.Analysis
{
    public sealed record TraceTrack
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required bool IsPlaceholder { get; init; } // 임시 Track 여부, 정상인 경우 false
        public long? DefinitionLineNumber { get; init; }
        public required IReadOnlyList<TraceSpan> Spans { get; init; }
    }
}

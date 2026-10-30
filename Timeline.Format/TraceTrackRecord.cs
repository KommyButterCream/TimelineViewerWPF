namespace Timeline.Format
{
    public sealed record TraceTrackRecord : TraceRecord
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
    }
}

namespace Timeline.Format
{
    public sealed record TraceEndRecord : TraceRecord
    {
        public required string Id { get; init; }
        public required long TimeUs { get; init; }
    }
}

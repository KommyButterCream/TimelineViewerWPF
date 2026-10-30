namespace Timeline.Format
{
    public sealed record TraceSessionEndRecord : TraceRecord
    {
        public required long TimeUs { get; init; }
        public required long DroppedRecordCount { get; init; }
    }
}

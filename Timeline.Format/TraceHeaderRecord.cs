namespace Timeline.Format
{
    public sealed record class TraceHeaderRecord : TraceRecord
    {
        public string Format { get; init; } = TimelineFormat.FormatName;
        public int Version { get; init; } = TimelineFormat.CurrentVersion;
        public string TimeUnit { get; init; } = TimelineFormat.TimeUnit;
        public required DateTimeOffset OriginUtc { get; init; }
    }
}

namespace Timeline.IO
{
    public enum TraceReadMode
    {
        Strict,
        Recovery
    }
    public sealed record TraceReadOptions
    {
        public TraceReadMode Mode { get; init; } = TraceReadMode.Recovery;

        public int MaxLineLength { get; init; } = 1_048_576;
    }
}

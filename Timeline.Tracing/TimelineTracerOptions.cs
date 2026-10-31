namespace Timeline.Tracing
{
    public sealed record TimelineTracerOptions
    {
        public int QueueCapacity { get; init; } = 8192;

        public TimeSpan FlushInterval { get; init; } = TimeSpan.FromSeconds(1);
    }
}

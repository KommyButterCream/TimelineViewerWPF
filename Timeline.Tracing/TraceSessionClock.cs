using System.Diagnostics;

namespace Timeline.Tracing
{
    internal sealed class TraceSessionClock
    {
        private readonly long _originTimestamp;

        public DateTimeOffset OriginUtc { get; }

        public TraceSessionClock()
        {
            _originTimestamp = Stopwatch.GetTimestamp();
            OriginUtc = DateTimeOffset.UtcNow;
        }

        public long GetElapsedMicroseconds()
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(_originTimestamp);

            return elapsed.Ticks / TimeSpan.TicksPerMicrosecond;
        }
    }
}

using System.Text.Json;

namespace Timeline.Tracing
{
    public interface ITracer
    {
        TraceTrackHandle CreateTrack(string name);

        TraceSpanHandle Begin(
            string name,
            TraceTrackHandle track,
            TraceSpanHandle? parent = null,
            IReadOnlyDictionary<string, JsonElement>? args = null);

        void End(TraceSpanHandle span);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Text.Json;

namespace Timeline.Tracing
{
    public static class TraceExtensions
    {
        public static TraceScope BeginScope(
            this ITracer tracer, // 확장 메서드 사용
            string name,
            TraceTrackHandle track,
            TraceSpanHandle? parent = null,
            IReadOnlyDictionary<string, JsonElement>? args = null)
        {
            ArgumentNullException.ThrowIfNull(tracer);

            TraceSpanHandle handle = tracer.Begin(name, track, parent, args);

            return new TraceScope(tracer, handle);
        }
    }
}

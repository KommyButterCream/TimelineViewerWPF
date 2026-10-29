using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timeline.Format
{
    public sealed record class TraceHeaderRecord : TraceRecord
    {
        public string Format { get; init; } = TimelineFormat.FormatName;
        public int Version { get; init; } = TimelineFormat.CurrentVersion;
        public string TimeUnitP { get; init; } = TimelineFormat.TimeUnit;
        public required DateTimeOffset OriginUtc { get; init; }
    }
}

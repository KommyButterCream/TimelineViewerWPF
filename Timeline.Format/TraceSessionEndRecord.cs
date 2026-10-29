using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timeline.Format
{
    public sealed record TraceSessionEndRecord : TraceRecord
    {
        public required long TimeUs { get; init; }
        public required long DroppedRecordCount { get; init; }
    }
}

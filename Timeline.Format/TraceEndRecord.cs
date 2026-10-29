using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timeline.Format
{
    public sealed record TraceEndRecord : TraceRecord
    {
        public required string Id { get; init; }
        public required long TimeUs { get; init; }
    }
}

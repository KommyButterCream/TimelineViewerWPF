using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Timeline.Format
{
    public sealed record TraceBeginRecord : TraceRecord
    {
        public required string Id { get; init; }
        public required string TrackId { get; init; }
        public required string Name { get; init; }
        public long TimeUs { get; init; }
        public string? ParentId { get; init; }
        public IReadOnlyDictionary<string, JsonElement>? Args { get; init; }
    }
}

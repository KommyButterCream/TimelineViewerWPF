using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timeline.Analysis;
using Timeline.IO;

namespace Timeline.Viewer.Models
{
    public sealed record TraceDocument
    {
        public required string FilePath { get; init; }
        public required TraceAnalysisResult Analysis { get; init; }
        public required IReadOnlyList<TraceReadDiagnostic> ReadDiagnostics
        {
            get;
            init;
        }

        public int DiagnosticCount => ReadDiagnostics.Count + Analysis.Diagnostics.Count;
    }
}

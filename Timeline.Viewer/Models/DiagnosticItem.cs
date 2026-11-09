using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timeline.Viewer.Models
{
    public sealed record DiagnosticItem
    {
        public required DiagnosticSource Source { get; init; }
        public required string Code { get; init; }
        public required string Message { get; init; }
        public required string FilePath { get; init; }
        public long? LineNumber { get; init; }
        public long? RelatedLineNumber { get; init; }
        public string? SpanId { get; init;  }
        public string? TrackId { get; init; }

        public bool IsFatal { get; init; }

        public string SourceText => Source switch
        {
            DiagnosticSource.Reader => "읽기",
            DiagnosticSource.Analysis => "분석",
            _ => "알 수 없음"
        };

        public string LocationText => LineNumber is long line
            ? $"{line:N0}행"
            : "위치 없음";

        public string RelatedLocationText =>
            RelatedLineNumber is long line
                ? $"{line:N0}행"
                : "관련 위치 없음";

        public bool HasSourceLocation => LineNumber is > 0;
    }
}

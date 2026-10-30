using Timeline.Format;

namespace Timeline.IO
{
    public sealed class TraceReadResult
    {
        public long LineNumber { get; }
        public TraceRecord? Record { get; }
        public TraceReadDiagnostic? Diagnostic { get; }

        public bool IsSuccess => Record is not null;

        private TraceReadResult(long lineNumber, TraceRecord? record, TraceReadDiagnostic? diagnostic)
        {
            LineNumber = lineNumber;
            Record = record;
            Diagnostic = diagnostic;
        }

        public static TraceReadResult FromRecord(long lineNumber, TraceRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            if (lineNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lineNumber),
                    lineNumber,
                    "줄 번호는 1 이상이어야 합니다.");
            }

            return new TraceReadResult(lineNumber, record, null);
        }

        public static TraceReadResult FromDiagnostic(TraceReadDiagnostic diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);

            if (diagnostic.LineNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(diagnostic),
                    "진단의 줄 번호는 1 이상이어야 합니다.");
            }

            return new TraceReadResult(diagnostic.LineNumber, null, diagnostic);
        }
    }
}

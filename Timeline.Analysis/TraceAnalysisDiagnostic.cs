namespace Timeline.Analysis
{
    public enum TraceAnalysisDiagnosticCode
    {
        DuplicateTrack,     // 같은 ID의 Track 정의가 중복됨
        DuplicateBegin,     // 같은 이벤트 ID의 Begin이 중복됨
        DuplicateEnd,       // 같은 이벤트 ID의 End가 중복됨
        MissingBegin,       // End만 있고 대응하는 Begin이 없음
        MissingEnd,         // Begin만 있고 대응하는 End가 없음
        InvalidEndTime,     // 종료 시각이 시작 시각보다 빠름
        MissingTrack,       // 작업이 참조한 Track 정의가 없음
        MissingParent,      // 부모로 지정한 작업이 없음
        SelfParent,         // 자기 자신을 부모로 지정함
        ParentCycle         // 여러 작업의 부모 관계가 순환함
    }

    public sealed record TraceAnalysisDiagnostic
    {
        public required TraceAnalysisDiagnosticCode Code { get; init; }
        public required string Message { get; init; }
        public required long LineNumber { get; init; } // 문제가 되는 레코드의 위치
        public long? RelatedLineNumber { get; init; } // 비교 근거가 되는 다른 레코드의 위치
        public string? SpanId { get; init; }
        public string? TrackId { get; init; }
    }
}

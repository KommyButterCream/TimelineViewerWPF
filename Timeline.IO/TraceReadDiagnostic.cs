namespace Timeline.IO
{
    public enum TraceReadDiagnosticCode
    {
        InvalidJson,            // JSON 문법 오류
        InvalidRecord,          // 필수 필드 누락, 잘못된 ID·시간, 알 수 없는 레코드 종류 등
        LineTooLong,            // 설정된 최대 줄 길이 초과
        MissingHeader,          // 빈 파일이거나 첫 레코드가 헤더가 아님
        InvalidHeader,          // 잘못된 헤더 또는 지원하지 않는 포맷·버전·단위
        DuplicateHeader,        // 첫 헤더 이후 또 다른 헤더 등장
        RecordAfterSessionEnd,  // 세션 종료 이후 추가 레코드 등장
        MissingSessionEnd       // 파일 끝까지 읽었지만 세션 종료 레코드가 없음
    }

    public sealed record TraceReadDiagnostic
    {
        public required long LineNumber { get; init; }
        public required TraceReadDiagnosticCode Code { get; init; }
        public required string Message { get; init; }
        public required bool IsFatal { get; init; }
    }
}

internal static class SandboxChecks
{
    private sealed record Scenario(
        int Number, string Name, string Group, string Description,
        Func<string, Task> Run);

    private static Task RunSync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static readonly Scenario[] Scenarios =
    [
        new(1, "serialize", "io", "레코드 직렬화", _ => RunSync(SerializerChecks.VerifySerialize)),
        new(2, "validate-header", "io", "헤더 값 검증", _ => RunSync(ValidatorChecks.VerifyHeader)),
        new(3, "validate-track", "io", "Track 값 검증", _ => RunSync(ValidatorChecks.VerifyTrack)),
        new(4, "validate-span", "io", "작업·시간 값 검증", _ => RunSync(ValidatorChecks.VerifySpan)),
        new(5, "validate-session", "io", "세션 종료 값 검증", _ => RunSync(ValidatorChecks.VerifySessionEnd)),
        new(6, "write", "io", "JSONL 저장·역직렬화", WriterChecks.VerifyRoundTripAsync),
        new(7, "write-header", "io", "헤더 없는 쓰기 거부", WriterChecks.VerifyHeaderRequiredAsync),
        new(8, "write-duplicate", "io", "중복 헤더 쓰기 거부", WriterChecks.VerifyDuplicateHeaderRejectedAsync),
        new(9, "write-ended", "io", "세션 종료 후 쓰기 거부", WriterChecks.VerifyWriteAfterSessionEndRejectedAsync),
        new(10, "write-disposed", "io", "Writer 종료 상태", WriterChecks.VerifyDisposedWriterAsync),
        new(11, "field-order", "io", "JSON 필드 순서", _ => RunSync(SerializerChecks.VerifyFieldOrder)),
        new(12, "missing-field", "io", "필수 필드 누락", _ => RunSync(SerializerChecks.VerifyMissingField)),
        new(13, "parse-errors", "io", "JSON·레코드 오류 구분", _ => RunSync(SerializerChecks.VerifyErrorKinds)),
        new(14, "read", "io", "정상 파일 읽기", ReaderChecks.VerifyNormalFileAsync),
        new(15, "io-header", "io", "Reader 헤더 정책", ReaderChecks.VerifyReaderHeadersAsync),
        new(16, "io-session", "io", "Reader 세션 종료 정책", ReaderChecks.VerifyReaderSessionEndAsync),
        new(17, "io-boundary", "io", "Reader 경계·복구·취소", ReaderBoundaryChecks.RunAsync),
        new(18, "io-policy", "io", "입력 규약·Writer 실패", IoPolicyChecks.RunAsync),
        new(19, "tracing-basic", "tracing", "Tracer 기본 기록", TracerChecks.RunBasicAsync),
        new(20, "tracing-flush", "tracing", "명시적 Flush", TracerChecks.VerifyFlushAsync),
        new(21, "tracing-shutdown", "tracing", "Flush 큐 대기·종료 경합", TracerChecks.VerifyFlushPressureAndShutdownAsync),
        new(22, "tracing-auto-flush", "tracing", "유휴 자동 Flush·타이머 해제", TracerAutoFlushChecks.RunAsync),
        new(23, "tracing-scope", "tracing", "Scope 자동 종료·중첩·반복 Dispose", TraceScopeChecks.RunAsync),
        new(24, "tracing-args", "tracing", "Args 복사·JSON 수명·Scope 전달", TraceArgsChecks.RunAsync),
        new(25, "tracing-failures", "tracing", "큐 포화·저장 실패·핸들 검사", TracerFailureChecks.RunAsync)
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            Console.Write("실행할 이름 또는 번호 입력 (Enter: 종료): ");
            string? selection = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(selection)) return 0;
            args = [selection];
        }

        string[] selectors = args.SelectMany(argument => argument.Split(
            [' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray();

        if (selectors.Any(value => value is "--help" or "-h" or "--list" or "list"))
        {
            PrintHelp();
            return 0;
        }

        var selected = new HashSet<Scenario>();
        foreach (string selector in selectors)
        {
            Scenario[] matches = Scenarios.Where(scenario =>
                selector.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                selector.Equals(scenario.Group, StringComparison.OrdinalIgnoreCase) ||
                selector.Equals(scenario.Name, StringComparison.OrdinalIgnoreCase) ||
                selector == scenario.Number.ToString()).ToArray();

            if (matches.Length == 0)
            {
                Console.Error.WriteLine($"알 수 없는 테스트: {selector}. --list로 목록을 확인하세요.");
                return 2;
            }
            selected.UnionWith(matches);
        }

        if (selected.Count == 0)
        {
            Console.Error.WriteLine("실행할 테스트를 지정하세요. --list로 목록을 확인하세요.");
            return 2;
        }

        string directory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(directory);
        int failures = 0;
        foreach (Scenario scenario in Scenarios.Where(selected.Contains))
        {
            Console.WriteLine($"\n=== TEST {scenario.Number}: {scenario.Name} — {scenario.Description} ===");
            try
            {
                await scenario.Run(directory);
                Console.WriteLine($"PASS: {scenario.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {scenario.Name}\n{exception}");
            }
        }

        Console.WriteLine($"\n실행 {selected.Count}개 / 성공 {selected.Count - failures}개 / 실패 {failures}개");
        return failures == 0 ? 0 : 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Timeline Sandbox — 선택한 테스트만 실행합니다.");
        Console.WriteLine("묶음: io | tracing | all / 개별: 이름 또는 번호 / 목록: --list");
        Console.WriteLine("여러 개 선택: read tracing-flush 또는 14,20");
        foreach (Scenario scenario in Scenarios)
            Console.WriteLine($"{scenario.Number,2}  {scenario.Name,-20} {scenario.Description}");
    }
}

using System.Text.Json;
using Timeline.Format;
using Timeline.IO;

internal static class ReaderChecks
{
    public static async Task VerifyNormalFileAsync(string logDirectory)
    {
        TraceRecord[] records = SampleTraceData.CreateRecords();
        string filePath = await SampleTraceData.WriteFileAsync(logDirectory);
        Console.WriteLine("\nTEST 14 - Reader로 정상 JSONL 파일 읽기");
        var traceReader = new JsonlTraceReader();
        int loadedRecordCount = 0;

        await foreach (TraceReadResult result in traceReader.ReadAsync(filePath))
        {
            if (result.Record is TraceRecord loadedRecord)
            {
                loadedRecordCount++;
                Console.WriteLine($"{result.LineNumber}줄: {loadedRecord.GetType().Name}");
            }
            else if (result.Diagnostic is TraceReadDiagnostic diagnostic)
            {
                throw new Exception(
                    $"정상 파일에서 예상하지 못한 진단: " +
                    $"{diagnostic.LineNumber}줄, {diagnostic.Message}");
            }
        }

        if (loadedRecordCount != records.Length)
        {
            throw new Exception(
                $"검증 실패: {records.Length}개를 예상했지만 " +
                $"{loadedRecordCount}개를 읽었습니다.");
        }

        Console.WriteLine("Reader 정상 파일 읽기 확인 완료");
    }

    public static async Task VerifyReaderHeadersAsync(string logDirectory)
    {
        string header = TraceJsonSerializer.Serialize(new TraceHeaderRecord
        {
            OriginUtc = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero)
        });
        string track = TraceJsonSerializer.Serialize(new TraceTrackRecord
        {
            Id = "main",
            Name = "Main Thread"
        });
        string sessionEnd = TraceJsonSerializer.Serialize(new TraceSessionEndRecord
        {
            TimeUs = 1000,
            DroppedRecordCount = 0
        });
    
        (string Name, string Content, TraceReadDiagnosticCode Code)[] cases =
        [
            ("empty", "", TraceReadDiagnosticCode.MissingHeader),
            ("missing", track + "\n" + header + "\n", TraceReadDiagnosticCode.MissingHeader),
            ("malformed", "{\n" + header + "\n", TraceReadDiagnosticCode.InvalidHeader),
            ("unsupported-version", header.Replace("\"version\":1", "\"version\":999") + "\n" + track + "\n", TraceReadDiagnosticCode.InvalidHeader),
            ("duplicate", string.Join("\n", header, header, track, sessionEnd) + "\n", TraceReadDiagnosticCode.DuplicateHeader)
        ];
    
        foreach (var testCase in cases)
        {
            string path = Path.Combine(logDirectory, $"reader-{testCase.Name}-{Guid.NewGuid():N}.jsonl");
    
            // Writer는 잘못된 순서를 거부하므로 Reader용 입력 파일을 직접 만듭니다.
            await File.WriteAllTextAsync(path, testCase.Content, new System.Text.UTF8Encoding(false));
    
            foreach (TraceReadMode mode in new[] { TraceReadMode.Strict, TraceReadMode.Recovery })
            {
                var reader = new JsonlTraceReader(new TraceReadOptions { Mode = mode });
                var results = new List<TraceReadResult>();
    
                await foreach (TraceReadResult result in reader.ReadAsync(path))
                {
                    results.Add(result);
                }
    
                bool duplicate = testCase.Code == TraceReadDiagnosticCode.DuplicateHeader;
                bool shouldContinue = duplicate && mode == TraceReadMode.Recovery;
                int expectedCount = duplicate ? (shouldContinue ? 4 : 2) : 1;
    
                if (results.Count != expectedCount)
                {
                    throw new Exception($"{testCase.Name}/{mode}: 결과 수 {results.Count}, 예상 값 {expectedCount}");
                }
    
                int diagnosticIndex = duplicate ? 1 : 0;
                TraceReadDiagnostic? diagnostic = results[diagnosticIndex].Diagnostic;
    
                if (diagnostic is null || diagnostic.Code != testCase.Code ||
                    diagnostic.LineNumber != (duplicate ? 2 : 1) ||
                    diagnostic.IsFatal != !shouldContinue)
                {
                    throw new Exception($"{testCase.Name}/{mode}: 진단 코드·줄 번호·중단 여부가 예상과 다릅니다.");
                }
    
                if (duplicate && results[0].Record is not TraceHeaderRecord)
                {
                    throw new Exception("첫 번째 정상 헤더가 전달되지 않았습니다.");
                }
    
                if (shouldContinue &&
                    (results[2].Record is not TraceTrackRecord || results[2].LineNumber != 3 ||
                     results[3].Record is not TraceSessionEndRecord || results[3].LineNumber != 4))
                {
                    throw new Exception("Recovery에서 중복 헤더 이후의 정상 레코드를 읽지 못했습니다.");
                }
    
                Console.WriteLine($"{testCase.Name} / {mode}: 통과");
            }
        }
    
        Console.WriteLine("Reader 헤더 검증 10개 시나리오 확인 완료");
    }

    public static async Task VerifyReaderSessionEndAsync(string logDirectory)
    {
        string header = TraceJsonSerializer.Serialize(new TraceHeaderRecord
        {
            OriginUtc = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero)
        });
        string track = TraceJsonSerializer.Serialize(new TraceTrackRecord
        {
            Id = "main",
            Name = "Main Thread"
        });
        string sessionEnd = TraceJsonSerializer.Serialize(new TraceSessionEndRecord
        {
            TimeUs = 1000,
            DroppedRecordCount = 0
        });
        string invalidEnd = """{"type":"sessionEnd","time":1000,"droppedRecordCount":-1}""";
    
        // 결과 전체를 비교하여 중단 위치와 불필요한 추가 진단까지 확인합니다.
        (string Name, string[] Lines, string[] Strict, string[] Recovery)[] cases =
        [
            ("normal", [header, track, sessionEnd],
                ["1:TraceHeaderRecord", "2:TraceTrackRecord", "3:TraceSessionEndRecord"],
                ["1:TraceHeaderRecord", "2:TraceTrackRecord", "3:TraceSessionEndRecord"]),
            ("missing-end", [header, track],
                ["1:TraceHeaderRecord", "2:TraceTrackRecord", "2:MissingSessionEnd:True"],
                ["1:TraceHeaderRecord", "2:TraceTrackRecord", "2:MissingSessionEnd:False"]),
            ("after-end", [header, sessionEnd, track, "{"],
                ["1:TraceHeaderRecord", "2:TraceSessionEndRecord", "3:RecordAfterSessionEnd:True"],
                ["1:TraceHeaderRecord", "2:TraceSessionEndRecord", "3:RecordAfterSessionEnd:False", "4:RecordAfterSessionEnd:False"]),
            ("duplicate-end", [header, sessionEnd, sessionEnd],
                ["1:TraceHeaderRecord", "2:TraceSessionEndRecord", "3:RecordAfterSessionEnd:True"],
                ["1:TraceHeaderRecord", "2:TraceSessionEndRecord", "3:RecordAfterSessionEnd:False"]),
            ("invalid-end", [header, invalidEnd],
                ["1:TraceHeaderRecord", "2:InvalidRecord:True"],
                ["1:TraceHeaderRecord", "2:InvalidRecord:False", "2:MissingSessionEnd:False"])
        ];
    
        foreach (var testCase in cases)
        {
            string path = Path.Combine(logDirectory, $"reader-session-{testCase.Name}-{Guid.NewGuid():N}.jsonl");
            await File.WriteAllTextAsync(
                path,
                string.Join("\n", testCase.Lines) + "\n",
                new System.Text.UTF8Encoding(false));
    
            foreach (TraceReadMode mode in new[] { TraceReadMode.Strict, TraceReadMode.Recovery })
            {
                var reader = new JsonlTraceReader(new TraceReadOptions { Mode = mode });
                var actual = new List<string>();
    
                await foreach (TraceReadResult result in reader.ReadAsync(path))
                {
                    if (result.Record is TraceRecord record)
                    {
                        actual.Add($"{result.LineNumber}:{record.GetType().Name}");
                    }
                    else if (result.Diagnostic is TraceReadDiagnostic diagnostic)
                    {
                        actual.Add($"{diagnostic.LineNumber}:{diagnostic.Code}:{diagnostic.IsFatal}");
                    }
                    else
                    {
                        throw new Exception("레코드와 진단이 모두 없는 결과입니다.");
                    }
                }
    
                string[] expected = mode == TraceReadMode.Strict ? testCase.Strict : testCase.Recovery;
    
                if (!actual.SequenceEqual(expected))
                {
                    throw new Exception(
                        $"{testCase.Name}/{mode}: 결과가 예상과 다릅니다.\n" +
                        $"예상: {string.Join(", ", expected)}\n실제: {string.Join(", ", actual)}");
                }
    
                Console.WriteLine($"{testCase.Name} / {mode}: 통과");
            }
        }
    
        Console.WriteLine("Reader 세션 종료 검증 10개 시나리오 확인 완료");
    }
}

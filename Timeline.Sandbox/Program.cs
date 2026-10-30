using Timeline.Format;
using Timeline.IO;
using System.Text.Json;

Console.WriteLine("테스트 시나리오 시작");

// TEST 1 - 레코드 Json Serialize 출력
Console.WriteLine("\nTEST 1 - 레코드 Json Serialize 출력");
TraceRecord[] records =
[
    new TraceHeaderRecord
    {
        OriginUtc = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero)
    },

    new TraceTrackRecord
    {
        Id = "main",
        Name = "Main Thread"
    },

    new TraceBeginRecord
    {
        Id = "42",
        TrackId = "main",
        Name = "LoadFile",
        TimeUs = 0
    },

    new TraceEndRecord
    {
        Id = "42",
        TimeUs = 7000
    },

    new TraceSessionEndRecord
    {
        TimeUs = 8000,
        DroppedRecordCount = 0
    }
];

foreach (TraceRecord record in records)
{
    TraceRecordValidator.Validate(record);

    string json = TraceJsonSerializer.Serialize(record);
    Console.WriteLine(json);
}

// TEST 2 - TraceRecordValidator(TraceHeaderRecord) 예외 테스트
Console.WriteLine("\nTEST 2 - TraceRecordValidator(TraceHeaderRecord) 예외 테스트");
var validHeader = new TraceHeaderRecord
{
    OriginUtc = new DateTimeOffset(
        2026, 10, 30, 0, 0, 0, TimeSpan.Zero)
};

TraceRecordValidator.Validate(validHeader);
Console.WriteLine("정상 헤더 : 검증 통과");

var invalidHeader = validHeader with
{
    Version = -1
};

try
{
    TraceRecordValidator.Validate(invalidHeader);
    Console.WriteLine("예상과 다름 : 잘못된 헤더가 통과했습니다.");
}
catch (ArgumentException exception)
{
    Console.WriteLine($"잘못된 헤더 : {exception.Message}");
}

// TEST 3 - TraceRecordValidator(TraceTrackRecord) 예외 테스트
Console.WriteLine("\nTEST 3 - TraceRecordValidator(TraceTrackRecord) 예외 테스트");
var validTrack = new TraceTrackRecord
{
    Id = "main",
    Name = "Main Thread"
};

TraceRecordValidator.Validate(validTrack);
Console.WriteLine("정상 Track: 검증 통과");

var invalidTrack = validTrack with
{
    Id = "   "
};

try
{
    TraceRecordValidator.Validate(invalidTrack);
    Console.WriteLine("예상과 다름: 잘못된 Track이 통과했습니다.");
}
catch (ArgumentException exception)
{
    Console.WriteLine($"잘못된 Track: {exception.Message}");
}

// TEST 4 - TraceRecordValidator(TraceBeginRecord, TraceEndRecord) 예외 테스트
Console.WriteLine("\nTEST 4 - TraceRecordValidator(TraceBeginRecord, TraceEndRecord) 예외 테스트");
var beginForValidation = new TraceBeginRecord
{
    Id = "100",
    TrackId = "main",
    Name = "Update",
    TimeUs = 0
};

var endForValidation = new TraceEndRecord
{
    Id = "100",
    TimeUs = TimelineFormat.MaxTimestampMicroseconds
};

TraceRecordValidator.Validate(beginForValidation);
TraceRecordValidator.Validate(endForValidation);

Console.WriteLine("시간 최솟값과 최댓값: 검증 통과");

try
{
    TraceRecordValidator.Validate(
        endForValidation with { TimeUs = -1 });
}
catch (ArgumentException exception)
{
    Console.WriteLine($"음수 시간 거부: {exception.Message}");
}

try
{
    TraceRecordValidator.Validate(
        beginForValidation with { ParentId = "100" });
}
catch (ArgumentException exception)
{
    Console.WriteLine($"자기 부모 지정 거부: {exception.Message}");
}

// TEST 5 - TraceRecordValidator(TraceSessionEndRecord) 예외 테스트
Console.WriteLine("\nTEST 5 - TraceRecordValidator(TraceSessionEndRecord) 예외 테스트");
TraceRecord invalidSessionEnd = new TraceSessionEndRecord
{
    TimeUs = 8000,
    DroppedRecordCount = -1
};

try
{
    TraceRecordValidator.Validate(invalidSessionEnd);
    Console.WriteLine("예상과 다름: 음수 누락 수가 통과했습니다.");
}
catch (ArgumentException exception)
{
    Console.WriteLine($"음수 누락 수 거부: {exception.Message}");
}

// TEST 6 - JSONL 파일 저장 테스트
Console.WriteLine("\nTEST 6 - JSONL 파일 저장 테스트");
string logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

Directory.CreateDirectory(logDirectory);

string fileName = $"sample-{Guid.NewGuid():N}.jsonl";
string filePath = Path.Combine(logDirectory, fileName);

await using (ITraceWriter writer = new JsonlTraceWriter(filePath))
{
    foreach (TraceRecord record in records)
    {
        await writer.WriteAsync(record);
    }

    await writer.FlushAsync();

    Console.WriteLine("모든 레코드 쓰기와 Flush가 완료되었습니다.");
}

Console.WriteLine("Writer가 종료되어 파일이 닫혔습니다.");
Console.WriteLine($"저장 경로: {filePath}");

string[] savedLines = await File.ReadAllLinesAsync(filePath);

Console.WriteLine($"저장된 레코드 수: {savedLines.Length}");

foreach (string line in savedLines)
{
    TraceRecord loadedRecord = TraceJsonSerializer.Deserialize(line);

    TraceRecordValidator.Validate(loadedRecord);

    Console.WriteLine($"읽은 타입 : {loadedRecord.GetType().Name}");

    Console.WriteLine(TraceJsonSerializer.Serialize(loadedRecord));
}

// TEST 7 - 헤더 누락 거부 및 이후 정상 기록 테스트
Console.WriteLine("\nTEST 7 - 헤더 누락 거부 및 이후 정상 기록 테스트");
await VerifyHeaderRequiredAsync(logDirectory);

// TEST 8 - 중복 헤더 거부 및 이후 정상 기록 테스트
Console.WriteLine("\nTEST 8 - 중복 헤더 거부 및 이후 정상 기록 테스트");
await VerifyDuplicateHeaderRejectedAsync(logDirectory);

// TEST 9 - 세션 종료 후 쓰기 거부 및 Flush 허용 테스트
Console.WriteLine("\nTEST 9 - 세션 종료 후 쓰기 거부 및 Flush 허용 테스트");
await VerifyWriteAfterSessionEndRejectedAsync(logDirectory);

// TEST 10 - 종료 후 사용 거부 및 반복 Dispose 테스트
Console.WriteLine("\nTEST 10 - 종료 후 사용 거부 및 반복 Dispose 테스트");
await VerifyDisposedWriterAsync(logDirectory);

// TEST 11 - type 필드가 마지막에 있는 JSON 읽기
Console.WriteLine("\nTEST 11 - type 필드가 마지막에 있는 JSON 읽기");
string reorderedJson = """{"id":"42","time":7000,"type":"end"}""";

TraceRecord reorderedRecord = TraceJsonSerializer.Deserialize(reorderedJson);

TraceRecordValidator.Validate(reorderedRecord);

if (reorderedRecord is not TraceEndRecord end || end.Id != "42" || end.TimeUs != 7000)
{
    throw new Exception("검증 실패: 필드 순서가 다른 End 레코드를 올바르게 읽지 못했습니다.");
}

Console.WriteLine("JSON 필드 순서와 무관한 역직렬화 확인 완료");

// TEST 12 - 헤더의 version 필드 누락 검사
Console.WriteLine("\nTEST 12 - 헤더의 version 필드 누락 검사");
string missingVersionJson =
    """
    {
        "type": "header",
        "format": "timeline",
        "timeUnit": "us",
        "originUtc": "2026-10-30T00:00:00Z"
    }
    """;

bool missingVersionRejected = false;

try
{
    TraceJsonSerializer.Deserialize(missingVersionJson);
}
catch (System.Text.Json.JsonException exception)
{
    missingVersionRejected = true;
    Console.WriteLine($"예상한 거부: {exception.Message}");
}

if (!missingVersionRejected)
{
    throw new Exception("검증 실패 : version이 없는 헤더가 허용되었습니다.");
}

Console.WriteLine("필수 필드 누락 거부 확인 완료");

// TEST 13 - JSON 문법 오류와 레코드 형식 오류 구분
Console.WriteLine("\nTEST 13 - JSON 문법 오류와 레코드 형식 오류 구분");
string[] invalidInputs =
    [
        """{"type":"end","id":"42","time":""",
        """{"type":"end","id":"42"}"""
    ];

foreach (string input in invalidInputs)
{
    JsonDocument document;

    try
    {
        document = JsonDocument.Parse(input);
    }
    catch (JsonException exception)
    {
        Console.WriteLine($"InvalidJson : {exception.Message}");
        continue;
    }

    using (document)
    {
        try
        {
            TraceRecord record = TraceJsonSerializer.Deserialize(document.RootElement);

            TraceRecordValidator.Validate(record);

            throw new Exception("검증 실패 : 잘못된 입력이 통과했습니다.");
        }
        catch (JsonException exception)
        {
            Console.WriteLine($"InvalidRecord : {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            Console.WriteLine($"InvalidRecord : {exception.Message}");
        }
    }
}

// TEST 14 - Reader로 정상 JSONL 파일 읽기
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

// TEST 15 - Reader 헤더 오류와 모드별 중단 정책
Console.WriteLine("\nTEST 15 - Reader 헤더 오류와 모드별 중단 정책");
await VerifyReaderHeadersAsync(logDirectory);

// TEST 16 - Reader 세션 종료 정책
Console.WriteLine("\nTEST 16 - Reader 세션 종료 정책");
await VerifyReaderSessionEndAsync(logDirectory);

// TEST 17 - Reader 경계 조건, 오류 복구, 취소 및 자원 해제
Console.WriteLine("\nTEST 17 - Reader 경계 조건, 오류 복구, 취소 및 자원 해제");
await ReaderBoundaryChecks.RunAsync(logDirectory);

// TEST 18 - 입력 규약 및 Writer 실패 처리
Console.WriteLine("\nTEST 18 - 입력 규약 및 Writer 실패 처리");
await IoPolicyChecks.RunAsync(logDirectory);

static async Task VerifyReaderSessionEndAsync(string logDirectory)
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

static async Task VerifyReaderHeadersAsync(string logDirectory)
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

static async Task VerifyHeaderRequiredAsync(string logDirectory)
{
    string filePath = Path.Combine(logDirectory, $"header-required-{Guid.NewGuid():N}.jsonl");

    await using (ITraceWriter writer = new JsonlTraceWriter(filePath))
    {
        var begin = new TraceBeginRecord
        {
            Id = "1",
            TrackId = "main",
            Name = "Test",
            TimeUs = 0
        };

        bool rejected = false;

        try
        {
            await writer.WriteAsync(begin);
        }
        catch (InvalidOperationException exception)
        {
            rejected = true;
            Console.WriteLine($"예상한 거부: {exception.Message}");
        }

        if (!rejected)
        {
            throw new Exception("검증 실패: 헤더 없이 Begin을 기록했습니다.");
        }

        var header = new TraceHeaderRecord
        {
            OriginUtc = DateTimeOffset.UtcNow
        };

        await writer.WriteAsync(header);
    }

    string[] lines = await File.ReadAllLinesAsync(filePath);

    if (lines.Length != 1)
    {
        throw new Exception($"검증 실패: 헤더 한 줄을 예상했지만 {lines.Length}줄입니다.");
    }

    using var document = System.Text.Json.JsonDocument.Parse(lines[0]);

    if (document.RootElement.GetProperty("type").GetString() != "header")
    {
        throw new Exception("검증 실패: 저장된 레코드가 헤더가 아닙니다.");
    }

    Console.WriteLine("헤더 누락 거부 및 이후 정상 기록 확인 완료");
}

static async Task VerifyDuplicateHeaderRejectedAsync(string logDirectory)
{
    string filePath = Path.Combine(logDirectory, $"duplicate-header-{Guid.NewGuid():N}.jsonl");

    await using (ITraceWriter writer = new JsonlTraceWriter(filePath))
    {
        var header = new TraceHeaderRecord
        {
            OriginUtc = DateTimeOffset.UtcNow
        };

        await writer.WriteAsync(header);

        bool rejected = false;

        try
        {
            await writer.WriteAsync(header);
        }
        catch (InvalidOperationException exception)
        {
            rejected = true;
            Console.WriteLine($"예상한 거부: {exception.Message}");
        }

        if (!rejected)
        {
            throw new Exception("검증 실패: 중복 헤더가 허용되었습니다.");
        }

        await writer.WriteAsync(new TraceSessionEndRecord
        {
            TimeUs = 1000,
            DroppedRecordCount = 0
        });
    }

    string[] lines = await File.ReadAllLinesAsync(filePath);

    if (lines.Length != 2)
    {
        throw new Exception($"검증 실패: 2줄을 예상했지만 {lines.Length}줄입니다.");
    }

    using var first = System.Text.Json.JsonDocument.Parse(lines[0]);
    using var second = System.Text.Json.JsonDocument.Parse(lines[1]);

    if (first.RootElement.GetProperty("type").GetString() != "header" ||
        second.RootElement.GetProperty("type").GetString() != "sessionEnd")
    {
        throw new Exception("검증 실패: 헤더와 세션 종료 순서로 저장되지 않았습니다.");
    }

    Console.WriteLine("중복 헤더 거부 및 이후 정상 기록 확인 완료");
}
static async Task VerifyWriteAfterSessionEndRejectedAsync(string logDirectory)
{
    string filePath = Path.Combine(
        logDirectory,
        $"after-session-end-{Guid.NewGuid():N}.jsonl");

    await using (ITraceWriter writer = new JsonlTraceWriter(filePath))
    {
        await writer.WriteAsync(new TraceHeaderRecord
        {
            OriginUtc = DateTimeOffset.UtcNow
        });

        await writer.WriteAsync(new TraceSessionEndRecord
        {
            TimeUs = 1000,
            DroppedRecordCount = 0
        });

        bool rejected = false;

        try
        {
            await writer.WriteAsync(new TraceTrackRecord
            {
                Id = "late-track",
                Name = "Late Track"
            });
        }
        catch (InvalidOperationException exception)
        {
            rejected = true;
            Console.WriteLine($"예상한 거부: {exception.Message}");
        }

        if (!rejected)
        {
            throw new Exception(
                "검증 실패: 세션 종료 후 추가 기록이 허용되었습니다.");
        }

        await writer.FlushAsync();
        Console.WriteLine("세션 종료 후 Flush 성공");
    }

    string[] lines = await File.ReadAllLinesAsync(filePath);

    if (lines.Length != 2)
    {
        throw new Exception(
            $"검증 실패: 2줄을 예상했지만 {lines.Length}줄입니다.");
    }

    using var first = System.Text.Json.JsonDocument.Parse(lines[0]);
    using var second = System.Text.Json.JsonDocument.Parse(lines[1]);

    if (first.RootElement.GetProperty("type").GetString() != "header" ||
        second.RootElement.GetProperty("type").GetString() != "sessionEnd")
    {
        throw new Exception(
            "검증 실패: 헤더와 세션 종료 순서로 저장되지 않았습니다.");
    }

    Console.WriteLine("세션 종료 후 쓰기 거부 및 Flush 허용 확인 완료");
}
static async Task VerifyDisposedWriterAsync(string logDirectory)
{
    string filePath = Path.Combine(
        logDirectory,
        $"disposed-writer-{Guid.NewGuid():N}.jsonl");

    await using (ITraceWriter writer = new JsonlTraceWriter(filePath))
    {
        await writer.WriteAsync(new TraceHeaderRecord
        {
            OriginUtc = DateTimeOffset.UtcNow
        });

        // 테스트를 위해 블록이 끝나기 전에 직접 종료합니다.
        await writer.DisposeAsync();

        bool writeRejected = false;

        try
        {
            await writer.WriteAsync(new TraceTrackRecord
            {
                Id = "main",
                Name = "Main Thread"
            });
        }
        catch (ObjectDisposedException)
        {
            writeRejected = true;
            Console.WriteLine("종료 후 쓰기 거부 확인");
        }

        if (!writeRejected)
        {
            throw new Exception(
                "검증 실패: 종료된 Writer에 쓰기가 허용되었습니다.");
        }

        bool flushRejected = false;

        try
        {
            await writer.FlushAsync();
        }
        catch (ObjectDisposedException)
        {
            flushRejected = true;
            Console.WriteLine("종료 후 Flush 거부 확인");
        }

        if (!flushRejected)
        {
            throw new Exception(
                "검증 실패: 종료된 Writer의 Flush가 허용되었습니다.");
        }

        await writer.DisposeAsync();
        Console.WriteLine("DisposeAsync 재호출 성공");
    }

    // 블록을 벗어나며 await using도 DisposeAsync를 호출합니다.
    Console.WriteLine("종료 상태 및 반복 자원 해제 확인 완료");
}

Console.WriteLine("테스트 시나리오 종료");

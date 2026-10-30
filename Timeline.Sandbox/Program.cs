using Timeline.Format;
using Timeline.IO;

// TEST 1 - 레코드 Json Serialize 출력
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
    Console.WriteLine(line);
}
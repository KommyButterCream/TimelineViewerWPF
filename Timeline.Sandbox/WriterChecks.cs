using System.Text.Json;
using Timeline.Format;
using Timeline.IO;

internal static class WriterChecks
{
    public static async Task VerifyRoundTripAsync(string logDirectory)
    {
        Console.WriteLine("\nTEST 6 - JSONL 파일 저장 테스트");
        TraceRecord[] records = SampleTraceData.CreateRecords();

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
    }

    public static async Task VerifyHeaderRequiredAsync(string logDirectory)
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

    public static async Task VerifyDuplicateHeaderRejectedAsync(string logDirectory)
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

    public static async Task VerifyWriteAfterSessionEndRejectedAsync(string logDirectory)
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

    public static async Task VerifyDisposedWriterAsync(string logDirectory)
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

}

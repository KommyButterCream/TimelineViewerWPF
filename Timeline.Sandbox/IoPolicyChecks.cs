using System.Text;
using System.Text.Json;
using Timeline.Format;
using Timeline.IO;

internal static class IoPolicyChecks
{
    private static readonly TraceHeaderRecord Header = new()
    {
        OriginUtc = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero)
    };

    public static async Task RunAsync(string directory)
    {
        try
        {
            TraceRecordValidator.Validate(new UnsupportedRecord());
            throw new Exception("지원하지 않는 레코드가 검증을 통과했습니다.");
        }
        catch (ArgumentException exception) when (
            exception.GetType() == typeof(ArgumentException) &&
            exception.ParamName == "record" &&
            exception.Message.Contains(nameof(UnsupportedRecord)))
        {
            Console.WriteLine("미지원 레코드의 예외 타입·매개변수·메시지: 통과");
        }

        foreach (string json in new[]
        {
            """{"type":"end","id":"1","time":1,"time":2}""",
            """{"type":"end","type":"end","id":"1","time":1}""",
            """{"type":"end","id":"1","time":1,"\u0074ime":2}""",
            """{"type":"begin","id":"1","track":"main","name":"Test","time":0,"args":{"nested":[{"x":1,"x":2}]}}"""
        })
        {
            await ExpectAsync<JsonException>(() =>
            {
                TraceJsonSerializer.Deserialize(json);
                return Task.CompletedTask;
            });
        }
        Console.WriteLine("중복 필드 거부(일반·type·이스케이프·중첩): 통과");

        TraceRecord extended = TraceJsonSerializer.Deserialize(
            """{"type":"end","id":"1","time":1,"future":{"enabled":true}}""");
        if (extended is not TraceEndRecord { Id: "1", TimeUs: 1 })
            throw new Exception("추가 필드를 포함한 레코드 읽기에 실패했습니다.");
        Console.WriteLine("알 수 없는 추가 필드 허용: 통과");

        string headerJson = TraceJsonSerializer.Serialize(Header);
        string endJson = TraceJsonSerializer.Serialize(new TraceSessionEndRecord { TimeUs = 1, DroppedRecordCount = 0 });
        string duplicateJson = """{"type":"end","id":"1","time":1,"time":2}""";
        string duplicatePath = Path.Combine(directory, $"duplicate-fields-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(duplicatePath, $"{headerJson}\n{duplicateJson}\n{endJson}\n", new UTF8Encoding(false));
        foreach (TraceReadMode mode in new[] { TraceReadMode.Strict, TraceReadMode.Recovery })
        {
            var results = new List<TraceReadResult>();
            await foreach (var result in new JsonlTraceReader(new TraceReadOptions { Mode = mode }).ReadAsync(duplicatePath))
                results.Add(result);
            bool strict = mode == TraceReadMode.Strict;
            if (results.Count != (strict ? 2 : 3) ||
                results[1].Diagnostic is not { Code: TraceReadDiagnosticCode.InvalidRecord, LineNumber: 2 } diagnostic ||
                diagnostic.IsFatal != strict || (!strict && results[2].Record is not TraceSessionEndRecord))
                throw new Exception("중복 필드의 Reader 진단 또는 복구가 잘못되었습니다.");
        }
        Console.WriteLine("중복 필드 Reader 모드별 처리: 통과");

        string bomPath = Path.Combine(directory, $"bom-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(bomPath, headerJson + "\n" + endJson, new UTF8Encoding(true));
        foreach (TraceReadMode mode in new[] { TraceReadMode.Strict, TraceReadMode.Recovery })
        {
            int count = 0;
            await foreach (var result in new JsonlTraceReader(new TraceReadOptions { Mode = mode }).ReadAsync(bomPath))
            {
                count++;
                if (result.Diagnostic is not { Code: TraceReadDiagnosticCode.InvalidHeader, IsFatal: true, LineNumber: 1 })
                    throw new Exception("BOM 입력이 치명적 헤더 오류로 처리되지 않았습니다.");
            }
            if (count != 1) throw new Exception("BOM 진단 개수가 잘못되었습니다.");
        }
        Console.WriteLine("UTF-8 BOM 거부: 통과");

        string existingPath = Path.Combine(directory, $"existing-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(existingPath, "preserve-original");
        await ExpectAsync<IOException>(async () =>
        {
            await using var unexpected = new JsonlTraceWriter(existingPath);
        });
        if (await File.ReadAllTextAsync(existingPath) != "preserve-original")
            throw new Exception("기존 파일 내용이 변경되었습니다.");
        Console.WriteLine("기존 파일 덮어쓰기 방지: 통과");

        using (var stream = new MemoryStream())
        {
            await using (var writer = new JsonlTraceWriter(stream, leaveOpen: true))
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                await ExpectAsync<OperationCanceledException>(() => writer.WriteAsync(Header, cancellation.Token));
                await ExpectAsync<OperationCanceledException>(() => writer.FlushAsync(cancellation.Token));
                await writer.WriteAsync(Header);
                await writer.WriteAsync(new TraceSessionEndRecord { TimeUs = 1, DroppedRecordCount = 0 });
                await writer.FlushAsync();
            }
            byte[] bytes = stream.ToArray();
            if (!stream.CanWrite || bytes.Length == 0 || bytes[0] != (byte)'{')
                throw new Exception("스트림 소유권 또는 BOM 없는 출력 검증에 실패했습니다.");
            if (Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length != 2)
                throw new Exception("사전 취소된 작업이 파일에 기록되었습니다.");
        }
        Console.WriteLine("사전 취소 후 정상 사용·leaveOpen·BOM 없는 출력: 통과");

        foreach (string failure in new[] { "write", "flush", "cancel" })
        {
            var stream = new FaultStream();
            var writer = new JsonlTraceWriter(stream);
            using var cancellation = new CancellationTokenSource();
            try
            {
                await writer.WriteAsync(Header);
                await writer.FlushAsync();
                if (failure == "write")
                {
                    stream.FailWrite = true;
                    await ExpectAsync<IOException>(() => writer.WriteAsync(new TraceTrackRecord
                    {
                        Id = "main",
                        Name = new string('x', 10000)
                    }));
                }
                else if (failure == "flush")
                {
                    stream.FailFlush = true;
                    await ExpectAsync<IOException>(() => writer.FlushAsync());
                }
                else
                {
                    stream.CancelOnWrite = cancellation;
                    await ExpectAsync<OperationCanceledException>(() => writer.WriteAsync(new TraceTrackRecord
                    {
                        Id = "main",
                        Name = new string('x', 10000)
                    }, cancellation.Token));
                }

                await ExpectAsync<InvalidOperationException>(() => writer.WriteAsync(new TraceTrackRecord { Id = "next", Name = "Next" }));
                await ExpectAsync<InvalidOperationException>(() => writer.FlushAsync());
            }
            finally
            {
                // 오류가 유지되어 Dispose에서도 flush가 실패할 수 있지만 소유 스트림은 해제해야 합니다.
                try { await writer.DisposeAsync(); }
                catch (IOException) { }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            }
            if (!stream.Disposed) throw new Exception($"{failure}: 소유 스트림이 해제되지 않았습니다.");
            await writer.DisposeAsync();
            Console.WriteLine($"Writer {failure} 실패 후 사용 거부 및 자원 해제: 통과");
        }
    }

    private static async Task ExpectAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new Exception($"예상한 {typeof(TException).Name}이 발생하지 않았습니다.");
    }

    private sealed record UnsupportedRecord : TraceRecord;

    private sealed class FaultStream : MemoryStream
    {
        public bool FailWrite { get; set; }
        public bool FailFlush { get; set; }
        public CancellationTokenSource? CancelOnWrite { get; set; }
        public bool Disposed { get; private set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (CancelOnWrite is not null)
            {
                CancelOnWrite.Cancel();
                throw new OperationCanceledException(CancelOnWrite.Token);
            }
            if (FailWrite) throw new IOException("테스트용 쓰기 실패");
            return base.WriteAsync(buffer, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (FailFlush) throw new IOException("테스트용 Flush 실패");
            return base.FlushAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

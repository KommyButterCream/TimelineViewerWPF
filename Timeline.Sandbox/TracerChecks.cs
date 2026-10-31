using Timeline.Format;
using Timeline.IO;
using Timeline.Tracing;

internal static class TracerChecks
{
    public static async Task RunAsync(string directory)
    {
        await RunBasicAsync(directory);
        await VerifyFlushAsync(directory);
        await VerifyFlushPressureAndShutdownAsync(directory);
    }

    public static async Task RunBasicAsync(string directory)
    {
        await VerifyEmptySessionAsync(directory);
        await VerifyNestedSpansAsync(directory);
        await VerifyConcurrentSpansAsync(directory);
        Console.WriteLine("Tracer 기본 기록·부모 관계·동시 기록 검증 완료");
    }

    private static async Task VerifyEmptySessionAsync(string directory)
    {
        string path = NewPath(directory, "empty");
        await using (var tracer = await TimelineTracer.CreateAsync(path))
        {
            // 작업이 없는 세션도 정상적으로 시작하고 종료할 수 있어야 합니다.
        }

        List<TraceRecord> records = await ReadAsync(path);
        Require(records.Count == 2, "빈 세션은 Header와 SessionEnd만 있어야 합니다.");
        Console.WriteLine("빈 세션 생성·종료: 통과");
    }

    private static async Task VerifyNestedSpansAsync(string directory)
    {
        string path = NewPath(directory, "nested");
        TraceTrackHandle track;
        TraceSpanHandle parent;
        TraceSpanHandle child;

        await using (var tracer = await TimelineTracer.CreateAsync(path))
        {
            ITracer recorder = tracer;
            track = recorder.CreateTrack("Main Thread");
            parent = recorder.Begin("Frame", track);
            child = recorder.Begin("Update", track, parent);

            Require(track.IsValid && parent.IsValid && child.IsValid,
                "기본 예제의 Track과 작업이 접수되지 않았습니다.");
            recorder.End(child);
            recorder.End(parent);
            recorder.End(default); // Begin 미접수 핸들은 추가 기록을 만들지 않습니다.
        }

        List<TraceRecord> records = await ReadAsync(path);
        Require(records.Count == 7, "중첩 작업 예제는 총 7개 레코드여야 합니다.");
        Require(records[1] is TraceTrackRecord { Name: "Main Thread" } definedTrack &&
                definedTrack.Id == track.Id, "Track 정의가 핸들과 일치하지 않습니다.");
        Require(records[2] is TraceBeginRecord { Name: "Frame", ParentId: null } parentBegin &&
                parentBegin.Id == parent.Id && parentBegin.TrackId == track.Id,
                "부모 Begin의 이름·ID·Track이 잘못되었습니다.");
        Require(records[3] is TraceBeginRecord { Name: "Update" } childBegin &&
                childBegin.Id == child.Id && childBegin.ParentId == parent.Id && childBegin.TrackId == track.Id,
                "자식 Begin의 부모·ID·Track이 잘못되었습니다.");
        Require(records[4] is TraceEndRecord childEnd && childEnd.Id == child.Id,
                "자식 End가 잘못되었습니다.");
        Require(records[5] is TraceEndRecord parentEnd && parentEnd.Id == parent.Id,
                "부모 End가 잘못되었습니다.");
        Console.WriteLine("Track·중첩 Begin/End·기본 핸들 생략: 통과");
    }

    private static async Task VerifyConcurrentSpansAsync(string directory)
    {
        const int count = 32;
        string path = NewPath(directory, "concurrent");
        await using (var tracer = await TimelineTracer.CreateAsync(path))
        {
            TraceTrackHandle track = tracer.CreateTrack("Workers");
            await Task.WhenAll(Enumerable.Range(0, count).Select(index => Task.Run(() =>
            {
                TraceSpanHandle span = tracer.Begin($"Work {index}", track);
                Require(span.IsValid, "동시 기록 예제에서 Begin이 누락되었습니다.");
                tracer.End(span);
            })));
        }

        List<TraceRecord> records = await ReadAsync(path);
        TraceBeginRecord[] begins = records.OfType<TraceBeginRecord>().ToArray();
        TraceEndRecord[] ends = records.OfType<TraceEndRecord>().ToArray();
        Require(records.Count == count * 2 + 3 && begins.Length == count && ends.Length == count,
                "동시 기록의 레코드 수가 예상과 다릅니다.");
        Require(begins.Select(record => record.Id).Distinct().Count() == count,
                "동시 기록에서 이벤트 ID가 중복되었습니다.");
        Require(begins.Select(record => record.Id).Order().SequenceEqual(
                    ends.Select(record => record.Id).Order()),
                "동시 기록의 Begin/End ID가 대응하지 않습니다.");
        foreach (TraceBeginRecord begin in begins)
        {
            TraceEndRecord end = ends.Single(record => record.Id == begin.Id);
            Require(end.TimeUs >= begin.TimeUs, "종료 시각이 시작 시각보다 빠릅니다.");
        }
        Console.WriteLine("32개 동시 작업의 ID 유일성·Begin/End 대응: 통과");
    }

    public static async Task VerifyFlushAsync(string directory)
    {
        string path = NewPath(directory, "flush");
        await using (var tracer = await TimelineTracer.CreateAsync(path))
        {
            TraceTrackHandle track = tracer.CreateTrack("Flush Track");
            TraceSpanHandle span = tracer.Begin("Before Flush", track);
            tracer.End(span);
            await tracer.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));

            // 종료 전 파일을 읽어 Dispose가 아닌 명시적 Flush의 효과를 확인합니다.
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var visible = new List<TraceRecord>();
            while (await reader.ReadLineAsync() is string line)
                visible.Add(TraceJsonSerializer.Deserialize(line));
            Require(visible.Count == 4 &&
                    visible[2] is TraceBeginRecord begin && begin.Id == span.Id &&
                    visible[3] is TraceEndRecord end && end.Id == span.Id,
                    "Flush 완료 전에 접수한 기록이 파일에 모두 나타나지 않았습니다.");
            Require(!visible.OfType<TraceSessionEndRecord>().Any(), "Flush가 세션을 종료했습니다.");

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool canceled = false;
            try { await tracer.FlushAsync(cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { canceled = true; }
            Require(canceled, "사전 취소된 Flush가 취소되지 않았습니다.");

            TraceSpanHandle next = tracer.Begin("After Flush", track);
            tracer.End(next);
            await tracer.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }

        List<TraceRecord> records = await ReadAsync(path);
        Require(records.Count == 7, "Flush 이후 세션을 계속 사용할 수 없습니다.");
        Console.WriteLine("명시적 Flush의 기록 반영·사전 취소·이후 재사용: 통과");
    }

    public static async Task VerifyFlushPressureAndShutdownAsync(string directory)
    {
        string path = NewPath(directory, "flush-pressure");
        var tracer = await TimelineTracer.CreateAsync(path, new TimelineTracerOptions { QueueCapacity = 1 });
        try
        {
            // 입력 하나를 넣은 뒤 작은 큐에 여러 Flush 요청을 동시에 접수합니다.
            tracer.CreateTrack("Small Queue");
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => tracer.FlushAsync()))
                .WaitAsync(TimeSpan.FromSeconds(10));

            Task[] pending = Enumerable.Range(0, 32).Select(_ => tracer.FlushAsync()).ToArray();
            Task disposal = tracer.DisposeAsync().AsTask();
            foreach (Task flush in pending)
            {
                try { await flush.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (ObjectDisposedException) { /* 종료 전에 접수되지 않은 요청은 거부됩니다. */ }
            }
            await disposal.WaitAsync(TimeSpan.FromSeconds(10));

            bool rejected = false;
            try { await tracer.FlushAsync(); }
            catch (ObjectDisposedException) { rejected = true; }
            Require(rejected, "종료 후 Flush가 허용되었습니다.");
        }
        finally
        {
            await tracer.DisposeAsync();
        }

        List<TraceRecord> records = await ReadAsync(path);
        Require(records.Count == 3, "Flush 요청이 로그 레코드로 저장되거나 누락으로 집계되었습니다.");
        Console.WriteLine("용량 1 큐의 동시 Flush·종료 경합·종료 후 거부: 통과");
    }

    private static async Task<List<TraceRecord>> ReadAsync(string path)
    {
        var records = new List<TraceRecord>();
        var reader = new JsonlTraceReader(new TraceReadOptions { Mode = TraceReadMode.Strict });
        await foreach (TraceReadResult result in reader.ReadAsync(path))
        {
            if (result.Diagnostic is TraceReadDiagnostic diagnostic)
                throw new Exception($"Tracer 출력 진단: {diagnostic.LineNumber}줄, {diagnostic.Code}: {diagnostic.Message}");
            records.Add(result.Record ?? throw new Exception("레코드가 없는 읽기 결과입니다."));
        }

        Require(records.FirstOrDefault() is TraceHeaderRecord header && header.OriginUtc.Offset == TimeSpan.Zero,
                "UTC 헤더가 없습니다.");
        Require(records.LastOrDefault() is TraceSessionEndRecord { DroppedRecordCount: 0 },
                "누락 없는 정상 종료 레코드가 없습니다.");
        long previousTime = 0;
        foreach (TraceRecord record in records)
        {
            long time = record switch
            {
                TraceBeginRecord begin => begin.TimeUs,
                TraceEndRecord end => end.TimeUs,
                TraceSessionEndRecord end => end.TimeUs,
                _ => previousTime
            };
            Require(time >= previousTime, "접수 순서의 상대 시간이 역전되었습니다.");
            previousTime = time;
        }

        // 정상 종료 이후 파일 핸들이 남지 않아야 합니다.
        using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        return records;
    }

    private static string NewPath(string directory, string name) =>
        Path.Combine(directory, $"tracer-{name}-{Guid.NewGuid():N}.jsonl");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

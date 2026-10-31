using Timeline.Format;
using Timeline.IO;
using Timeline.Tracing;

internal static class TracerFailureChecks
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static TimelineTracerOptions Options(int capacity) => new()
    {
        QueueCapacity = capacity,
        FlushInterval = TimeSpan.FromHours(1)
    };

    public static async Task RunAsync(string directory)
    {
        await VerifyOverflowAsync();
        await VerifyFailureAsync(failFlush: false);
        await VerifyFailureAsync(failFlush: true);
        await VerifyInitializationFailureAsync();
        await VerifyForeignHandlesAsync();
    }

    private static async Task VerifyOverflowAsync()
    {
        var writer = new ControlledWriter { BlockTrack = true };
        var tracer = await TimelineTracer.CreateAsync(writer, Options(1), TimeProvider.System);
        try
        {
            var track = tracer.CreateTrack("Blocked Track");
            await writer.Entered.Task.WaitAsync(Timeout);
            var accepted = tracer.Begin("Accepted", track); // 큐의 한 자리를 채웁니다.
            var dropped = tracer.Begin("Dropped", track);
            Require(accepted.IsValid && !dropped.IsValid, "큐 포화 Begin의 접수 결과가 잘못됐습니다.");
            tracer.End(dropped); // 추가 집계하지 않습니다.
            tracer.End(accepted); // 큐 포화로 End 한 개가 누락됩니다.
            await ExpectAsync<InvalidOperationException>(() =>
            {
                tracer.CreateTrack("Rejected Track");
                return Task.CompletedTask;
            });

            using var cancellation = new CancellationTokenSource();
            Task waitingFlush = tracer.FlushAsync(cancellation.Token);
            Require(!waitingFlush.IsCompleted, "가득 찬 큐의 Flush가 대기하지 않았습니다.");
            cancellation.Cancel();
            await ExpectAsync<OperationCanceledException>(() => waitingFlush);
            writer.Release.TrySetResult();
            await tracer.FlushAsync().WaitAsync(Timeout);
        }
        finally
        {
            writer.Release.TrySetResult();
            await tracer.DisposeAsync().AsTask().WaitAsync(Timeout);
        }

        Require(writer.Disposed && writer.FlushCount == 1, "취소한 Flush가 접수되었거나 Writer가 정리되지 않았습니다.");
        Require(writer.Records.Count == 4 && writer.Records.OfType<TraceBeginRecord>().Single().Name == "Accepted" &&
                !writer.Records.OfType<TraceEndRecord>().Any() &&
                writer.Records.Last() is TraceSessionEndRecord { DroppedRecordCount: 2 },
                "포화 시 누락 레코드 수 또는 저장 내용이 잘못됐습니다.");
        Console.WriteLine("큐 포화의 Begin/End 누락 2개·Track 거부·Flush 대기 취소: 통과");
    }

    private static async Task VerifyFailureAsync(bool failFlush)
    {
        var marker = new IOException("테스트용 저장 실패");
        var writer = new ControlledWriter
        {
            BlockTrack = !failFlush,
            BlockFlush = failFlush,
            Failure = marker
        };
        var tracer = await TimelineTracer.CreateAsync(writer, Options(2), TimeProvider.System);
        try
        {
            tracer.CreateTrack("Failure Track");
            Task? currentFlush = failFlush ? tracer.FlushAsync() : null;
            await writer.Entered.Task.WaitAsync(Timeout);
            Task first = tracer.FlushAsync();
            Task second = tracer.FlushAsync();
            Require(!first.IsCompleted && !second.IsCompleted, "실패 전 Flush 대기가 만들어지지 않았습니다.");
            writer.Release.TrySetResult();
            if (currentFlush is not null) await ExpectSameFailureAsync(currentFlush, marker);
            await ExpectSameFailureAsync(first, marker);
            await ExpectSameFailureAsync(second, marker);

            await ExpectAsync<InvalidOperationException>(() => tracer.FlushAsync());
            await ExpectAsync<InvalidOperationException>(() =>
            {
                tracer.CreateTrack("After Failure");
                return Task.CompletedTask;
            });
        }
        finally
        {
            writer.Release.TrySetResult();
            await ExpectSameFailureAsync(tracer.DisposeAsync().AsTask(), marker);
        }
        Require(writer.Disposed && !writer.Records.OfType<TraceSessionEndRecord>().Any(),
            "저장 실패 뒤 정상 종료를 기록했거나 자원 정리를 빠뜨렸습니다.");
        Console.WriteLine($"{(failFlush ? "Flush" : "쓰기")} 실패·대기자 오류 전달·정상 종료 생략·자원 해제: 통과");
    }

    private static async Task VerifyInitializationFailureAsync()
    {
        var initial = new IOException("헤더 실패");
        var cleanup = new IOException("정리 실패");
        var writer = new ControlledWriter { HeaderFailure = initial, CleanupFailure = cleanup };
        bool observed = false;
        try
        {
            await TimelineTracer.CreateAsync(writer, Options(2), TimeProvider.System);
        }
        catch (AggregateException exception)
        {
            observed = exception.InnerExceptions.Count == 2 &&
                ReferenceEquals(exception.InnerExceptions[0], initial) &&
                ReferenceEquals(exception.InnerExceptions[1], cleanup);
        }
        Require(observed && writer.Disposed, "초기화·정리 실패의 원인 둘을 보존하지 못했습니다.");
        Console.WriteLine("초기화 실패와 정리 실패의 두 원인 보존: 통과");
    }

    private static async Task VerifyForeignHandlesAsync()
    {
        var firstWriter = new ControlledWriter();
        var secondWriter = new ControlledWriter();
        await using var first = await TimelineTracer.CreateAsync(firstWriter, Options(16), TimeProvider.System);
        await using var second = await TimelineTracer.CreateAsync(secondWriter, Options(16), TimeProvider.System);
        var firstTrack = first.CreateTrack("First");
        var secondTrack = second.CreateTrack("Second");
        var firstSpan = first.Begin("First Span", firstTrack);
        await ExpectAsync<ArgumentException>(() => { second.Begin("Foreign Track", firstTrack); return Task.CompletedTask; });
        await ExpectAsync<ArgumentException>(() => { second.Begin("Foreign Parent", secondTrack, firstSpan); return Task.CompletedTask; });
        await ExpectAsync<ArgumentException>(() => { second.End(firstSpan); return Task.CompletedTask; });
        await ExpectAsync<ArgumentException>(() => { second.Begin("Invalid Track", default); return Task.CompletedTask; });
        first.End(firstSpan);
        var ownSpan = second.Begin("Own Span", secondTrack, default(TraceSpanHandle));
        second.End(ownSpan);
        await second.FlushAsync().WaitAsync(Timeout);
        Require(secondWriter.Records.OfType<TraceBeginRecord>().Single() is { Name: "Own Span", ParentId: null },
            "다른 세션의 요청이 기록되었거나 기본 부모 핸들 처리가 잘못됐습니다.");
        Console.WriteLine("다른 세션의 Track·Parent·End와 기본 Track 거부: 통과");
    }

    private static async Task ExpectSameFailureAsync(Task task, Exception expected)
    {
        try { await task.WaitAsync(Timeout); }
        catch (Exception exception) when (ReferenceEquals(exception, expected)) { return; }
        throw new Exception("원래 저장 오류가 전달되지 않았습니다.");
    }

    private static async Task ExpectAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action().WaitAsync(Timeout); }
        catch (TException) { return; }
        throw new Exception($"예상한 {typeof(TException).Name}이 발생하지 않았습니다.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class ControlledWriter : ITraceWriter
    {
        public List<TraceRecord> Records { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockTrack { get; init; }
        public bool BlockFlush { get; init; }
        public Exception? Failure { get; init; }
        public Exception? HeaderFailure { get; init; }
        public Exception? CleanupFailure { get; init; }
        public bool Disposed { get; private set; }
        public int FlushCount { get; private set; }

        public async Task WriteAsync(TraceRecord record, CancellationToken cancellationToken = default)
        {
            if (record is TraceHeaderRecord && HeaderFailure is not null) throw HeaderFailure;
            if (BlockTrack && record is TraceTrackRecord) await BlockAsync();
            Records.Add(record);
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            if (BlockFlush) await BlockAsync();
            FlushCount++;
        }

        private async Task BlockAsync()
        {
            Entered.TrySetResult();
            await Release.Task;
            if (Failure is not null) throw Failure;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (CleanupFailure is not null) throw CleanupFailure;
            return ValueTask.CompletedTask;
        }
    }
}

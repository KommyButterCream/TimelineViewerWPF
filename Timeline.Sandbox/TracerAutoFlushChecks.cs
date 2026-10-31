using System.Diagnostics;
using Timeline.Format;
using Timeline.IO;
using Timeline.Tracing;

internal static class TracerAutoFlushChecks
{
    public static async Task RunAsync(string directory)
    {
        await VerifyIdleFlushAsync(directory, writeSpan: false);
        await VerifyIdleFlushAsync(directory, writeSpan: true);
        await VerifyLongIntervalShutdownAsync(directory);
    }

    private static async Task VerifyIdleFlushAsync(string directory, bool writeSpan)
    {
        string path = Path.Combine(directory, $"auto-flush-{Guid.NewGuid():N}.jsonl");
        var time = new TrackingTimeProvider();
        var tracer = await TimelineTracer.CreateAsync(path,
            new TimelineTracerOptions { FlushInterval = TimeSpan.FromMilliseconds(40) }, time);
        int expectedCount = writeSpan ? 4 : 1;
        try
        {
            if (writeSpan)
            {
                TraceTrackHandle track = tracer.CreateTrack("Idle Track");
                TraceSpanHandle span = tracer.Begin("Single Work", track);
                Require(span.IsValid, "예제 Begin이 접수되지 않았습니다.");
                tracer.End(span);
            }

            // 이후에는 이벤트·명시적 Flush·Dispose를 호출하지 않습니다.
            List<TraceRecord> visible = [];
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(5))
            {
                visible = await ReadVisibleAsync(path);
                if (visible.Count == expectedCount) break;
                await Task.Delay(10);
            }

            Require(time.CreatedCount == 1 && time.TickCount > 0,
                "실제 주기 타이머가 동작하지 않았습니다.");
            Require(visible.Count == expectedCount && visible[0] is TraceHeaderRecord,
                "추가 이벤트 없는 상태에서 자동 Flush가 기록을 반영하지 못했습니다.");
            Require(!visible.OfType<TraceSessionEndRecord>().Any(), "관찰 전에 세션이 종료되었습니다.");
            if (writeSpan)
                Require(visible[2] is TraceBeginRecord begin && visible[3] is TraceEndRecord end &&
                        begin.Id == end.Id, "유휴 상태에서 Begin/End가 모두 저장되지 않았습니다.");
        }
        finally
        {
            await tracer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }

        await AssertStoppedAsync(tracer, time, path, expectedCount + 1);
        Console.WriteLine($"추가 이벤트 없는 자동 Flush / 작업 기록={writeSpan}: 통과");
    }

    private static async Task VerifyLongIntervalShutdownAsync(string directory)
    {
        string path = Path.Combine(directory, $"timer-stop-{Guid.NewGuid():N}.jsonl");
        var time = new TrackingTimeProvider();
        var tracer = await TimelineTracer.CreateAsync(path,
            new TimelineTracerOptions { FlushInterval = TimeSpan.FromHours(1) }, time);
        try
        {
            // 소비자가 타이머를 생성한 뒤 대기 중인 상태에서 종료합니다.
            var elapsed = Stopwatch.StartNew();
            while (time.CreatedCount == 0 && elapsed.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(10);
            Require(time.CreatedCount == 1, "소비자가 타이머를 생성하지 않았습니다.");
        }
        finally
        {
            // 다음 tick인 1시간을 기다리지 않고 종료되어야 합니다.
            await tracer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }

        await AssertStoppedAsync(tracer, time, path, 2);
        Require(time.TickCount == 0, "긴 주기의 타이머가 예상치 않게 실행됐습니다.");
        Console.WriteLine("1시간 타이머 대기 중 즉시 종료·반복 Dispose·타이머 해제: 통과");
    }

    private static async Task AssertStoppedAsync(
        TimelineTracer tracer, TrackingTimeProvider time, string path, int expectedCount)
    {
        Require(time.CreatedCount == 1 && time.DisposedCount == 1,
            "종료한 소비자의 타이머가 정확히 한 번 해제되지 않았습니다.");
        await tracer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Require(time.DisposedCount == 1, "반복 종료가 타이머를 중복 정리했습니다.");

        int count = 0;
        var reader = new JsonlTraceReader(new TraceReadOptions { Mode = TraceReadMode.Strict });
        await foreach (TraceReadResult result in reader.ReadAsync(path))
        {
            Require(result.Diagnostic is null, "종료된 자동 Flush 로그에 Reader 진단이 있습니다.");
            count++;
        }
        Require(count == expectedCount, "자동 Flush가 로그를 중복 기록하거나 기록을 누락했습니다.");
        using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static async Task<List<TraceRecord>> ReadVisibleAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string text = await reader.ReadToEndAsync();
        // 쓰기 도중 관찰한 마지막 불완전 줄은 다음 polling에서 다시 확인합니다.
        int lastNewline = text.LastIndexOf('\n');
        if (lastNewline < 0) return [];
        return text[..lastNewline].Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => TraceJsonSerializer.Deserialize(line)).ToList();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class TrackingTimeProvider : TimeProvider
    {
        private int _created;
        private int _disposed;
        private int _ticks;
        public int CreatedCount => Volatile.Read(ref _created);
        public int DisposedCount => Volatile.Read(ref _disposed);
        public int TickCount => Volatile.Read(ref _ticks);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer = System.CreateTimer(value =>
            {
                Interlocked.Increment(ref _ticks);
                callback(value);
            }, state, dueTime, period);
            Interlocked.Increment(ref _created);
            return new TrackingTimer(timer, () => Interlocked.Increment(ref _disposed));
        }
    }

    private sealed class TrackingTimer(ITimer timer, Action onDispose) : ITimer
    {
        private int _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(dueTime, period);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            timer.Dispose();
            onDispose();
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            await timer.DisposeAsync();
            onDispose();
        }
    }
}

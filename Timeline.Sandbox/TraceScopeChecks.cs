using System.Text.Json;
using Timeline.Format;
using Timeline.IO;
using Timeline.Tracing;

internal static class TraceScopeChecks
{
    public static async Task RunAsync(string directory)
    {
        string path = Path.Combine(directory, $"trace-scopes-{Guid.NewGuid():N}.jsonl");
        TraceSpanHandle parentHandle;
        TraceSpanHandle childHandle;

        await using (var tracer = await TimelineTracer.CreateAsync(path))
        {
            ITracer recorder = tracer;
            TraceTrackHandle track = recorder.CreateTrack("Scope Track");

            using (recorder.BeginScope("Normal", track))
            {
                // 일반적인 블록 종료에서 End를 기록합니다.
            }

            var marker = new InvalidOperationException("Scope 내부 예외 테스트");
            bool originalCaught = false;
            try
            {
                using var scope = recorder.BeginScope("Exception", track);
                throw marker;
            }
            catch (InvalidOperationException exception) when (ReferenceEquals(exception, marker))
            {
                originalCaught = true;
            }
            Require(originalCaught, "블록 내부의 원래 예외가 전달되지 않았습니다.");

            using (var parent = recorder.BeginScope("Parent", track))
            {
                parentHandle = parent.Handle;
                using (var child = recorder.BeginScope("Child", track, parent.Handle))
                {
                    childHandle = child.Handle;
                }
            }

            using (var scope = recorder.BeginScope("Repeated", track))
            {
                scope.Dispose();
                scope.Dispose();
            }

            using (var scope = recorder.BeginScope("Concurrent Dispose", track))
            {
                await Task.WhenAll(Enumerable.Range(0, 32)
                    .Select(_ => Task.Run(scope.Dispose)));
            }

            using (recorder.BeginScope("Across Await", track))
            {
                await Task.Yield();
            }
        }

        var records = new List<TraceRecord>();
        var reader = new JsonlTraceReader(new TraceReadOptions { Mode = TraceReadMode.Strict });
        await foreach (TraceReadResult result in reader.ReadAsync(path))
        {
            if (result.Diagnostic is TraceReadDiagnostic diagnostic)
                throw new Exception($"Scope 출력 진단: {diagnostic.LineNumber}: {diagnostic.Message}");
            records.Add(result.Record ?? throw new Exception("빈 읽기 결과입니다."));
        }

        string[] names = ["Normal", "Exception", "Parent", "Child", "Repeated", "Concurrent Dispose", "Across Await"];
        TraceBeginRecord[] begins = records.OfType<TraceBeginRecord>().ToArray();
        TraceEndRecord[] ends = records.OfType<TraceEndRecord>().ToArray();
        Require(records.Count == 17 && begins.Length == names.Length && ends.Length == names.Length,
            "Scope 수와 Begin/End 수가 일치하지 않습니다.");
        Require(begins.Select(begin => begin.Name).SequenceEqual(names), "Scope 시작 순서가 다릅니다.");

        foreach (TraceBeginRecord begin in begins)
        {
            TraceEndRecord[] matching = ends.Where(end => end.Id == begin.Id).ToArray();
            Require(matching.Length == 1, $"{begin.Name}: End가 정확히 하나가 아닙니다.");
            Require(matching[0].TimeUs >= begin.TimeUs, $"{begin.Name}: 종료 시각이 역전됐습니다.");
            Require(records.IndexOf(begin) < records.IndexOf(matching[0]), "End가 Begin보다 먼저 저장됐습니다.");
        }

        TraceBeginRecord childBegin = begins.Single(begin => begin.Id == childHandle.Id);
        Require(childBegin.ParentId == parentHandle.Id, "자식 Scope의 Parent가 일치하지 않습니다.");
        Require(begins.Where(begin => begin.Id != childHandle.Id).All(begin => begin.ParentId is null),
            "명시하지 않은 부모가 자동 설정됐습니다.");
        int childEndIndex = records.FindIndex(record => record is TraceEndRecord end && end.Id == childHandle.Id);
        int parentEndIndex = records.FindIndex(record => record is TraceEndRecord end && end.Id == parentHandle.Id);
        Require(childEndIndex < parentEndIndex, "자식 Scope보다 부모 Scope가 먼저 종료됐습니다.");
        Require(records.First() is TraceHeaderRecord &&
                records.Last() is TraceSessionEndRecord { DroppedRecordCount: 0 },
            "Scope 세션이 정상 종료되지 않았습니다.");

        var rejectedTracer = new RejectedBeginTracer();
        using (var scope = rejectedTracer.BeginScope("Not Accepted", default))
        {
            Require(!scope.Handle.IsValid, "미접수 Begin에 유효한 핸들이 생성됐습니다.");
            scope.Dispose();
        }
        Require(rejectedTracer.EndCalls == 1 && !rejectedTracer.EndHandle.IsValid,
            "기본 핸들의 Scope 종료가 중복 호출되거나 핸들이 변경됐습니다.");

        using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Console.WriteLine("Scope 정상 종료·예외 시 종료·원래 예외 유지: 통과");
        Console.WriteLine("부모·자식 Scope의 참조와 종료 순서: 통과");
        Console.WriteLine("반복 Dispose·32개 동시 Dispose에서 End 한 번: 통과");
        Console.WriteLine("await를 포함한 Scope·미접수 Begin 핸들·파일 해제: 통과");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    // 큐 포화 타이밍에 의존하지 않고 Begin 미접수 시 Scope를 검증합니다.
    private sealed class RejectedBeginTracer : ITracer
    {
        public int EndCalls { get; private set; }
        public TraceSpanHandle EndHandle { get; private set; }
        public TraceTrackHandle CreateTrack(string name) => default;
        public TraceSpanHandle Begin(
            string name, 
            TraceTrackHandle track, 
            TraceSpanHandle? parent = null,
            IReadOnlyDictionary<string, System.Text.Json.JsonElement>? args = null) => default;
        public void End(TraceSpanHandle span)
        {
            EndCalls++;
            EndHandle = span;
        }
    }
}

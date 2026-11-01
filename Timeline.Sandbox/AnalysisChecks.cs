using System.Text.Json;
using Timeline.Analysis;
using Timeline.Format;
using Timeline.IO;

internal static class AnalysisChecks
{
    private static TraceHeaderRecord Header() => new()
    {
        OriginUtc = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)
    };
    private static TraceTrackRecord Track(string id = "main", string name = "Main") => new() { Id = id, Name = name };
    private static TraceBeginRecord Begin(string id, long time = 0, string? parent = null, string track = "main") => new()
    {
        Id = id, Name = id, TrackId = track, TimeUs = time, ParentId = parent
    };
    private static TraceEndRecord End(string id, long time = 10) => new() { Id = id, TimeUs = time };
    private static TraceAssembler Collect(params TraceRecord[] records)
    {
        var assembler = new TraceAssembler();
        for (int index = 0; index < records.Length; index++) assembler.Add(records[index], index + 1);
        return assembler;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void Expect<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new Exception($"예상한 {typeof(TException).Name}이 발생하지 않았습니다.");
    }
    private static void Codes(TraceAnalysisResult result, params TraceAnalysisDiagnosticCode[] expected)
    {
        Require(result.Diagnostics.Select(item => item.Code).Order().SequenceEqual(expected.Order()),
            $"진단 코드가 다릅니다. 실제: {string.Join(", ", result.Diagnostics.Select(item => item.Code))}");
        Require(result.Diagnostics.All(item => item.LineNumber > 0), "진단 줄 번호가 1 미만입니다.");
        Require(result.Diagnostics.Select(item => item.LineNumber).SequenceEqual(
            result.Diagnostics.Select(item => item.LineNumber).Order()), "진단 줄 번호가 정렬되지 않았습니다.");
    }

    public static void VerifyBasic()
    {
        var sessionEnd = new TraceSessionEndRecord { TimeUs = 100, DroppedRecordCount = 2 };
        var assembler = Collect(Header(), End("later", 30), Begin("later", 20),
            Begin("early", 5), End("early", 5), Track(), Track("idle"), sessionEnd);
        var result = assembler.Build();
        Codes(result);
        Require(result.Spans.Select(span => span.Id).SequenceEqual(["early", "later"]), "전체 작업 정렬이 잘못됐습니다.");
        Require(result.Spans[0] is { Status: TraceSpanStatus.Completed, StartTimeUs: 5, EndTimeUs: 5, DurationUs: 0,
                    BeginLineNumber: 4, EndLineNumber: 5 }, "0 길이 구간이 잘못됐습니다.");
        Require(result.Spans[1] is { Status: TraceSpanStatus.Completed, DurationUs: 10, BeginLineNumber: 3, EndLineNumber: 2 },
            "뒤늦은 Begin 또는 End 연결이 잘못됐습니다.");
        Require(ReferenceEquals(result.SessionEnd, sessionEnd), "세션 종료 정보가 유실됐습니다.");
        var repeated = assembler.Build();
        Require(repeated.Diagnostics.Count == 0 && repeated.Spans.SequenceEqual(result.Spans), "반복 Build가 결과를 누적했습니다.");
        assembler.Add(Begin("new"), 9);
        assembler.Add(End("new"), 10);
        Require(assembler.Build().Spans.Count == 3 && result.Spans.Count == 2, "다음 Build가 이전 결과를 바꿨습니다.");
        var empty = Collect(Header()).Build();
        Require(empty.Spans.Count == 0 && empty.Tracks.Count == 0 && empty.SessionEnd is null,
            "빈 분석 결과 또는 선택적인 SessionEnd 처리가 잘못됐습니다.");

        var caseSensitive = Collect(Header(), Track("A"), Track("a"),
            Begin("A", track: "A"), Begin("a", track: "a"), End("A"), End("a")).Build();
        Codes(caseSensitive);
        Require(caseSensitive.Spans.Count == 2 && caseSensitive.Tracks.Count == 2,
            "ID의 대소문자 구분 또는 Track/작업 ID의 별도 공간 처리가 잘못됐습니다.");
        Console.WriteLine("정상 연결·역순 입력·0 길이·정렬·반복 Build: 통과");
    }

    public static void VerifyRecordProblems()
    {
        var result = Collect(Header(), Track(), Track(name: "Ignored"),
            Begin("a", 20), Begin("a", 1), End("a", 10), End("a", 30),
            Begin("missing", 50), End("orphan")).Build();
        Codes(result, TraceAnalysisDiagnosticCode.DuplicateTrack, TraceAnalysisDiagnosticCode.DuplicateBegin,
            TraceAnalysisDiagnosticCode.DuplicateEnd, TraceAnalysisDiagnosticCode.InvalidEndTime,
            TraceAnalysisDiagnosticCode.MissingEnd, TraceAnalysisDiagnosticCode.MissingBegin);
        Require(result.Tracks.Single().Name == "Main", "첫 Track 정의를 유지하지 않았습니다.");
        var invalid = result.Spans.Single(span => span.Id == "a");
        Require(invalid is { Status: TraceSpanStatus.InvalidEnd, StartTimeUs: 20, EndTimeUs: null, DurationUs: null,
            BeginLineNumber: 4, EndLineNumber: 6 }, "첫 Begin/End 유지 또는 InvalidEnd 표현이 잘못됐습니다.");
        Require(result.Spans.Single(span => span.Id == "missing") is
            { Status: TraceSpanStatus.MissingEnd, EndTimeUs: null, EndLineNumber: null }, "MissingEnd 표현이 잘못됐습니다.");
        Require(result.Diagnostics.Single(item => item.Code == TraceAnalysisDiagnosticCode.MissingEnd).LineNumber == 8,
            "종료 누락 진단은 Begin 줄 번호를 가리켜야 합니다.");
        foreach (var expected in new[] { (TraceAnalysisDiagnosticCode.DuplicateTrack, 3L, 2L),
            (TraceAnalysisDiagnosticCode.DuplicateBegin, 5L, 4L), (TraceAnalysisDiagnosticCode.DuplicateEnd, 7L, 6L),
            (TraceAnalysisDiagnosticCode.InvalidEndTime, 6L, 4L) })
        {
            var diagnostic = result.Diagnostics.Single(item => item.Code == expected.Item1);
            Require(diagnostic.LineNumber == expected.Item2 && diagnostic.RelatedLineNumber == expected.Item3,
                $"{expected.Item1}의 원본 위치가 잘못됐습니다.");
        }
        Require(result.Spans.All(span => span.Id != "orphan"), "Begin 없는 End로 작업을 만들었습니다.");
        Console.WriteLine("중복 첫 입력 유지·미완료·잘못된 시간·원본 줄 번호: 통과");
    }

    public static void VerifyParents()
    {
        var assembler = Collect(Header(), Track(), Track("other"));
        long line = 4;
        foreach (var item in new[] { ("A", "B"), ("B", "C"), ("C", "D"), ("D", "B"), ("E", "C"),
            ("missing", "absent"), ("self", "self"), ("child", "parent") })
        {
            assembler.Add(Begin(item.Item1, parent: item.Item2), line++);
            assembler.Add(End(item.Item1), line++);
        }
        assembler.Add(Begin("parent", 100, track: "other"), line++);
        assembler.Add(End("parent", 200), line++);
        var result = assembler.Build();
        Codes(result, TraceAnalysisDiagnosticCode.ParentCycle, TraceAnalysisDiagnosticCode.ParentCycle,
            TraceAnalysisDiagnosticCode.ParentCycle, TraceAnalysisDiagnosticCode.MissingParent, TraceAnalysisDiagnosticCode.SelfParent);
        var spans = result.Spans.ToDictionary(span => span.Id);
        foreach (string id in new[] { "B", "C", "D", "missing", "self" })
            Require(spans[id].ParentId is null && spans[id].OriginalParentId is not null, "잘못된 부모 연결 해제 또는 원본 보존에 실패했습니다.");
        Require(spans["A"].ParentId == "B" && spans["E"].ParentId == "C", "순환에 들어가는 정상 연결까지 해제했습니다.");
        Require(spans["child"].ParentId == "parent", "다른 Track·시간 범위 밖·뒤늦게 등장한 부모를 연결하지 못했습니다.");
        foreach (var diagnostic in result.Diagnostics.Where(item => item.Code == TraceAnalysisDiagnosticCode.ParentCycle))
            Require(diagnostic.RelatedLineNumber == spans[spans[diagnostic.SpanId!].OriginalParentId!].BeginLineNumber,
                "순환 진단의 관련 줄 번호가 다릅니다.");

        const int count = 10000;
        var deep = Collect(Header(), Track());
        for (int index = 0; index < count; index++)
        {
            string id = index.ToString();
            deep.Add(Begin(id, parent: index + 1 < count ? (index + 1).ToString() : null), index * 2L + 3);
            deep.Add(End(id), index * 2L + 4);
        }
        var deepResult = deep.Build();
        Codes(deepResult);
        Require(deepResult.Spans.Count == count, "깊은 계층에서 작업이 유실됐습니다.");

        var disjoint = Collect(Header(), Track(),
            Begin("x", parent: "y"), End("x"), Begin("y", parent: "x"), End("y"),
            Begin("u", parent: "v"), End("u"), Begin("v", parent: "u"), End("v"),
            Begin("root"), End("root"), Begin("left", parent: "root"), End("left"),
            Begin("right", parent: "root"), End("right")).Build();
        Codes(disjoint, TraceAnalysisDiagnosticCode.ParentCycle, TraceAnalysisDiagnosticCode.ParentCycle,
            TraceAnalysisDiagnosticCode.ParentCycle, TraceAnalysisDiagnosticCode.ParentCycle);
        Require(disjoint.Spans.Where(span => span.Id is "x" or "y" or "u" or "v").All(span => span.ParentId is null) &&
            disjoint.Spans.Where(span => span.Id is "left" or "right").All(span => span.ParentId == "root"),
            "독립적인 순환 검사 또는 같은 부모를 공유하는 정상 관계 처리가 잘못됐습니다.");
        Console.WriteLine("부모 누락·자기 참조·B/C/D 순환·진입 연결 유지·1만 계층: 통과");
    }

    public static void VerifyTracks()
    {
        var result = Collect(Header(), Begin("late", 20), Begin("early", 5), Begin("tie", 5),
            Begin("missing1", track: "unknown"), Begin("missing2", track: "unknown"),
            End("late", 30), End("early"), End("tie"), End("missing1"), End("missing2"),
            Track(), Track("idle")).Build();
        Codes(result, TraceAnalysisDiagnosticCode.MissingTrack, TraceAnalysisDiagnosticCode.MissingTrack);
        Require(result.Tracks.Select(track => track.Id).SequenceEqual(["main", "idle", "unknown"]), "Track 순서가 다릅니다.");
        Require(result.Tracks[0].Spans.Select(span => span.Id).SequenceEqual(["early", "tie", "late"]), "Track 내부의 시간·줄 번호 순서가 다릅니다.");
        Require(result.Tracks[1] is { IsPlaceholder: false, DefinitionLineNumber: 13 } && result.Tracks[1].Spans.Count == 0,
            "작업이 없는 정상 Track을 유지하지 못했습니다.");
        Require(result.Tracks[2] is { IsPlaceholder: true, DefinitionLineNumber: null, Name: "unknown" } &&
            result.Tracks[2].Spans.Count == 2, "같은 미정의 Track을 임시 Track 하나로 묶지 않았습니다.");
        foreach (var track in result.Tracks)
            foreach (var span in track.Spans)
                Require(ReferenceEquals(span, result.Spans.Single(item => item.Id == span.Id)), "Track과 전체 목록이 서로 다른 작업 객체를 참조합니다.");
        Console.WriteLine("뒤늦은 Track 정의·빈 Track·공통 임시 Track·동일 객체 참조: 통과");
    }

    public static void VerifySnapshots()
    {
        var assembler = Collect(Header(), Track());
        using (var document = JsonDocument.Parse("""{"nested":[{"value":"kept"}]}"""))
        {
            var args = new Dictionary<string, JsonElement> { ["payload"] = document.RootElement };
            assembler.Add(Begin("snapshot") with { Args = args }, 3);
            args.Clear();
        }
        assembler.Add(End("snapshot"), 4);
        var result = assembler.Build();
        Require(result.Spans.Single().Args!["payload"].GetProperty("nested")[0].GetProperty("value").GetString() == "kept",
            "Args가 원본 Dictionary 또는 JSON 문서 수명에 의존합니다.");
        Expect<NotSupportedException>(() => ((IList<TraceSpan>)result.Spans).Clear());
        Expect<NotSupportedException>(() => ((IList<TraceTrack>)result.Tracks).Clear());
        Expect<NotSupportedException>(() => ((IList<TraceSpan>)result.Tracks[0].Spans).Clear());
        Expect<NotSupportedException>(() => ((IList<TraceAnalysisDiagnostic>)result.Diagnostics).Clear());
        Expect<NotSupportedException>(() => ((IDictionary<string, JsonElement>)result.Spans.Single().Args!).Clear());
        Require(assembler.Build().Spans.Count == 1, "외부 변경이 다음 Build에 영향을 줬습니다.");
        Console.WriteLine("Add 시 Args 복사본 보존·결과 목록 변경 거부: 통과");
    }

    public static void VerifyInputContract()
    {
        var assembler = new TraceAssembler();
        Expect<ArgumentNullException>(() => assembler.Add(null!, 1));
        Expect<ArgumentOutOfRangeException>(() => assembler.Add(Header(), 0));
        Expect<InvalidOperationException>(() => assembler.Build());
        Expect<InvalidOperationException>(() => Collect(Header(), Header()).Build());
        var end = new TraceSessionEndRecord { TimeUs = 1, DroppedRecordCount = 0 };
        Expect<InvalidOperationException>(() => Collect(Header(), end, end).Build());
        Console.WriteLine("null·줄 번호·헤더 누락/중복·종료 중복 거부: 통과");
    }

    public static async Task VerifyFileAsync(string directory)
    {
        string path = await SampleTraceData.WriteFileAsync(directory);
        var assembler = new TraceAssembler();
        await foreach (var read in new JsonlTraceReader(new TraceReadOptions { Mode = TraceReadMode.Strict }).ReadAsync(path))
        {
            Require(read.Diagnostic is null, "정상 파일에서 Reader 진단이 발생했습니다.");
            assembler.Add(read.Record!, read.LineNumber);
        }
        var result = assembler.Build();
        Codes(result);
        Require(result.Spans.Single() is { Id: "42", Status: TraceSpanStatus.Completed, DurationUs: 7000 } &&
            result.Tracks.Single().Id == "main" && result.SessionEnd is not null, "Reader에서 분석 결과까지 연결한 결과가 다릅니다.");
        Console.WriteLine("JSONL 저장 → Reader → Assembler → 완료 구간: 통과");
    }
}

using System.Text.Json;
using Timeline.Format;
using Timeline.IO;
using Timeline.Tracing;

internal static class TraceArgsChecks
{
    public static async Task RunAsync(string directory)
    {
        VerifySnapshot();
        string path = Path.Combine(directory, $"trace-args-{Guid.NewGuid():N}.jsonl");
        TraceSpanHandle parent;

        await using (var tracer = await TimelineTracer.CreateAsync(path))
        {
            var track = tracer.CreateTrack("Args Track");
            using (var document = JsonDocument.Parse("""{"count":2,"items":["original",true]}"""))
            {
                var source = new Dictionary<string, JsonElement>
                {
                    ["payload"] = document.RootElement,
                    ["file"] = JsonSerializer.SerializeToElement("original.json"),
                    ["optional"] = JsonSerializer.SerializeToElement<string?>(null)
                };
                parent = tracer.Begin("Direct Args", track, args: source);
                Require(parent.IsValid, "Args를 가진 Begin이 접수되지 않았습니다.");
                source["file"] = JsonSerializer.SerializeToElement("changed.json");
                source.Clear();
            }
            tracer.End(parent);

            using (var document = JsonDocument.Parse("""{"nested":{"value":42}}"""))
            {
                var source = new Dictionary<string, JsonElement> { ["scope"] = document.RootElement };
                using (var scope = tracer.BeginScope("Scope Args", track, parent, source))
                {
                    Require(scope.Handle.IsValid, "Args를 가진 Scope가 접수되지 않았습니다.");
                    source.Clear();
                    // 원본 문서를 Scope가 끝나기 전에 해제해도 복사본은 유지되어야 합니다.
                    document.Dispose();
                    await Task.Yield();
                }
            }

            using (tracer.BeginScope("No Args", track)) { }
            using (tracer.BeginScope("Empty Args", track, args: new Dictionary<string, JsonElement>())) { }

            bool rejected = false;
            try
            {
                tracer.Begin("Undefined", track, args: new Dictionary<string, JsonElement>
                {
                    ["bad"] = default
                });
            }
            catch (ArgumentException exception) when (exception.ParamName == "args")
            {
                rejected = true;
            }
            Require(rejected, "Undefined가 호출 시점에 거부되지 않았습니다.");
            await tracer.FlushAsync();
        }

        var records = new List<TraceRecord>();
        await foreach (var result in new JsonlTraceReader(new TraceReadOptions { Mode = TraceReadMode.Strict }).ReadAsync(path))
        {
            if (result.Diagnostic is not null) throw new Exception(result.Diagnostic.Message);
            records.Add(result.Record ?? throw new Exception("빈 읽기 결과입니다."));
        }
        var begins = records.OfType<TraceBeginRecord>().ToDictionary(begin => begin.Name);
        Require(begins.Count == 4 && records.Count == 11, "거부한 Begin 또는 불필요한 레코드가 저장됐습니다.");
        var direct = begins["Direct Args"].Args!;
        Require(direct.Count == 3 && direct["file"].GetString() == "original.json" &&
                direct["optional"].ValueKind == JsonValueKind.Null &&
                direct["payload"].GetProperty("count").GetInt32() == 2 &&
                direct["payload"].GetProperty("items")[0].GetString() == "original" &&
                direct["payload"].GetProperty("items")[1].GetBoolean(),
                "Begin의 Args가 원본 변경·해제 후 유지되지 않았습니다.");
        var scopeBegin = begins["Scope Args"];
        Require(scopeBegin.ParentId == parent.Id &&
                scopeBegin.Args!["scope"].GetProperty("nested").GetProperty("value").GetInt32() == 42,
                "Scope의 Parent 또는 Args가 전달되지 않았습니다.");
        Require(begins["No Args"].Args is null && begins["Empty Args"].Args is { Count: 0 },
                "null과 빈 Args가 구분되지 않았습니다.");
        Require(records.Last() is TraceSessionEndRecord { DroppedRecordCount: 0 }, "정상 종료되지 않았습니다.");
        Console.WriteLine("Args 복사·원본 Dictionary 변경·JSON 문서 해제: 통과");
        Console.WriteLine("Begin/Scope Args·부모 참조·null/빈 값·Undefined 거부 후 재사용: 통과");
    }

    private static void VerifySnapshot()
    {
        IReadOnlyDictionary<string, JsonElement> snapshot;
        using (var document = JsonDocument.Parse("""{"nested":[{"value":"kept"}]}"""))
        {
            var source = new Dictionary<string, JsonElement> { ["data"] = document.RootElement };
            snapshot = TimelineTracer.CopyArgs(source)!;
            source.Clear();
        }
        Require(snapshot.Count == 1 &&
                snapshot["data"].GetProperty("nested")[0].GetProperty("value").GetString() == "kept",
                "복사본이 원본 Dictionary 또는 JsonDocument에 의존합니다.");
        Require(TimelineTracer.CopyArgs(null) is null, "null Args의 복사 결과가 null이 아닙니다.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

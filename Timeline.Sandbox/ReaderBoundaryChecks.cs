using System.Text;
using Timeline.Format;
using Timeline.IO;

internal static class ReaderBoundaryChecks
{
    public static async Task RunAsync(string directory)
    {
        string header = TraceJsonSerializer.Serialize(new TraceHeaderRecord
        {
            OriginUtc = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero)
        });
        string track = TraceJsonSerializer.Serialize(new TraceTrackRecord { Id = "main", Name = "Main" });
        string end = TraceJsonSerializer.Serialize(new TraceSessionEndRecord { TimeUs = 1, DroppedRecordCount = 0 });
        string[] normal = ["1:TraceHeaderRecord", "2:TraceTrackRecord", "3:TraceSessionEndRecord"];

        async Task<string> CreateFileAsync(string name, string content)
        {
            string path = Path.Combine(directory, $"boundary-{name}-{Guid.NewGuid():N}.jsonl");
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(false));
            return path;
        }

        async Task CheckAsync(string name, string content, int limit,
            string[] strictExpected, string[] recoveryExpected)
        {
            string path = await CreateFileAsync(name, content);
            foreach (TraceReadMode mode in new[] { TraceReadMode.Strict, TraceReadMode.Recovery })
            {
                var reader = new JsonlTraceReader(new TraceReadOptions { Mode = mode, MaxLineLength = limit });
                var actual = new List<string>();
                await foreach (TraceReadResult result in reader.ReadAsync(path))
                {
                    actual.Add(Describe(result));
                }

                string[] expected = mode == TraceReadMode.Strict ? strictExpected : recoveryExpected;
                if (!actual.SequenceEqual(expected))
                {
                    throw new Exception($"{name}/{mode}\n예상: {string.Join(",", expected)}\n실제: {string.Join(",", actual)}");
                }
                AssertReleased(path);
                Console.WriteLine($"{name} / {mode}: 통과");
            }
        }

        foreach (string newline in new[] { "\n", "\r\n", "\r" })
        {
            string label = newline.Replace("\r", "CR").Replace("\n", "LF");
            await CheckAsync(label, string.Join(newline, header, track, end) + newline, 8192, normal, normal);
            await CheckAsync(label + "-no-final-newline", string.Join(newline, header, track, end), 8192, normal, normal);
        }

        // 4096문자 버퍼의 마지막 위치에 CR, 다음 버퍼의 첫 위치에 LF를 배치합니다.
        await CheckAsync("buffer-boundary-CRLF", header.PadRight(4095) + "\r\n" + track + "\r\n" + end,
            8192, normal, normal);
        await CheckAsync("exact-line-limit", header + "\n" + track.PadRight(256) + "\n" + end,
            256, normal, normal);

        foreach (int length in new[] { 257, 10000 })
        {
            await CheckAsync($"overlong-{length}", header + "\n" + track.PadRight(length) + "\n" + track + "\n" + end,
                256,
                ["1:TraceHeaderRecord", "2:LineTooLong:True"],
                ["1:TraceHeaderRecord", "2:LineTooLong:False", "3:TraceTrackRecord", "4:TraceSessionEndRecord"]);
        }

        await CheckAsync("overlong-at-EOF", header + "\n" + new string('x', 10000), 256,
            ["1:TraceHeaderRecord", "2:LineTooLong:True"],
            ["1:TraceHeaderRecord", "2:LineTooLong:False", "2:MissingSessionEnd:False"]);

        foreach (var invalid in new[]
        {
            (Name: "broken-json", Text: "{", Code: "InvalidJson"),
            (Name: "blank-line", Text: "", Code: "InvalidJson"),
            (Name: "missing-field", Text: """{"type":"end","id":"42"}""", Code: "InvalidRecord"),
            (Name: "negative-time", Text: """{"type":"end","id":"42","time":-1}""", Code: "InvalidRecord")
        })
        {
            await CheckAsync(invalid.Name, header + "\n" + invalid.Text + "\n" + track + "\n" + end, 8192,
                ["1:TraceHeaderRecord", $"2:{invalid.Code}:True"],
                ["1:TraceHeaderRecord", $"2:{invalid.Code}:False", "3:TraceTrackRecord", "4:TraceSessionEndRecord"]);
        }

        string normalPath = await CreateFileAsync("lifetime", string.Join("\n", header, track, end));
        var lifetimeReader = new JsonlTraceReader();

        foreach (bool cancelBeforeStart in new[] { true, false })
        {
            using var cancellation = new CancellationTokenSource();
            if (cancelBeforeStart) cancellation.Cancel();
            int count = 0;
            bool canceled = false;
            try
            {
                // WithCancellation을 통해 전달한 취소 토큰도 작동하는지 확인합니다.
                await foreach (var result in lifetimeReader.ReadAsync(normalPath).WithCancellation(cancellation.Token))
                {
                    count++;
                    cancellation.Cancel();
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                canceled = true;
            }
            if (!canceled || count != (cancelBeforeStart ? 0 : 1))
                throw new Exception("취소 시점과 결과 수가 예상과 다릅니다.");
            AssertReleased(normalPath);
            Console.WriteLine($"취소 / 시작 전={cancelBeforeStart}: 통과");
        }

        await foreach (var result in lifetimeReader.ReadAsync(normalPath))
        {
            break;
        }
        AssertReleased(normalPath);
        Console.WriteLine("순회 break 이후 파일 해제: 통과");

        var marker = new InvalidOperationException("소비자 예외 테스트");
        try
        {
            await foreach (var result in lifetimeReader.ReadAsync(normalPath))
            {
                throw marker;
            }
        }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, marker))
        {
            AssertReleased(normalPath);
            Console.WriteLine("소비자 예외 이후 파일 해제: 통과");
        }

        string invalidUtf8Path = await CreateFileAsync("invalid-utf8", "");
        await File.WriteAllBytesAsync(invalidUtf8Path, [0xFF]);
        bool decodingFailed = false;
        try
        {
            await foreach (var result in lifetimeReader.ReadAsync(invalidUtf8Path)) { }
        }
        catch (DecoderFallbackException)
        {
            decodingFailed = true;
        }
        if (!decodingFailed) throw new Exception("잘못된 UTF-8이 거부되지 않았습니다.");
        AssertReleased(invalidUtf8Path);
        Console.WriteLine("디코딩 예외 이후 파일 해제: 통과");
        Console.WriteLine("Reader 경계·복구 30개 및 취소·자원 해제 5개 검증 완료");
    }

    private static string Describe(TraceReadResult result)
    {
        if (result.Record is TraceRecord record)
            return $"{result.LineNumber}:{record.GetType().Name}";
        if (result.Diagnostic is TraceReadDiagnostic diagnostic)
            return $"{diagnostic.LineNumber}:{diagnostic.Code}:{diagnostic.IsFatal}";
        throw new Exception("레코드와 진단이 모두 없는 결과입니다.");
    }

    private static void AssertReleased(string path)
    {
        // 배타적으로 다시 열어 Reader의 파일 핸들이 남아 있지 않은지 확인합니다.
        using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}

using Timeline.Format;
using Timeline.IO;

internal static class SampleTraceData
{
    public static TraceRecord[] CreateRecords() =>
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

    public static async Task<string> WriteFileAsync(string directory)
    {
        string path = Path.Combine(directory, $"sample-{Guid.NewGuid():N}.jsonl");
        await using var writer = new JsonlTraceWriter(path);
        foreach (TraceRecord record in CreateRecords())
            await writer.WriteAsync(record);
        return path;
    }
}

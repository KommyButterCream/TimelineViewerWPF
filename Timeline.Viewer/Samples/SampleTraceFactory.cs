using System;
using System.Collections.Generic;
using Timeline.Viewer.ViewModels;

namespace Timeline.Viewer.Samples
{
    public static class SampleTraceFactory
    {
        public static IReadOnlyList<TrackItemViewModel> CreateTracks()
        {
            var mainTrack = new TrackItemViewModel("main", "Main thread");

            mainTrack.Spans.Add(new SpanItemViewModel(
                "span-1",
                "Load configuration",
                12_000,
                52_000));

            mainTrack.Spans.Add(new SpanItemViewModel(
                "span-2",
                "Create main window",
                64_000,
                136_000));

            mainTrack.Spans.Add(new SpanItemViewModel(
                "span-3",
                "First render",
                148_000,
                211_000));

            mainTrack.Spans.Add(new SpanItemViewModel(
                "span-overlap-1", "Startup background task", 20_000, 100_000));
            mainTrack.Spans.Add(new SpanItemViewModel(
                "span-overlap-2", "Parallel initialization", 30_000, 45_000));

            var workerTrack = new TrackItemViewModel("workers", "Worker pool");

            workerTrack.Spans.Add(new SpanItemViewModel(
                "span-4",
                "Build indexes",
                55_000,
                151_000));

            workerTrack.Spans.Add(new SpanItemViewModel(
                "span-5",
                "Warm cache",
                158_000,
                220_000));

            var fileTrack = new TrackItemViewModel("file-io", "File I/O");

            fileTrack.Spans.Add(new SpanItemViewModel(
                "span-6",
                "Read blocks",
                32_000,
                117_500));

            fileTrack.Spans.Add(new SpanItemViewModel(
                "span-instant", "Checkpoint", 140_000, 140_000));
            workerTrack.Spans.Add(new SpanItemViewModel(
                "span-open", "Background probe", 170_000, null));

            return new[]
            {
                mainTrack,
                workerTrack,
                fileTrack
            };
        }
    }
}

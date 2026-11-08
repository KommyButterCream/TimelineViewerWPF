using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text.Json;
using Timeline.Analysis;

namespace Timeline.Viewer.ViewModels
{
    public sealed class SpanItemViewModel
    {
        private static readonly JsonSerializerOptions ArgsJsonOptions = new()
        {
            WriteIndented = true
        };

        public SpanItemViewModel(
            string id,
            string name,
            long startTimeUs,
            long? endTimeUs,
            IReadOnlyDictionary<string, JsonElement>? args = null,
            string? sourceFilePath = null,
            long? beginLineNumber = null,
            long? endLineNumber = null,
            TraceSpanStatus? analysisStatus = null)
        {
            if (startTimeUs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startTimeUs));
            }

            if (endTimeUs is long end && end < startTimeUs)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(endTimeUs),
                    "종료 시간은 시작 시간보다 빠를 수 없습니다.");
            }

            if (beginLineNumber is < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(beginLineNumber));
            }

            if (endLineNumber is < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(endLineNumber));
            }

            Id = id;
            Name = name;
            StartTimeUs = startTimeUs;
            EndTimeUs = endTimeUs;
            SourceFilePath = sourceFilePath;
            BeginLineNumber = beginLineNumber;
            EndLineNumber = endLineNumber;
            AnalysisStatus = analysisStatus;

            ArgsText = args is null || args.Count == 0
                ? "Args 없음"
                : JsonSerializer.Serialize(args, ArgsJsonOptions);
        }

        public string Id { get; }
        public string Name { get; }
        public long StartTimeUs { get; }
        public long? EndTimeUs { get; }
        public long? DurationUs => EndTimeUs - StartTimeUs;
        public double? DurationMs => DurationUs / 1000.0;
        public bool IsMissingEnd => EndTimeUs is null;
        public bool IsInstant => DurationUs == 0;

        public TraceSpanStatus? AnalysisStatus { get; }
        public string DurationText => DurationMs is double duration
            ? duration.ToString("0.###", CultureInfo.CurrentCulture) + " ms"
            : "— · 종료 누락";

        public string StartTimeText => (StartTimeUs / 1000.0).ToString("0.###", CultureInfo.CurrentCulture) + " ms";

        public string EndTimeText => EndTimeUs is long endTimeUs ?
                 (endTimeUs / 1000.0).ToString("0.###", CultureInfo.CurrentCulture) + " ms"
                : "— · 종료 누락";

        public string StatusText =>
            AnalysisStatus == TraceSpanStatus.InvalidEnd
                ? "종료 시각 오류"
                : IsMissingEnd
                    ? "종료 누락"
                    : IsInstant
                        ? "순간 이벤트"
                        : "완료";
        public string ArgsText { get; }
        public string? SourceFilePath { get; }
        public long? BeginLineNumber { get; }
        public long? EndLineNumber { get; }

        public string SourceFileText =>
            string.IsNullOrWhiteSpace(SourceFilePath)
                ? "원본 파일 정보 없음"
                : SourceFilePath;

        public string BeginLineText =>
            BeginLineNumber is long line
                ? line.ToString(CultureInfo.CurrentCulture) + "행"
                : "—";

        public string EndLineText =>
            EndLineNumber is long line
                ? line.ToString(CultureInfo.CurrentCulture) + "행"
                : "—";
    }
}

using System;
using System.Globalization;

namespace Timeline.Viewer.ViewModels
{
    public sealed class SpanItemViewModel
    {
        public SpanItemViewModel(
            string id,
            string name,
            long startTimeUs,
            long? endTimeUs)
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

            Id = id;
            Name = name;
            StartTimeUs = startTimeUs;
            EndTimeUs = endTimeUs;
        }

        public string Id { get; }
        public string Name { get; }
        public long StartTimeUs { get; }
        public long? EndTimeUs { get; }
        public long? DurationUs => EndTimeUs - StartTimeUs;
        public double? DurationMs => DurationUs / 1000.0;
        public bool IsMissingEnd => EndTimeUs is null;
        public bool IsInstant => DurationUs == 0;
        public string DurationText => DurationMs is double duration
            ? duration.ToString("0.###", CultureInfo.CurrentCulture) + " ms"
            : "— · 종료 누락";
    }
}

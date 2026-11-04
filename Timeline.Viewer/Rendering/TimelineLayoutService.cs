using System;
using System.Collections.Generic;
using System.Linq;
using Timeline.Viewer.ViewModels;

namespace Timeline.Viewer.Rendering
{
    public sealed class TimelineLayoutService
    {
        public TrackLayout BuildTrackLayout(IEnumerable<SpanItemViewModel> spans)
        {
            ArgumentNullException.ThrowIfNull(spans);

            var placements = new List<SpanPlacement>();

            var laneEndTimes = new List<long>();

            foreach (SpanItemViewModel span in spans.OrderBy(span => span.StartTimeUs))
            {
                int laneIndex = FindAvailableLane(laneEndTimes, span.StartTimeUs);

                if (laneIndex < 0)
                {
                    laneIndex = laneEndTimes.Count;

                    laneEndTimes.Add(span.EndTimeUs ?? long.MaxValue);
                }
                else
                {
                    laneEndTimes[laneIndex] = span.EndTimeUs ?? long.MaxValue;
                }

                placements.Add(new SpanPlacement(span, laneIndex));
            }

            int laneCount = Math.Max(1, laneEndTimes.Count);

            return new TrackLayout(placements.AsReadOnly(), laneCount);
        }

        private static int FindAvailableLane(IReadOnlyList<long> laneEndTimes, long startTimeUs)
        {
            for (int i = 0; i < laneEndTimes.Count; i++)
            {
                if (laneEndTimes[i] <= startTimeUs)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

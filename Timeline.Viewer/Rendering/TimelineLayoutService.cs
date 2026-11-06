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

            // 시작 순서로 정렬 후 span 의 Lane 을 처리한다.
            foreach (SpanItemViewModel span in spans.OrderBy(span => span.StartTimeUs))
            {
                int laneIndex = FindAvailableLane(laneEndTimes, span.StartTimeUs);

                if (laneIndex < 0)
                {
                    // 빈 Lane 이 없으므로 Lane 을 새롭게 만들어야 한다.
                    laneIndex = laneEndTimes.Count;

                    laneEndTimes.Add(span.EndTimeUs ?? long.MaxValue);
                }
                else
                {
                    // 기존 Lane 끝에 Span 이 추가 될 것이므로 EndTime 을 갱신한다.
                    laneEndTimes[laneIndex] = span.EndTimeUs ?? long.MaxValue;
                }

                placements.Add(new SpanPlacement(span, laneIndex));
            }

            int laneCount = Math.Max(1, laneEndTimes.Count);

            return new TrackLayout(placements.AsReadOnly(), laneCount);
        }

        private static int FindAvailableLane(IReadOnlyList<long> laneEndTimes, long startTimeUs)
        {
            // 0번 Lane 부터 사용 가능한 Lane 번호를 반환.

            for (int i = 0; i < laneEndTimes.Count; i++)
            {
                if (laneEndTimes[i] <= startTimeUs)
                {
                    // 같은 lane 에 놓아도 겹치지 않는다.
                    return i;
                }
            }

            // 비어 있는 lane 이 없다. lane 을 새로 등록해야한다.
            return -1;
        }
    }
}

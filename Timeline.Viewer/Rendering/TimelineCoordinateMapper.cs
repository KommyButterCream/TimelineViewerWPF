using System;
using System.Collections.Generic;

namespace Timeline.Viewer.Rendering
{
    public sealed class TimelineCoordinateMapper
    {
        public TimelineCoordinateMapper(
            long visibleStartTimeUs,
            long visibleDurationUs,
            double viewportWidth)
        {
            if (visibleStartTimeUs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(visibleStartTimeUs));
            }

            if (visibleDurationUs <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(visibleDurationUs));
            }

            if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(viewportWidth));
            }

            VisibleStartTimeUs = visibleStartTimeUs;
            VisibleDurationUs = visibleDurationUs;
            ViewportWidth = viewportWidth;
        }

        public long VisibleStartTimeUs { get; }
        public long VisibleDurationUs { get; }
        public double ViewportWidth { get; }
        public double TimeToX(long timeUs)
        {
            if (timeUs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeUs));
            }

            long relativeTimeUs = timeUs - VisibleStartTimeUs;

            return (double)relativeTimeUs / VisibleDurationUs * ViewportWidth;
        }

        public double DurationToWidth(long durationUs)
        {
            if (durationUs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationUs));
            }

            return (double)durationUs / VisibleDurationUs * ViewportWidth;
        }
    }
}
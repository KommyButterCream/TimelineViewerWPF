using System.Collections.Generic;
using Timeline.Viewer.ViewModels;

namespace Timeline.Viewer.Rendering
{
    public sealed class SpanPlacement
    {
        public SpanPlacement(SpanItemViewModel span, int laneIndex)
        {
            Span = span;
            LaneIndex = laneIndex;
        }

        public SpanItemViewModel Span { get; }

        public int LaneIndex { get; }
    }
}

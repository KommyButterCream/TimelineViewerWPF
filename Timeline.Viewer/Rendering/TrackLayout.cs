using System.Collections.Generic;

namespace Timeline.Viewer.Rendering
{
    public sealed class TrackLayout
    {
        public TrackLayout(IReadOnlyList<SpanPlacement> placements, int laneCount)
        {
            Placements = placements;
            LaneCount = laneCount;
        }

        public IReadOnlyList<SpanPlacement> Placements { get; }

        public int LaneCount { get; }
    }
}

using System.Windows.Media;

namespace Timeline.Viewer.Rendering
{
    public static class TrackPalette
    {
        private static readonly Brush[] Brushes = CreateBrushes();

        public static Brush GetBrush(string trackId)
        {
            // Stable across processes and Track ordering (unlike string.GetHashCode).
            uint hash = 2166136261;
            foreach (char character in trackId)
            {
                hash = unchecked((hash ^ character) * 16777619);
            }

            return Brushes[hash % (uint)Brushes.Length];
        }

        private static Brush[] CreateBrushes()
        {
            string[] colors = { "#56B9C6", "#88BD74", "#D7B064", "#B99ADD", "#79A8D4", "#D68F99" };
            return colors.Select(color =>
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                brush.Freeze();
                return (Brush)brush;
            }).ToArray();
        }
    }
}

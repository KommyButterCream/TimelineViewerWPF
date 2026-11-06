using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Timeline.Viewer;
using Timeline.Viewer.Controls;
using Timeline.Viewer.Models;
using Timeline.Viewer.Rendering;
using Timeline.Viewer.Services;
using Timeline.Viewer.ViewModels;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static void Main(string[] args)
    {
        var service = new TimelineLayoutService();
        var a = new SpanItemViewModel("a", "A", 10, 80);
        var b = new SpanItemViewModel("b", "B", 20, 40);
        var c = new SpanItemViewModel("c", "C", 40, 60);
        TrackLayout layout = service.BuildTrackLayout(new[] { c, b, a });
        Check(layout.LaneCount == 2, "unordered overlap uses two lanes");
        Check(layout.Placements[2].LaneIndex == 1, "touching endpoints reuse lane");
        Check(service.BuildTrackLayout(Array.Empty<SpanItemViewModel>()).LaneCount == 1, "empty track keeps row");
        var open = new SpanItemViewModel("open", "Open", 0, null);
        Check(open.DurationUs is null && open.DurationMs is null && open.IsMissingEnd, "missing end remains unknown");
        Check(service.BuildTrackLayout(new[] { open, a }).LaneCount == 2, "open span reserves lane");
        var instant = new SpanItemViewModel("instant", "Instant", 240_000, 240_000);
        Check(instant.IsInstant && instant.DurationUs == 0, "instant preserves zero duration");
        Check(service.BuildTrackLayout(new[] { a, b, new SpanItemViewModel("d", "D", 30, 50) }).LaneCount == 3,
            "three concurrent spans use three lanes");
        var mapper = new TimelineCoordinateMapper(20_000, 240_000, 960);
        Check(mapper.TimeToX(12_000) == -32, "offscreen coordinates remain negative");
        Check(mapper.DurationToWidth(40_000) == 160, "duration mapping");
        Check(new TimelineCoordinateMapper(20_000, 240_000, 480).DurationToWidth(40_000) == 80, "resize scales width");
        long origin = 9_007_199_254_000_000;
        Check(new TimelineCoordinateMapper(origin, 100, 100).TimeToX(origin + 1) == 1, "large origin retains relative precision");
        Check(ReferenceEquals(TrackPalette.GetBrush("main"), TrackPalette.GetBrush("main")), "stable palette");
        Check(new[] { "main", "workers", "file-io" }.Select(TrackPalette.GetBrush).Distinct().Count() == 3, "sample tracks have distinct colors");

        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow();
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = window.DataContext;
        string? output = args.FirstOrDefault();
        byte[]? previousThemePixels = null;
        foreach (AppTheme theme in Enum.GetValues<AppTheme>())
        {
            var viewModel = (MainWindowViewModel)content.DataContext;
            if (viewModel.CurrentTheme != theme) viewModel.ToggleThemeCommand.Execute(null);
            // Detached content needs an explicit resource scope for offscreen theme invalidation.
            content.Resources = new ResourceDictionary
            {
                Source = new Uri($"/Timeline.Viewer;component/Themes/{theme}.xaml", UriKind.Relative)
            };
            foreach (int width in new[] { 900, 1280, 1800 })
            {
                content.Measure(new Size(width, 760));
                content.Arrange(new Rect(0, 0, width, 760));
                content.UpdateLayout();
                var bitmap = Render(content, width, 760);
                Check(AlphaAt(bitmap, width / 2, 100) > 0, $"{theme} content rendered at {width} DIP");
                if (output is not null && width == 1280)
                {
                    Directory.CreateDirectory(output);
                    Save(bitmap, Path.Combine(output, $"stage04-{theme}.png"));
                }
                if (width == 1280)
                {
                    byte[] pixels = Pixels(bitmap);
                    if (previousThemePixels is not null)
                        Check(!previousThemePixels.SequenceEqual(pixels), "theme changes rendered pixels");
                    previousThemePixels = pixels;
                }
            }
        }

        var track = new TrackItemViewModel("main", "Boundary checks");
        track.Spans.Add(new SpanItemViewModel("start", "Start", 0, 0));
        track.Spans.Add(instant);
        var tracks = new ObservableCollection<TrackItemViewModel> { track };
        var surface = new TimelineSurface { Tracks = tracks, AxisBrush = Brushes.Gray, GridLineBrush = Brushes.DimGray };
        surface.Measure(new Size(600, 180));
        surface.Arrange(new Rect(0, 0, 600, 180));
        surface.UpdateLayout();
        var first = Render(surface, 600, 180);
        Check(AlphaAt(first, 157, 50) > 0 && AlphaAt(first, 583, 50) > 0, "zero markers visible at both edges");
        track.Spans.Add(new SpanItemViewModel("added", "Added", 100_000, 120_000));
        surface.UpdateLayout();
        var added = Render(surface, 600, 180);
        Check(!Pixels(first).SequenceEqual(Pixels(added)), "span addition invalidates drawing");
        track.Spans.RemoveAt(2);
        surface.UpdateLayout();
        Check(Pixels(first).SequenceEqual(Pixels(Render(surface, 600, 180))), "span removal restores drawing");
        track.Spans.Add(new SpanItemViewModel("late", "Late", 315_000, 315_000));
        surface.UpdateLayout();
        surface.HorizontalAxis.ScrollTo(surface.HorizontalAxis.MaxOffset);
        surface.UpdateLayout();
        var scrolled = Render(surface, 600, 180);
        // At 75 ms offset, the 120 ms grid line and 240 ms marker move left together.
        Check(AlphaAt(scrolled, 236, 95) > 0 && AlphaAt(scrolled, 450, 50) > 0
            && AlphaAt(scrolled, 583, 50) > 0, "scroll aligns ticks and markers through the right edge");
        var zoom = typeof(TimelineSurface).GetMethod("ZoomAtPointer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        double markerX = 156 + (240_000 - 75_000) / 240_000.0 * surface.HorizontalAxis.Viewport;
        double oldExtent = surface.HorizontalAxis.Extent;
        zoom.Invoke(surface, new object[] { markerX, 120 });
        surface.UpdateLayout();
        Check(Math.Abs(surface.HorizontalAxis.Extent / oldExtent - 1.25) < 0.000001
            && AlphaAt(Render(surface, 600, 180), 450, 50) > 0, "zoom keeps marker under pointer");
        zoom.Invoke(surface, new object[] { markerX, -120 });
        surface.UpdateLayout();
        Check(Pixels(scrolled).SequenceEqual(Pixels(Render(surface, 600, 180))), "zoom round trip restores drawing");
        tracks.Clear();
        surface.UpdateLayout();
        Check(!Pixels(first).SequenceEqual(Pixels(Render(surface, 600, 180))), "track reset invalidates drawing");
        CheckLargeSession(window, content);
        window.Close();
        Console.WriteLine($"PASS: {_checks} checks");
    }

    private static void CheckLargeSession(MainWindow window, FrameworkElement content)
    {
        var vm = (MainWindowViewModel)window.DataContext;
        vm.Tracks.Clear();
        const long hourUs = 3_600_000_000;
        for (int i = 0; i < 128; i++)
        {
            var track = new TrackItemViewModel($"track-{i}", $"Worker track {i}");
            long time = hourUs * i / 127;
            track.Spans.Add(new SpanItemViewModel($"event-{i}", "Checkpoint", time, time));
            vm.Tracks.Add(track);
        }

        void Resize(int width, int height)
        {
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            Render(content, width, height);
        }

        Resize(900, 560);
        var timeline = (TimelineSurface)window.FindName("TimelineView");
        var horizontal = timeline.HorizontalAxis;
        var vertical = timeline.VerticalAxis;
        Check(vertical.Viewport > 100 && vertical.MaxOffset > 0 && horizontal.MaxOffset > 0,
            "128 tracks and legend leave a usable chart in the minimum window");
        Check(horizontal.Offset == 0 && vertical.Offset == 0, "initial left and top bounds");
        horizontal.ScrollTo(horizontal.Extent);
        vertical.ScrollTo(vertical.Extent);
        Resize(1280, 760);
        Check(Math.Abs(horizontal.Offset - horizontal.MaxOffset) < 0.001 && vertical.Offset == vertical.MaxOffset,
            "right and bottom bounds survive larger viewport");
        Resize(900, 560);
        Check(Math.Abs(horizontal.Offset - horizontal.MaxOffset) < 0.001 && vertical.Offset <= vertical.MaxOffset,
            "right boundary survives smaller viewport");
        vertical.ScrollTo(vertical.MaxOffset);
        double lastRowOffset = vertical.Offset;
        timeline.FitToView();
        content.UpdateLayout();
        Check(horizontal.Offset == 0 && horizontal.MaxOffset == 0 && horizontal.Extent == horizontal.Viewport,
            "one hour fits exactly from zero to last event");
        Check(vertical.Offset == lastRowOffset, "fit preserves the last visible track");
        vm.Tracks.Clear();
        timeline.FitToView();
        content.UpdateLayout();
        Check(horizontal.MaxOffset == 0 && vertical.Offset == 0 && vertical.MaxOffset == 0,
            "empty session fits and clamps both axes");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        _checks++;
    }

    private static RenderTargetBitmap Render(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    private static byte AlphaAt(BitmapSource bitmap, int x, int y) => Pixels(bitmap)[(y * bitmap.PixelWidth + x) * 4 + 3];

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}

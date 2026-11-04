using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Timeline.Viewer.Rendering;
using Timeline.Viewer.ViewModels;

namespace Timeline.Viewer.Controls
{
    public sealed class TimelineSurface : FrameworkElement
    {
        private const long VisibleStartTimeUs = 0;
        private const long VisibleDurationUs = 240_000;
        private const double HorizontalPadding = 16;
        private const double TrackLabelWidth = 140;
        private const double AxisHeight = 28;
        private const double TrackVerticalPadding = 18;
        private const double LaneGap = 8;
        private const double SpanHeight = 28;
        private const double ChartLeft = HorizontalPadding + TrackLabelWidth;

        private readonly HashSet<INotifyCollectionChanged> _observedSpanCollections = new();
        private readonly TimelineLayoutService _layoutService = new();

        public static readonly DependencyProperty TracksProperty = DependencyProperty.Register(
            nameof(Tracks),
            typeof(IEnumerable<TrackItemViewModel>),
            typeof(TimelineSurface),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnTracksChanged));

        public static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register(
            nameof(AxisBrush),
            typeof(Brush),
            typeof(TimelineSurface),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GridLineBrushProperty = DependencyProperty.Register(
            nameof(GridLineBrush),
            typeof(Brush),
            typeof(TimelineSurface),
            new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

        public TimelineSurface()
        {
            ClipToBounds = true;
        }

        public IEnumerable<TrackItemViewModel>? Tracks
        {
            get => (IEnumerable<TrackItemViewModel>?)GetValue(TracksProperty);
            set => SetValue(TracksProperty, value);
        }

        public Brush? AxisBrush
        {
            get => (Brush?)GetValue(AxisBrushProperty);
            set => SetValue(AxisBrushProperty, value);
        }

        public Brush? GridLineBrush
        {
            get => (Brush?)GetValue(GridLineBrushProperty);
            set => SetValue(GridLineBrushProperty, value);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            double viewportWidth = ActualWidth - ChartLeft - HorizontalPadding;

            if (!double.IsFinite(viewportWidth)
                || viewportWidth <= 0
                || ActualHeight <= 0)
            {
                return;
            }

            var mapper = new TimelineCoordinateMapper(VisibleStartTimeUs, VisibleDurationUs, viewportWidth);

            DrawTimeAxis(drawingContext, mapper, viewportWidth);

            if (Tracks is null)
            {
                return;
            }

            double rowTop = AxisHeight;
            foreach (TrackItemViewModel track in Tracks)
            {
                TrackLayout layout = _layoutService.BuildTrackLayout(track.Spans);
                double trackHeight = GetTrackHeight(layout);
                DrawTrack(drawingContext, mapper, track, layout, rowTop, trackHeight);
                rowTop += trackHeight;
            }
        }

        private static void OnTracksChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            var surface = (TimelineSurface)dependencyObject;

            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                CollectionChangedEventManager.RemoveHandler(oldCollection, surface.OnTracksCollectionChanged);
            }

            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                CollectionChangedEventManager.AddHandler(newCollection, surface.OnTracksCollectionChanged);
            }

            surface.RefreshSpanSubscriptions();
        }

        private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshSpanSubscriptions();

            InvalidateVisual();
        }

        private void RefreshSpanSubscriptions()
        {
            foreach (INotifyCollectionChanged collection in _observedSpanCollections)
            {
                CollectionChangedEventManager.RemoveHandler(collection, OnSpansCollectionChanged);
            }

            _observedSpanCollections.Clear();

            if (Tracks is null)
            {
                return;
            }

            foreach (TrackItemViewModel track in Tracks)
            {
                if (_observedSpanCollections.Add(track.Spans))
                {
                    CollectionChangedEventManager.AddHandler(track.Spans, OnSpansCollectionChanged);
                }
            }
        }

        private void OnSpansCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            InvalidateVisual();
        }

        private static double GetTrackHeight(TrackLayout layout)
        {
            return TrackVerticalPadding * 2
                + layout.LaneCount * SpanHeight
                + (layout.LaneCount - 1) * LaneGap;
        }

        private void DrawTrack(
            DrawingContext drawingContext,
            TimelineCoordinateMapper mapper,
            TrackItemViewModel track,
            TrackLayout layout,
            double rowTop,
            double trackHeight)
        {
            Brush spanBrush = TrackPalette.GetBrush(track.Id);
            Brush? textBrush = AxisBrush;
            Brush? lineBrush = GridLineBrush;

            if (spanBrush is null || textBrush is null || lineBrush is null)
            {
                return;
            }

            var label = new FormattedText(
                track.Name,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = TrackLabelWidth - 12,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };

            drawingContext.DrawText(label, new Point(
                HorizontalPadding,
                rowTop + (trackHeight - label.Height) / 2));

            drawingContext.DrawLine(new Pen(lineBrush, 1),
                new Point(HorizontalPadding, rowTop + trackHeight),
                new Point(ActualWidth - HorizontalPadding, rowTop + trackHeight));

            // Keep spans inside the time area, including when a span extends past its bounds.
            drawingContext.PushClip(new RectangleGeometry(new Rect(
                ChartLeft, rowTop,
                ActualWidth - ChartLeft - HorizontalPadding, trackHeight)));

            foreach (SpanPlacement placement in layout.Placements)
            {
                SpanItemViewModel span = placement.Span;
                double spanTop = rowTop + TrackVerticalPadding
                    + placement.LaneIndex * (SpanHeight + LaneGap);

                DrawSpan(drawingContext, mapper, span, spanBrush, spanTop);
            }

            drawingContext.Pop();
        }

        private void DrawSpan(
            DrawingContext drawingContext,
            TimelineCoordinateMapper mapper,
            SpanItemViewModel span,
            Brush brush,
            double top)
        {
            long visibleEnd = VisibleStartTimeUs + VisibleDurationUs;
            if (span.StartTimeUs > visibleEnd || span.EndTimeUs < VisibleStartTimeUs)
            {
                return;
            }

            double x = ChartLeft + mapper.TimeToX(span.StartTimeUs);
            if (span.IsInstant)
            {
                // The marker has a visible width; the data duration remains exactly zero.
                drawingContext.DrawLine(new Pen(brush, 3),
                    new Point(x, top), new Point(x, top + SpanHeight));
                drawingContext.DrawEllipse(brush, null,
                    new Point(x, top + SpanHeight / 2), 4, 4);
                return;
            }

            double left = Math.Max(ChartLeft, x);
            double right = Math.Min(ActualWidth - HorizontalPadding,
                ChartLeft + mapper.TimeToX(span.EndTimeUs ?? visibleEnd));
            if (right <= left)
            {
                return;
            }

            var bounds = new Rect(left, top, right - left, SpanHeight);
            drawingContext.PushClip(new RectangleGeometry(bounds));
            if (span.IsMissingEnd)
            {
                // Visible range end is only a drawing boundary, never an inferred EndTimeUs.
                drawingContext.PushOpacity(0.16);
                drawingContext.DrawRectangle(brush, null, bounds);
                drawingContext.Pop();
                drawingContext.PushOpacity(0.45);
                var hatchPen = new Pen(brush, 1);
                for (double hatchX = left - SpanHeight; hatchX < right; hatchX += 8)
                {
                    drawingContext.DrawLine(hatchPen,
                        new Point(hatchX, top + SpanHeight),
                        new Point(hatchX + SpanHeight, top));
                }
                drawingContext.Pop();
                var openPen = new Pen(brush, 1) { DashStyle = DashStyles.Dash };
                drawingContext.DrawLine(openPen, new Point(left, top), new Point(right, top));
                drawingContext.DrawLine(openPen,
                    new Point(left, top + SpanHeight), new Point(right, top + SpanHeight));
            }
            else
            {
                drawingContext.DrawRoundedRectangle(brush, null, bounds, 3, 3);
            }

            if (bounds.Width > 36)
            {
                var text = new FormattedText(
                    span.IsMissingEnd ? span.Name + " · 종료 누락 →" : span.Name,
                    CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 11,
                    span.IsMissingEnd ? AxisBrush ?? brush : Brushes.Black,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip)
                {
                    MaxTextWidth = bounds.Width - 10,
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis
                };
                drawingContext.DrawText(text,
                    new Point(left + 5, top + (SpanHeight - text.Height) / 2));
            }
            drawingContext.Pop();
        }

        private void DrawTimeAxis(DrawingContext drawingContext, TimelineCoordinateMapper mapper, double viewportWidth)
        {
            Brush? axisBrush = AxisBrush;
            Brush? gridLineBrush = GridLineBrush;

            if (axisBrush is null || gridLineBrush is null)
            {
                return;
            }

            const double axisBottom = AxisHeight;
            const double labelTop = 4;
            const double minimumTickSpacing = 90;

            int intervalCount = (int)Math.Clamp(Math.Floor(viewportWidth / minimumTickSpacing), 1, 6);

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            var typeface = new Typeface("Segoe UI");
            var gridPen = new Pen(gridLineBrush, 1);

            drawingContext.DrawLine(gridPen,
                new Point(ChartLeft, axisBottom),
                new Point(ChartLeft + viewportWidth, axisBottom));

            for (int i = 0; i <= intervalCount; i++)
            {
                long timeUs = VisibleStartTimeUs + VisibleDurationUs * i / intervalCount;

                double x = ChartLeft + mapper.TimeToX(timeUs);

                drawingContext.DrawLine(gridPen,
                    new Point(x, axisBottom),
                    new Point(x, Math.Max(axisBottom, ActualHeight)));

                string label = (timeUs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " ms";

                var text = new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    11,
                    axisBrush,
                    pixelsPerDip);

                double labelX = x - text.Width / 2;

                labelX = Math.Clamp(labelX, ChartLeft, Math.Max(ChartLeft, ActualWidth - text.Width));

                drawingContext.DrawText(text, new Point(labelX, labelTop));
            }
        }
    }
}

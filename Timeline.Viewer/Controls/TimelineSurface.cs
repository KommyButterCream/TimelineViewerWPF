using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Timeline.Viewer.Rendering;
using Timeline.Viewer.ViewModels;
using System.ComponentModel;
using System.Windows.Input;
using System.Linq;

namespace Timeline.Viewer.Controls
{
    public sealed class TimelineSurface : FrameworkElement
    {
        private const long InitialDurationUs = 240_000;
        private const long MinDurationUs = 1_000;
        private const double ZoomFactor = 1.25;
        private const double HorizontalPadding = 16;
        private const double TrackLabelWidth = 140;
        private const double AxisHeight = 28;
        private const double TrackVerticalPadding = 18;
        private const double LaneGap = 8;
        private const double SpanHeight = 28;
        private const double CollapsedTrackHeight = 32;
        private const double ChartLeft = HorizontalPadding + TrackLabelWidth;

        private Point? _panStartPoint;
        private long _visibleDurationUs = InitialDurationUs;
        private long _latestTimeUs;
        private bool _fitRequested;

        public void FitToView()
        {
            _fitRequested = true;
            InvalidateVisual();
        }

        public void ZoomIn() => ZoomAtCenter(120);

        public void ZoomOut() => ZoomAtCenter(-120);

        private void ZoomAtCenter(int wheelDelta)
        {
            if (_panStartPoint is not null) return;
            ZoomAtPointer(ChartLeft + HorizontalAxis.Viewport / 2, wheelDelta);
        }
        private double _panStartHorizontalOffset;
        private double _panStartVerticalOffset;

        private readonly HashSet<INotifyCollectionChanged> _observedSpanCollections = new();
        private readonly HashSet<TrackItemViewModel> _observedTracks = new();
        private readonly TimelineLayoutService _layoutService = new();

        private readonly List<(Rect Bounds, SpanItemViewModel Span)> _spanHitAreas = new();

        private readonly ScrollAxis _horizontalAxis = new();
        private readonly ScrollAxis _verticalAxis = new();

        private SpanItemViewModel? _pendingRevealSpan;

        public static readonly DependencyProperty TracksProperty = DependencyProperty.Register(
            nameof(Tracks),
            typeof(IEnumerable<TrackItemViewModel>),
            typeof(TimelineSurface),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnTracksChanged));

        public static readonly DependencyProperty SelectedSpanProperty = DependencyProperty.Register(
            nameof(SelectedSpan),
            typeof(SpanItemViewModel),
            typeof(TimelineSurface),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault |
                FrameworkPropertyMetadataOptions.AffectsRender));

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
            Focusable = true;
            KeyboardNavigation.SetIsTabStop(this, true);
            _horizontalAxis.PropertyChanged += OnAxisPropertyChanged;
            _verticalAxis.PropertyChanged += OnAxisPropertyChanged;
        }

        public void RevealSpan(SpanItemViewModel span)
        {
            ArgumentNullException.ThrowIfNull(span);
            if (Tracks is not null)
            {
                foreach (TrackItemViewModel track in Tracks)
                {
                    if (track.Spans.Contains(span))
                    {
                        track.IsExpanded = true;
                        break;
                    }
                }
            }
            _pendingRevealSpan = span;
            InvalidateVisual();
        }

        public ScrollAxis HorizontalAxis => _horizontalAxis;
        public ScrollAxis VerticalAxis => _verticalAxis;

        public IEnumerable<TrackItemViewModel>? Tracks
        {
            get => (IEnumerable<TrackItemViewModel>?)GetValue(TracksProperty);
            set => SetValue(TracksProperty, value);
        }

        public SpanItemViewModel? SelectedSpan
        {
            get => (SpanItemViewModel?)GetValue(SelectedSpanProperty);
            set => SetValue(SelectedSpanProperty, value);
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

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            InvalidateVisual();
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_panStartPoint is not Point startPoint)
            {
                return;
            }

            Point currentPoint = e.GetPosition(this);
            Vector drag = currentPoint - startPoint;

            HorizontalAxis.ScrollTo(_panStartHorizontalOffset - drag.X);
            VerticalAxis.ScrollTo(_panStartVerticalOffset - drag.Y);

            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            if (e.Handled || e.Delta == 0)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (_panStartPoint is null)
                {
                    ZoomAtPointer(e.GetPosition(this).X, e.Delta);
                }
                e.Handled = true;
                return;
            }

            bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            ScrollAxis axis = shiftPressed ? HorizontalAxis : VerticalAxis;

            const double lineSize = 24.0;
            const double deltaPerNotch = 120.0;

            int lines = SystemParameters.WheelScrollLines;
            double distancePerNotch = lines < 0 ? axis.Viewport : lines * lineSize;
            double distance = e.Delta / deltaPerNotch * distancePerNotch;

            double previousOffset = axis.Offset;
            axis.ScrollTo(previousOffset - distance);

            if (axis.Offset != previousOffset)
            {
                e.Handled = true;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Handled
                || Keyboard.Modifiers != ModifierKeys.None
                || _panStartPoint is not null)
            {
                return;
            }

            if (e.Key == Key.Escape)
            {
                SetCurrentValue(SelectedSpanProperty, null);
                e.Handled = true;
                return;
            }

            if (e.Key != Key.Left
                && e.Key != Key.Right
                && e.Key != Key.Home
                && e.Key != Key.End)
            {
                return;
            }

            List<SpanItemViewModel> spans = GetKeyboardSpans();

            if (spans.Count == 0)
            {
                return;
            }

            int index = SelectedSpan is null
                ? -1
                : spans.IndexOf(SelectedSpan);

            switch (e.Key)
            {
                case Key.Home:
                    index = 0;
                    break;

                case Key.End:
                    index = spans.Count - 1;
                    break;

                case Key.Left:
                    index = index < 0
                        ? spans.Count - 1
                        : Math.Max(0, index - 1);
                    break;

                case Key.Right:
                    index = index < 0
                        ? 0
                        : Math.Min(spans.Count - 1, index + 1);
                    break;
            }

            SpanItemViewModel span = spans[index];

            SetCurrentValue(SelectedSpanProperty, span);
            RevealSpan(span);

            e.Handled = true;
        }
        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Handled)
            {
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                if (_panStartPoint is null)
                {
                    Focus();

                    SpanItemViewModel? span = FindSpanAt(e.GetPosition(this));
                    SetCurrentValue(SelectedSpanProperty, span);
                }

                e.Handled = true;
                return;
            }

            if (e.ChangedButton != MouseButton.Middle)
            {
                return;
            }

            if (HorizontalAxis.MaxOffset <= 0 && VerticalAxis.MaxOffset <= 0)
            {
                return;
            }

            if (!CaptureMouse())
            {
                return;
            }

            _panStartPoint = e.GetPosition(this);
            _panStartHorizontalOffset = HorizontalAxis.Offset;
            _panStartVerticalOffset = VerticalAxis.Offset;

            e.Handled = true;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.ChangedButton != MouseButton.Middle || _panStartPoint is null)
            {
                return;
            }

            ReleaseMouseCapture();

            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);

            _panStartPoint = null;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            _spanHitAreas.Clear();

            double viewportWidth = ActualWidth - ChartLeft - HorizontalPadding;

            if (!double.IsFinite(viewportWidth)
                || viewportWidth <= 0
                || ActualHeight <= 0)
            {
                return;
            }

            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

            double viewportHeight = Math.Max(0, ActualHeight - AxisHeight);
            double extentHeight = 0;
            long latestTimeUs = 0;

            SpanItemViewModel? revealSpan = _pendingRevealSpan;
            double? revealTop = null;

            var rows = new List<(TrackItemViewModel Track, TrackLayout Layout, double Height)>();

            if (Tracks is not null)
            {
                // 유효한 스크롤 Extent, Track 정보 저장
                foreach (TrackItemViewModel track in Tracks)
                {
                    TrackLayout layout = _layoutService.BuildTrackLayout(track.Spans);
                    double trackHeight = track.IsExpanded
                        ? GetTrackHeight(layout)
                        : CollapsedTrackHeight;

                    rows.Add((track, layout, trackHeight));

                    // 가로축은 가장 늦은 시간 찾기
                    foreach (SpanPlacement placement in layout.Placements)
                    {
                        SpanItemViewModel span = placement.Span;
                        long lastKnownTimeUs = span.EndTimeUs ?? span.StartTimeUs;

                        latestTimeUs = Math.Max(latestTimeUs, lastKnownTimeUs);

                        if (ReferenceEquals(span, revealSpan))
                        {
                            revealTop = extentHeight + TrackVerticalPadding + placement.LaneIndex * (SpanHeight + LaneGap);
                        }
                    }

                    // 전체 Track 높이 더하여 전체 track 영역 높이 계산
                    extentHeight += trackHeight;
                }
            }

            // 전체 시간이 화면에서 몇 DIP 인지 계산
            _latestTimeUs = latestTimeUs;
            double oldWidth = HorizontalAxis.Viewport;
            double oldOffset = HorizontalAxis.Offset;
            if (_fitRequested)
            {
                _visibleDurationUs = Math.Max(MinDurationUs, latestTimeUs);
            }
            UpdateHorizontalRange(viewportWidth);
            if (_fitRequested)
            {
                HorizontalAxis.ScrollTo(0);
                _fitRequested = false;
            }
            else if (oldWidth > 0 && oldWidth != viewportWidth)
            {
                // 같은 시작 시각이 새 너비에서 차지하는 DIP 거리로 환산한다.
                HorizontalAxis.ScrollTo(oldOffset / oldWidth * viewportWidth);
            }
            _verticalAxis.UpdateSize(extentHeight, viewportHeight);

            if (revealSpan is not null && revealTop is double spanTop)
            {
                var contentMapper = new TimelineCoordinateMapper(0, _visibleDurationUs, viewportWidth);

                double spanStartX = contentMapper.TimeToX(revealSpan.StartTimeUs);

                HorizontalAxis.ScrollTo(spanStartX - viewportWidth / 2);
                VerticalAxis.ScrollTo(spanTop + SpanHeight / 2 - viewportHeight / 2);
            }

            _pendingRevealSpan = null;

            // 스크롤과 화면 거리(DIP)를 시간(us)으로 바꾼다.
            long maxStartUs = Math.Max(0L, latestTimeUs - _visibleDurationUs);
            double offsetUs = HorizontalAxis.Offset / viewportWidth * _visibleDurationUs;

            long visibleStartUs = offsetUs >= maxStartUs ? maxStartUs : (long)Math.Round(offsetUs);

            // 현재 보고 있는 구간에서 특정 시각이 어디에 그려지는지 계산
            var mapper = new TimelineCoordinateMapper(visibleStartUs, _visibleDurationUs, viewportWidth);

            DrawTimeAxis(drawingContext, mapper, viewportWidth);

            var trackArea = new Rect(0, AxisHeight, ActualWidth, viewportHeight);
            drawingContext.PushClip(new RectangleGeometry(trackArea));

            double rowTop = AxisHeight - _verticalAxis.Offset;

            // Track 그리기
            foreach (var row in rows)
            {
                DrawTrack(drawingContext, mapper, row.Track, row.Layout, rowTop, row.Height);
                rowTop += row.Height;
            }

            drawingContext.Pop();


            if (IsKeyboardFocused && AxisBrush is Brush focusBrsuh)
            {
                var focusPen = new Pen(focusBrsuh, 1)
                {
                    DashStyle = DashStyles.Dot
                };

                var focusBounds = new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1));

                drawingContext.DrawRectangle(null, focusPen, focusBounds);
            }
        }

        private void ZoomAtPointer(double pointerX, int wheelDelta)
        {
            double width = HorizontalAxis.Viewport;
            double chartX = pointerX - ChartLeft;
            if (width <= 0 || chartX < 0 || chartX > width)
            {
                return;
            }

            long maxDurationUs = Math.Max(InitialDurationUs, _latestTimeUs);
            double requestedUs = _visibleDurationUs / Math.Pow(ZoomFactor, wheelDelta / 120.0);
            long durationUs = requestedUs >= maxDurationUs
                ? maxDurationUs
                : Math.Max(MinDurationUs, (long)Math.Round(requestedUs));
            if (durationUs == _visibleDurationUs)
            {
                return;
            }

            double pointerRatio = chartX / width;
            double anchorUs = (HorizontalAxis.Offset / width + pointerRatio) * _visibleDurationUs;
            _visibleDurationUs = durationUs;
            UpdateHorizontalRange(width);
            HorizontalAxis.ScrollTo((anchorUs / durationUs - pointerRatio) * width);
            InvalidateVisual();
        }

        private void UpdateHorizontalRange(double width)
        {
            var scaleMapper = new TimelineCoordinateMapper(0, _visibleDurationUs, width);
            HorizontalAxis.UpdateSize(scaleMapper.DurationToWidth(_latestTimeUs), width);
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
            foreach (TrackItemViewModel track in _observedTracks)
            {
                PropertyChangedEventManager.RemoveHandler(
                    track, OnTrackPropertyChanged, nameof(TrackItemViewModel.IsExpanded));
            }
            _observedTracks.Clear();

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
                if (_observedTracks.Add(track))
                {
                    PropertyChangedEventManager.AddHandler(
                        track, OnTrackPropertyChanged, nameof(TrackItemViewModel.IsExpanded));
                }
                if (_observedSpanCollections.Add(track.Spans))
                {
                    CollectionChangedEventManager.AddHandler(track.Spans, OnSpansCollectionChanged);
                }
            }
        }

        private void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            InvalidateVisual();
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

            // Track 이름 그리기
            drawingContext.DrawText(label, new Point(
                HorizontalPadding,
                rowTop + (trackHeight - label.Height) / 2));

            // Track 과 Track 사이 구분선 그리기
            drawingContext.DrawLine(new Pen(lineBrush, 1),
                new Point(HorizontalPadding, rowTop + trackHeight),
                new Point(ActualWidth - HorizontalPadding, rowTop + trackHeight));

            // ViewPort 영역을 벗어나지 않도록 클리핑 영역 설정
            if (!track.IsExpanded)
            {
                return;
            }

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
            long visibleStart = mapper.VisibleStartTimeUs;
            long visibleEnd = visibleStart + mapper.VisibleDurationUs;

            if (span.StartTimeUs > visibleEnd || span.EndTimeUs < visibleStart)
            {
                return;
            }

            double x = ChartLeft + mapper.TimeToX(span.StartTimeUs);

            if (span.IsInstant)
            {
                // Start 와 End 이 같아서 Duration 이 0 인 경우에
                // 직선과 원이 겹친 도형을 그려준다.
                // 즉발성 이벤트 같은거 그려준다고 보면 된다.

                RegisterHitArea(new Rect(x - 4, top, 8, SpanHeight), span);

                drawingContext.DrawLine(
                    new Pen(brush, 3),
                    new Point(x, top),
                    new Point(x, top + SpanHeight));

                drawingContext.DrawEllipse(
                    brush,
                    null,
                    new Point(x, top + SpanHeight / 2),
                    4, 4);

                DrawSelectionBorder(drawingContext, span, new Rect(x - 4, top, 8, SpanHeight));

                return;
            }

            double left = Math.Max(ChartLeft, x);
            double right = Math.Min(ActualWidth - HorizontalPadding,
                ChartLeft + mapper.TimeToX(span.EndTimeUs ?? visibleEnd));

            if (right <= left)
            {
                return;
            }

            // Span 을 그릴 Rectangle Geometry 설정
            var bounds = new Rect(left, top, right - left, SpanHeight);

            RegisterHitArea(bounds, span);

            drawingContext.PushClip(new RectangleGeometry(bounds));

            if (span.IsMissingEnd)
            {
                // End 가 없는 경우 대각선 빗금이 그려진 반투명 사각형을 그린다.

                // 테두리 없는 반투명 사각형 그리기
                drawingContext.PushOpacity(0.16);
                drawingContext.DrawRectangle(brush, null, bounds);
                drawingContext.Pop();

                // LB->RT 향하는 대각선 빗금 그리기
                drawingContext.PushOpacity(0.45);
                var hatchPen = new Pen(brush, 1);
                for (double hatchX = left - SpanHeight; hatchX < right; hatchX += 8)
                {
                    drawingContext.DrawLine(hatchPen,
                        new Point(hatchX, top + SpanHeight),
                        new Point(hatchX + SpanHeight, top));
                }
                drawingContext.Pop();

                // 사각형 위 아래로 점선 형태의 테두리 선을 그린다.
                var openPen = new Pen(brush, 1) { DashStyle = DashStyles.Dash };
                drawingContext.DrawLine(openPen, new Point(left, top), new Point(right, top));
                drawingContext.DrawLine(openPen, new Point(left, top + SpanHeight), new Point(right, top + SpanHeight));
            }
            else
            {
                // 일반적인 경우 Rounded-Rectangle 을 그린다.
                drawingContext.DrawRoundedRectangle(brush, null, bounds, 3, 3);
            }

            if (bounds.Width > 36)
            {
                // Span Name 을 그린다.
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

            DrawSelectionBorder(drawingContext, span, bounds);
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

            // 시간축 가로선 그리기
            drawingContext.DrawLine(gridPen,
                new Point(ChartLeft, axisBottom),
                new Point(ChartLeft + viewportWidth, axisBottom));

            // 시간축 세로선
            long tickStepUs = Math.Max(1L, mapper.VisibleDurationUs / intervalCount);
            long remainderUs = mapper.VisibleStartTimeUs % tickStepUs;
            long firstTickOffsetUs = remainderUs == 0 ? 0 : tickStepUs - remainderUs;
            for (long relativeUs = firstTickOffsetUs; relativeUs <= mapper.VisibleDurationUs;)
            {
                // 시간축 1칸에 대한 시간(us)
                long timeUs = mapper.VisibleStartTimeUs + relativeUs;

                // 시간축 세로선을 그릴 스크린 상의 시작 위치 x 좌표
                double x = ChartLeft + mapper.TimeToX(timeUs);

                // 세로선 그리기
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

                // 시간축 글자가 축 중심에 오도록 계산 후 그리기
                drawingContext.DrawText(text, new Point(labelX, labelTop));
                if (mapper.VisibleDurationUs - relativeUs < tickStepUs)
                {
                    break;
                }
                relativeUs += tickStepUs;
            }
        }

        private void OnAxisPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScrollAxis.Offset))
            {
                InvalidateVisual();
            }
        }

        private void RegisterHitArea(Rect bounds, SpanItemViewModel span)
        {
            var viewport = new Rect(
                ChartLeft,
                AxisHeight,
                Math.Max(0, ActualWidth - ChartLeft - HorizontalPadding),
                Math.Max(0, ActualHeight - AxisHeight));

            bounds.Intersect(viewport);

            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                _spanHitAreas.Add((bounds, span));
            }
        }

        private SpanItemViewModel? FindSpanAt(Point point)
        {
            for (int i = _spanHitAreas.Count - 1; i >= 0; i--)
            {
                var hitArea = _spanHitAreas[i];

                if (hitArea.Bounds.Contains(point))
                {
                    return hitArea.Span;
                }
            }

            return null;
        }

        private void DrawSelectionBorder(DrawingContext drawingContext, SpanItemViewModel span, Rect bounds)
        {
            if (!ReferenceEquals(span, SelectedSpan) || AxisBrush is null)
            {
                return;
            }

            // 테두리 두께의 절반만큼 안쪽에 배치한다.
            const double thickness = 2;

            if (bounds.Width <= thickness || bounds.Height <= thickness)
            {
                return;
            }

            bounds.Inflate(-thickness / 2, -thickness / 2);

            var pen = new Pen(AxisBrush, thickness);
            drawingContext.DrawRoundedRectangle(null, pen, bounds, 2, 2);
        }

        private List<SpanItemViewModel> GetKeyboardSpans()
        {
            if (Tracks is null)
            {
                return new List<SpanItemViewModel>();
            }

            return Tracks.SelectMany(track => track.Spans)
                         .OrderBy(span => span.StartTimeUs)
                         .ToList();
        }
    }
}

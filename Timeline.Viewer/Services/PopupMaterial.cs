using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    // Popup은 WindowChrome 대신 자체 HWND에 프레임과 Acrylic을 적용합니다.
    public static class PopupMaterial
    {
        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(PopupMaterial), new PropertyMetadata(false, OnEnabledChanged));
        public static readonly DependencyProperty UseMaterialProperty = DependencyProperty.RegisterAttached(
            "UseMaterial", typeof(bool), typeof(PopupMaterial),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits, OnAppearanceChanged));
        public static readonly DependencyProperty ThemeProperty = DependencyProperty.RegisterAttached(
            "Theme", typeof(AppTheme), typeof(PopupMaterial),
            new FrameworkPropertyMetadata(AppTheme.Black, FrameworkPropertyMetadataOptions.Inherits, OnAppearanceChanged));
        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State", typeof(PopupState), typeof(PopupMaterial));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);
        public static bool GetUseMaterial(DependencyObject obj) => (bool)obj.GetValue(UseMaterialProperty);
        public static void SetUseMaterial(DependencyObject obj, bool value) => obj.SetValue(UseMaterialProperty, value);
        public static AppTheme GetTheme(DependencyObject obj) => (AppTheme)obj.GetValue(ThemeProperty);
        public static void SetTheme(DependencyObject obj, AppTheme value) => obj.SetValue(ThemeProperty, value);

        private static void OnEnabledChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            if (obj is not Popup popup) return;
            if (popup.GetValue(StateProperty) is PopupState old) old.Dispose();
            popup.ClearValue(StateProperty);
            if ((bool)e.NewValue) popup.SetValue(StateProperty, new PopupState(popup));
        }

        private static void OnAppearanceChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            if (obj.GetValue(StateProperty) is PopupState state) state.Apply();
        }

        private sealed class PopupState : IDisposable
        {
            private readonly Popup _popup;
            private readonly IWindowMaterialService _service = new WindowMaterialService();
            private Window? _owner;

            public PopupState(Popup popup)
            {
                _popup = popup;
                popup.Opened += OnOpened;
                popup.Closed += OnClosed;
                popup.Unloaded += OnUnloaded;
                if (popup.IsOpen) OnOpened(popup, EventArgs.Empty);
            }

            private void OnOpened(object? sender, EventArgs e)
            {
                DetachOwner();
                _owner = _popup.PlacementTarget is null ? null : Window.GetWindow(_popup.PlacementTarget);
                if (_owner is not null)
                {
                    _owner.LocationChanged += Reposition;
                    _owner.SizeChanged += Reposition;
                    _owner.Closed += OnOwnerClosed;
                }
                SystemParameters.StaticPropertyChanged += OnSystemChanged;
                Apply();
            }

            public void Apply()
            {
                if (!_popup.IsOpen || _popup.Child is not Border border
                    || PresentationSource.FromVisual(border) is not HwndSource source) return;

                bool requested = GetUseMaterial(_popup) && !SystemParameters.HighContrast;
                bool supported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
                bool applied = false;
                // Layered Popup 대신 일반 HWND를 사용해야 시스템 backdrop을 표시할 수 있습니다.
                if (supported && !_popup.AllowsTransparency)
                {
                    var margins = new Margins(requested ? -1 : 0);
                    bool extended = DwmExtendFrameIntoClientArea(source.Handle, ref margins) >= 0;
                    applied = extended && _service.TryApply(source.Handle,
                        requested ? WindowMaterial.Acrylic : WindowMaterial.None, GetTheme(_popup)) && requested;
                    if (!applied)
                    {
                        margins = new Margins(0);
                        DwmExtendFrameIntoClientArea(source.Handle, ref margins);
                        _service.TryApply(source.Handle, WindowMaterial.None, GetTheme(_popup));
                    }
                }
                if (applied)
                {
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                    border.Background = Brushes.Transparent;
                }
                else
                {
                    source.CompositionTarget.BackgroundColor = SystemColors.WindowColor;
                    border.SetResourceReference(Border.BackgroundProperty,
                        SystemParameters.HighContrast ? SystemColors.WindowBrushKey : "SurfaceBrush");
                }
            }

            private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(SystemParameters.HighContrast))
                    _popup.Dispatcher.InvokeAsync(Apply);
            }

            private void Reposition(object? sender, EventArgs e)
            {
                // 새 레이아웃의 앵커 좌표가 확정된 뒤 Popup 위치를 다시 계산합니다.
                _popup.Dispatcher.InvokeAsync(() =>
                {
                    if (!_popup.IsOpen) return;
                    double offset = _popup.HorizontalOffset;
                    _popup.SetCurrentValue(Popup.HorizontalOffsetProperty, offset + 0.01);
                    _popup.SetCurrentValue(Popup.HorizontalOffsetProperty, offset);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }

            private void OnOwnerClosed(object? sender, EventArgs e) => _popup.SetCurrentValue(Popup.IsOpenProperty, false);
            private void OnUnloaded(object sender, RoutedEventArgs e) => _popup.SetCurrentValue(Popup.IsOpenProperty, false);
            private void OnClosed(object? sender, EventArgs e) => DetachOwner();

            private void DetachOwner()
            {
                SystemParameters.StaticPropertyChanged -= OnSystemChanged;
                if (_owner is null) return;
                _owner.LocationChanged -= Reposition;
                _owner.SizeChanged -= Reposition;
                _owner.Closed -= OnOwnerClosed;
                _owner = null;
            }

            public void Dispose()
            {
                DetachOwner();
                _popup.Opened -= OnOpened;
                _popup.Closed -= OnClosed;
                _popup.Unloaded -= OnUnloaded;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left, Right, Top, Bottom;
            public Margins(int value) => Left = Right = Top = Bottom = value;
        }

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    }
}

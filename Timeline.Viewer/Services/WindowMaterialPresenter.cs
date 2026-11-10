using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timeline.Viewer.Models;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows;

namespace Timeline.Viewer.Services
{
    public static class WindowMaterialPresenter
    {
        public static void Apply(
            Window window,
            IWindowMaterialService service,
            AppTheme theme,
            bool enabled)
        {
            window.Dispatcher.VerifyAccess();

            IntPtr handle = new WindowInteropHelper(window).Handle;

            if (handle == IntPtr.Zero)
            {
                return;
            }

            WindowChrome? chrome = WindowChrome.GetWindowChrome(window);

            if (chrome is null)
            {
                return;
            }

            if (chrome.IsFrozen)
            {
                chrome = (WindowChrome)chrome.Clone();
                WindowChrome.SetWindowChrome(window, chrome);
            }

            bool requested = enabled && !SystemParameters.HighContrast;

            WindowMaterial material = requested ? WindowMaterial.Mica : WindowMaterial.None;

            bool succeeded = service.TryApply(handle, material, theme);
            bool useMaterial = requested && succeeded;

            chrome.GlassFrameThickness = useMaterial ? new Thickness(-1) : new Thickness(0);

            if (useMaterial)
            {
                window.Background = Brushes.Transparent;

                HwndSource? source = HwndSource.FromHwnd(handle);

                if (source?.CompositionTarget is not null)
                {
                    source.CompositionTarget.BackgroundColor = Colors.Transparent;
                }
            }
            else
            {
                window.SetResourceReference(Window.BackgroundProperty, "SurfaceBrush");
            }
        }
    }
}

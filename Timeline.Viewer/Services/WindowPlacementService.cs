using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    public sealed record MonitorArea(string DeviceName, Rect WorkArea, double Scale, bool IsPrimary);

    public static class WindowPlacementService
    {
        // 순수 계산을 분리하여 실제 모니터 구성을 변경하지 않고도 경계 조건을 검증합니다.
        public static Rect CalculateBounds(UserSettings settings, IReadOnlyList<MonitorArea> monitors)
        {
            if (monitors.Count == 0) throw new ArgumentException("모니터가 없습니다.", nameof(monitors));
            MonitorArea? saved = monitors.FirstOrDefault(m => m.DeviceName == settings.MonitorDeviceName);
            var target = saved ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            Rect work = target.WorkArea;
            double scale = target.Scale;
            double width = Math.Clamp(double.IsFinite(settings.WindowWidth) ? settings.WindowWidth : 1280,
                Math.Min(900, work.Width / scale), work.Width / scale) * scale;
            double height = Math.Clamp(double.IsFinite(settings.WindowHeight) ? settings.WindowHeight : 760,
                Math.Min(560, work.Height / scale), work.Height / scale) * scale;
            double left = work.Left + (work.Width - width) / 2;
            double top = work.Top + (work.Height - height) / 2;
            if (saved is not null && settings.MonitorOffsetX is double x && double.IsFinite(x)
                && settings.MonitorOffsetY is double y && double.IsFinite(y))
            {
                left = work.Left + x * scale;
                top = work.Top + y * scale;
            }
            else if (settings.MonitorDeviceName is null && settings.WindowLeft is double oldLeft
                && double.IsFinite(oldLeft) && settings.WindowTop is double oldTop && double.IsFinite(oldTop))
            {
                // 이전 형식은 주 모니터 기준으로 안전하게 보정합니다.
                left = oldLeft * scale;
                top = oldTop * scale;
            }
            return new Rect(Math.Clamp(left, work.Left, work.Right - width),
                Math.Clamp(top, work.Top, work.Bottom - height), width, height);
        }

        public static void Restore(Window window, UserSettings settings)
        {
            var monitors = GetMonitors();
            if (monitors.Count == 0) return;
            Rect bounds = CalculateBounds(settings, monitors);
            var monitor = monitors.First(m => m.WorkArea.Contains(
                new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)));
            window.MinWidth = Math.Min(900, monitor.WorkArea.Width / monitor.Scale);
            window.MinHeight = Math.Min(560, monitor.WorkArea.Height / monitor.Scale);
            // 화면 좌표는 물리 픽셀로 전달하고 WPF가 WM_DPICHANGED를 처리하게 합니다.
            SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
                (int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                (int)Math.Round(bounds.Width), (int)Math.Round(bounds.Height), 0x0014);
        }

        public static UserSettings Capture(Window window, UserSettings basis)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect)) return basis;
            var monitors = GetMonitors();
            var bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            var monitor = monitors.OrderByDescending(m =>
            {
                Rect intersection = Rect.Intersect(m.WorkArea, bounds);
                return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
            }).FirstOrDefault();
            if (monitor is null) return basis;
            double scale = GetDpiForWindow(handle) / 96.0;
            if (scale <= 0) scale = monitor.Scale;
            return basis with
            {
                WindowWidth = bounds.Width / scale, WindowHeight = bounds.Height / scale,
                WindowLeft = window.Left, WindowTop = window.Top,
                MonitorDeviceName = monitor.DeviceName,
                MonitorOffsetX = (bounds.Left - monitor.WorkArea.Left) / scale,
                MonitorOffsetY = (bounds.Top - monitor.WorkArea.Top) / scale
            };
        }

        public static IReadOnlyList<MonitorArea> GetMonitors()
        {
            var result = new List<MonitorArea>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data) =>
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = string.Empty };
                if (GetMonitorInfo(monitor, ref info))
                {
                    double scale = GetDpiForMonitor(monitor, 0, out uint dpi, out _) >= 0 && dpi > 0 ? dpi / 96.0 : 1;
                    var work = info.Work;
                    result.Add(new(info.Device, new Rect(work.Left, work.Top, work.Right - work.Left,
                        work.Bottom - work.Top), scale, (info.Flags & 1) != 0));
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfo
        {
            public int Size; public NativeRect Monitor; public NativeRect Work; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    public sealed class WindowMaterialService : IWindowMaterialService
    {
        private const int UseImmersiveDarkModeAttribute = 20;
        private const int SystemBackdropTypeAttribute = 38;

        private const int BackdropNone = 1;
        private const int BackdropMica = 2;
        private const int BackdropAcrylic = 3;

        public bool TryApply(IntPtr windowHandle, WindowMaterial material, AppTheme theme)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return false;
            }

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                return false;
            }

            // 고대비 모드에서는 시스템 재질을 사용하지 않습니다.
            WindowMaterial effectiveMaterial = SystemParameters.HighContrast ? WindowMaterial.None : material;

            int backdrop = effectiveMaterial switch
            {
                WindowMaterial.None => BackdropNone,
                WindowMaterial.Mica => BackdropMica,
                WindowMaterial.Acrylic => BackdropAcrylic,
                _ => throw new ArgumentOutOfRangeException(nameof(material))
            };

            int useDarkMode = theme == AppTheme.Black ? 1 : 0;

            int themeResult = DwmSetWindowAttribute(
                windowHandle,
                UseImmersiveDarkModeAttribute,
                ref useDarkMode,
                sizeof(int));

            if (themeResult < 0)
            {
                return false;
            }
            
            int materialResult = DwmSetWindowAttribute(
                windowHandle,
                SystemBackdropTypeAttribute,
                ref backdrop,
                sizeof(int));

            return materialResult >= 0;
        }

        [DllImport("dwmapi.dll", ExactSpelling = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int valueSize);
    }
}

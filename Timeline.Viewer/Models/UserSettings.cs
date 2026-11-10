using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timeline.Viewer.Models
{
    public sealed record UserSettings
    {
        public int Version { get; init; } = 1;

        public AppTheme Theme { get; init; } = AppTheme.Black;

        public bool UseWindowMaterial { get; init; } = true;

        public double WindowWidth { get; init; } = 1280;

        public double WindowHeight { get; init; } = 760;

        public double? WindowLeft { get; init; }

        public double? WindowTop { get; init; }

        public bool IsMaximized { get; init; }

        // 모니터 작업 영역 기준 DIP 위치: 음수 화면 좌표와 배율 변경을 구분합니다.
        public string? MonitorDeviceName { get; init; }
        public double? MonitorOffsetX { get; init; }
        public double? MonitorOffsetY { get; init; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timeline.Format
{
    public static class TimelineFormat
    {
        public const string FormatName = "timeline";
        public const int CurrentVersion = 1;
        public const string TimeUnit = "us";
        public const long MaxTimestampMicroseconds = 9_007_199_254_740_991L;
    }
}

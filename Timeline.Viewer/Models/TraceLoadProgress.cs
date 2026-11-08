using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timeline.Viewer.Models
{
    public enum TraceLoadStage
    {
        None,
        Reading,
        Analyzing,
        Preparing,
        Cancelling
    }

    public sealed record TraceLoadProgress(TraceLoadStage Stage, long RecordCount);
}

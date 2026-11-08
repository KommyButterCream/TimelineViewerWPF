using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timeline.IO;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    public interface ITraceLoadService
    {
        Task<TraceDocument> LoadAsync(
            string filePath,
            TraceReadMode mode = TraceReadMode.Recovery,
            CancellationToken cancellationToken = default,
            IProgress<TraceLoadProgress>? progress = null);
    }
}

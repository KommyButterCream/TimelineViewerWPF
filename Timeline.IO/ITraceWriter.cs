using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Timeline.Format;

namespace Timeline.IO
{
    public interface ITraceWriter : IAsyncDisposable
    {
        Task WriteAsync(TraceRecord record, CancellationToken cancellationToken = default);

        Task FlushAsync(CancellationToken cancellationToken = default);
    }
}

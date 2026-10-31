using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Timeline.Tracing
{
    public sealed class TraceScope : IDisposable
    {
        private ITracer? _tracer;

        public TraceSpanHandle Handle { get; }

        internal TraceScope(ITracer tracer, TraceSpanHandle handle)
        {
            ArgumentNullException.ThrowIfNull(tracer);

            _tracer = tracer;
            Handle = handle;
        }

        public void Dispose()
        {
            ITracer? tracer = Interlocked.Exchange(ref _tracer, null);

            tracer?.End(Handle);
        }
    }
}

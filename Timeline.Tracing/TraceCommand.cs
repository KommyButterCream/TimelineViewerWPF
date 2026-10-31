using Timeline.Format;

namespace Timeline.Tracing
{
    internal abstract record TraceCommand;

    internal sealed record WriteTraceCommand(TraceRecord Record) : TraceCommand;

    internal sealed record FlushTraceCommand(TaskCompletionSource Completion) : TraceCommand;
}

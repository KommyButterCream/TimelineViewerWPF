using System.Threading.Channels;
using Timeline.IO;
using Timeline.Format;
using System.Runtime.ExceptionServices;
using System.Globalization;
using System.Text.Json;

namespace Timeline.Tracing
{
    public sealed class TimelineTracer : ITracer, IAsyncDisposable
    {
        private readonly Guid _sessionId;
        private readonly TraceSessionClock _clock;
        private readonly TimelineTracerOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ITraceWriter _writer;
        private readonly Channel<TraceCommand> _commands;

        private readonly object _gate = new();

        private Exception? _failure;

        private Task _consumerTask = Task.CompletedTask;

        private bool _stopping;
        private Task? _disposeTask;
        private long _droppedRecordCount;

        private long _nextTrackId;
        private long _nextSpanId;

        private TimelineTracer(ITraceWriter writer, TimelineTracerOptions options, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(writer);

            ValidateOptions(options);

            _sessionId = Guid.NewGuid();
            _clock = new TraceSessionClock();
            _options = options;
            _timeProvider = timeProvider;
            _writer = writer;

            _commands = Channel.CreateBounded<TraceCommand>(
                new BoundedChannelOptions(options.QueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false
                });
        }

        public static Task<TimelineTracer> CreateAsync(
            string filePath,
            TimelineTracerOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return CreateAsync(filePath, options, TimeProvider.System, cancellationToken);
        }

        // Sandbox에서 타이머 생성·해제를 관찰하기 위한 내부 생성 경로입니다.
        internal static async Task<TimelineTracer> CreateAsync(
            string filePath,
            TimelineTracerOptions? options,
            TimeProvider timeProvider,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            options ??= new TimelineTracerOptions();

            ValidateOptions(options);
            cancellationToken.ThrowIfCancellationRequested();

            var writer = new JsonlTraceWriter(filePath);

            return await CreateAsync(writer, options, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        // Writer 소유권을 넘겨받는 내부 경로입니다. Sandbox의 실패 주입에도 사용합니다.
        internal static async Task<TimelineTracer> CreateAsync(
            ITraceWriter writer,
            TimelineTracerOptions options,
            TimeProvider timeProvider,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(writer);

            try
            {
                ArgumentNullException.ThrowIfNull(timeProvider);
                ValidateOptions(options);
                cancellationToken.ThrowIfCancellationRequested();

                var tracer = new TimelineTracer(writer, options, timeProvider);

                var header = new TraceHeaderRecord
                {
                    OriginUtc = tracer._clock.OriginUtc
                };

                await writer.WriteAsync(header, cancellationToken);

                tracer._consumerTask = Task.Run(tracer.ConsumeAsync);

                return tracer;
            }
            catch (Exception initializationFailure)
            {
                try
                {
                    await writer.DisposeAsync();
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "Tracer 초기화와 Writer 정리 중 오류가 발생했습니다.",
                        initializationFailure,
                        cleanupFailure);
                }
                throw;
            }
        }

        public TraceTrackHandle CreateTrack(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            lock (_gate)
            {
                ThrowIfUnavailable();

                long sequence = checked(++_nextTrackId);

                string id = "track-" + sequence.ToString(CultureInfo.InvariantCulture);

                var record = new TraceTrackRecord
                {
                    Id = id,
                    Name = name
                };

                bool accepted = _commands.Writer.TryWrite(new WriteTraceCommand(record));

                if (!accepted)
                {
                    throw new InvalidOperationException("큐가 가득 차 Track을 등록할 수 없습니다.");
                }

                return new TraceTrackHandle(_sessionId, id);
            }
        }

        public TraceSpanHandle Begin(
            string name,
            TraceTrackHandle track,
            TraceSpanHandle? parent = null,
            IReadOnlyDictionary<string, JsonElement>? args = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            IReadOnlyDictionary<string, JsonElement>? argsSnapshot = CopyArgs(args);

            lock (_gate)
            {
                ThrowIfUnavailable();

                if (!track.IsValid)
                {
                    throw new ArgumentException("유효한 Track 핸들이 필요합니다.", nameof(track));
                }

                if (track.SessionId != _sessionId)
                {
                    throw new ArgumentException("다른 세션의 Track은 사용할 수 없습니다.", nameof(track));
                }

                string? parentId = null;

                if (parent is TraceSpanHandle parentHandle &&
                    parentHandle.IsValid)
                {
                    if (parentHandle.SessionId != _sessionId)
                    {
                        throw new ArgumentException("다른 세션의 작업은 부모로 지정할 수 없습니다.", nameof(parent));
                    }

                    parentId = parentHandle.Id;
                }

                long sequence = checked(++_nextSpanId);

                string id = "span-" + sequence.ToString(CultureInfo.InvariantCulture);

                var record = new TraceBeginRecord
                {
                    Id = id,
                    TrackId = track.Id!,
                    Name = name,
                    TimeUs = _clock.GetElapsedMicroseconds(),
                    ParentId = parentId,
                    Args = argsSnapshot
                };

                bool accepted = _commands.Writer.TryWrite(new WriteTraceCommand(record));

                if (!accepted)
                {
                    _droppedRecordCount++;
                    return default;
                }

                return new TraceSpanHandle(_sessionId, id);
            }
        }

        public void End(TraceSpanHandle span)
        {
            if (!span.IsValid)
            {
                return;
            }

            lock (_gate)
            {
                ThrowIfUnavailable();

                if (span.SessionId != _sessionId)
                {
                    throw new ArgumentException("다른 세션의 작업은 종료할 수 없습니다.", nameof(span));
                }

                var record = new TraceEndRecord
                {
                    Id = span.Id!,
                    TimeUs = _clock.GetElapsedMicroseconds()
                };

                bool accepted = _commands.Writer.TryWrite(new WriteTraceCommand(record));

                if (!accepted)
                {
                    _droppedRecordCount++;
                }
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var command = new FlushTraceCommand(completion);

            while (true)
            {
                lock (_gate)
                {
                    ThrowIfUnavailable();
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_commands.Writer.TryWrite(command))
                    {
                        break;
                    }
                }

                // 큐가 가득 찬 동안에는 lock 밖에서 기다립니다.
                if (!await _commands.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
                {
                    lock (_gate)
                    {
                        ThrowIfUnavailable();
                    }

                    throw new InvalidOperationException("요청 큐가 닫혔습니다.");
                }
            }

            try
            {
                await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 접수 후 대기가 취소되어도 소비자는 요청을 처리합니다.
                // 이후의 저장 실패가 관찰되지 않은 Task 예외로 남지 않도록 합니다.
                _ = completion.Task.ContinueWith(
                    task => { _ = task.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_disposeTask is null)
                {
                    _stopping = true;

                    long endTimeUs = _clock.GetElapsedMicroseconds();

                    _commands.Writer.TryComplete();

                    _disposeTask = Task.Run(
                        () => CompleteAsync(endTimeUs));
                }

                return new ValueTask(_disposeTask);
            }
        }

        private async Task ConsumeAsync()
        {
            try
            {
                using var timer = new PeriodicTimer(_options.FlushInterval, _timeProvider);
                using var stopWaiting = new CancellationTokenSource();

                // 큐에 읽을 요청이 생기거나 큐가 종료됨
                Task<bool> dataReady = _commands.Reader.WaitToReadAsync(stopWaiting.Token).AsTask();

                // 다음 Flush 주기가 도달함
                Task<bool> tickReady = timer.WaitForNextTickAsync().AsTask();

                try
                {
                    while (true)
                    {
                        // 둘 중 하나가 완료될 때까지 기다린다.
                        await Task.WhenAny(dataReady, tickReady);

                        if (tickReady.IsCompleted)
                        {
                            if (!await tickReady)
                            {
                                // 타이머가 종료되었습니다.
                                break;
                            }

                            // Auto-Flush
                            await _writer.FlushAsync();

                            tickReady = timer.WaitForNextTickAsync().AsTask();
                        }

                        if (dataReady.IsCompleted)
                        {
                            if (!await dataReady)
                            {
                                // 큐가 닫혔고 남은 데이터 없음
                                break;
                            }

                            // 큐에서 명령을 하나 꺼내서 처리
                            if (_commands.Reader.TryRead(out TraceCommand? command))
                            {
                                await ProcessCommandAsync(command);
                            }

                            dataReady = _commands.Reader.WaitToReadAsync(stopWaiting.Token).AsTask();
                        }
                    }
                }
                finally
                {
                    await stopWaiting.CancelAsync();
                    timer.Dispose();

                    try
                    {
                        await dataReady;
                    }
                    catch (OperationCanceledException)
                    {
                        // 종료 시 남아 있던 큐 대기를 취소한 것입니다.
                    }

                    await tickReady;
                }
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _failure = exception;

                    _commands.Writer.TryComplete(exception);
                }

                while (_commands.Reader.TryRead(out TraceCommand? pending))
                {
                    if (pending is FlushTraceCommand flush)
                    {
                        flush.Completion.TrySetException(exception);
                    }
                }

                throw;
            }
        }

        private async Task ProcessCommandAsync(TraceCommand command)
        {
            switch (command)
            {
                case WriteTraceCommand write:
                    await _writer.WriteAsync(write.Record);
                    break;

                case FlushTraceCommand flush:
                    try
                    {
                        await _writer.FlushAsync();

                        flush.Completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        flush.Completion.TrySetException(exception);
                        throw;
                    }

                    break;

                default:
                    throw new InvalidOperationException(
                        $"지원하지 않는 요청 타입입니다: {command.GetType().Name}");
            }
        }

        private static void ValidateOptions(TimelineTracerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.QueueCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.QueueCapacity,
                    "큐 용량은 1 이상이어야 합니다.");
            }

            TimeSpan minimumInterval = TimeSpan.FromMilliseconds(1);

            TimeSpan maximumInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

            if (options.FlushInterval < minimumInterval ||
                options.FlushInterval > maximumInterval)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.FlushInterval,
                    $"Flush 주기는 {minimumInterval}부터 " + $"{maximumInterval}까지여야 합니다.");
            }
        }

        private async Task CompleteAsync(long endTimeUs)
        {
            Exception? failure = null;

            try
            {
                await _consumerTask;

                await _writer.WriteAsync(new TraceSessionEndRecord
                {
                    TimeUs = endTimeUs,
                    DroppedRecordCount = _droppedRecordCount
                });
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                await _writer.DisposeAsync();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(
                        "Tracer 종료와 Writer 정리 중 오류가 발생했습니다.",
                        failure,
                        exception);
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private void ThrowIfUnavailable()
        {
            if (_stopping)
            {
                throw new ObjectDisposedException(nameof(TimelineTracer));
            }

            if (_failure is not null)
            {
                throw new InvalidOperationException(
                    "백그라운드 저장이 실패하여 Tracer를 사용할 수 없습니다.",
                    _failure);
            }
        }

        internal static IReadOnlyDictionary<string, JsonElement>? CopyArgs(
            IReadOnlyDictionary<string, JsonElement>? args)
        {
            if (args is null)
            {
                return null;
            }

            var copy = new Dictionary<string, JsonElement>(args.Count, StringComparer.Ordinal);

            foreach (var pair in args)
            {
                if (pair.Value.ValueKind == JsonValueKind.Undefined)
                {
                    throw new ArgumentException(
                        $"Args에 초기화되지 않은 JSON 값이 있습니다: {pair.Key}",
                        nameof(args));
                }

                copy.Add(pair.Key, pair.Value.Clone());
            }

            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonElement>(copy);
        }
    }
}

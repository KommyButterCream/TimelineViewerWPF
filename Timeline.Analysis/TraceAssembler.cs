using System.Collections.ObjectModel;
using System.Text.Json;
using Timeline.Format;

namespace Timeline.Analysis
{
    public sealed class TraceAssembler
    {
        private readonly List<(TraceRecord Record, long LineNumber)> _records = [];

        /// <summary>
        /// 값 검증을 통과한 레코드와 1부터 시작하는 원본 줄 번호를 보관합니다.
        /// Args는 원본 컬렉션과 JSON 문서의 수명에서 분리해 복사합니다.
        /// </summary>
        public void Add(TraceRecord record, long lineNumber)
        {
            ArgumentNullException.ThrowIfNull(record);

            if (lineNumber < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lineNumber),
                    lineNumber,
                    "줄 번호는 1 이상이어야 합니다.");
            }

            TraceRecord snapshot = CopyRecord(record);

            _records.Add((snapshot, lineNumber));
        }

        /// <summary>
        /// 전체 입력의 구간과 참조 관계를 분석합니다. Add와 동시에 호출할 수 없습니다.
        /// 헤더는 정확히 하나, 세션 종료 레코드는 최대 하나가 필요합니다.
        /// </summary>
        public TraceAnalysisResult Build()
        {
            CollectedRecords collected = CollectRecords();

            List<TraceSpan> spans = BuildSpans(collected);

            ResolveParentCandidates(spans, collected.Diagnostics);

            RemoveParentCycles(spans, collected.Diagnostics);

            TraceSpan[] orderedSpans = spans
                .OrderBy(span => span.StartTimeUs)
                .ThenBy(span => span.BeginLineNumber)
                .ThenBy(span => span.Id, StringComparer.Ordinal)
                .ToArray();

            List<TraceTrack> tracks = BuildTracks(collected, orderedSpans);

            TraceAnalysisDiagnostic[] diagnostics = collected.Diagnostics
                .OrderBy(diagnostic => diagnostic.LineNumber)
                .ThenBy(diagnostic => diagnostic.Code)
                .ThenBy(diagnostic => diagnostic.SpanId, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.TrackId, StringComparer.Ordinal)
                .ToArray();

            return new TraceAnalysisResult
            {
                Header = collected.Header!,
                SessionEnd = collected.SessionEnd,
                Tracks = tracks.AsReadOnly(),
                Spans = Array.AsReadOnly(orderedSpans),
                Diagnostics = Array.AsReadOnly(diagnostics)
            };
        }

        private static TraceRecord CopyRecord(TraceRecord record)
        {
            if (record is not TraceBeginRecord begin ||
                begin.Args is null)
            {
                return record;
            }

            var args = new Dictionary<string, JsonElement>(
                begin.Args.Count, StringComparer.Ordinal);

            foreach (var pair in begin.Args)
            {
                args.Add(pair.Key, pair.Value.Clone());
            }

            return begin with
            {
                Args = new ReadOnlyDictionary<string, JsonElement>(args)
            };
        }

        private static void AddFirst<TRecord>(
            Dictionary<string, (TRecord Record, long LineNumber)> destination,
            string id,
            TRecord record,
            long lineNumber,
            TraceAnalysisDiagnosticCode duplicateCode,
            List<TraceAnalysisDiagnostic> diagnostics)
            where TRecord : TraceRecord
        {
            // ID 로 중복된 레코드가 있는지 확인한다.

            if (destination.TryGetValue(id, out var existing))
            {
                // 중복 존재
                bool isTrack = record is TraceTrackRecord;

                diagnostics.Add(new TraceAnalysisDiagnostic
                {
                    Code = duplicateCode,
                    Message = $"같은 ID의 레코드가 이미 존재합니다: {id}",
                    LineNumber = lineNumber,
                    RelatedLineNumber = existing.LineNumber,
                    SpanId = isTrack ? null : id,
                    TrackId = isTrack ? id : null
                });

                return;
            }

            destination.Add(id, (record, lineNumber));
        }

        private CollectedRecords CollectRecords()
        {
            // 레코드 분류와 중복 처리

            var collected = new CollectedRecords();

            foreach (var entry in _records)
            {
                switch (entry.Record)
                {
                    case TraceHeaderRecord header:
                        if (collected.Header is not null)
                        {
                            throw new InvalidOperationException(
                                "분석 입력에 헤더가 두 개 이상 있습니다.");
                        }

                        collected.Header = header;
                        break;

                    case TraceSessionEndRecord sessionEnd:
                        if (collected.SessionEnd is not null)
                        {
                            throw new InvalidOperationException(
                                "분석 입력에 세션 종료 레코드가 두 개 이상 있습니다.");
                        }

                        collected.SessionEnd = sessionEnd;
                        break;

                    case TraceTrackRecord track:
                        AddFirst(
                            collected.Tracks,
                            track.Id,
                            track,
                            entry.LineNumber,
                            TraceAnalysisDiagnosticCode.DuplicateTrack,
                            collected.Diagnostics);
                        break;

                    case TraceBeginRecord begin:
                        AddFirst(
                            collected.Begins,
                            begin.Id,
                            begin,
                            entry.LineNumber,
                            TraceAnalysisDiagnosticCode.DuplicateBegin,
                            collected.Diagnostics);
                        break;

                    case TraceEndRecord end:
                        AddFirst(
                            collected.Ends,
                            end.Id,
                            end,
                            entry.LineNumber,
                            TraceAnalysisDiagnosticCode.DuplicateEnd,
                            collected.Diagnostics);
                        break;

                    default:
                        throw new ArgumentException(
                            $"지원하지 않는 레코드 타입입니다: " +
                            entry.Record.GetType().Name);
                }
            }

            if (collected.Header is null)
            {
                throw new InvalidOperationException(
                    "분석하려면 헤더가 필요합니다.");
            }

            return collected;
        }

        private static List<TraceSpan> BuildSpans(CollectedRecords collected)
        {
            // 읽은 레코드로부터 Begin - End 쌍을 찾아 TraceSpan 을 생성하여 저장한다.
            // 시작·종료 누락과 종료 시각 역전은 진단으로 처리합니다.

            var spans = new List<TraceSpan>();

            // Begin Record 기준으로 End Record 를 찾는다.
            foreach (var entry in collected.Begins.Values)
            {
                TraceBeginRecord begin = entry.Record;

                long? endTimeUs = null;
                long? endLineNumber = null;
                TraceSpanStatus status;

                if (!collected.Ends.TryGetValue(begin.Id, out var endEntry))
                {
                    // End Record 가 없는 경우
                    status = TraceSpanStatus.MissingEnd;

                    collected.Diagnostics.Add(new TraceAnalysisDiagnostic
                    {
                        Code = TraceAnalysisDiagnosticCode.MissingEnd,
                        Message = $"종료 레코드가 없습니다: {begin.Id}",
                        LineNumber = entry.LineNumber,
                        SpanId = begin.Id,
                        TrackId = begin.TrackId
                    });
                }
                else
                {
                    // End Record 가 있는 경우
                    // endEntry: 찾은 End Record와 LineNumber의 튜플

                    TraceEndRecord end = endEntry.Record;

                    endLineNumber = endEntry.LineNumber;

                    if (end.TimeUs < begin.TimeUs)
                    {
                        // End 시간이 Begin 보다 작은 경우
                        status = TraceSpanStatus.InvalidEnd;

                        collected.Diagnostics.Add(new TraceAnalysisDiagnostic
                        {
                            Code = TraceAnalysisDiagnosticCode.InvalidEndTime,
                            Message = $"종료 시각 {end.TimeUs}µs가 시작 시각 " +
                            $"{begin.TimeUs}µs보다 빠릅니다.",
                            LineNumber = endEntry.LineNumber,
                            RelatedLineNumber = entry.LineNumber,
                            SpanId = begin.Id,
                            TrackId = begin.TrackId
                        });
                    }
                    else
                    {
                        // Time 기록이 정상인 경우
                        // 유효한 Begin/End 쌍의 상태와 종료 시각을 기록합니다.
                        status = TraceSpanStatus.Completed;
                        endTimeUs = end.TimeUs;
                    }
                }

                // TraceSpan 을 저장한다.
                // 유효한 종료가 없으면 EndTimeUs는 null입니다.
                // 잘못된 End가 존재하면 EndLineNumber는 원본 위치를 유지합니다.
                spans.Add(new TraceSpan
                {
                    Id = begin.Id,
                    TrackId = begin.TrackId,
                    Name = begin.Name,
                    StartTimeUs = begin.TimeUs,
                    EndTimeUs = endTimeUs,
                    Status = status,
                    OriginalParentId = begin.ParentId,
                    ParentId = null,
                    Args = begin.Args,
                    BeginLineNumber = entry.LineNumber,
                    EndLineNumber = endLineNumber
                });
            }

            // End Record 기준으로 Begin Record 를 찾는다.
            foreach (var entry in collected.Ends.Values)
            {
                TraceEndRecord end = entry.Record;

                if (collected.Begins.ContainsKey(end.Id))
                {
                    // Begin 찾은 경우 정상, 다음 End Record 진행.
                    continue;
                }

                // Begin 못찾은 경우
                // 시작 레코드가 없다는 진단을 저장한다.
                collected.Diagnostics.Add(new TraceAnalysisDiagnostic
                {
                    Code = TraceAnalysisDiagnosticCode.MissingBegin,
                    Message = $"시작 레코드가 없습니다: {end.Id}",
                    LineNumber = entry.LineNumber,
                    SpanId = end.Id
                });
            }

            return spans;
        }

        private static void ResolveParentCandidates(
            List<TraceSpan> spans,
            List<TraceAnalysisDiagnostic> diagnostics)
        {
            var spansById = spans.ToDictionary(
                span => span.Id,
                StringComparer.Ordinal);

            for (int index = 0; index < spans.Count; index++)
            {
                TraceSpan span = spans[index];
                string? parentId = span.OriginalParentId;

                // Parent 가 없으면 다음 진행
                if (parentId is null)
                {
                    continue;
                }

                if (string.Equals(
                    span.Id,
                    parentId,
                    StringComparison.Ordinal))
                {
                    // 내 자신이 Parent 인 경우
                    diagnostics.Add(new TraceAnalysisDiagnostic
                    {
                        Code = TraceAnalysisDiagnosticCode.SelfParent,
                        Message = "작업은 자기 자신을 부모로 지정할 수 없습니다.",
                        LineNumber = span.BeginLineNumber,
                        SpanId = span.Id,
                        TrackId = span.TrackId
                    });

                    continue;
                }

                if (!spansById.ContainsKey(parentId))
                {
                    // 원본 부모 ID에 해당하는 작업을 찾지 못한 경우
                    diagnostics.Add(new TraceAnalysisDiagnostic
                    {
                        Code = TraceAnalysisDiagnosticCode.MissingParent,
                        Message = $"부모 작업을 찾을 수 없습니다: {parentId}",
                        LineNumber = span.BeginLineNumber,
                        SpanId = span.Id,
                        TrackId = span.TrackId
                    });

                    continue;
                }

                // 유효한 부모를 찾았을 때에만 ParentId 지정
                // 못찾은 경우에 ParentId 는 null
                spans[index] = span with
                {
                    ParentId = parentId
                };
            }
        }

        private static void RemoveParentCycles(
            List<TraceSpan> spans,
            List<TraceAnalysisDiagnostic> diagnostics)
        {
            var indexById = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int index = 0; index < spans.Count; index++)
            {
                indexById.Add(spans[index].Id, index);
            }

            var processed = new HashSet<string>(StringComparer.Ordinal);

            for (int startIndex = 0; startIndex < spans.Count; startIndex++)
            {
                string startId = spans[startIndex].Id;

                if (processed.Contains(startId))
                {
                    continue;
                }

                var path = new List<string>();

                // 현재 경로에서 각 ID 가 몇 번째에 나왔는지 확인
                // 같은 ID 를 만났을 때, 경로의 어디부터 순환인지 알 수 있다.
                var positionInPath = new Dictionary<string, int>(StringComparer.Ordinal);

                string? currentId = startId;

                while (currentId is not null &&
                       !processed.Contains(currentId))
                {
                    if (positionInPath.TryGetValue(
                        currentId,
                        out int cycleStart))
                    {
                        // 순환 발견한 경우
                        for (int position = cycleStart; position < path.Count; position++)
                        {
                            // 순환에 참여한 작업만 for-loop 처리
                            int spanIndex = indexById[path[position]];
                            TraceSpan span = spans[spanIndex];

                            long? parentLineNumber = span.ParentId is string parentId
                                ? spans[indexById[parentId]].BeginLineNumber
                                : null;

                            diagnostics.Add(new TraceAnalysisDiagnostic
                            {
                                Code = TraceAnalysisDiagnosticCode.ParentCycle,
                                Message = "부모 관계가 순환하여 연결을 해제했습니다.",
                                LineNumber = span.BeginLineNumber,
                                RelatedLineNumber = parentLineNumber,
                                SpanId = span.Id,
                                TrackId = span.TrackId
                            });

                            // 부모 연결 해제
                            spans[spanIndex] = span with
                            {
                                ParentId = null
                            };
                        }

                        break;
                    }

                    // 처음 만난 작업인 경우 경로에 추가
                    positionInPath.Add(currentId, path.Count);
                    path.Add(currentId);

                    TraceSpan current = spans[indexById[currentId]];
                    currentId = current.ParentId;
                }

                foreach (string id in path)
                {
                    processed.Add(id);
                }
            }
        }

        private static List<TraceTrack> BuildTracks(
            CollectedRecords collected,
            IReadOnlyList<TraceSpan> spans)
        {
            // 작업을 TrackID 에 따라 그룹핑을 한다.
            // 이때, 시작 시간과 line number 를 기준으로 정렬한다.
            // Track 정의가 없을 수 있다.
            var spansByTrack = spans
                .GroupBy(span => span.TrackId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(span => span.StartTimeUs)
                        .ThenBy(span => span.BeginLineNumber)
                        .ToArray(),
                    StringComparer.Ordinal);

            var tracks = new List<TraceTrack>();

            // 모든 Track 정의를 순회
            foreach (var entry in collected.Tracks.Values
                .OrderBy(entry => entry.LineNumber)
                .ThenBy(entry => entry.Record.Id, StringComparer.Ordinal))
            {
                TraceTrackRecord definition = entry.Record;

                if (!spansByTrack.TryGetValue(definition.Id, out TraceSpan[]? trackSpans))
                {
                    // 작업이 없는 Track 에는 비어있는 배열을 사용.
                    trackSpans = [];
                }

                // 정상 Track 결과 생성
                tracks.Add(new TraceTrack
                {
                    Id = definition.Id,
                    Name = definition.Name,
                    IsPlaceholder = false,
                    DefinitionLineNumber = entry.LineNumber,
                    Spans = Array.AsReadOnly(trackSpans)
                });
            }

            // 정의가 빠진 Track 처리
            foreach (var group in spansByTrack
                .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                string trackId = group.Key;

                if (collected.Tracks.ContainsKey(trackId))
                {
                    // 첫 번째 반복문에서 처리한 정상 Track은 건너뜁니다.
                    continue;
                }

                // Span 은 있는데 Track 이 없는 경우
                foreach (TraceSpan span in group.Value)
                {
                    // 없는 Track 을 참조한 작업마다 진단을 추가한다.
                    collected.Diagnostics.Add(new TraceAnalysisDiagnostic
                    {
                        Code = TraceAnalysisDiagnosticCode.MissingTrack,
                        Message = $"Track 정의를 찾을 수 없습니다: {trackId}",
                        LineNumber = span.BeginLineNumber,
                        SpanId = span.Id,
                        TrackId = trackId
                    });
                }

                // 임시 Track 생성
                tracks.Add(new TraceTrack
                {
                    Id = trackId,
                    Name = trackId,
                    IsPlaceholder = true,
                    DefinitionLineNumber = null,
                    Spans = Array.AsReadOnly(group.Value)
                });
            }

            return tracks;
        }

        private sealed class CollectedRecords
        {
            public TraceHeaderRecord? Header { get; set; }
            public TraceSessionEndRecord? SessionEnd { get; set; }

            public Dictionary<
                string,
                (TraceTrackRecord Record, long LineNumber)> Tracks
            { get; } = new(StringComparer.Ordinal);

            public Dictionary<
               string,
               (TraceBeginRecord Record, long LineNumber)> Begins
            { get; } = new(StringComparer.Ordinal);

            public Dictionary<
               string,
               (TraceEndRecord Record, long LineNumber)> Ends
            { get; } = new(StringComparer.Ordinal);

            public List<TraceAnalysisDiagnostic> Diagnostics { get; } = [];
        }
    }
}

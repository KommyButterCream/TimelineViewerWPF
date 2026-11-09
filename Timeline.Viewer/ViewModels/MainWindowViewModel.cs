using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Timeline.Analysis;
using Timeline.IO;
using Timeline.Viewer.Commands;
using Timeline.Viewer.Models;
using Timeline.Viewer.Samples;
using Timeline.Viewer.Services;

namespace Timeline.Viewer.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private const string DefaultStatusText = "Ready · 로그 파일을 열어주세요";

        private readonly IThemeService _themeService;
        private readonly IFileDialogService _fileDialogService;
        private readonly ITraceLoadService _traceLoadService;
        private ViewerState _state = ViewerState.Empty;
        private long _loadGeneration;
        public TraceDocument? CurrentDocument { get; private set; }
        public bool IsLoading => State == ViewerState.Loading;

        public ViewerState State
        {
            get => _state;
            private set
            {
                bool wasLoading = IsLoading;
                if (SetProperty(ref _state, value) && wasLoading != IsLoading)
                {
                    OnPropertyChanged(nameof(IsLoading));
                    OpenFileCommand.NotifyCanExecuteChanged();
                    CancelLoadCommand.NotifyCanExecuteChanged();
                    UpdateDiagnosticCommands();
                    OpenSampleCommand.NotifyCanExecuteChanged();
                }
            }
        }
        private string _statusText = DefaultStatusText;
        private AppTheme _currentTheme = AppTheme.Black;

        private TrackItemViewModel? _selectedTrack;
        private SpanItemViewModel? _selectedSpan;

        private string _searchText = string.Empty;
        private string _searchSummary = "이름 · ID · Track으로 검색";

        private ObservableCollection<TrackItemViewModel> _tracks = new();
        private TraceReadMode _selectedReadMode = TraceReadMode.Recovery;

        private CancellationTokenSource? _loadCancellation;

        private TraceLoadStage _loadingStage;
        private long _loadedRecordCount;

        private IReadOnlyList<DiagnosticItem> _diagnostics = Array.Empty<DiagnosticItem>();

        public MainWindowViewModel(
            IThemeService themeService,
            IFileDialogService fileDialogService,
            ITraceLoadService traceLoadService)
        {
            ArgumentNullException.ThrowIfNull(themeService);
            ArgumentNullException.ThrowIfNull(fileDialogService);
            ArgumentNullException.ThrowIfNull(traceLoadService);

            _themeService = themeService;
            _fileDialogService = fileDialogService;
            _traceLoadService = traceLoadService;
            InitializeDiagnosticCommands();
            OpenSampleCommand = new RelayCommand(OpenSample, () => !IsLoading);

            ResetStatusCommand = new RelayCommand(ResetStatus, CanResetStatus);

            ToggleThemeCommand = new RelayCommand(ToggleTheme);

            AddSampleSpanCommand = new RelayCommand(AddSampleSpan);

            SearchCommand = new RelayCommand(Search);

            CancelLoadCommand = new RelayCommand(CancelLoad,
                () => IsLoading
                && _loadCancellation is not null
                && !_loadCancellation.IsCancellationRequested);

            OpenFileCommand = new AsyncRelayCommand(OpenFileAsync, OnOpenFileError, () => !IsLoading);

            PreviousSearchResultCommand = new RelayCommand(
                () => MoveSearchResult(-1),
                () => SearchResults.Count > 0);

            NextSearchResultCommand = new RelayCommand(
                () => MoveSearchResult(1),
                () => SearchResults.Count > 0);

            _themeService.ApplyTheme(_currentTheme);

        }

        public RelayCommand ResetStatusCommand { get; }

        public RelayCommand ToggleThemeCommand { get; }

        public RelayCommand AddSampleSpanCommand { get; }

        public RelayCommand SearchCommand { get; }

        public RelayCommand PreviousSearchResultCommand { get; }
        public RelayCommand NextSearchResultCommand { get; }

        public RelayCommand CancelLoadCommand { get; }
        public AsyncRelayCommand OpenFileCommand { get; }

        public event Action<SpanItemViewModel>? SpanRevealRequested;
        public event Action? DocumentOpened;

        public ObservableCollection<TrackItemViewModel> Tracks
        {
            get => _tracks;

            private set => SetProperty(ref _tracks, value);
        }

        public ObservableCollection<SpanItemViewModel> SearchResults { get; } = new();
        public TrackItemViewModel? SelectedTrack
        {
            get => _selectedTrack;
            set
            {
                if (SetProperty(ref _selectedTrack, value))
                {
                    SelectedSpan = null;
                }
            }
        }

        public SpanItemViewModel? SelectedSpan
        {
            get => _selectedSpan;
            set
            {
                if (value is not null)
                {
                    foreach (TrackItemViewModel track in Tracks)
                    {
                        if (track.Spans.Contains(value))
                        {
                            SelectedTrack = track;
                            break;
                        }
                    }
                }

                if (SetProperty(ref _selectedSpan, value) && SearchResults.Count > 0)
                {
                    int index = value is null ? -1 : SearchResults.IndexOf(value);
                    SearchSummary = index >= 0
                        ? $"검색 결과 {index + 1} / {SearchResults.Count}"
                        : $"검색 결과 {SearchResults.Count}개 · 결과 선택 없음";
                }
            }
        }

        public string StatusText
        {
            get => _statusText;

            set
            {
                if (SetProperty(ref _statusText, value))
                {
                    ResetStatusCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public AppTheme CurrentTheme
        {
            get => _currentTheme;

            private set
            {
                if (SetProperty(ref _currentTheme, value))
                {
                    OnPropertyChanged(nameof(ThemeButtonText));
                }
            }
        }

        public string ThemeButtonText =>
            CurrentTheme == AppTheme.Black
                ? "White 테마로 전환"
                : "Black 테마로 전환";

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public string SearchSummary
        {
            get => _searchSummary;
            private set => SetProperty(ref _searchSummary, value);
        }

        public IReadOnlyList<TraceReadMode> ReadModes { get; } =
            Array.AsReadOnly(Enum.GetValues<TraceReadMode>());

        public TraceReadMode SelectedReadMode
        {
            get => _selectedReadMode;
            set => SetProperty(ref _selectedReadMode, value);
        }

        public TraceLoadStage LoadingStage
        {
            get => _loadingStage;
            private set
            {
                if (SetProperty(ref _loadingStage, value))
                {
                    OnPropertyChanged(nameof(LoadingStageText));
                }
            }
        }

        public long LoadedRecordCount
        {
            get => _loadedRecordCount;
            private set
            {
                if (SetProperty(ref _loadedRecordCount, value))
                {
                    OnPropertyChanged(nameof(LoadingStageText));
                }
            }
        }

        public string LoadingStageText => LoadingStage switch
        {
            TraceLoadStage.Reading =>
                $"파일 읽는 중 · 정상 레코드 {LoadedRecordCount:N0}개",

            TraceLoadStage.Analyzing =>
                $"분석 중 · 정상 레코드 {LoadedRecordCount:N0}개",

            TraceLoadStage.Preparing =>
                "화면 데이터 준비 중",

            TraceLoadStage.Cancelling =>
                "취소 요청 중 · 현재 작업이 끝나기를 기다립니다.",

            _ => string.Empty
        };

        public IReadOnlyList<DiagnosticItem> Diagnostics
        {
            get => _diagnostics;
            private set
            {
                if (SetProperty(ref _diagnostics, value))
                {
                    OnPropertyChanged(nameof(DiagnosticCount));
                    OnPropertyChanged(nameof(HasDiagnostics));
                    OnPropertyChanged(nameof(DiagnosticSummary));
                }
            }
        }

        public int DiagnosticCount => Diagnostics.Count;

        public bool HasDiagnostics => DiagnosticCount > 0;

        public string DiagnosticSummary => HasDiagnostics
            ? $"진단 {DiagnosticCount:N0}건"
            : "진단 없음";

        private void ToggleTheme()
        {
            AppTheme nextTheme =
                CurrentTheme == AppTheme.Black
                    ? AppTheme.White
                    : AppTheme.Black;

            _themeService.ApplyTheme(nextTheme);

            CurrentTheme = nextTheme;
        }

        private void ResetStatus()
        {
            StatusText = DefaultStatusText;
        }

        private bool CanResetStatus()
        {
            return StatusText != DefaultStatusText;
        }

        private void AddSampleSpan()
        {
            TrackItemViewModel? track = SelectedTrack;

            if (track is null)
            {
                return;
            }

            var span = new SpanItemViewModel(
                Guid.NewGuid().ToString("N"),
                "added sample",
                220_000,
                235_000);

            track.Spans.Add(span);
        }

        private void Search()
        {
            SearchResults.Clear();

            UpdateSearchCommands();

            string query = SearchText.Trim();

            if (query.Length == 0)
            {
                SearchSummary = "검색어를 입력하세요";
                return;
            }

            foreach (TrackItemViewModel track in Tracks)
            {
                bool trackMatches = track.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || track.Id.Contains(query, StringComparison.OrdinalIgnoreCase);

                foreach (SpanItemViewModel span in track.Spans)
                {
                    bool spanMatches = span.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || span.Id.Contains(query, StringComparison.OrdinalIgnoreCase);

                    if (trackMatches || spanMatches)
                    {
                        SearchResults.Add(span);
                    }
                }
            }

            SearchSummary = $"검색 결과 {SearchResults.Count}개";

            UpdateSearchCommands();

            if (SearchResults.Count > 0)
            {
                SelectSearchResult(0);
            }
        }

        private void UpdateSearchCommands()
        {
            PreviousSearchResultCommand.NotifyCanExecuteChanged();
            NextSearchResultCommand.NotifyCanExecuteChanged();
        }

        private void SelectSearchResult(int index)
        {
            SpanItemViewModel span = SearchResults[index];

            SelectedSpan = span;
            SearchSummary = $"검색 결과 {index + 1} / {SearchResults.Count}";

            SpanRevealRequested?.Invoke(span);
        }

        private void MoveSearchResult(int direction)
        {
            if (SearchResults.Count == 0)
            {
                return;
            }

            int index = SelectedSpan is null ? -1 : SearchResults.IndexOf(SelectedSpan);

            if (index < 0)
            {
                index = direction > 0 ? 0 : SearchResults.Count - 1;
            }
            else
            {
                index = (index + direction + SearchResults.Count) % SearchResults.Count;
            }

            SelectSearchResult(index);
        }

        private static List<TrackItemViewModel> CreateTrackViewModels(TraceDocument document, CancellationToken cancellationToken)
        {
            var tracks = new List<TrackItemViewModel>();

            foreach (TraceTrack sourceTrack in document.Analysis.Tracks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var track = new TrackItemViewModel(sourceTrack.Id, sourceTrack.Name);

                foreach (TraceSpan sourceSpan in sourceTrack.Spans)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var span = new SpanItemViewModel(
                        sourceSpan.Id,
                        sourceSpan.Name,
                        sourceSpan.StartTimeUs,
                        sourceSpan.EndTimeUs,
                        args: sourceSpan.Args,
                        sourceFilePath: document.FilePath,
                        beginLineNumber: sourceSpan.BeginLineNumber,
                        endLineNumber: sourceSpan.EndLineNumber,
                        analysisStatus: sourceSpan.Status);

                    track.Spans.Add(span);
                }

                tracks.Add(track);
            }

            return tracks;
        }

        private async Task OpenFileAsync()
        {
            string? filePath = _fileDialogService.SelectTraceFile();

            if (filePath is null)
            {
                return;
            }

            await OpenFilePathAsync(filePath);
        }

        public async Task OpenFilePathAsync(string filePath)
        {
            if (IsLoading)
            {
                return;
            }

            using var cancellation = new CancellationTokenSource();
            long generation = ++_loadGeneration;
            ViewerState previousState = State;
            ViewerState completionState = previousState;

            _loadCancellation = cancellation;
            CancelSourcePreview();
            NotificationText = string.Empty;

            LoadedRecordCount = 0;
            LoadingStage = TraceLoadStage.Reading;
            State = ViewerState.Loading;

            try
            {
                await LoadAndApplyAsync(filePath, generation, cancellation.Token);
                completionState = CurrentDocument is { DiagnosticCount: > 0 }
                    ? ViewerState.ReadyWithDiagnostics
                    : ViewerState.Ready;
            }
            catch (OperationCanceledException exception)
            {
                if (generation == _loadGeneration)
                    OnOpenFileError(exception);
            }
            catch (Exception exception)
            {
                completionState = ViewerState.Failed;
                if (generation == _loadGeneration)
                    OnOpenFileError(exception);
            }
            finally
            {
                if (generation == _loadGeneration)
                {
                    _loadCancellation = null;
                    LoadingStage = TraceLoadStage.None;
                    State = completionState;
                }
            }
        }

        private async Task LoadAndApplyAsync(string filePath, long generation, CancellationToken cancellationToken)
        {
            TraceReadMode mode = SelectedReadMode;

            StatusText = $"읽는 중 · {Path.GetFileName(filePath)} · {mode}";

            bool acceptProgress = true;
            var progress = new Progress<TraceLoadProgress>(update =>
            {
                if (!acceptProgress || generation != _loadGeneration
                    || cancellationToken.IsCancellationRequested)
                    return;

                LoadedRecordCount = update.RecordCount;
                LoadingStage = update.Stage;
            });

            TraceDocument document;
            try
            {
                document = await _traceLoadService.LoadAsync(
                    filePath, mode, cancellationToken, progress);
            }
            finally
            {
                acceptProgress = false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _loadGeneration)
                return;
            LoadingStage = TraceLoadStage.Preparing;

            var prepared = await Task.Run(() =>
            {
                List<TrackItemViewModel> tracks = CreateTrackViewModels(document, cancellationToken);

                IReadOnlyList<DiagnosticItem> diagnostics = DiagnosticMapper.Map(document, cancellationToken);

                return (Tracks: tracks, Diagnostics: diagnostics);
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // await 중 작업이 바뀌었다면 오래된 문서를 화면에 게시하지 않습니다.
            if (generation != _loadGeneration)
                return;

            var newTracks = new ObservableCollection<TrackItemViewModel>(prepared.Tracks);

            SearchResults.Clear();
            UpdateSearchCommands();
            SearchText = string.Empty;
            SearchSummary = "이름 · ID · Track으로 검색";

            SelectedSpan = null;
            SelectedTrack = null;

            SelectedDiagnostic = null;
            Tracks = newTracks;
            HasSession = true;
            Diagnostics = prepared.Diagnostics;
            CurrentDocument = document;
            OnPropertyChanged(nameof(CurrentDocument));
            OnPropertyChanged(nameof(SessionTitle));
            OnPropertyChanged(nameof(SessionSummary));
            SelectedTrack = Tracks.Count > 0 ? Tracks[0] : null;
            NotificationText = document.DiagnosticCount > 0
                ? $"진단 포함 완료 · {document.DiagnosticCount:N0}건을 확인하세요."
                : "완료 · 로그 파일을 열었습니다.";

            StatusText =
                $"Ready · {Path.GetFileName(document.FilePath)}"
                + $" · {mode}"
                + $" · Track {Tracks.Count}개"
                + $" · Span {document.Analysis.Spans.Count}개";

            DocumentOpened?.Invoke();
        }

        private void OnOpenFileError(Exception exception)
        {
            string message = exception switch
            {
                OperationCanceledException =>
                    "파일 열기가 취소되었습니다.",

                FileNotFoundException =>
                    "파일을 찾을 수 없습니다. 이동하거나 삭제되었는지 확인하세요.",

                DirectoryNotFoundException =>
                    "파일이 있는 폴더를 찾을 수 없습니다.",

                UnauthorizedAccessException =>
                    "파일을 읽을 권한이 없습니다.",

                InvalidDataException =>
                    $"로그 형식을 확인하세요. {exception.Message}",

                IOException =>
                    $"파일을 읽는 중 오류가 발생했습니다. {exception.Message}",

                _ =>
                    $"파일을 여는 중 오류가 발생했습니다. {exception.Message}"
            };

            string prefix = exception is OperationCanceledException
                ? "열기 취소"
                : "파일 열기 실패";

            StatusText = $"{prefix} · {message}";
            NotificationText = $"{prefix} · {message}";
        }

        private void CancelLoad()
        {
            if (!CancelLoadCommand.CanExecute(null))
            {
                return;
            }

            _loadCancellation!.Cancel();

            LoadingStage = TraceLoadStage.Cancelling;
            StatusText = LoadingStageText;

            CancelLoadCommand.NotifyCanExecuteChanged();
        }
    }
}

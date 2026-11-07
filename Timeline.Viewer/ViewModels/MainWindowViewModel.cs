using System;
using System.Collections.ObjectModel;
using Timeline.Viewer.Commands;
using Timeline.Viewer.Models;
using Timeline.Viewer.Services;
using Timeline.Viewer.Samples;

namespace Timeline.Viewer.ViewModels
{
    public class MainWindowViewModel : ObservableObject
    {
        private const string DefaultStatusText = "Ready · 샘플 세션";

        private readonly IThemeService _themeService;
        private string _statusText = DefaultStatusText;
        private AppTheme _currentTheme = AppTheme.Black;

        private TrackItemViewModel? _selectedTrack;
        private SpanItemViewModel? _selectedSpan;

        private string _searchText = string.Empty;
        private string _searchSummary = "이름 · ID · Track으로 검색";

        public MainWindowViewModel(IThemeService themeService)
        {
            ArgumentNullException.ThrowIfNull(themeService);

            _themeService = themeService;

            ResetStatusCommand = new RelayCommand(ResetStatus, CanResetStatus);

            ToggleThemeCommand = new RelayCommand(ToggleTheme);

            AddSampleSpanCommand = new RelayCommand(AddSampleSpan);

            SearchCommand = new RelayCommand(Search);

            PreviousSearchResultCommand = new RelayCommand(
                () => MoveSearchResult(-1),
                () => SearchResults.Count > 0);

            NextSearchResultCommand = new RelayCommand(
                () => MoveSearchResult(1),
                () => SearchResults.Count > 0);

            _themeService.ApplyTheme(_currentTheme);

            foreach (TrackItemViewModel track in SampleTraceFactory.CreateTracks())
            {
                Tracks.Add(track);
            }

            SelectedTrack = Tracks.Count > 0 ? Tracks[0] : null;
        }

        public RelayCommand ResetStatusCommand { get; }

        public RelayCommand ToggleThemeCommand { get; }

        public RelayCommand AddSampleSpanCommand { get; }

        public RelayCommand SearchCommand { get; }

        public RelayCommand PreviousSearchResultCommand { get; }
        public RelayCommand NextSearchResultCommand { get; }

        public event Action<SpanItemViewModel>? SpanRevealRequested;

        public ObservableCollection<TrackItemViewModel> Tracks { get; } =
            new ObservableCollection<TrackItemViewModel>();

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
    }
}

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

        public MainWindowViewModel(IThemeService themeService)
        {
            ArgumentNullException.ThrowIfNull(themeService);

            _themeService = themeService;

            ResetStatusCommand = new RelayCommand(ResetStatus, CanResetStatus);

            ToggleThemeCommand = new RelayCommand(ToggleTheme);

            AddSampleSpanCommand = new RelayCommand(AddSampleSpan);

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

        public ObservableCollection<TrackItemViewModel> Tracks { get; } =
            new ObservableCollection<TrackItemViewModel>();

        public TrackItemViewModel? SelectedTrack
        {
            get => _selectedTrack;
            set => SetProperty(ref _selectedTrack, value);
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
    }
}

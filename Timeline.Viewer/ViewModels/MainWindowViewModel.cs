using System;
using System.Collections.ObjectModel;
using Timeline.Viewer.Commands;
using Timeline.Viewer.Models;
using Timeline.Viewer.Services;

namespace Timeline.Viewer.ViewModels
{
    public class MainWindowViewModel : ObservableObject
    {
        private const string DefaultStatusText = "Ready · 세션 없음 · 진단 0건";

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

            _themeService.ApplyTheme(_currentTheme);

            Tracks.Add(new TrackItemViewModel("main", "Main thread"));
            Tracks.Add(new TrackItemViewModel("workers", "Worker pool"));
            Tracks.Add(new TrackItemViewModel("file-io", "File I/O"));

            SelectedTrack = Tracks[0];
        }

        public RelayCommand ResetStatusCommand { get; }

        public RelayCommand ToggleThemeCommand { get; }

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
    }
}

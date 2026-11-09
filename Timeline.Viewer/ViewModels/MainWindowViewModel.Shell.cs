using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Timeline.Viewer.Commands;
using Timeline.Viewer.Models;
using Timeline.Viewer.Samples;

namespace Timeline.Viewer.ViewModels
{
    public partial class MainWindowViewModel
    {
        private bool _hasSession;
        private bool _isDiagnosticsExpanded;
        public RelayCommand OpenSampleCommand { get; }
        public bool HasSession
        {
            get => _hasSession;
            private set => SetProperty(ref _hasSession, value);
        }
        public bool IsDiagnosticsExpanded
        {
            get => _isDiagnosticsExpanded;
            set => SetProperty(ref _isDiagnosticsExpanded, value);
        }
        public string SessionTitle => CurrentDocument is null
            ? "샘플 세션" : Path.GetFileName(CurrentDocument.FilePath);
        public string SessionSummary => $"{Tracks.Count:N0} tracks · {Tracks.Sum(t => t.Spans.Count):N0} spans · 상대 시간";

        private void OpenSample()
        {
            CancelSourcePreview();
            SelectedDiagnostic = null;
            SelectedSpan = null;
            SelectedTrack = null;
            SearchResults.Clear();
            UpdateSearchCommands();
            SearchText = string.Empty;
            SearchSummary = "이름 · ID · Track으로 검색";
            Diagnostics = System.Array.Empty<DiagnosticItem>();
            CurrentDocument = null;
            Tracks = new ObservableCollection<TrackItemViewModel>(SampleTraceFactory.CreateTracks());
            SelectedTrack = Tracks.FirstOrDefault();
            HasSession = true;
            IsDiagnosticsExpanded = false;
            State = ViewerState.Ready;
            StatusText = "Ready · 샘플 세션";
            NotificationText = string.Empty;
            OnPropertyChanged(nameof(CurrentDocument));
            OnPropertyChanged(nameof(SessionTitle));
            OnPropertyChanged(nameof(SessionSummary));
            DocumentOpened?.Invoke();
        }
    }
}

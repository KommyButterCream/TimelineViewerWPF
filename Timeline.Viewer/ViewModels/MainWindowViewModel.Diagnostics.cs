using System;
using System.Threading;
using System.Threading.Tasks;
using Timeline.Viewer.Commands;
using Timeline.Viewer.Models;
using Timeline.Viewer.Services;

namespace Timeline.Viewer.ViewModels
{
    // partial은 동일한 ViewModel을 파일별로 나누는 문법입니다.
    public partial class MainWindowViewModel
    {
        private readonly SourcePreviewService _sourcePreviewService = new();
        private DiagnosticItem? _selectedDiagnostic;
        private CancellationTokenSource? _sourceCancellation;
        private long _sourceGeneration;
        private string _sourcePreviewText = "진단을 선택하고 원본 줄 보기를 누르세요.";
        private string _diagnosticNavigationText = "진단을 선택하세요.";
        private string _notificationText = string.Empty;

        public AsyncRelayCommand ShowSourceCommand { get; private set; } = null!;
        public AsyncRelayCommand ShowRelatedSourceCommand { get; private set; } = null!;
        public RelayCommand DismissNotificationCommand { get; private set; } = null!;

        public DiagnosticItem? SelectedDiagnostic
        {
            get => _selectedDiagnostic;
            set
            {
                if (!SetProperty(ref _selectedDiagnostic, value)) return;
                CancelSourcePreview();
                DiagnosticNavigationText = "진단을 선택하세요.";
                if (value is not null) NavigateToDiagnostic(value);
                UpdateDiagnosticCommands();
            }
        }

        public string SourcePreviewText
        {
            get => _sourcePreviewText;
            private set => SetProperty(ref _sourcePreviewText, value);
        }

        public string DiagnosticNavigationText
        {
            get => _diagnosticNavigationText;
            private set => SetProperty(ref _diagnosticNavigationText, value);
        }

        public string NotificationText
        {
            get => _notificationText;
            private set
            {
                if (SetProperty(ref _notificationText, value))
                    OnPropertyChanged(nameof(HasNotification));
            }
        }

        public bool HasNotification => !string.IsNullOrEmpty(NotificationText);

        private void InitializeDiagnosticCommands()
        {
            ShowSourceCommand = new AsyncRelayCommand(
                () => ShowSourceAsync(false), OnSourcePreviewError,
                () => !IsLoading && SelectedDiagnostic?.HasSourceLocation == true);
            ShowRelatedSourceCommand = new AsyncRelayCommand(
                () => ShowSourceAsync(true), OnSourcePreviewError,
                () => !IsLoading && SelectedDiagnostic?.RelatedLineNumber is > 0);
            DismissNotificationCommand = new RelayCommand(() => NotificationText = string.Empty);
        }

        private void UpdateDiagnosticCommands()
        {
            ShowSourceCommand.NotifyCanExecuteChanged();
            ShowRelatedSourceCommand.NotifyCanExecuteChanged();
        }

        private void NavigateToDiagnostic(DiagnosticItem diagnostic)
        {
            if (diagnostic.SpanId is string spanId)
            {
                foreach (TrackItemViewModel track in Tracks)
                    foreach (SpanItemViewModel span in track.Spans)
                    {
                        if (!string.Equals(span.Id, spanId, StringComparison.Ordinal)) continue;
                        SelectedSpan = span;
                        SpanRevealRequested?.Invoke(span);
                        DiagnosticNavigationText = $"관련 Span: {span.Name}";
                        return;
                    }
            }

            SelectedSpan = null;
            TrackItemViewModel? relatedTrack = null;
            foreach (TrackItemViewModel track in Tracks)
            {
                if (string.Equals(track.Id, diagnostic.TrackId, StringComparison.Ordinal))
                {
                    relatedTrack = track;
                    break;
                }
            }
            SelectedTrack = relatedTrack;
            DiagnosticNavigationText = diagnostic.SpanId is null
                ? "연결된 Span이 없습니다. 원본 위치를 확인하세요."
                : "해당 Span을 현재 문서에서 찾을 수 없습니다. 원본 위치를 확인하세요.";
        }

        public void CancelSourcePreview()
        {
            _sourceGeneration++;
            _sourceCancellation?.Cancel();
            SourcePreviewText = "진단을 선택하고 원본 줄 보기를 누르세요.";
        }

        private async Task ShowSourceAsync(bool related)
        {
            DiagnosticItem? diagnostic = SelectedDiagnostic;
            TraceDocument? document = CurrentDocument;
            long? line = related ? diagnostic?.RelatedLineNumber : diagnostic?.LineNumber;
            if (document is null || line is not > 0 || diagnostic is null) return;

            CancelSourcePreview();
            long generation = _sourceGeneration;
            using var cancellation = new CancellationTokenSource();
            _sourceCancellation = cancellation;
            SourcePreviewText = $"{line:N0}행 주변을 읽는 중…";
            try
            {
                string text = await _sourcePreviewService.ReadAsync(document, line.Value, cancellation.Token);
                if (generation == _sourceGeneration)
                    SourcePreviewText = $"{document.FilePath}\n{text}";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (generation == _sourceGeneration) OnSourcePreviewError(exception);
            }
            finally
            {
                if (ReferenceEquals(_sourceCancellation, cancellation))
                    _sourceCancellation = null;
            }
        }

        private void OnSourcePreviewError(Exception exception)
        {
            SourcePreviewText = $"원본 조회 실패 · {exception.Message}";
        }
    }
}

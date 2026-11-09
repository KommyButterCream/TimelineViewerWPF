using System.Windows;
using System.IO;
using Timeline.Viewer.Services;
using Timeline.Viewer.ViewModels;
using System;
using System.ComponentModel;
using System.Windows.Threading;
using Timeline.Viewer.Views;

namespace Timeline.Viewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _loadingTimer;
        private readonly MainWindowViewModel _viewModel;

        private LoadingWindow? _loadingWindow;
        private bool _closeRequested;

        public MainWindow()
        {
            InitializeComponent();

            var themeService = new ThemeService();

            _loadingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };

            _loadingTimer.Tick += OnLoadingTimerTick;

            _viewModel = new MainWindowViewModel(
                themeService,
                new FileDialogService(),
                new TraceLoadService());

            _viewModel.SpanRevealRequested += TimelineView.RevealSpan;

            _viewModel.DocumentOpened += OnDocumentOpened;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            DataContext = _viewModel;
        }

        private void OnFitToView(object sender, RoutedEventArgs e)
        {
            TimelineView.FitToView();
        }

        private void OnZoomIn(object sender, RoutedEventArgs e) => TimelineView.ZoomIn();

        private void OnZoomOut(object sender, RoutedEventArgs e) => TimelineView.ZoomOut();

        private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

        private void OnMaximizeRestore(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }

        private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

        private void OnShowDiagnostics(object sender, RoutedEventArgs e)
        {
            _viewModel.IsDiagnosticsExpanded = true;
            _viewModel.DismissNotificationCommand.Execute(null);
        }

        private void OnDocumentOpened()
        {
            TimelineView.VerticalAxis.ScrollTo(0);
            TimelineView.FitToView();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_viewModel.IsLoading)
            {
                // 실제 작업이 끝나면 IsLoading 변경 처리에서 다시 닫습니다.
                _closeRequested = true;
                _loadingTimer.Stop();
                e.Cancel = true;

                if (_viewModel.CancelLoadCommand.CanExecute(null))
                {
                    _viewModel.CancelLoadCommand.Execute(null);
                }
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.CancelSourcePreview();
            _loadingTimer.Stop();
            _loadingTimer.Tick -= OnLoadingTimerTick;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.SpanRevealRequested -= TimelineView.RevealSpan;
            _viewModel.DocumentOpened -= OnDocumentOpened;
            _loadingWindow?.CloseAfterLoading();
            _loadingWindow = null;
            base.OnClosed(e);
        }

        private static string? GetDroppedFile(IDataObject data)
        {
            if (data.GetData(DataFormats.FileDrop) is string[] files
                && files.Length == 1 && File.Exists(files[0]))
                return files[0];

            return null;
        }

        private void OnFileDragOver(object sender, DragEventArgs e)
        {
            bool canOpen = DataContext is MainWindowViewModel vm && !vm.IsLoading
                && (e.AllowedEffects & DragDropEffects.Copy) != 0
                && GetDroppedFile(e.Data) is not null;
            e.Effects = canOpen ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void OnFileDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.Effects = DragDropEffects.None;
            if (DataContext is MainWindowViewModel vm && !vm.IsLoading
                && (e.AllowedEffects & DragDropEffects.Copy) != 0
                && GetDroppedFile(e.Data) is string filePath)
            {
                e.Effects = DragDropEffects.Copy;
                await vm.OpenFilePathAsync(filePath);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MainWindowViewModel.IsLoading))
            {
                return;
            }

            _loadingTimer.Stop();

            if (_viewModel.IsLoading)
            {
                _loadingTimer.Start();
                return;
            }

            bool restoreFocus = _loadingWindow?.IsActive == true;
            BusyShade.Visibility = Visibility.Collapsed;

            _loadingWindow?.CloseAfterLoading();
            _loadingWindow = null;

            if (_closeRequested)
            {
                Close();
                return;
            }

            if (restoreFocus)
            {
                Activate();
                TimelineView.Focus();
            }
        }

        private void OnLoadingTimerTick(object? sender, EventArgs e)
        {
            _loadingTimer.Stop();

            if (!_viewModel.IsLoading
                || _loadingWindow is not null
                || _closeRequested)
            {
                return;
            }

            _loadingWindow = new LoadingWindow
            {
                Owner = this,
                DataContext = _viewModel
            };

            BusyShade.Visibility = Visibility.Visible;
            _loadingWindow.Show();
        }
    }
}

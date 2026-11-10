using System.Windows;
using System.IO;
using Timeline.Viewer.Services;
using Timeline.Viewer.ViewModels;
using System;
using System.ComponentModel;
using System.Windows.Threading;
using Timeline.Viewer.Views;
using Timeline.Viewer.Models;

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
        private bool _isClosed;
        private UserSettings _normalSettings = new();
        private bool _lastMaximized;

        private readonly UserSettings _initialSettings;

        private readonly IWindowMaterialService _materialService = new WindowMaterialService();
        public MainWindow()
            : this(new UserSettings())
        {

        }

        public MainWindow(UserSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            _initialSettings = settings;
            _normalSettings = settings;
            _lastMaximized = settings.IsMaximized;

            InitializeComponent();
            PopupMaterial.SetUseMaterial(this, settings.UseWindowMaterial);

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

            _viewModel.ApplyTheme(settings.Theme);
            Width = 1280;
            Height = 760;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Loaded += (_, _) =>
            {
                if (settings.IsMaximized) WindowState = WindowState.Maximized;
                else RememberNormalBounds();
            };
            LocationChanged += (_, _) => RememberNormalBounds();
            SizeChanged += (_, _) => RememberNormalBounds();
            StateChanged += (_, _) =>
            {
                if (WindowState != WindowState.Minimized)
                    _lastMaximized = WindowState == WindowState.Maximized;
            };
        }

        // HWND가 만들어진 뒤에만 Windows 재질을 적용합니다.
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowPlacementService.Restore(this, _initialSettings);
            _normalSettings = WindowPlacementService.Capture(this, _initialSettings);
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            ApplyWindowMaterial();
        }

        private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
        {
            bool displayChanged = e.PropertyName is nameof(SystemParameters.WorkArea)
                or nameof(SystemParameters.VirtualScreenWidth) or nameof(SystemParameters.VirtualScreenHeight)
                or nameof(SystemParameters.VirtualScreenLeft) or nameof(SystemParameters.VirtualScreenTop);
            if (!displayChanged && e.PropertyName != nameof(SystemParameters.HighContrast)) return;
            Dispatcher.InvokeAsync(() =>
            {
                if (_isClosed) return;
                if (displayChanged)
                {
                    if (WindowState == WindowState.Normal)
                        WindowPlacementService.Restore(this, _normalSettings);
                    return;
                }
                _viewModel.ApplyTheme(_viewModel.CurrentTheme);
                TimelineView.InvalidateVisual();
                ApplyWindowMaterial();
                _loadingWindow?.ApplyWindowMaterial();
            });
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
            _isClosed = true;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
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
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentTheme))
            {
                TimelineView.InvalidateVisual();
                ApplyWindowMaterial();
                _loadingWindow?.ApplyWindowMaterial();
                return;
            }

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
                DataContext = _viewModel,
                UseWindowMaterial = _initialSettings.UseWindowMaterial
            };

            BusyShade.Visibility = Visibility.Visible;
            _loadingWindow.Show();
        }

        private void RememberNormalBounds()
        {
            if (!IsLoaded || WindowState != WindowState.Normal) return;
            _normalSettings = WindowPlacementService.Capture(this, _normalSettings);
        }

        public UserSettings CaptureSettings()
        {
            RememberNormalBounds();
            return _normalSettings with
            {
                Theme = _viewModel.CurrentTheme,
                IsMaximized = _lastMaximized
            };
        }
        private void ApplyWindowMaterial()
        {
            WindowMaterialPresenter.Apply(
                this,
                _materialService,
                _viewModel.CurrentTheme,
                _initialSettings.UseWindowMaterial);
        }
    }
}

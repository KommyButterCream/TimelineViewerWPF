using System.Windows;
using System.IO;
using Timeline.Viewer.Services;
using Timeline.Viewer.ViewModels;

namespace Timeline.Viewer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var themeService = new ThemeService();

            var viewModel = new MainWindowViewModel(
                themeService, 
                new FileDialogService(),
                new TraceLoadService());

            viewModel.SpanRevealRequested += TimelineView.RevealSpan;

            viewModel.DocumentOpened += () =>
            {
                TimelineView.VerticalAxis.ScrollTo(0);
                TimelineView.FitToView();
            };

            DataContext = viewModel;
        }

        private void OnFitToView(object sender, RoutedEventArgs e)
        {
            TimelineView.FitToView();
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
    }
}

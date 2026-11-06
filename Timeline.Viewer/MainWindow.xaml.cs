using System.Windows;
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

            DataContext = new MainWindowViewModel(themeService);
        }

        private void OnFitToView(object sender, RoutedEventArgs e)
        {
            TimelineView.FitToView();
        }
    }
}

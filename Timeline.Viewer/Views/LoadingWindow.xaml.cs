using System.ComponentModel;
using System.Windows;
using Timeline.Viewer.ViewModels;

namespace Timeline.Viewer.Views
{
    /// <summary>
    /// LoadingWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class LoadingWindow : Window
    {
        private bool _allowClose;

        public LoadingWindow()
        {
            InitializeComponent();
        }

        public void CloseAfterLoading()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose
                 && DataContext is MainWindowViewModel viewModel
                 && viewModel.IsLoading)
            {
                e.Cancel = true;

                if (viewModel.CancelLoadCommand.CanExecute(null))
                {
                    viewModel.CancelLoadCommand.Execute(null);
                }
            }

            base.OnClosing(e);
        }
    }
}


using System.ComponentModel;
using System.Windows;
using System;
using Timeline.Viewer.Services;
using Timeline.Viewer.ViewModels;

namespace Timeline.Viewer.Views
{
    /// <summary>
    /// LoadingWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class LoadingWindow : Window
    {
        private bool _allowClose;
        private readonly IWindowMaterialService _materialService = new WindowMaterialService();

        public bool UseWindowMaterial { get; set; } = true;

        public LoadingWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyWindowMaterial();
        }

        public void ApplyWindowMaterial()
        {
            if (DataContext is not MainWindowViewModel viewModel) return;
            WindowMaterialPresenter.Apply(this, _materialService,
                viewModel.CurrentTheme, UseWindowMaterial);
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


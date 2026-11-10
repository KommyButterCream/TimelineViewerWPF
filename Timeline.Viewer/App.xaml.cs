using System.Diagnostics;
using System.Windows;
using Timeline.Viewer.Models;
using Timeline.Viewer.Services;

namespace Timeline.Viewer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IUserSettingsService _settingsService = new UserSettingsService();

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                UserSettings settings = await _settingsService.LoadAsync();

                var window = new MainWindow(settings);

                UserSettings settingsToSave = settings;

                window.Closing += (_, args) =>
                {
                    if (!args.Cancel)
                    {
                        settingsToSave = window.CaptureSettings();
                    }
                };

                window.Closed += async (_, _) =>
                {
                    try
                    {
                        await _settingsService.SaveAsync(settingsToSave);
                    }
                    catch (Exception exception)
                    {
                        Debug.WriteLine(
                            $"설정 저장 실패: {exception}");
                    }
                    finally
                    {
                        Shutdown();
                    }
                };

                MainWindow = window;
                window.Show();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"앱을 시작하지 못했습니다.\n{exception.Message}",
                    "Timeline.Viewer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(-1);
            }
        }
    }
}

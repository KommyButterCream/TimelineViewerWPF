using System;
using System.Windows;
using Timeline.Viewer.Models;

namespace Timeline.Viewer.Services
{
    public sealed class ThemeService : IThemeService
    {
        public void ApplyTheme(AppTheme theme)
        {
            Application application = Application.Current
                ?? throw new InvalidOperationException(
                    "WPF Application이 초기화되지 않았습니다.");

            application.Dispatcher.VerifyAccess();

            string fileName = theme switch
            {
                AppTheme.Black => "Black.xaml",
                AppTheme.White => "White.xaml",

                _ => throw new ArgumentOutOfRangeException(
                    nameof(theme),
                    theme,
                    "지원하지 않는 테마입니다.")
            };

            var newDictionary = new ResourceDictionary
            {
                Source = new Uri(
                    $"/Timeline.Viewer;component/Themes/{fileName}",
                    UriKind.Relative)
            };

            var dictionaries = application.Resources.MergedDictionaries;

            for (int i = 0; i < dictionaries.Count; i++)
            {
                string? source = dictionaries[i].Source?.OriginalString;

                if (source is null)
                {
                    continue;
                }

                bool isThemeDictionary = source.EndsWith(
                        "Themes/Black.xaml",
                        StringComparison.OrdinalIgnoreCase) ||
                    source.EndsWith(
                        "Themes/White.xaml",
                        StringComparison.OrdinalIgnoreCase);

                if (isThemeDictionary)
                {
                    dictionaries[i] = newDictionary;
                    return;
                }
            }

            dictionaries.Add(newDictionary);
        }
    }
}

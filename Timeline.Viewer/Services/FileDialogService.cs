using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Timeline.Viewer.Services
{
    public sealed class FileDialogService : IFileDialogService
    {
        public string? SelectTraceFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Timeline 로그 파일 열기",
                Filter = "JSON Lines 로그 (*.jsonl)|*.jsonl|모든 파일 (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true,
                CheckPathExists = true
            };

            Window? owner = Application.Current?.MainWindow;

            bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);

            return result == true ? dialog.FileName : null;
        }
    }
}

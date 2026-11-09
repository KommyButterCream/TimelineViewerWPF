using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Timeline.Viewer.Commands
{
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Action<Exception> _onError;
        private readonly Func<bool>? _canExecute;

        private bool _isExecuting;

        public AsyncRelayCommand(
            Func<Task> execute,
            Action<Exception> onError,
            Func<bool>? canExecute = null)
        {
            ArgumentNullException.ThrowIfNull(execute);
            ArgumentNullException.ThrowIfNull(onError);

            _execute = execute;
            _onError = onError;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object? parameter)
        {
            try
            {
                await ExecuteAsync();
            }
            catch (Exception exception)
            {
                _onError(exception);
            }
        }

        public async Task ExecuteAsync()
        {
            if (!CanExecute(null))
            {
                return;
            }

            try
            {
                _isExecuting = true;
                NotifyCanExecuteChanged();

                await _execute();
            }
            finally
            {
                _isExecuting = false;
                NotifyCanExecuteChanged();
            }
        }

        public void NotifyCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

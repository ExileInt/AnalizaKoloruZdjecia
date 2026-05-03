using System.Windows.Input;

public class CommandHandler : ICommand
{
    private readonly Action _action;
    private readonly Func<bool>? _canExecute;

    public CommandHandler(Action action, Func<bool>? canExecute = null)
    {
        _action = action;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add
        {
            CommandManager.RequerySuggested += value;
        }
        remove
        {
            CommandManager.RequerySuggested -= value;
        }
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute == null || _canExecute();
    }
    public void Execute(object? parameter)
    {
        _action();
    }

}

public class CommandHandler<T> : ICommand
{
    private readonly Action<T> _action;
    private readonly Func<T, bool>? _canExecute;

    public CommandHandler(Action<T> action, Func<T, bool>? canExecute = null)
    {
        _action = action;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter)
    {
        if (parameter is T typedParameter)
        {
            return _canExecute == null || _canExecute(typedParameter);
        }
        return _canExecute == null;
    }

    public void Execute(object? parameter)
    {
        if (parameter is T typedParameter)
        {
            _action(typedParameter);
        }
        else if (parameter == null && !typeof(T).IsValueType)
        {
            _action(default!);
        }
    }
}
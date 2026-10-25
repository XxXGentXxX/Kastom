using System.Windows;
using System.Windows.Threading;
using Kastom.Sdk.Services;

namespace Kastom.Launcher.Services;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;

    public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action).Task;
}

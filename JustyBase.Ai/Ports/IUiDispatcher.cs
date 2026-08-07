namespace JustyBase.Ai.Ports;

/// <summary>
/// UI-thread marshaler used by the chat pipeline to read host editor state.
/// Hosts implement this over their UI framework dispatcher (Avalonia Dispatcher,
/// WindowsForms ISynchronizeInvoke, ...).
/// </summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    Task<T> InvokeAsync<T>(Func<T> func);

    Task InvokeAsync(Action action);
}

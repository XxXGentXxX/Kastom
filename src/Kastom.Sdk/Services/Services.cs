namespace Kastom.Sdk.Services;

public interface IModuleLogger
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}

public interface INotificationService
{
    /// <summary>Коротка підказка на екрані (OSD), що зникає сама: «Мікрофон вимкнено».</summary>
    void ShowOsd(string text);
}

public interface IUiDispatcher
{
    /// <summary>Виконати дію в потоці інтерфейсу лаунчера.</summary>
    Task InvokeAsync(Action action);
}

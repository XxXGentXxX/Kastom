using Kastom.Sdk.Actions;
using Kastom.Sdk.Services;
using Kastom.Sdk.Settings;
using Kastom.Sdk.Widgets;

namespace Kastom.Sdk;

/// <summary>Усе, що лаунчер дає модулю. Передається в <see cref="IKastomModule.Configure"/>.</summary>
public interface IModuleContext
{
    /// <summary>Ідентифікатор модуля з module.json, наприклад "audio".</summary>
    string ModuleId { get; }

    /// <summary>Папка, куди модуль може писати свої дані (%AppData%\Kastom\modules\&lt;id&gt;). Уже існує.</summary>
    string DataDirectory { get; }

    IModuleLogger Log { get; }

    /// <summary>Налаштування модуля: опис для інтерфейсу лаунчера й збережені значення.</summary>
    ISettingsRegistry Settings { get; }

    /// <summary>Дії, які користувач може прив'язати до клавіш, кругового меню чи кнопки віджета.</summary>
    IActionRegistry Actions { get; }

    IWidgetRegistry Widgets { get; }

    INotificationService Notifications { get; }

    /// <summary>Виконання коду в потоці інтерфейсу лаунчера.</summary>
    IUiDispatcher Ui { get; }
}

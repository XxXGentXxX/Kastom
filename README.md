# Kastom

Лаунчер-ядро для кастомізації Windows 11 і модулі до нього. Повний опис функцій — у сховищі Obsidian:
`Мій диск\Програмування\Кастомізація Windows\01 — Опис програми.md`. Цей файл описує код.

## Будова

| Проєкт | Що всередині |
|---|---|
| `src/Kastom.Sdk` | Контракт для модулів: `IKastomModule`, `IModuleContext`, описи налаштувань, дії, віджети, поєднання клавіш. Модулі посилаються лише на нього. |
| `src/Kastom.Core` | Логіка без інтерфейсу: каталог і його підпис, встановлення модулів, завантаження в окремий `AssemblyLoadContext`, стан лаунчера, налаштування модулів, оновлення, гарячі клавіші, лог. |
| `src/Kastom.Launcher` | WPF-застосунок `Kastom.exe`: значок у треї, головне вікно, сторінки, швидка панель, віджети, підказки на екрані, Velopack. |
| `modules/Kastom.Modules.Demo` | Демо-модуль для перевірки контракту: налаштування, дія з клавішею, віджет. |
| `tools/Kastom.CatalogTool` | `kastom-catalog`: ключі, пакування модулів, збирання й підпис каталогу. |
| `tests/Kastom.Core.Tests` | xUnit-тести ядра. |

Незмінна основа (міняється лише свідомим рішенням): усе в `Kastom.Sdk`, `Kastom.Core/Abstractions`,
`Kastom.Core/Models`, `KastomPaths.cs`, `ModuleVersion.cs`, `KastomJson.cs`, `Directory.Build.props`.

## Як модуль потрапляє до користувача

1. Модуль збирається; поруч з його dll лежить `module.json` (`ModuleManifest`).
2. `kastom-catalog pack` пакує папку збірки в zip.
3. `kastom-catalog catalog` складає `catalog.json`: id, версія, адреса zip, SHA-256, розмір.
4. `kastom-catalog sign` підписує точні байти `catalog.json` ключем ECDSA P-256 → `catalog.json.sig`.
5. Лаунчер раз на годину читає каталог, перевіряє підпис вшитим відкритим ключем (`KastomTrust`),
   а перед встановленням — розмір і SHA-256 пакета; розпаковує з захистом від шляхів на кшталт `..\`.
6. Модуль лягає в `%LocalAppData%\Kastom\modules\<id>\<version>\` і завантажується у свій `AssemblyLoadContext`.
   `Kastom.Sdk` модуль бере з лаунчера, а не свій.

Секретний ключ підпису лежить поза репозиторієм і поза Google Диском: `%USERPROFILE%\.kastom\keys\catalog-signing.pem`.

## Файли користувача

| Що | Де |
|---|---|
| Модулі | `%LocalAppData%\Kastom\modules\<id>\<version>\` |
| Кеш каталогу, завантаження, логи | `%LocalAppData%\Kastom\catalog-cache`, `downloads`, `logs` |
| Стан лаунчера | `%AppData%\Kastom\settings\launcher.json` |
| Налаштування модулів | `%AppData%\Kastom\settings\modules\<id>.json` |
| Дані модулів | `%AppData%\Kastom\modules\<id>\` |

Velopack при оновленні лаунчера замінює лише `%LocalAppData%\Kastom\current`.

## Команди

```
dotnet build Kastom.slnx
dotnet test Kastom.slnx
dotnet run --project src/Kastom.Launcher -- --catalog-dir <папка з catalog.json>
```

Аргументи лаунчера: `--catalog-dir <папка>` — локальний каталог розробника (дозволяє `file://`);
`--minimized` — старт без головного вікна (автозапуск); `--render-pages <папка>` — зберегти знімки сторінок у PNG і вийти.

## Перевірка без вікон на екрані

Каталог розробника лежить в `artifacts\dev-catalog` (git його ігнорує). Пересібрати після змін демо-модуля:

```
dotnet build modules\Kastom.Modules.Demo -c Release
kastom-catalog pack --dir modules\Kastom.Modules.Demo\bin\Release\net10.0-windows --out artifacts\dev-catalog\demo-1.0.0.zip
kastom-catalog catalog --packages artifacts\dev-catalog --base-url file:///C:/AI/Kastom/artifacts/dev-catalog --out artifacts\dev-catalog\catalog.json
kastom-catalog sign --key %USERPROFILE%\.kastom\keys\catalog-signing.pem --catalog artifacts\dev-catalog\catalog.json
```

`Kastom.exe --render-pages <папка> --catalog-dir artifacts\dev-catalog` у тимчасовій теці ставить модулі з каталогу,
малює всі сторінки поза видимим робочим столом у PNG (900×600 і 1600×1000) і пише стан модулів у `render-report.txt`.
Справжні дані користувача, автозапуск і гарячі клавіші цей режим не чіпає. Каталог розробника має окремий кеш
(`catalog-cache-dev`), щоб не заважати захисту справжнього каталогу від підміни старішим.

## Що ще не готово (стан на 2026-10-25)

- **Репозиторій приватний:** https://github.com/XxXGentXxX/Kastom. Поки він приватний, лаунчер друзів не бачить ні
  каталогу (`catalog/catalog.json` через raw.githubusercontent.com), ні релізів для самооновлення. Перед роздачею —
  зробити репозиторій публічним (або винести каталог і релізи в окремий публічний репозиторій) і викласти перший каталог.
- **Інсталятор не збирався.** `scripts\pack-launcher.ps1` готовий, але потребує `vpk` (`dotnet tool install -g vpk`).
- **Закріплення на панелі задач** перевіряється лише на встановленій збірці: кнопка з'являється, тільки коли
  AppUserModelID ярлика Velopack збігається з ідентифікатором процесу.
- **Час у каталозі.** Лаунчер відкидає каталог, старіший за вже прийнятий; якщо підписати каталог на ПК з годинником
  у майбутньому, наступні правильні каталоги буде відкинуто. Підписувати лише з правильним системним часом.
- **Контракт v1** остаточно включає виклик `StartAsync`/`StopAsync` у фоновому потоці; модулі, яким потрібен
  інтерфейс, звертаються до нього через `context.Ui`.
- Кругового меню в лаунчері немає й не буде: його роблять модулі (перше — у модулі «Звук»). Дизайн інтерфейсу — наступний етап.

## Хто що пише (етап 0)

| Елемент | Файли |
|---|---|
| E1 Доставка | `src/Kastom.Core/Catalog/**`, `src/Kastom.Core/Installation/**`, `tools/Kastom.CatalogTool/**`, `tests/Kastom.Core.Tests/Delivery/**` |
| E2 Виконання | `src/Kastom.Core/Runtime/**`, `Updates/**`, `Logging/**`, `modules/Kastom.Modules.Demo/**`, `tests/Kastom.Core.Tests/Runtime/**`, `tests/Kastom.Core.Tests/Kastom.Core.Tests.csproj` |
| E3 Клавіші | `src/Kastom.Core/Hotkeys/**`, `src/Kastom.Launcher/Hotkeys/**`, `tests/Kastom.Core.Tests/Hotkeys/**` |
| E4a Оболонка | `src/Kastom.Launcher/**`, крім `Hotkeys/**` і файлів E4b; `Kastom.Launcher.csproj`, `App.xaml`, `MainWindow`, `Pages/ModulesPage*`, `Pages/LauncherSettingsPage*`, `Services/**`, `Resources/Strings.*` |
| E4b Налаштування й віджети | `src/Kastom.Launcher/Settings/**`, `Widgets/**`, `Notifications/**`, `Pages/ModuleSettingsAggregatePage*`, `Pages/ModulePage*`, `Resources/UiStrings.*`, `UiStubs.cs` |

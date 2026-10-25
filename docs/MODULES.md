# Як написати модуль

Посібник для автора модуля Kastom. Усе, що тут сказано про контракт, взято з `src/Kastom.Sdk`; якщо щось розходиться з кодом, правдою є код.

## Що таке модуль

Модуль — окрема збірка .NET (WPF, `net10.0-windows`), яку лаунчер завантажує в процес у власний `AssemblyLoadContext`. Модуль реєструє налаштування, дії та віджети, а лаунчер сам малює для них інтерфейс, зберігає значення й призначає клавіші.

Де що живе:

| Що | Де |
|---|---|
| Код модуля | `modules/Kastom.Modules.<Ім'я>/` у репозиторії |
| Встановлений модуль | `%LocalAppData%\Kastom\modules\<id>\<version>\` |
| Налаштування модуля | `%AppData%\Kastom\settings\modules\<id>.json` |
| Дані модуля (`context.DataDirectory`) | `%AppData%\Kastom\modules\<id>\` |
| Лог | `%LocalAppData%\Kastom\logs\kastom-ГГГГММДД.log`, у рядку категорія `[<id>]` |

Модуль посилається лише на `Kastom.Sdk`. Усе інше (каталог, встановлення, підпис) робить ядро.

## Створення з шаблону

Шаблон лежить у `templates/Kastom.Modules.Template`. Скрипт копіює його, перейменовує файли, підставляє простір імен, клас, id, назву, `entryType` та `entryAssembly` і додає проєкт у `Kastom.slnx`:

```powershell
.\scripts\new-module.ps1 -Id show-greeting -Name "Привітання" -ClassName ShowGreeting
```

| Параметр | Значення |
|---|---|
| `-Id` | id модуля: латиниця в нижньому регістрі, цифри, дефіси між ними (`^[a-z0-9]+(-[a-z0-9]+)*$`), до 40 символів, не `kastom` |
| `-Name` | назва для людей, можна українською |
| `-ClassName` | PascalCase; з нього виходять тека `Kastom.Modules.<ClassName>`, простір імен і клас `<ClassName>Module` |
| `-OutputRoot` | куди покласти теку модуля; типово `modules` у репозиторії |
| `-NoSolution` | не додавати проєкт у `Kastom.slnx` |

Якщо тека вже існує, скрипт відмовляє. Після створення відкрийте `module.json`, напишіть `description` і замініть налаштування, дію та віджет у `<ClassName>Module.cs` на свої. Збірка: `dotnet build modules\Kastom.Modules.<ClassName>`.

## module.json

Файл лежить у корені пакета (у проєкті він копіюється поруч з dll). Імена полів у camelCase, значення `runMode` — рядок.

```json
{
  "id": "demo",
  "name": "Демо-модуль",
  "description": "Перевірка контракту модулів: налаштування, дія з клавішею, віджет.",
  "version": "1.0.0",
  "contractVersion": 1,
  "entryAssembly": "Kastom.Modules.Demo.dll",
  "entryType": "Kastom.Modules.Demo.DemoModule",
  "minLauncherVersion": "0.1.0",
  "author": "Kastom"
}
```

| Поле | Обов'язкове | Що означає |
|---|---|---|
| `id` | так | Латиниця в нижньому регістрі, цифри, дефіси: `audio`, `desktop-widgets`. До 40 символів, не `kastom` (так названо сам лаунчер). Має збігатися з назвою теки встановлення. |
| `name` | так | Назва для людей, українською. |
| `description` | ні (типово `""`) | Короткий опис, показується в списку модулів. |
| `version` | так | SemVer: `1.2.3` або `1.2.3-beta.1`. Передрелізна версія молодша за таку саму без суфікса. Частину після `+` ігнорують. |
| `contractVersion` | так | Версія контракту, під яку зібрано модуль: `KastomSdk.ContractVersion` (зараз `1`). Інші версії лаунчер не завантажує. |
| `entryAssembly` | так | Ім'я головної збірки, `Kastom.Modules.Audio.dll`. Має лежати в теці модуля. |
| `entryType` | так | Повне ім'я класу, що реалізує `IKastomModule`. |
| `minLauncherVersion` | ні (типово `0.1.0`) | Найстаріший лаунчер, з яким модуль працює. |
| `runMode` | ні (типово `inProcess`) | `inProcess` — у процесі лаунчера. `outOfProcess` у контракті є, але поки не підтримується: такий модуль не запуститься. |
| `author` | ні | Автор. |
| `icon` | ні | Значок модуля — PNG у пакеті, шлях відносний і лише через `/` (`icon.png`, `assets/icon.png`; без `..`, `\`, диска й початкового `/`). До 32 768 байтів, бажано квадрат 64 чи 128 px. Лаунчер 0.1.3+ показує його у вкладці «Модулі» (32 px), у заголовку сторінки модуля (24) і в навігації (20); без значка — перша літера назви на кольорі акценту. Файл треба скопіювати у вихідну теку збірки, як `module.json`. |

## IKastomModule

```csharp
public interface IKastomModule
{
    void Configure(IModuleContext context);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

- `Configure` — один раз за запуск лаунчера, у потоці інтерфейсу, ще до `StartAsync`. Тут реєструють налаштування, дії й віджети. Систему змінювати не можна.
- `StartAsync` — модуль увімкнено. Викликається у фоновому потоці з обмеженням часу (зараз 10 с).
- `StopAsync` — модуль вимикають, лаунчер закривається або Windows завершує сеанс. Треба зупинитися й повернути все, що модуль змінив у системі. Фоновий потік і ліміт часу: близько 10 с при вимкненні модуля, 5 с при виході лаунчера, ще менше при завершенні сеансу Windows. Тримайте зупинку швидкою. Після `StopAsync` лаунчер може знову викликати `StartAsync` без повторного `Configure`.

## IModuleContext

Передається в `Configure`. Зберігайте його в полі, якщо він потрібен у `StartAsync`.

| Член | Що дає |
|---|---|
| `string ModuleId` | id з `module.json` |
| `string DataDirectory` | тека для даних модуля, `%AppData%\Kastom\modules\<id>`; уже існує |
| `IModuleLogger Log` | `Info(string)`, `Warn(string)`, `Error(string, Exception? = null)` |
| `ISettingsRegistry Settings` | опис налаштувань і їхні значення |
| `IActionRegistry Actions` | дії, які користувач прив'язує до клавіш, кругового меню чи кнопки віджета |
| `IWidgetRegistry Widgets` | віджети |
| `INotificationService Notifications` | `ShowOsd(string)` — коротка підказка на екрані, що зникає сама |
| `IUiDispatcher Ui` | `Task InvokeAsync(Action)` — виконати код у потоці інтерфейсу лаунчера |

## Життєвий цикл і потоки

- `Configure` і `Execute` дії викликаються в потоці інтерфейсу; `CreateContent` віджета теж.
- `StartAsync` і `StopAsync` викликаються у **фоновому** потоці. Створювати чи чіпати WPF-елементи там не можна. Для інтерфейсу використовуйте `context.Ui`:

```csharp
public Task StartAsync(CancellationToken cancellationToken)
{
    return _context!.Ui.InvokeAsync(() => _context.Notifications.ShowOsd("Модуль запущено"));
}
```

- Якщо `Configure` чи `StartAsync` кинули виняток або не вклалися в ліміт часу, модуль не запускається, а помилка записується в лог.
- Безпечний режим вимикає модуль, що падає при старті.
- `StopAsync` має повертати систему до початкового стану: це єдине місце, де ви гарантовано встигнете прибрати за собою.

## Налаштування

Описуються в `Configure` через `context.Settings.Define(...)`. Лаунчер малює елемент керування на сторінці модуля й у вкладці «Налаштування модулів», зберігає значення у файл і повертає його при наступному запуску. Спільні поля `SettingDefinition`: `Key` (латиниця, цифри, дефіс; унікальний у межах модуля), `Title`, `Description`, `Group` (група на сторінці), `IsQuick` (показати ще й у швидкій панелі та віджеті лаунчера).

| Клас | Додаткові поля | Читання |
|---|---|---|
| `ToggleSetting` | `Default` (bool) | `GetBool(key)` |
| `RangeSetting` | `Min` (0), `Max` (100), `Step` (1), `Default`, `Unit` (наприклад `"%"`) | `GetDouble(key)` |
| `ChoiceSetting` | `Options` (`ChoiceOption(Value, Title)`), `Default` (значення варіанта) | `GetString(key)` |
| `TextSetting` | `Default` | `GetString(key)` |
| `ButtonSetting` | `ActionId` — id дії модуля; значення не зберігає | — |

```csharp
context.Settings.Define(new RangeSetting
{
    Key = "intensity",
    Title = "Інтенсивність",
    Min = 0,
    Max = 100,
    Step = 5,
    Default = 50,
    Unit = "%",
    IsQuick = true,
});
```

Записати значення з коду: `Settings.Set(key, bool | double | string)` (повзунок лаунчер обріже до `Min..Max`; для вибору значення має бути одним з варіантів). Зміну, з інтерфейсу чи з коду, повідомляє подія `Settings.Changed` (`SettingChangedEventArgs.Key` і `.Value`: `bool`, `double` або `string`); вона може прийти не з потоку інтерфейсу.

Для складного інтерфейсу (скажімо, мікшера) є власна сторінка: `Settings.SetCustomPage(() => new MyPage())`. Фабрика викликається в потоці інтерфейсу щоразу, коли сторінку відкривають; описані налаштування показуються під нею.

## Дії й типові клавіші

```csharp
context.Actions.Register(new ModuleAction
{
    Id = "hello",
    Title = "Привітання",
    DefaultHotkey = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 'H'),
    Execute = _ =>
    {
        context.Notifications.ShowOsd("Привіт!");
        return Task.CompletedTask;
    },
});
```

- `Id` — латиниця в нижньому регістрі, цифри, дефіс. Повний id у лаунчері: `<moduleId>/<Id>`, наприклад `audio/mute-mic`.
- `DefaultHotkey` — необов'язкове. Лаунчер не призначить його, якщо поєднання вже зайняте.
- **Адреса модуля (Г2, лаунчер 0.1.3+).** Користувач бачить клавіші так: у кожного розділу (лаунчер і кожен модуль) є «адреса» — набір модифікаторів (Ctrl, Alt, Shift, Win), який він обирає сам, а в кожної дії — лише своя клавіша. Поєднання дії = адреса модуля + клавіша дії; змінив адресу — перереєструвалися всі дії модуля. З `DefaultHotkey` лаунчер бере клавішу (`VirtualKey`), а типову адресу модуля — з модифікаторів його типових поєднань (найчастіших серед них). Тож **усі типові поєднання модуля мають мати однакові модифікатори** (у «Звуку» й лаунчера — Ctrl+Alt); без типових поєднань типова адреса — Ctrl+Alt+Shift.
- AltGr у Windows — це Ctrl+Alt, тож з адресою Ctrl+Alt не беріть клавішу U: в українській розкладці AltGr+U дає «ґ».
- Типову клавішу лаунчер призначає лише тоді, коли поєднання адреса + клавіша вільне; зайняте (інша дія, Windows чи інша програма) — дія лишається без клавіші, а користувач бачить причину біля неї.
- Налаштування модуля, його гарячі клавіші, віджети й дії лаунчер 0.1.3+ показує на вкладці «Налаштування» сторінки модуля (туди веде й кнопка «Налаштування» на картці у вкладці «Модулі»). Власна сторінка (`SetCustomPage`) — на сусідній вкладці «Модуль» і створюється лише тоді, коли ця вкладка відкрита.
- `HotkeyGesture(HotkeyModifiers, int VirtualKey)`: модифікатори `Alt`, `Control`, `Shift`, `Win`; клавіша — віртуальний код Windows (для літер і цифр підходить сам символ, `'H'`). Без модифікатора дозволені лише F1–F24 і медіаклавіші. `HotkeyGesture.TryParse("Ctrl+Alt+M", out var g)` розбирає текстовий запис.
- `Execute` отримує `ActionContext.Trigger`: `Ui`, `Hotkey`, `RadialMenu` або `Widget`. Викликається в потоці інтерфейсу: довгу роботу виносьте в `Task.Run`.
- Дія стає видимою лише після успішного `StartAsync`, а при зупинці модуля зникає.
- `ButtonSetting` з `ActionId` робить на сторінці модуля кнопку, що виконує таку дію.

## Віджети

```csharp
context.Widgets.Register(new WidgetDefinition
{
    Id = "counter",
    Title = "Лічильник",
    DefaultWidth = 220,   // типово 280
    DefaultHeight = 120,  // типово 160
    CreateContent = CreateCounter,
});
```

Лаунчер сам створює вікно, рамку, перетягування й зберігає місце. `CreateContent` повертає `FrameworkElement`; викликається в потоці інтерфейсу щоразу, коли віджет показують. Підписки на події модуля робіть у `Loaded` і знімайте в `Unloaded`, щоб закритий віджет не лишався в пам'яті (приклад у `modules/Kastom.Modules.Demo/DemoModule.cs`, метод `CreateCounter`).

## Логи, підказки й дані

- `context.Log.Info/Warn/Error` пишуть у загальний лог лаунчера з категорією `[<id>]`. Виняток у `Error(message, ex)` потрапляє в лог повністю.
- `context.Notifications.ShowOsd("Мікрофон вимкнено")` — підказка на екрані.
- `context.DataDirectory` — власні файли модуля. Налаштування туди писати не треба: їх веде `context.Settings`.

## Правила збірки

Проєкт модуля повторює шаблон (`templates/Kastom.Modules.Template/*.csproj`):

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <RootNamespace>Kastom.Modules.Demo</RootNamespace>
  <EnableDynamicLoading>true</EnableDynamicLoading>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="..\..\src\Kastom.Sdk\Kastom.Sdk.csproj">
    <Private>false</Private>
    <ExcludeAssets>runtime</ExcludeAssets>
  </ProjectReference>
</ItemGroup>

<ItemGroup>
  <None Update="module.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- `EnableDynamicLoading` готує `deps.json` поруч з dll, щоб лаунчер завантажив модуль у окремий `AssemblyLoadContext` разом із залежностями.
- `Private=false` і `ExcludeAssets=runtime` не дають скопіювати `Kastom.Sdk.dll` у теку модуля. Інакше модуль завантажив би власну копію SDK, типи `IKastomModule`, `ModuleAction` тощо стали б іншими типами, ніж у лаунчера, і модуль не знайшов би свій вхід. `Kastom.Sdk` модуль завжди бере з лаунчера.
- `module.json` має потрапляти у вихідну теку збірки: `kastom-catalog pack` пакує саме її.
- Збірка іде з `Directory.Build.props`: `Nullable` і `ImplicitUsings` увімкнено. Проєкти в `Kastom.slnx` збираються з `-warnaserror`, тож без попереджень.

## Версії й сумісність

- `contractVersion` у `module.json` мусить дорівнювати `KastomSdk.ContractVersion` лаунчера. Контракт збільшується лише при несумісній зміні SDK; модуль під іншу версію лаунчер не завантажує й пропонує оновити.
- **Версія збірки Kastom.Sdk заморожена на 0.1.3.0** (контракт 1) і не йде за версією лаунчера. Модуль, зібраний з цього репозиторію, посилається на SDK 0.1.3.0, а .NET не підставить збірку старішої версії: у лаунчері 0.1.0–0.1.2 (SDK 0.1.0–0.1.2) такий модуль не завантажиться («немає типу …»). Тож **`minLauncherVersion` нових модулів — не менше 0.1.3** (так зламався «Звук» 1.2.0 у лаунчері 0.1.2, виправлено в 1.2.1).
- `minLauncherVersion` — найстаріший лаунчер, що вміє все потрібне модулю. Лаунчер старіший за цю версію модуль не запускає: причина — «Потрібен лаунчер X або новіший».
- `version` піднімайте при кожній зміні вмісту. Той самий номер з іншим вмістом публікувати не можна (див. нижче).

## Перевірка локально

Каталог розробника лежить у `artifacts\dev-catalog` (git його ігнорує). Пересобрати його після змін модуля (приклад для демо; для свого замініть теку й ім'я zip):

```powershell
dotnet build modules\Kastom.Modules.Demo -c Release
kastom-catalog pack --dir modules\Kastom.Modules.Demo\bin\Release\net10.0-windows --out artifacts\dev-catalog\demo-1.0.0.zip
kastom-catalog catalog --packages artifacts\dev-catalog --base-url file:///C:/AI/Kastom/artifacts/dev-catalog --out artifacts\dev-catalog\catalog.json
kastom-catalog sign --key %USERPROFILE%\.kastom\keys\catalog-signing.pem --catalog artifacts\dev-catalog\catalog.json
```

Без встановленого `kastom-catalog` збирайте його з репозиторію: `dotnet build tools\Kastom.CatalogTool`, виконуваний файл — `tools\Kastom.CatalogTool\bin\Debug\net10.0-windows\kastom-catalog.exe`.

**Знімки сторінок без вікон на екрані.** `Kastom.exe --render-pages <тека> --catalog-dir artifacts\dev-catalog` у тимчасовій теці встановлює модулі з каталогу, малює всі сторінки поза видимим робочим столом у PNG і пише стан модулів у `render-report.txt` (там видно, чи модуль запустився й чому ні). Справжні дані, автозапуск і гарячі клавіші цей режим не чіпає.

Знімки робляться в чотирьох розмірах: 900×600, 1600×1000, 1536×824 при масштабі 125 % і 1280×688 при масштабі 150 % — кожен у світлій і темній темі. Імена файлів мають суфікси теми `-light` / `-dark` і масштабу `@125` / `@150` (для двох перших розмірів масштабу в імені немає). Швидка панель і віджет теж знімаються для обох тем.

**Живий запуск у пісочниці.**

```powershell
Kastom.exe --data-dir <тека> --test-screen <left,top,width,height> --catalog-dir artifacts\dev-catalog
```

- `--data-dir <тека>` — усі дані лаунчера лежать у цій теці; автозапуск у реєстрі не чіпається, сам лаунчер не оновлюється; «один екземпляр» окремий від основного лаунчера. Теки пісочниці теж окремі, тож налаштування й модулі основного лаунчера вона не зачіпає.
- `--test-screen left,top,width,height` — усі вікна лаунчера відкриваються в цій області (значення в DIP) і не забирають фокус. Виберіть область на другому моніторі, не менше **900×600 DIP**. Працює **лише разом з `--data-dir`**: без нього лаунчер відмовляється стартувати.
- `--catalog-dir` дозволяє `file://` і має власний кеш (`catalog-cache-dev`), щоб не заважати захисту справжнього каталогу від підміни старішим.

Гарячі клавіші пісочниця реєструє в системі по-справжньому. Поки працює справжній лаунчер, однакові поєднання клавіш конфліктують: система віддасть їх лише тому, хто зареєстрував першим. Для перевірки клавіш закрийте основний лаунчер або змініть поєднання в пісочниці.

## Публікація

```powershell
scripts\publish-module.ps1 -Project modules\Kastom.Modules.<Ім'я>
scripts\publish-module.ps1 -Project modules\Kastom.Modules.<Ім'я> -Publish
```

Спершу запускайте без `-Publish`, дивіться результат, потім із `-Publish`.

**Без `-Publish`** скрипт збирає модуль, пакує, складає новий каталог на основі `catalog\catalog.json` (`--base`, тож решта модулів лишається), підписує й перевіряє його. У мережу нічого не відправляється, а `catalog\catalog.json` **не змінюється**: підписаний каталог лягає в `artifacts\modules\<id>\<версія>\catalog\` (`catalog.json` і `catalog.json.sig`) як попередній перегляд, і скрипт про це каже. Туди ж, у `artifacts\modules\<id>\<версія>\`, кладуться збірка й zip.

**З `-Publish`** скрипт спершу, ще до збирання, перевіряє git: `git fetch origin`; поточна гілка `main`; `HEAD` збігається з `origin/main`; у `catalog\` немає незакомічених змін. Якщо щось не так, він відмовляє з поясненням. Далі по порядку:

1. збирання модуля й пакета;
2. складання каталогу, підпис і перевірка в тимчасовій теці;
3. `gh release create <id>-v<версія>` з zip (наприклад, `audio-v1.0.0`); якщо цей крок впав, каталог у репозиторії не чіпається;
4. перенесення пари `catalog.json` і `catalog.json.sig` у `catalog\` (підпис першим);
5. `git add`, `git commit`, `git push`.

Коміт каталогу має рядок `Co-Authored-By` наприкінці повідомлення. Файл повідомлення пишеться в UTF-8 без BOM.

**Перший каталог.** Каталог, який склав скрипт, базується на `catalog\catalog.json`. Якщо його немає (явно заданий `-Catalog`, якого не існує, теж), скрипт відмовляє: так модулі не втратяться через неправильний шлях. Для найпершого каталогу додайте `-InitCatalog`; якщо каталог уже є, `-InitCatalog` відмовляє з тієї ж причини. Відносний `-Catalog` рахується від поточної теки.

Правила:

- Секретний ключ — лише `%USERPROFILE%\.kastom\keys\catalog-signing.pem`, поза репозиторієм і Google Диском. У CI ключа немає й не буде. Скрипт відмовляє, якщо ключ лежить у репозиторії чи на Google Диску.
- Версія в `module.json` складається лише з `[0-9A-Za-z.-]`: суфікс `+` (build metadata) скрипт відхиляє, бо версія стає назвою теки й тегу.
- Той самий номер версії з іншим вмістом публікувати не можна: SHA-256 у вже підписаному каталозі мусить збігатися з zip, який лежить у релізі. Змінили код (навіть на один символ) — піднімайте `version`. `kastom-catalog catalog --base` ще й сам відмовляє, якщо версія пакета не новіша за версію в каталозі.
- Час створення каталогу (`GeneratedAt`) ставить крок `kastom-catalog catalog` (теперішній час UTC) і вимагає, щоб він був строго пізніший за час бази. `kastom-catalog sign` лише відмовляється підписувати каталог, датований більш ніж на 5 хвилин у майбутньому. Лаунчер відкидає каталог, старіший за вже прийнятий, тож системний годинник має бути правильним: каталог, складений на ПК з годинником у майбутньому, заблокував би наступні правильні.
- `--base` має бути підписаним каталогом: поруч лежить `catalog.json.sig`, і пара перевіряється вшитим відкритим ключем (`--public` потрібен лише для тестів з тимчасовим ключем).
- Теги модулів (`<id>-v<версія>`) не обробляє `release.yml`: релізи модулів створює лише цей скрипт.

## Відкликати модуль

Каталог **ніколи не відкочують**: `git revert` каталогу повертає старий файл зі старішим `GeneratedAt`, і лаунчери, що вже прийняли новіший каталог, його відкинуть як підміну. Щоб прибрати модуль чи якусь версію, складіть новий каталог із копії поточної бази без цього запису й підпишіть його. Новий каталог матиме новий `GeneratedAt`, тож лаунчери його приймуть.

Інструмент вилучати записи не вміє (`catalog --base` лише додає й замінює), тож це робиться руками: скопіюйте `catalog\catalog.json` і `catalog.json.sig` у тимчасову теку, у копії `catalog.json` видаліть запис модуля чи версії з поля `modules`, а `generatedAt` поставте на теперішній час UTC (строго пізніше за старе значення). Підпишіть копію (`kastom-catalog sign`; старий `.sig` при цьому перезаписується), перевірте (`kastom-catalog verify`), перенесіть пару в `catalog\` (підпис першим), закомітьте й запушіть. Реліз GitHub з пакетом можна лишити або видалити окремо: лаунчер дивиться лише в каталог.

## Випуск лаунчера

1. Піднімаєте версію, комітите й ставите тег `vX.Y.Z` (наприклад, `v0.1.0`), пушите тег. Версія в тегу має точно
   збігатися з `--launcher-version` у каталозі: лаунчер шукає оновлення саме в релізі `v<версія з каталогу>`.
2. Тег запускає `.github/workflows/release.yml`. Job `build` (токен лише читає, без збережених облікових даних) робить збірку з `-warnaserror`, тести, `dotnet publish`, `vpk pack` (ті самі параметри, що в `scripts\pack-launcher.ps1`) і передає файли Velopack артефактом. Окремий job `release` (єдиний із правом запису) лише завантажує артефакт і створює **чернетку** релізу GitHub. Версія `vpk` береться з `PackageReference Velopack` у `Kastom.Launcher.csproj`: ці версії мусять збігатися.
3. Каталог підписується лише локально. Завантажте з чернетки файл `Kastom-X.Y.Z-full.nupkg` у теку (для прикладу `artifacts\launcher-packages`). Складайте й підписуйте **копію в тимчасовій теці**, а не `catalog\catalog.json` на місці:

```powershell
$work = Join-Path $env:TEMP "kastom-launcher-catalog"
New-Item -ItemType Directory -Force "$work\empty" | Out-Null
kastom-catalog catalog --base catalog\catalog.json --packages "$work\empty" --base-url https://github.com/XxXGentXxX/Kastom/releases --launcher-package artifacts\launcher-packages\Kastom-X.Y.Z-full.nupkg --launcher-version X.Y.Z --out "$work\catalog.json"
kastom-catalog sign --key $env:USERPROFILE\.kastom\keys\catalog-signing.pem --catalog "$work\catalog.json"
kastom-catalog verify --catalog "$work\catalog.json"
```

   `--packages` — **порожня** тека: нових модулів у цьому каталозі бути не повинно, решту бере `--base` (разом з їхніми адресами). `--base-url` ні на що не впливає, але має бути `https://`: адреса `file:` пропустила б у каталог записи з `file://`. `verify` без `--public` перевіряє вшитим ключем, тобто саме тим, яким лаунчер перевірить каталог. `catalog` відмовляє, якщо версія лаунчера нижча за ту, що вже в базі, або та сама версія має інший SHA-256.
4. Спершу **опублікуйте чернетку релізу** (кнопка на GitHub або `gh release edit vX.Y.Z --draft=false`): Velopack має бачити реліз ще до того, як лаунчери побачать каталог. Потім перенесіть пару в репозиторій (підпис першим), закомітьте й запушіть:

```powershell
Copy-Item "$work\catalog.json.sig" catalog\catalog.json.sig
Copy-Item "$work\catalog.json" catalog\catalog.json
git add catalog\catalog.json catalog\catalog.json.sig
git commit
git push
```

   У повідомленні коміту каталогу, як і в скрипті публікації модуля, наприкінці має бути рядок `Co-Authored-By`.

Стан на 2026-10-25: репозиторій публічний, цим ланцюгом випущено лаунчер 0.1.0, 0.1.1 і 0.1.2 (оновлення встановленої збірки перевірено наживо). Підпису програми (сертифікат для `Kastom.exe` та інсталятора, Л14) ще немає: SmartScreen попереджає при першому запуску.

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
| `tools/Kastom.CatalogTool` | `kastom-catalog`: ключі, пакування модулів, збирання (зокрема поверх попереднього каталогу, `--base`) і підпис каталогу. |
| `tests/Kastom.Core.Tests` | xUnit-тести ядра, каналу «одного екземпляра» й `kastom-catalog`. |
| `templates/Kastom.Modules.Template` | Шаблон модуля; новий модуль — `scripts
ew-module.ps1`. Посібник — `docs/MODULES.md`. |
| `scripts/` | `new-module.ps1` (модуль із шаблону), `publish-module.ps1` (збирання → пакет → каталог → підпис), `pack-launcher.ps1` (інсталятор, потрібен `vpk`). |
| `.github/workflows` | `ci.yml` — збирання з `-warnaserror` і тести на кожен push; `release.yml` — чернетка релізу лаунчера за тегом `vX.Y.Z`. |

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
`--minimized` — старт без головного вікна (автозапуск); `--render-pages <папка>` — зберегти знімки сторінок у PNG і вийти;
`--data-dir <папка>` — пісочниця: усі дані лаунчера в цій теці, автозапуск у реєстрі не чіпається, самооновлення
вимкнене, окремий «один екземпляр»; `--test-screen left,top,width,height` — усі вікна лаунчера в цій області (DIP, не
менше 900×600) і без фокусу; працює лише разом з `--data-dir`.

## Перевірка без вікон на екрані

Каталог розробника лежить в `artifacts\dev-catalog` (git його ігнорує). Пересібрати після змін демо-модуля:

```
dotnet build modules\Kastom.Modules.Demo -c Release
kastom-catalog pack --dir modules\Kastom.Modules.Demo\bin\Release\net10.0-windows --out artifacts\dev-catalog\demo-1.0.0.zip
kastom-catalog catalog --packages artifacts\dev-catalog --base-url file:///C:/AI/Kastom/artifacts/dev-catalog --out artifacts\dev-catalog\catalog.json
kastom-catalog sign --key %USERPROFILE%\.kastom\keys\catalog-signing.pem --catalog artifacts\dev-catalog\catalog.json
```

`Kastom.exe --render-pages <папка> --catalog-dir artifacts\dev-catalog` у тимчасовій теці ставить модулі з каталогу,
малює всі сторінки поза видимим робочим столом у PNG і пише стан модулів у `render-report.txt`. Розміри: 900×600,
1600×1000, 1536×824 при 125 % і 1280×688 при 150 % (уся робоча область монітора 1920×1080 за такого масштабу), кожен
у світлій і темній темі (`-light`, `-dark`); швидка панель і віджет — теж в обох темах.
Справжні дані користувача, автозапуск і гарячі клавіші цей режим не чіпає. Каталог розробника має окремий кеш
(`catalog-cache-dev`), щоб не заважати захисту справжнього каталогу від підміни старішим.

## Жива перевірка на другому моніторі

Вікна, які відкриваються під час перевірок, не мають з'являтися на головному моніторі й забирати фокус. Пісочниця
з тестовою областю на другому моніторі (на ПК Івана DISPLAY2 — ліворуч, робоча область `-1920,-104,1920,1032`):

```
Kastom.exe --data-dir <тимчасова тека> --test-screen -1920,-104,1920,1032 --catalog-dir artifacts\dev-catalog --minimized
```

Головне вікно відкриває другий запуск тієї самої команди без `--minimized` (теж без фокусу). Збережене місце
головного вікна й віджетів поза тестовою областю пісочниця ігнорує. Гарячі клавіші пісочниця реєструє по-справжньому,
тож поки працює справжній лаунчер, однакові поєднання конфліктують. Значок у треї з'являється на головній панелі задач.

Перша жива перевірка (2026-10-25, Release): у спокої процесор 0 с за 60 с; пам'ять з віджетом — 133 МБ робочого
набору, 71 МБ приватних; з відкритим головним вікном після обходу всіх сторінок — 177 / 139 МБ; після його закриття —
168 МБ робочого набору, 130 МБ приватних, у Диспетчері завдань (приватний робочий набір) — 47,5 МБ.

**Перед кліком по екрану (computer-use) перевіряти, чиє вікно під точкою** (`WindowFromPoint`): computer-use ховає
на знімках вікна інших програм, а віджет на шарі робочого столу лежить під ними.

## Випуски (стан на 2026-10-25)

Репозиторій публічний (MIT). Опубліковано: `demo-v1.0.0`, лаунчер `v0.1.0`, `v0.1.1` і `v0.1.2`; каталог —
`catalog/catalog.json` у main (через https://raw.githubusercontent.com/XxXGentXxX/Kastom/main/catalog/catalog.json).
Друзі ставлять `Kastom-win-Setup.exe` з https://github.com/XxXGentXxX/Kastom/releases/latest.

- **Каталог — точні байти.** Підпис покриває байти `catalog.json` (з CRLF, як їх пише `kastom-catalog`), тож
  `.gitattributes` має `catalog/** -text`, а `publish-module.ps1` відмовляє без цього атрибута. Без нього git зберіг
  би LF, і лаунчер відкинув би каталог з GitHub як підроблений.
- **Самооновлення** шукає реліз за тегом `v<версія з підписаного каталогу>` (`releases.win.json` і пакет у цьому
  релізі), а не в стрічці релізів GitHub: Velopack читає лише першу сторінку з 10 релізів, і релізи модулів у тому
  самому репозиторії витіснили б реліз лаунчера. Перевірено наживо: 0.1.0 → 0.1.1.
- **Встановлена збірка перевірена на ПК Івана:** модуль з GitHub, оновлення, закріплення на панелі задач (без
  запиту, коли користувач сам натискає кнопку), видалення (файли, ярлики, `Run\Kastom`, запис у списку програм;
  `%AppData%\Kastom` лишається).
- **Перевстановлення лаунчера.** Видалення Velopack стирає `%LocalAppData%\Kastom` разом із модулями, а стан
  у `%AppData%\Kastom` лишається. З 0.1.2 `ModuleManager` на старті знаходить модулі без файлів і ставить їх
  з каталогу (та сама або новіша сумісна версія, прапорець «увімкнено» зберігається); модуль, якого в каталозі
  немає, прибирається зі стану; без мережі картка пропонує «Перевстановити».
- **Velopack не ставить оновлення сам при старті** (`SetAutoApplyOnStartup(false)`): лише `VelopackLauncherUpdater`,
  після перевірки SHA-256 за каталогом.
- **Старі коміти з поштою.** Після переписування історії GitHub ще віддає старі коміти за SHA (їх видно в стрічці
  подій). Прибрати їх може лише GitHub Support: текст звернення — `artifacts\github-support-request.md` (поза git).
- **Застосунок Claude працює в контейнері MSIX:** програми, які він запускає, пишуть `%LocalAppData%`, `%AppData%`
  і частину HKCU у віртуальну теку пакета. Інсталятор, запущений звідти, ставить Kastom «наполовину» (ярлики справжні,
  файли — у контейнері). Ставити й перевіряти встановлену збірку — з Провідника.
- **Підпису програми немає (Л14):** SmartScreen попереджає при першому запуску інсталятора.
- **Час у каталозі.** Лаунчер відкидає каталог, старіший за вже прийнятий. `kastom-catalog sign` не підписує каталог,
  датований більш ніж на 5 хв наперед, а `catalog --base` вимагає дату, пізнішу за базу; але годинник, що вже спішить,
  інструмент не помітить. Підписувати лише з правильним системним часом. Каталог ніколи не відкочувати (див.
  `docs/MODULES.md`, «Відкликати модуль»).
- **Віджети на шарі робочого столу (Л7, 🟡)** типово вимкнені: власник віджета — Progman з процесу Провідника, Windows
  зв'язує черги вводу обох потоків. Такий віджет лежить під вікнами програм і не бере фокус (поля введення в ньому
  не працюють). Перезапуск Провідника наживо не перевірявся; віджети перевідкриваються за `TaskbarCreated`.
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

Етап 0б (2026-10-25): `SingleInstance` переїхав у `src/Kastom.Core/Runtime`; нові `Services/ScreenPolicy.cs`,
`Services/LauncherThemes.cs`, `Widgets/DesktopLayer.cs`; автори модулів — `docs/MODULES.md`, `templates/**`,
`scripts/new-module.ps1`, `scripts/publish-module.ps1`; збирання — `.github/workflows/**`.

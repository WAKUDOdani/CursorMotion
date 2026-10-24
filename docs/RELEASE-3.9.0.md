# CursorMotion 3.9.0 — Portable в одном EXE

<details>
<summary><strong>English release notes</strong></summary>

### CursorMotion 3.9.0 — One portable EXE

The complete app now fits in one downloadable EXE, with .NET and Electron included. The accepted cursor motion and shape transitions are preserved.

### Download

| Edition | Purpose |
| --- | --- |
| [**Windows x64 Portable**](https://github.com/WAKUDOdani/CursorMotion/releases/download/v3.9.0/CursorMotion-3.9.0-portable-x64.exe) | No installation or extra runtime downloads |

### What's new

- A single portable EXE, with a verified component cache and recovery of damaged files.
- Settings and UI data stay in AppData. The app creates nothing beside the EXE.
- Startup uses the main EXE, including renamed files and paths containing spaces or Unicode.
- Five theme packs, eight cursor forms, independent appearance controls, whole-image motion blur, and smooth shape morphs.
- A new repository with genuine application screenshots, RU/EN documentation, and release checksums.

### Recording and compatibility

The optional NVIDIA mode from3.8.5 is preserved. It trims transparent frame padding without reducing the glyph size or changing motion settings. An exceptionally long blur can be omitted if it does not fit the compact frame. Ordinary mode remains unrestricted. The user confirmed NVIDIA recording, a single Discord pointer, and that the previously observed blink no longer appears on their PC.

<details>
<summary>Verification and technical details</summary>

The package is self-contained for Windows x64; the UI is in Russian. First launch prepares its local AppData runtime; later launches verify/reuse it. No internet is needed to start the app. .NET10 and Electron45 are included with their notices.

The release is checked with offscreen native rendering, a real hidden settings window, backend/tray tests, bootstrap cache/integrity/concurrency tests, and the actual packaged EXE. Package checks cover cold/warm launch, corruption recovery, a renamed Unicode path, no adjacent files, and preservation of the installed profile/autostart. Private tests do not replace live Windows/recording observations; exact package checks and SHA-256 are attached.

Builds remain unsigned. Download only the EXE to run; `SHA256SUMS.txt` and `package-verification.json` are verification records. `Source code` is for developers. Exit the old instance through the tray before replacing it. Settings transfer through profile export/import; startup must be refreshed after moving the EXE. There is no automatic updater.

</details>

---

Developed by **WAKUDO Dani** · [Telegram](https://t.me/WAKUDOdani).

[MIT](https://github.com/WAKUDOdani/CursorMotion/blob/v3.9.0/LICENSE). Initial SDF geometry/exposure integration: Motion Blur Cursor OBS script, Unlicense; [provenance and notices](https://github.com/WAKUDOdani/CursorMotion/blob/v3.9.0/docs/UPSTREAM.md).

</details>

Всё приложение теперь скачивается одним EXE: .NET и Electron уже внутри, а принятые моушн и переходы курсора сохранены.

## Скачать

| Вариант | Назначение |
| --- | --- |
| [**Portable Windows x64**](https://github.com/WAKUDOdani/CursorMotion/releases/download/v3.9.0/CursorMotion-3.9.0-portable-x64.exe) | Без установки и дополнительных сред запуска |

## Что нового

- Один portable EXE, проверенный кэш компонентов и восстановление повреждённых файлов.
- Настройки и служебные данные в AppData. Приложение ничего не создаёт рядом с EXE.
- Автозапуск ссылается на основной EXE, в том числе после переименования в путь с пробелами или кириллицей.
- Пять наборов, восемь форм курсора, независимое оформление, размытие всего изображения и плавное превращение форм.
- Новый репозиторий с настоящими скриншотами, документацией RU/EN и контрольными суммами.

## Запись и совместимость

Сохранён опциональный режим NVIDIA из3.8.5: он убирает прозрачные поля кадров, не уменьшает сам курсор и не меняет значения моушна. Очень длинный шлейф может временно отключаться, если не помещается в компактный кадр. Обычный режим не ограничен. Пользователь подтвердил исправную запись NVIDIA, один курсор в Discord и что прежнее мигание больше не проявляется на его ПК.

<details>
<summary>Проверки и технические подробности</summary>

Самостоятельная сборка Windows x64; интерфейс на русском. Первый запуск подготавливает компоненты в AppData, последующие проверяют и используют кэш. Интернет при запуске не нужен. .NET10 и Electron45 включены вместе с лицензиями.

Проверки охватывают приватный нативный рендер, настоящее скрытое окно настроек, протокол и трей, целостность/параллельные запуски bootstrap и готовый EXE. Для пакета проверяются первый/повторный запуск, восстановление кэша, переименование с кириллицей, отсутствие файлов рядом и сохранность реального профиля/автозапуска. Автотесты не подменяют живую проверку Windows и записи; итоговая проверка пакета и SHA-256 приложены к выпуску.

Сборка без цифровой подписи издателя. Для работы нужен только EXE; `SHA256SUMS.txt` и `package-verification.json` подтверждают файл, `Source code` предназначен разработчикам. Перед заменой полностью выйди из прежней версии через трей. Профиль переносится экспортом/импортом; после перемещения EXE нужно обновить автозапуск. Автоматического обновлятора нет.

</details>

---

Развитие CursorMotion: **WAKUDO Dani** · [Telegram](https://t.me/WAKUDOdani).

[MIT](https://github.com/WAKUDOdani/CursorMotion/blob/v3.9.0/LICENSE). Основа первоначальной SDF-геометрии и экспозиции — OBS-скрипт Motion Blur Cursor, Unlicense; [происхождение и сторонние компоненты](https://github.com/WAKUDOdani/CursorMotion/blob/v3.9.0/docs/UPSTREAM.md).

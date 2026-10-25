<details>
<summary><strong>English release notes</strong></summary>

## CursorMotion 3.10.0 — Motion and personal presets

A rebuilt motion renderer, clearer cursor edges across mixed-DPI displays and personal presets, in the same single portable EXE.

### Download

Download **CursorMotion-3.10.0-portable-x64.exe** below. Fully exit the previous version through the tray before replacing it. Settings and presets remain in AppData; installation and administrator rights are unnecessary.

### Changes

- Cursor size is a hard **0.5–4.5×** range, including individual shapes, linked sizes, profile imports and presets. Oversized old values are clamped.
- Whole-cursor motion integrates a dense physical trajectory with exposure and density controls. The system pointer position remains exact.
- Clear vector edges and consistent rendering on mixed 4K/2K displays, with automatic destination-grid rendering.
- Save, apply, update, delete, import and export personal presets.
- Short in-app option explanations; inapplicable controls explain why they are disabled.
- Refreshed first-launch preparation window with app icon, progress and high-DPI rendering.
- New genuine application screenshots and updated RU/EN guides.

### Compatibility and checks

NVIDIA recording and stopping with motion were verified at 4.5× on the test setup. Other drivers and unusual shadows can behave differently. Discord was separately reported to show one pointer. Native Windows resize-press behavior is retained.

Local Windows checks cover native rendering, size validation/import/presets, JavaScript models, hidden UI, protocol, tray and the actual portable EXE. Hidden-window checks are not live recording tests. Checksums and the package verification report are attached. There is no automatic updater or Windows publisher signature.

[Project](https://github.com/WAKUDOdani/CursorMotion) · [Guide](https://github.com/WAKUDOdani/CursorMotion/blob/v3.10.0/docs/USAGE.en.md) · [Telegram](https://t.me/WAKUDOdani)

</details>

## CursorMotion 3.10.0 — Плавный моушн и свои пресеты

Обновлённый моушн, чёткие края на мониторах с разным масштабированием и библиотека своих настроек — в одном portable EXE.

### Скачать

**[CursorMotion-3.10.0-portable-x64.exe](https://github.com/WAKUDOdani/CursorMotion/releases/download/v3.10.0/CursorMotion-3.10.0-portable-x64.exe)**

Полностью выйди из старой версии через трей и замени EXE. Установка и права администратора не нужны. Настройки и пресеты сохраняются в AppData.

![Настоящий интерфейс CursorMotion — коллекция](https://raw.githubusercontent.com/WAKUDOdani/CursorMotion/v3.10.0/docs/assets/collection-rounded.svg)

### Что нового

- **Размер 0.5–4.5× — жёсткий предел.** Работает для отдельных форм, общего размера, импорта профиля и пресетов. Старые бо́льшие значения ограничиваются при загрузке.
- **Пересобранный моушн-блюр.** Размывается всё изображение по подробной физической траектории. Есть экспозиция, плотность и качество; системная позиция мыши остаётся точной.
- **Чёткие векторные края.** Отрисовка адаптируется к монитору; работа с 4K и 2K проверена пользователем.
- **Личные пресеты.** Сохранение, применение, обновление, удаление, импорт и экспорт всех настроек.
- **Понятные опции.** Короткие описания внутри приложения и объяснения для неактивных параметров.
- **Новое окно подготовки.** Иконка, прогресс распаковки и отрисовка для высокого DPI.
- **Свежие скриншоты и инструкции** на русском и английском.

<details>
<summary><strong>Посмотреть настройки и пресеты</strong></summary>

![Настройки курсора](https://raw.githubusercontent.com/WAKUDOdani/CursorMotion/v3.10.0/docs/assets/cursor-rounded.svg)
![Настройки моушна](https://raw.githubusercontent.com/WAKUDOdani/CursorMotion/v3.10.0/docs/assets/motion-rounded.svg)
![Пользовательские пресеты](https://raw.githubusercontent.com/WAKUDOdani/CursorMotion/v3.10.0/docs/assets/presets-rounded.svg)

Скриншоты сняты в реальном приложении на демонстрационном профиле Crystal.

</details>

<details>
<summary><strong>Совместимость и проверки</strong></summary>

- На тестовой конфигурации запись NVIDIA с моушном проверена на 4.5×: изображение курсора корректное, запись останавливается. Другие драйверы и нестандартные тени могут потребовать проверки.
- Для Discord отдельно подтверждён один указатель в трансляции.
- Поведение Windows при нажатии стрелками изменения размера сохранено.
- Локально проверены рендер, ограничение размера, импорт и пресеты, модели JavaScript, скрытое окно интерфейса, протокол, трей и готовый portable EXE. Проверки скрытого окна не заменяют живую проверку записи.
- Вложения `SHA256SUMS.txt` и `package-verification.json` относятся к опубликованному EXE.
- Сборка без цифровой подписи издателя Windows. Автообновлятора нет — обновление заменой файла после выхода из трея.

</details>

[Инструкция](https://github.com/WAKUDOdani/CursorMotion/blob/v3.10.0/docs/USAGE.md) · [Сообщить об ошибке](https://github.com/WAKUDOdani/CursorMotion/issues) · [Telegram автора](https://t.me/WAKUDOdani)

MIT · © WAKUDO Dani. Сведения о сторонних компонентах включены в приложение и репозиторий.

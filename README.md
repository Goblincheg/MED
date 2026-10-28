# MED — Mikhalych Emoji Dock (Мёд)

**MED — панель эмодзи для Windows с поиском на русском языке.**
Russian and English emoji search, Fluent Emoji 3D, and repeated insertion without closing the panel.

## ⬇️ СКАЧАТЬ MED / DOWNLOAD MED

### [MED Setup — Windows x64](https://github.com/Goblincheg/MED/releases/latest/download/MED-Setup-x64.exe)
### [MED Portable — Windows x64](https://github.com/Goblincheg/MED/releases/latest/download/MED-Portable-x64.zip)

Готовые приложения, не архивы исходников. Обе сборки автономные: .NET 10 включён.
Ready-to-run applications, not source archives. Both builds include the .NET 10 runtime.

[Заметки релиза / Release notes](https://github.com/Goblincheg/MED/releases/tag/v1.0.0) · [English ↓](#english)

## 🇷🇺 Возможности

- **Русский и английский поиск**: названия, ключевые слова, русские синонимы и разговорные выражения — например, `уточка`, `ржу`, `палец вверх`. Регистр и различие е/ё не мешают поиску; перевода через сеть нет.
- **Win + .** открывает панель там, где ты вводишь текст. Esc и клик снаружи закрывают её.
- **Microsoft Fluent Emoji 3D**: 1545 изображений 64 px, системный fallback, виртуализированная сетка с ограниченным кешем.
- **Множественная вставка**: выбирай эмодзи последовательно или повторно, панель остаётся открытой. Получатель проверяется, буфер обмена защищён.
- **История использования**, светлая и тёмная темы.
- **Фоновая работа без обязательного значка в трее**. Шестерёнка в панели открывает настройки, включая «Показывать значок в трее» и «Выход из MED».
- **Автозагрузка Windows**: включена по умолчанию в Setup, выключена по умолчанию в portable; управляется в настройках.

## Совместимость

**Windows 10 x64 — основная целевая система, работоспособность проверена.**

**Windows 11 — пока не тестировалась. Совместимость не гарантируется.**

## Установка и первый запуск

**Setup:** скачай EXE по ссылке выше. Установка выполняется для текущего пользователя в `%LOCALAPPDATA%\Programs\MED`, без обязательного UAC и отдельной установки runtime. Автозагрузку можно отключить в установщике или настройках. Ярлыки создаются в меню «Пуск», на рабочем столе — нет. Данные находятся в `%APPDATA%\MED`; при удалении сохраняются.

**Portable:** распакуй весь ZIP в доступный для записи каталог и запусти `MED.exe`. Оставь `portable.txt` рядом с EXE. Настройки и история сохраняются в `Data`. Не запускай приложение прямо из ZIP. Перед переносом отключи автозагрузку; в новом расположении её можно включить заново для исправления пути.

После запуска MED работает в фоне, панель и постоянный значок по умолчанию скрыты. Нажми **Win + .**, затем шестерёнку для настроек. Команда **«Выход из MED»** освобождает перехват и возвращает штатную панель Windows. Настройки можно открыть также командой `MED.exe --settings`.

Перед переходом с Classic Emoji Picker заверши старую копию и отключи её автозагрузку через её меню. Настройки и история копируются в MED без удаления оригиналов. Если portable Data недоступен для записи, MED явно сообщает о резервном каталоге; точный путь показан в настройках.

[Подробная инструкция](docs/MED-1.0.0-README.txt)

## Известные ограничения

- При экстремально быстрых кликах иногда появляется предупреждение о невозможности безопасной вставки.
- Windows 11 пока не тестировалась; совместимость не гарантируется.
- Внешний вид вставленных Unicode-эмодзи зависит от приложения-получателя и системных шрифтов. Вставка в повышенные приложения может быть запрещена Windows.
- Установщик не имеет цифровой подписи; Windows SmartScreen может показывать предупреждение.
- Шесть нестандартных ninja-cat последовательностей остаются скрытыми: для них нет подходящего Fluent-изображения и системного glyph.
- При предупреждении скрытый значок может временно появиться для уведомления.

## Сборка из исходников

Нужны Windows x64, .NET SDK **10.0.401**, а для Setup — установленный **Inno Setup 7**. Сторонние инструменты автоматически не устанавливаются.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Build-MEDRelease.ps1
```

ExecutionPolicy здесь относится только к дочернему процессу PowerShell и не меняет системную политику. Скрипт выполняет проверки, создаёт self-contained публикацию и ZIP, собирает Setup при наличии ISCC. Результаты — в `artifacts/releases`. Установку и изменение реальной автозагрузки скрипт не выполняет.

Подготовленные 64 px ресурсы уже включены в репозиторий. Вся коллекция Microsoft не нужна для обычной сборки. Для повторной генерации скачай официальный [Fluent Emoji](https://github.com/microsoft/fluentui-emoji), распакуй в `external/fluentui-emoji-main` и следуй [инструкции генератора](tools/FluentEmojiResources/README.md). Коллекция, пользовательские данные и артефакты сборки исключены из Git. Trimming отключён.

## English

**MED is an emoji picker for Windows with Russian-language search.**

### Features

- Russian and English names, keywords, Russian synonyms and colloquial expressions. Case-insensitive search with е/ё normalization; no online translation.
- Open with **Win + .**; close with Esc or an outside click.
- **Microsoft Fluent Emoji 3D**, system fallback and a virtualized grid with a bounded image cache.
- Insert multiple emojis without closing the panel. The original recipient is validated and clipboard contents are protected.
- Recent history, light/dark themes, background operation without a mandatory tray icon.
- Open settings using the panel's gear button. Show/hide the tray icon, manage Windows startup, or choose **Exit MED** to restore the standard Windows shortcut.
- Setup and portable editions, both self-contained with .NET 10 included.

### Compatibility

**Windows 10 x64 — primary target platform, tested and working.**

**Windows 11 — not yet tested. Compatibility is not guaranteed.**

### Install and run

**Setup:** use the prominent download link above. Installs for the current user into `%LOCALAPPDATA%\Programs\MED` without requiring elevation. Startup is enabled by default and can be disabled. Start-menu shortcuts are created; no desktop shortcut. User data lives in `%APPDATA%\MED` and is retained on uninstall.

**Portable:** extract the entire ZIP into a writable directory and run `MED.exe`. Keep `portable.txt` alongside it; data is stored in `Data`. Startup is off by default. Disable startup before moving the folder, then enable it from the new location to repair the path. A clearly reported AppData fallback is used if local Data is unwritable.

MED starts in the background with no picker window or permanent tray icon. Press **Win + .** and use the gear button for settings or exit; `MED.exe --settings` also works. Close any old Classic Emoji Picker instance and disable its startup first. Existing settings/history are copied without deleting the originals.

### Known limitations

- Extremely fast clicks may occasionally trigger a safe-insertion warning.
- Windows 11 has not been tested; compatibility is not guaranteed.
- Inserted Unicode emojis are rendered by the receiving application and its fonts, not by MED's images. Windows may block insertion into elevated applications.
- The installer is unsigned; Windows SmartScreen may display a warning.
- Six nonstandard ninja-cat sequences remain hidden. A hidden tray icon may appear temporarily for a warning notification.

### Build

Windows x64, .NET SDK 10.0.401 and Inno Setup 7 are required for the full build. Run the PowerShell command above. No system tools are installed automatically. Generated 64 px resources are committed; the complete upstream Microsoft collection is only needed to regenerate them.

## Происхождение / Credits and licences

**Based on Classic Emoji Picker by Platima.** [Original repository](https://github.com/platima/Classic-EmojiPicker).

MED preserves the original project's copyright and MIT licence. Source history is retained without rewriting. Внутренние namespace и путь `EmojiPicker/EmojiPicker.csproj` сохранены; имя приложения и EXE — MED.

[MIT / LICENSE](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md) · [Full licence texts](licenses/)

Microsoft Fluent Emoji is MIT-licensed. System fonts are not redistributed.

### Техническая документация / Technical documentation

- [Русский поиск / Russian search data](docs/RUSSIAN-SEARCH.md)
- [Fluent integration](docs/FLUENT-EMOJI-INTEGRATION.md)
- [Resource generator](tools/FluentEmojiResources/README.md)
- [Setup build and validation](docs/MED-1.0.0-SETUP.md)
- [User guide](docs/MED-1.0.0-README.txt)

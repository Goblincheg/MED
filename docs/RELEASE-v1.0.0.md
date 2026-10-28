# MED 1.0.0 — Mikhalych Emoji Dock (Мёд)

## 🇷🇺 MED — панель эмодзи для Windows с поиском на русском языке

### Скачать

- **[MED Setup — Windows x64](https://github.com/Goblincheg/MED/releases/latest/download/MED-Setup-x64.exe)** — установка для текущего пользователя, автозагрузка включена по умолчанию; её можно отключить.
- **[MED Portable — Windows x64](https://github.com/Goblincheg/MED/releases/latest/download/MED-Portable-x64.zip)** — распакуй полностью и запусти MED.exe; автозагрузка по умолчанию выключена.

Обе сборки автономные: .NET 10 включён, отдельный runtime не нужен.

### Возможности

- Русский и английский поиск: названия, синонимы и разговорные выражения, нормализация е/ё.
- Открытие через Win + ., закрытие по Esc и клику снаружи.
- Microsoft Fluent Emoji 3D и системный fallback.
- Множественная вставка без закрытия панели, история использования и защита буфера обмена.
- Работа в фоне без обязательного значка трея. Настройки и «Выход из MED» доступны по шестерёнке.
- Управление автозагрузкой; portable Data и AppData MED для установленной версии; безопасное копирование старых настроек и истории.

**Windows 10 x64 — основная целевая система, работоспособность проверена.**

**Windows 11 — пока не тестировалась. Совместимость не гарантируется.**

Перед первым запуском закрой предыдущую копию Classic Emoji Picker / MED. После запуска нажми Win + .; отсутствие постоянного значка в трее — штатное поведение.

### Известные ограничения

- При экстремально быстрых кликах иногда может появляться предупреждение о невозможности безопасной вставки.
- Windows 11 пока не тестировалась.
- Внешний вид вставленных Unicode-эмодзи зависит от приложения-получателя и системных шрифтов; вставка в повышенные приложения может блокироваться Windows.
- Установщик не имеет цифровой подписи; Windows SmartScreen может показывать предупреждение.
- Шесть нестандартных ninja-cat последовательностей скрыты. При предупреждении значок может временно появиться.

## 🇬🇧 MED — Windows emoji picker with Russian-language search

### Download

- **[MED Setup — Windows x64](https://github.com/Goblincheg/MED/releases/latest/download/MED-Setup-x64.exe)** — per-user installation with optional Windows startup enabled by default.
- **[MED Portable — Windows x64](https://github.com/Goblincheg/MED/releases/latest/download/MED-Portable-x64.zip)** — extract the entire archive and run MED.exe; startup is disabled by default.

Both builds include .NET 10; no separate runtime installation is needed.

### Features

Russian and English names, synonyms and colloquial search expressions; Win + .; Fluent Emoji 3D with system fallback; repeated insertion without closing the panel; recent history; clipboard protection; background operation without a mandatory tray icon. The gear button opens settings and Exit MED. Startup can be configured, and existing settings/history are copied without deleting the originals.

**Windows 10 x64 — primary target platform, tested and working.**

**Windows 11 — not yet tested. Compatibility is not guaranteed.**

Close any old Classic Emoji Picker / MED instance before first launch. Press Win + . after starting MED; having no permanent tray icon is normal.

### Known limitations

- Extremely fast clicks may occasionally trigger a safe-insertion warning.
- Windows 11 has not been tested; compatibility is not guaranteed.
- Inserted Unicode emojis depend on the receiving application and system fonts. Windows may block insertion into elevated applications.
- The installer is unsigned; Windows SmartScreen may display a warning.
- Six nonstandard ninja-cat sequences remain hidden. The tray icon may appear temporarily for a warning.

## Validation / Проверки

6683 automated checks plus 24 isolated startup/lifecycle checks passed. Setup was compiled with Inno Setup 7.1.0 without errors or warnings. Both packages contain identical MED 1.0.0 runtime files and complete licence notices. Automated logic checks do not replace real Windows input/installation testing.

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| MED-Setup-x64.exe | 65043893 | C6FF8CB4AAD59AC143591A0F8F2AA5CF85DCA195457EE0B1EC4E1F3F96093D01 |
| MED-Portable-x64.zip | 84596907 | 94D638D4F32C9D7B3F9FF4291BD61D53D4385116340F4D52D16A2CC1F6458FE1 |

**Based on Classic Emoji Picker by Platima.** [Original project](https://github.com/platima/Classic-EmojiPicker). MIT licence; original copyright and third-party notices are preserved. Microsoft Fluent Emoji is MIT-licensed.

# Legacy upstream MSI

`Package.wxs` is retained as historical Classic Emoji Picker infrastructure.
It is **not** a MED installer: it installs per-machine, expects EmojiPicker.exe,
and is no longer invoked by the release workflow. Do not use it for MED 1.0.0.

The supported MED installer source is `../EmojiPicker.iss` (per-user Inno Setup,
MED.exe, HKCU Run, no required elevation). The project file/path names remain
unchanged deliberately.

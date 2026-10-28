# Third-Party Notices

MED (Mikhalych Emoji Dock), based on Classic Emoji Picker, is distributed under the [MIT License](LICENSE) and includes
or depends on the following third-party components. Each remains under its own
licence, reproduced in the `licenses` directory and linked below. Original licence
bytes, source URLs/package versions and SHA-256 are recorded in
`licenses/THIRD-PARTY-LICENSE-SOURCES.txt` for the additional release notices.

## Bundled data

### Microsoft Fluent Emoji 3D
- **Used for:** 1545 exact Unicode-mapped 64 px PNG resources displayed in the picker.
- **Author:** Microsoft Corporation.
- **Source:** https://github.com/microsoft/fluentui-emoji (local audited snapshot;
  inventory fingerprint documented in `docs/FLUENT-EMOJI-AUDIT.md`).
- **Licence:** MIT; complete original copyright, permission and disclaimer in
  `licenses/Microsoft-Fluent-Emoji-LICENSE.txt`, copied to build/publish output.
- **Changes:** reduced from original 3D PNG using the approved linear-light,
  premultiplied-alpha Lanczos3 resampler, without extra sharpening; lossless PNG
  IDAT recompression. Original files are not modified or redistributed as 256 px assets.
- **Reproduction/provenance:** `tools/FluentEmojiResources`, generated
  `EmojiPicker/Resources/Fluent64/manifest.json` (Unicode, source paths, source and
  output SHA-256), and `docs/FLUENT-EMOJI-INTEGRATION.md`.

### Russian annotations: Unicode CLDR 48
- **Used for:** names and keyword phrases in `EmojiPicker/Resources/keywords.ru.json`.
- **Source:** https://github.com/unicode-org/cldr/tree/release-48/common/annotations
  and https://github.com/unicode-org/cldr/tree/release-48/common/annotationsDerived
  (`ru.xml` in each directory).
- **Author:** Unicode, Inc. and contributors.
- **Licence:** Unicode License V3 (SPDX: Unicode-3.0); the complete licence is
  included in `licenses/CLDR-48-LICENSE.txt` and copied to build/publish output.
- **Reproduction:** `tools/Import-RussianAnnotations.ps1` verifies the SHA-256
  of each source file. Source hashes and coverage are documented in
  `docs/RUSSIAN-SEARCH.md`.
- **Local additions:** `tools/russian-aliases.json` and
  `tools/russian-supplement.json` are original project contributions under
  this project's MIT licence, not additional CLDR translations.

### emojibase
- **Used for:** the search keyword/tag data in `EmojiPicker/Resources/keywords.json`
  (merged with emojilib, below), generated from emojibase's annotations
  (which derive from Unicode CLDR).
- **Author:** Miles Johnson and contributors
- **Licence:** MIT — https://github.com/milesj/emojibase/blob/master/LICENSE
- **Full text:** `licenses/emojibase-LICENSE.txt`.
- **Project:** https://github.com/milesj/emojibase

### emojilib
- **Used for:** community keyword associations merged into
  `EmojiPicker/Resources/keywords.json` (e.g. "laugh" for 😅), giving search
  coverage similar to the Windows 10 panel.
- **Author:** Mu-An Chiou and contributors
- **Licence:** MIT — https://github.com/muan/emojilib/blob/main/LICENSE
- **Full text:** `licenses/emojilib-LICENSE.txt`.
- **Project:** https://github.com/muan/emojilib

### Unicode emoji frequency data
- **Used for:** the search-result popularity ordering in
  `EmojiPicker/Resources/popularity.json`, parsed from Unicode's published
  emoji-frequency tiers and supplemented with popular post-2018 emoji absent
  from that dataset.
- **Author:** Unicode, Inc.
- **Licence:** Unicode License — https://www.unicode.org/license.txt
- **Full Unicode License V3 text:** `licenses/CLDR-48-LICENSE.txt` (shared Unicode licence).
- **Source:** https://home.unicode.org/emoji/emoji-frequency/

## NuGet package dependencies

These are restored at build time and bundled into the published executable.

### Emoji.Wpf
- **Used for:** the Unicode catalog and fallback colour rendering via the system Segoe UI Emoji font.
- **Author:** Sam Hocevar
- **Licence:** WTFPL — see the package's licence at https://github.com/samhocevar/emoji.wpf
- **Copyright:** Copyright © 2017–2021 Sam Hocevar (package metadata).
- **Full licence text:** `licenses/Emoji.Wpf-and-Stfu-WTFPL.txt` (SPDX WTFPL,
  as declared by the NuGet package).
- **Project:** https://github.com/samhocevar/emoji.wpf

### VirtualizingWrapPanel
- **Used for:** UI virtualization of the emoji grid.
- **Author:** Sebastian Bäumlisberger
- **Licence:** MIT — https://github.com/sbaeumlisberger/VirtualizingWrapPanel/blob/master/LICENSE
- **Full text:** `licenses/VirtualizingWrapPanel-2.5.2-LICENSE.txt`, from the exact
  repository commit recorded by the installed 2.5.2 NuGet package.
- **Project:** https://github.com/sbaeumlisberger/VirtualizingWrapPanel

### Stfu 0.1.1
- **Used for:** Emoji.Wpf transitive runtime dependency.
- **Author/copyright:** Copyright © 2017–2021 Sam Hocevar (package metadata).
- **Licence:** WTFPL; `licenses/Emoji.Wpf-and-Stfu-WTFPL.txt`.
- **Project:** https://github.com/samhocevar/stfu

## Bundled .NET runtime 10.0.12

The self-contained distribution includes Microsoft.NETCore.App.Runtime.win-x64
and Microsoft.WindowsDesktop.App.Runtime.win-x64 10.0.12. Complete original
licences from those exact NuGet packages are included in
`licenses/dotnet-runtime-10.0.12-LICENSE.txt` and
`licenses/dotnet-windowsdesktop-10.0.12-LICENSE.txt`. The runtime's complete
additional notices are in `licenses/dotnet-runtime-10.0.12-THIRD-PARTY-NOTICES.txt`.

## Installer components

The Setup EXE is compiled with Inno Setup 7.1.0; its original licence is included
as `licenses/Inno-Setup-7-LICENSE.txt`. Inno Setup components are used by the
installer/uninstaller, not by the portable MED application.

## System components (not redistributed)

- **Segoe UI Emoji** — Microsoft's colour emoji font, used for rendering at runtime.
  It ships with Windows (10 version 1809+ and Windows 11) and is **not** bundled with
  or redistributed by this project.

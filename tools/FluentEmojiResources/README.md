# Fluent Emoji 64 px generator

Requires the existing .NET SDK 10.0.401 on Windows. Uses the same Emoji.Wpf 0.3.4
catalog and linear-light, premultiplied-alpha Lanczos3 as the approved comparison
prototype. No image-processing package or new runtime dependency is required.

From the repository root:

```powershell
dotnet run --project tools/FluentEmojiResources/FluentEmojiResources.csproj -c Release -- generate C:/Projects/MED/external/fluentui-emoji-main C:/Projects/MED
dotnet run --project tools/FluentEmojiResources/FluentEmojiResources.csproj -c Release -- verify C:/Projects/MED/external/fluentui-emoji-main C:/Projects/MED
```

The first command writes only `EmojiPicker/Resources/Fluent64` and the Microsoft
licence under `licenses`. Identical files are not rewritten. The second recomputes
all results and fails on any missing or different output; it never repairs them.
Both commands read upstream without modifying it and verify each used source PNG
again after resampling. Unexpected Unicode ambiguity, coverage change or extra
output PNG causes failure, not selection of an arbitrary image.

Indexing uses only metadata.unicode/unicodeSkintones and the corresponding tone
folder, never glyph or folder labels. Unicode keys retain VS16, ZWJ and skin-tone
scalars. The catalog remains pinned to 1558 entries, 1545 PNG, 13 missing.

`manifest.json` preserves complete provenance and all SHA-256 values. `index.json`
is the compact runtime lookup, avoiding parsing source paths/hashes at startup.
Both and the PNG are embedded into the executable's managed assembly; original
256 px images and the external source tree are not included in the application.

All images have maximum side 64 px; two non-square MED resources are 62×64.
Alpha and aspect ratio are preserved. The PNG encoder is followed only by an
optional lossless IDAT recompression; no palette quantisation or post-sharpening
is used. Reproducibility is checked against the specified source snapshot, SDK/
runtime and Windows WIC encoder; different encoder versions can change PNG bytes.
The exact measured output exceeds the audit's 6.76 MiB estimate, which used a
different WPF resizing path; see the integration report for actual sizes.

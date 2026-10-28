# Fluent Emoji 3D integration — stage 5.2

## Scope and coverage

The existing picker layout, 32-DIP grid images, 23-DIP category icons, themes,
VirtualizingWrapPanel/recycling, hotkeys, target validation, insertion queue,
clipboard protection and search algorithm are preserved. Only image bindings and
catalog display eligibility change.

- Original catalog: **1558** exact Unicode sequences, retained unchanged.
- Fluent assets: **1545**, matched by metadata `unicode` / `unicodeSkintones`.
- No Fluent match: **13**; no fuzzy, folder-name or `glyph` matching is used.
- System-renderable before integration on the test host: **1405**.
- Displayable after integration: **1552** = 1545 Fluent + 7 system fallbacks.
- Newly displayable through Fluent: **147**.
- Six nonstandard Windows ninja-cat ZWJ sequences have neither a Fluent asset
  nor a supported system rendering here. They remain in the complete catalog,
  retain their original Unicode, and remain excluded from the visible catalog.
  The exact 13 missing sequences are recorded in `manifest.json` and the audit.

No Unicode normalization is performed when mapping or inserting characters.
Variation selectors, skin-tone modifiers, keycaps and ZWJ sequences retain their
exact scalar sequences. Existing keyword normalization remains unchanged.

## Resources and reproduction

Source: the audited local `external/fluentui-emoji-main` snapshot of Microsoft's
Fluent Emoji repository. The source tree remains ignored by Git and unmodified.
See `tools/FluentEmojiResources/README.md` for generation and verification commands.

The generator uses the approved prototype's linear-light, premultiplied-alpha,
antialiased Lanczos3 resampling without sharpening. Aspect ratio and alpha are
preserved. 1543 PNG are 64×64; two are 62×64. Optional IDAT recompression is
lossless and changes neither pixels nor dimensions. Twelve overlapping prototype
samples are automatically compared pixel-for-pixel with the generated resources.

| Generated resource | Bytes | MiB |
| --- | ---: | ---: |
| 1545 PNG | 9,867,808 | 9.411 |
| Full provenance manifest | 710,356 | 0.677 |
| Compact runtime index | 95,410 | 0.091 |
| Total embedded resource data | 10,673,574 | 10.179 |

The audit's **6.76 MiB** estimate used a different WPF resizing path. Actual
output from the approved prototype algorithm is larger; quality was not reduced
to force the estimate. There is no quantization or lossy compression.

`manifest.json` records Unicode, dimensions, bytes, SHA-256, original PNG path and
hash, metadata hash, source-index fingerprint and original MIT licence hash.
`index.json` contains only runtime image descriptors, avoiding parsing all
provenance at startup. `verify` recomputes every generated image and index and
fails on differences. Reproduction is byte-checked with SDK 10.0.401, runtime
10.0.12 and the current Windows WIC encoder; encoder changes can affect bytes.

PNG and both indexes are raw embedded resources in `EmojiPicker.dll`; no original
256 px PNG or external source collection is included in the application. Full
Microsoft MIT copyright, permission and disclaimer are copied unchanged into
`licenses/Microsoft-Fluent-Emoji-LICENSE.txt` in build and publish output, alongside
the project LICENSE, CLDR licence and THIRD-PARTY-NOTICES.md.

## Loading, cache and fallback

`FluentEmojiImage.Source` replaces the two former Emoji.Wpf image bindings.
Only loaded, visible items request images. An existing system image is shown
while loading or when an asset is absent/unavailable; a visible question marker
is the final fallback if the system cannot render the sequence. Item layout is
unchanged and WPF HighQuality scaling handles the existing DIP dimensions/DPI.

`FluentEmojiImages` decodes on background tasks, with at most two active decoders.
Streams use OnLoad and are closed immediately; decoded BitmapImage objects are
frozen before crossing threads. Assignments explicitly dispatch to the UI thread
and validate cancellation, current binding and visibility to avoid recycling
stale images into other cells. Unloaded/hidden/rebound cells cancel requests and
release their ImageSource. The cache retains no UI elements.

The LRU limit is **128 images**, at most **2 MiB of decoded pixel data**; framework
and object overhead are additional. Visible cells can independently retain their
current images. Eviction releases the cache reference; WPF/GC controls actual
native-memory reclamation. Cancellation and generation checks prevent obsolete
work from repopulating a cleared cache. The catalog is never fully decoded at
startup. Missing/failed assets do not produce broken-image UI.

## Automated validation

The Windows STA check executable uses a plain WPF Application, avoiding the real
tray host's startup, single-instance hook and input registration. It instantiates
compiled MainWindow BAML and runs **6650 checks**, including previous search,
queue/lifecycle/target-policy checks plus:

- exact catalog retention/order, 1545 mappings and all 13 missing entries;
- every embedded PNG's hash, size, Unicode filename, dimensions and visible alpha;
- Russian/English search and original Unicode preservation;
- pixel equality with 12 approved prototype samples;
- system fallback, cancelled loads, cache capacity, frozen sources and cache hits;
- late loads after cache clear, recycled bindings, hide/show/unload and themes;
- real VirtualizingWrapPanel/recycling, all categories and scroll positions,
  no blank realized images and bounded realized element count.

Generator `verify` also passed for all 1545 assets and both indexes. These checks
do not send real Windows input and do not confirm external application behavior.

## Performance measurements

Measured on the same Windows host, Release win-x64 self-contained, .NET 10.0.12.
Baseline source is an isolated snapshot of commit `21ed6da`; current working
source is not replaced. Each variant runs in a separate process with a plain WPF
Application and an off-screen, nonactivating compiled picker window. No hotkey,
insertion or clipboard operations are invoked. Numbers are local observations,
not hardware-independent guarantees.

| Measurement | System baseline | Fluent 64 px |
| --- | ---: | ---: |
| Window construction | 416.5 ms | 403.3 ms |
| First Show, synchronous work | 430.5 ms | 438.4 ms |
| First Show through image readiness | 457.3 ms | 523.8 ms |
| Warm Show, median of 5 | 1.48 ms | 4.93 ms |
| Search median / P95, 105 queries | 3.51 / 9.15 ms | 3.86 / 8.75 ms |
| Category/scroll readiness median / P95 | 68.1 / 209.3 ms | 79.9 / 158.7 ms |
| Maximum realized grid items | 66 | 66 |
| Managed heap after collection | 21.09 MiB | 20.82 MiB |
| Working set at measurement | 37.36 MiB | 44.96 MiB |

Category/scroll measurements include layout, dispatcher work and image readiness
over 24 category changes / 72 scrolls; they are **not frame-time/FPS measurements**.
Search has 147 additional eligible entries. The first full image-ready view adds
about 66 ms, mostly asynchronous work; the synchronous first-show difference is
about 8 ms. Runtime metadata was split from provenance after startup measurement;
neither virtualization nor cache bounds were relaxed.

An additional 60-pass endurance run performed **360 category changes and 1080
scrolls**. Cache stayed at 128 / 2 MiB; the post-collection managed heap ranged
20.00–20.96 MiB across checkpoints and ended at 20.02 MiB, without a monotonic
growth trend. Maximum realized count remained 66. This finite test does not prove
absence of every leak during hours of interactive use.

The production application prewarms its window, unlike the cold isolated Show
measurement. Real hotkey-to-visible latency, perceived scrolling smoothness,
125–150% DPI and actual receiving-app rendering require manual verification.

## Build and manual checks

Final Release win-x64 self-contained publication (including .NET 10.0.12):
**193,688,444 bytes / 184.72 MiB**, 482 files. Build completed with **0 errors and
0 warnings**. Output EXE: `C:\Projects\MED\publish\med52-win-x64\EmojiPicker.exe`.
The published managed assembly is SHA-256-identical to the assembly exercised by
the checks. The published Microsoft licence is byte-identical to the upstream
licence; original 256 px images are absent.

```powershell
dotnet build tests/EmojiPicker.Checks/EmojiPicker.Checks.csproj -c Release -r win-x64 --self-contained true -p:BaseOutputPath=bin/med52/
& tests/EmojiPicker.Checks/bin/med52/Release/net10.0-windows/win-x64/EmojiPicker.Checks.exe
dotnet publish EmojiPicker/EmojiPicker.csproj -c Release -r win-x64 --self-contained true -p:BaseOutputPath=bin/med52/ -o publish/med52-win-x64
```

Exit any previously running copy through its tray menu before launching the new
EXE; otherwise single-instance handling can activate the old process.

Manually check light/dark themes, Windows DPI 100/125/150%, all categories and
Recent, rapid scrolling and reopen after hiding. Search `утка`, `уточка`, `любовь`,
`ржу`, `палец вверх` and English equivalents. Check ordinary/ZWJ/keycap images
and pair/family fallbacks. Insert `🤣🤣🤣🤣🤣` and `😀😊❤️🦆` into a known target,
verify Esc, outside click, history and clipboard restoration. The existing
warning at extremely fast clicks is unchanged. Newly displayable icons do not
guarantee that a receiving application's font supports the Unicode sequence.

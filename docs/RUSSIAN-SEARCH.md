# Russian search and repeated insertion

Russian search is offline. No translation service, morphology package or new
NuGet dependency is used. English names/tags remain available.

## Dictionary provenance

`EmojiPicker/Resources/keywords.ru.json` contains 4,348 Unicode-keyed records:
4,342 from Unicode CLDR 48, plus six original names for Microsoft's non-standard
cat sequences. CLDR `tts` annotations supply names; the other annotations supply
keyword phrases. U+FE0F is removed only from lookup keys, never from inserted text.

Pinned upstream inputs:

| Source | SHA-256 |
| --- | --- |
| https://raw.githubusercontent.com/unicode-org/cldr/release-48/common/annotations/ru.xml | `325BC6BB96AFF4A5A9124A9087313669B0E88DA920B84E261D61A402EA9405A8` |
| https://raw.githubusercontent.com/unicode-org/cldr/release-48/common/annotationsDerived/ru.xml | `CD3AA5D40A42D473C7796584D560CAC9B35F223561DD3E0E57594FEC5003C010` |

CLDR is covered by Unicode-3.0; the full release-specific licence is in
`licenses/CLDR-48-LICENSE.txt`. Both it and THIRD-PARTY-NOTICES.md are copied to
build/publish output. Local synonyms (such as "уточка", "ржу", "лайк", "пивко")
and the six cat names are original MIT-licensed contributions stored separately
in `tools/russian-aliases.json` and `tools/russian-supplement.json`.

Rebuild the dictionary with PowerShell 7:

```powershell
./tools/Import-RussianAnnotations.ps1
```

The importer downloads missing source XML into ignored `EmojiPicker/obj/cldr-48`,
verifies hashes, rejects missing names, and generates the embedded JSON. Neither
generation nor internet access is needed when running the application.

Validation on this Windows 10 machine found 1,558/1,558 catalog entries covered,
including all 1,405 renderable entries. The six local cat names cover stunt,
hipster, astro, ninja, dino and hacker cats absent from standard CLDR. There are
no missing Russian records in the current application's catalog. This does not
mean every inflected Russian word or slang expression is supported. Dictionary
coverage must be checked again if Emoji.Wpf or its catalog changes.

## Search ordering

Queries, names and tags ignore case, fold ё to е and collapse whitespace.
Exact names / complete Russian keyword phrases precede word-start matches,
which precede substring matches. Within these groups the existing popularity
tiers, name-over-tag penalty and stable catalog ordering are retained. Multiword
aliases, such as "палец вверх", are matched as phrases, not stemmed words.

## Repeated insertion

Opening the picker captures a fixed window, process, thread and focused child.
Each choice is queued. Input restores and validates that recipient, uses the
existing hybrid/paste/keystroke mechanism, and returns focus to the still-open
picker without clearing the search or selection. Clipboard transactions never
overlap. Enter pressed during a transfer queues a choice instead of sending a
newline to the recipient.

Outside mouse-down or Escape cancels pending choices; normal loss of activation
also closes the picker. Programmatic transfer to the captured target is guarded.
Win+. while open closes the current session rather than opening another panel.
Once input has been sent it cannot be undone by cancellation; clipboard cleanup
continues even after the panel closes. Clipboard history exclusion and the
sequence-number check protecting a new user copy are retained.

An unknown/closed/replaced/elevated recipient, changed focus, held modifier,
unreadable clipboard format, or failed SendInput produces a tray warning rather
than a guessed insertion or clipboard fallback. A successful SendInput means
Windows accepted the events, not that every application accepted the text.
As before, slow/remote paste recipients may need a longer `pasteRestoreDelayMs`.

## Automated checks

```powershell
dotnet run --project tests/EmojiPicker.Checks/EmojiPicker.Checks.csproj -c Release -r win-x64 --self-contained true
```

This dependency-free regression executable checks dictionary coverage, Russian
and English queries, ranking, case/ё normalisation, compiled WPF resources,
repeated/mixed/ZWJ choice queues, cancellation, Escape, and invalid recipients.
Window handler checks use an off-screen non-activating test window. Tests do not
send input to other applications or modify the clipboard. They do not prove
actual insertion/focus behaviour in a recipient application.

## Manual Windows 10 checks

1. In Notepad, open with Win+. in both RU/EN layouts; only one panel should open.
2. Search: утка, уточка, сердце, любовь, смех, ржу, кот, кошка, огонь, праздник,
   палец вверх; then smile, heart, splash. Compare uppercase and е/ё variants.
3. Click 🤣 five times, then test five Enter presses: expect 🤣🤣🤣🤣🤣 with
   no newlines, no lost focus and the panel still open.
4. Select 😀😊❤️🦆 sequentially, then 👩‍💻 and other joined emoji. Repeat in a
   browser text field. Selection/search should survive every insertion.
5. Test arrows, Tab/Shift+Tab, click selection, and Enter with a pending search.
6. Click outside (including the original target) during a paste, or press Escape
   with a nonempty search: the panel should close and pending choices stop.
7. Open, then close/change the target window or switch apps during transfer.
   No queued emoji should go into the newly active app. Tray-open has no target
   and should warn on selection. An elevated recipient should be rejected.
8. Verify pasted joined emoji with an empty clipboard and with copied text,
   image and file selections. Previous content should return. Copy something new
   during the restore delay: that new content must survive. Test Win+V exclusion.
9. Exit/reopen and verify successful choices appear in Recent. Exit should restore
   the system Win+. behaviour. Leave Start with Windows unchanged during testing.

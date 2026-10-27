# MED 4.2: diagnostic build for unexpected hiding

The specific runtime cause is not established. The source contains several
independent silent close paths consistent with the manual report:

| Logged reason | Existing close condition |
| --- | --- |
| `drain-finally-not-foreground` | The consumer finished but the picker was not the foreground window after the focus-return attempt. |
| `insert-postcheck-foreign-foreground` | A window other than the picker or captured recipient was foreground after an insertion. |
| `deactivated-idle` | WPF delivered Deactivated while the insertion guard was inactive. |
| `deactivated-deferred-foreign-foreground` | The deferred Deactivated check observed another foreground window. |
| `outside-click` | WindowFromPoint/GetAncestor classified a mouse-down as belonging to another root window. |

These conditions are findings about the code, not proof of which condition
caused the user's reproduction. This stage preserves all existing close rules,
input validation, mouse-activation handling, queue limits and clipboard cleanup.
No speculative lifecycle fix, retries or timing delays have been introduced.

## Trace contents

Each opening starts a session with an incrementing ID and sequence numbers.
The last 256 events are buffered in memory. Actual hiding/closing flushes that
buffer to `%APPDATA%\ClassicEmojiPicker\picker-lifecycle.log`, independently of
the Debug logging toggle. One old log is kept after rotation at approximately
5 MiB. Every line identifies this build as `MED-4.2-v1`.

Records include fixed event codes, PID, window handles, queue counts, visibility,
activation, cancellation and programmatic-focus flags. Mouse-down records include
coordinates, picker bounds, hit/root handles and injected-event flags to distinguish
a physical outside click from a possible hit-test misclassification.

The trace distinguishes hook/WPF Escape, outside clicks, close button, Win+.,
application exit, input-monitor failure, all queue-driven hides, Deactivated,
LostFocus/LostKeyboardFocus, native MA_NOACTIVATE decisions, programmatic target
activation and picker-focus return, visibility changes, and warm-up hiding.

There are no field contents, clipboard data, selected emoji, search queries,
window titles or process command lines in this separate diagnostic log. Low-level
callbacks only add bounded in-memory records; file output happens on hiding.
The existing general debug log is separate and need not be enabled for this test.

## Manual reproduction

1. Exit the old Classic Emoji Picker from its tray menu. The single-instance
   mutex would otherwise forward launching the new EXE to the old process.
2. Run `EmojiPicker/bin/med42/Release/net10.0-windows/win-x64/EmojiPicker.exe`.
   This diagnostic output is separate because the ordinary EXE was locked by the
   still-running user process during the build; no process was terminated.
3. In Notepad or the previously affected application, open via Win+. and reproduce
   rapid repeated clicks with the pointer stationary. Also test 50 choices, Escape
   during processing, an actual outside click, and reopening after closing.
4. If it closes unexpectedly, the log is flushed automatically. If it stays open,
   press Escape after the test to flush the successful session.
5. Inspect/provide the last affected session of `picker-lifecycle.log` (and, if it
   rotated, `picker-lifecycle.old.log`). The `dismiss reason=...` and preceding
   programmatic-focus/mouse records identify the next investigation path.

Do not use the old normal-output EXE for this reproduction. The diagnostic build
is a self-contained Release win-x64 .NET 10 build; keep its entire folder together.

## Automated scope

The existing queue/search tests remain. Added checks verify bounded trace storage,
reason separation, field-text exclusion, 50 queued choices with programmatic
Deactivated callbacks, no closing from the new focus-loss observers, Escape and
outside cancellation during insertion, and independent tracing after reopening.
Test windows remain off-screen and non-activating, and test log batches stay in
memory. These checks validate diagnostic routing and existing logic; they do not
prove native Windows input or establish the user's runtime close cause.

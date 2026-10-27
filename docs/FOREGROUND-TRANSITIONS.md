# MED 4.3: NULL foreground during repeated insertion

The MED-4.2-v1 manual trace confirmed an accepted insertion followed by
foreground=0 and `insert-postcheck-foreign-foreground`. The previous comparison
treated every handle other than the picker or target as foreign, including NULL.
Microsoft explicitly documents that GetForegroundWindow can return NULL while a
window is losing activation:
https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow

This documents a valid Win32 transition state. It does not by itself establish
that MA_NOACTIVATE alone caused it. Mouse activation handling remains unchanged.

## Narrow fix

`ForegroundPolicy` distinguishes Unknown (NULL), Picker, Target and Foreign.

- After insertion and at drain completion, only a confirmed foreign handle can
  trigger the existing foreign-window close path. Unknown and the captured target
  are not evidence of user departure during a programmatic focus transition.
- Deferred Deactivated checks use the same foreign-window rule. Idle Deactivated
  still closes for an actual non-picker window, but not for Unknown or Picker.
- A preflight Unknown permits a focus-restoration attempt, not input. The existing
  TextInjector still validates target process/thread/window/control identity and
  actual foreground/focused-control ownership before SendInput or Ctrl+V.
- If restoration/focus cannot be confirmed, insertion fails through the existing
  warning/queue-clear path; no blind insertion or automatic replay is added.
- An accepted insertion is never retried just because its postcheck sees NULL.
- Escape and actual outside mouse-down still cancel immediately, independently
  of the foreground classification. FIFO limits and clipboard cleanup are unchanged.

There are no new delays, polling, hooks, dependencies, settings or UI changes.
An unresolved NULL can leave the panel visible; it cannot authorise input. The
existing checks react if a confirmed foreign window is subsequently observed.

## Checks and runtime evidence

Automated tests use the real FIFO drain plus simulated foreground/focus boundaries
to verify NULL -> target, NULL -> foreign, 50 repeated choices with transient NULL,
outside/Escape cancellation, and refusal of unconfirmed recipients. The existing
native target validation and WPF activation/lifecycle checks remain. These tests
do not send real Windows input or establish native stress-test behaviour.

```powershell
dotnet build tests/EmojiPicker.Checks/EmojiPicker.Checks.csproj -c Release -r win-x64 --self-contained true --no-restore -p:BaseOutputPath=bin/med43/
./tests/EmojiPicker.Checks/bin/med43/Release/net10.0-windows/win-x64/EmojiPicker.Checks.exe
```

For the manual Windows 10 test, Exit the previous tray process first and run
`EmojiPicker/bin/med43/Release/net10.0-windows/win-x64/EmojiPicker.exe`. Keep the
entire self-contained output folder together. The separate output avoids replacing
the executable used by a prior manually launched process.

The bounded lifecycle log remains `%APPDATA%\ClassicEmojiPicker\picker-lifecycle.log`
and now uses the marker `MED-4.3-v1`. Decision records include `state=Unknown`,
`Picker`, `Target` or `Foreign` plus `observedForeground`, the exact handle sampled
for that decision. The general `foreground` field is a separate, later diagnostic
sample and can differ during a transition. No field or clipboard text is logged.

Repeat 50 rapid selections, then test actual outside click and Escape during a
burst. An Unknown postcheck should be followed by picker-focus restoration or a
safe refusal, not the foreign-window dismiss reason. If the issue persists, the
new session trace will distinguish that path from a genuinely foreign handle.

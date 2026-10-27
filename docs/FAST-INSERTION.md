# Fast repeated insertion fix

## Cause found in the code

There was already one sequential insertion consumer, not overlapping inserters.
But the UI thread yields while TextInjector awaits focus settling or clipboard
restoration. A subsequent mouse-down on the inactive picker could activate it
before `Target.HasFocus()` checked the recipient. That check correctly returned
false. MainWindow then reported failure and cleared the remaining choices.
PickerDismissMonitor watched outside clicks and Enter/Escape; it did not prevent
normal activation by a click inside the picker. The window's DragMove and WPF
keyboard-focus requests were additional ways of changing focus during transfer.

This establishes a failure path in the implementation consistent with the manual
report. No runtime trace of the user's failing Windows input was collected;
the final end-to-end confirmation is a manual stress test.

## Narrow changes

- MainWindow handles WM_MOUSEACTIVATE with MA_NOACTIVATE during insertion. Windows
  still delivers the click, so its emoji is queued without activating the panel.
- WPF focus requests and DragMove are suppressed during that same interval.
  The deliberate return of focus to the picker is explicitly permitted; normal
  activation works again when the consumer finishes.
- The existing FIFO drain is isolated in `InsertionQueue` to test actual async
  processing with controlled completion gates. Only one drain can own dequeueing.
- Capacity is 128 pending requests plus at most one in-flight request. Overflow
  rejects the new request and reports it once per busy period; previously accepted
  requests are not overwritten. Within capacity, choices are not coalesced or
  automatically retried, preserving order and avoiding duplicate insertion.

TextInjector and PickerDismissMonitor are unchanged by this fix. Recipient
identity/focus validation, outside-click/Escape cancellation, clipboard sequence
checks, cleanup and history exclusion remain in force. A genuine unsafe recipient
still fails and clears pending work. No new sleeps or timing delays were added.

## Automated evidence

Run the existing regression executable:

```powershell
dotnet run --project tests/EmojiPicker.Checks/EmojiPicker.Checks.csproj -c Release -r win-x64 --self-contained true
```

Stress tests use the production queue and TaskCompletionSource gates, not timed
sleeps. They cover 50 identical and 50 different choices (including arrivals while
an operation is pending), FIFO order, no replay, no parallel consumer, cancellation
through the same cancel/clear protocol as Escape and outside click, bounded
capacity, and a simulated recipient change at the insertion boundary.

WPF checks invoke the real commit handler 50 times and verify the activation-hook
result, focus suppression, deliberate focus return, and idle activation. Existing
search, compiled-XAML and invalid-recipient checks still run.

These are logic/handler tests. The recipient-change and clipboard-cleanup boundaries
are simulated; the tests do not send real Windows input or modify the clipboard.

## Manual confirmation

In Notepad and then a browser text field, rapidly select 🤣 50 times and compare
the actual resulting count. Repeat with different emoji and ZWJ sequences in a
known order. Try mouse clicks and Enter, then Escape/outside click during a burst.
Switch or close the recipient during processing: pending emoji must not be sent
to the new window. Check clipboard restoration with text and an image, including
copying new content during paste restoration. The picker must remain responsive,
and no warning should appear solely because a click inside it arrived mid-transfer.

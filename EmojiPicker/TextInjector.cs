using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmojiPicker
{
    /// <summary>
    /// Types text into the window that was focused before the picker opened,
    /// mimicking the Windows 10 emoji panel's insert behaviour.
    /// </summary>
    internal static class TextInjector
    {
        internal readonly record struct Target(IntPtr Window, IntPtr Focus, uint Process, uint Thread)
        {
            internal static Target Capture(IntPtr window, IntPtr focus)
            {
                var thread = NativeMethods.GetWindowThreadProcessId(window, out var process);
                return new Target(window, focus, process, thread);
            }

            internal bool IsValid()
            {
                if (Window == IntPtr.Zero || Focus == IntPtr.Zero || Process == 0 || Thread == 0
                    || Process == (uint)Environment.ProcessId || !NativeMethods.IsWindow(Window)
                    || !NativeMethods.IsWindow(Focus))
                {
                    return false;
                }
                var thread = NativeMethods.GetWindowThreadProcessId(Window, out var process);
                var focusThread = NativeMethods.GetWindowThreadProcessId(Focus, out var focusProcess);
                return process == Process && thread == Thread && focusProcess == Process
                    && focusThread == Thread && (Focus == Window || NativeMethods.IsChild(Window, Focus));
            }

            internal bool HasFocus()
            {
                if (!IsValid() || NativeMethods.GetForegroundWindow() != Window) { return false; }
                var gui = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
                return NativeMethods.GetGUIThreadInfo(Thread, ref gui) && gui.hwndFocus == Focus;
            }
        }

        // Whether this process runs elevated; injection into an elevated target
        // from a non-elevated process is silently discarded by UIPI, so we
        // detect that case and report failure without changing the clipboard
        private static readonly bool SelfElevated = IsSelfElevated();
        private static Action? pendingClipboardRestore;

        internal static void FinishPendingClipboardRestore()
        {
            var restore = pendingClipboardRestore;
            pendingClipboardRestore = null;
            restore?.Invoke();
        }

        /// <summary>
        /// Returns the control that currently has keyboard focus within
        /// <paramref name="topLevel"/>'s thread (e.g. Explorer's Search or address
        /// edit), or the top-level window itself when it can't be determined.
        /// Captured before the picker opens so focus can be restored on insert.
        /// </summary>
        public static IntPtr GetFocusedControl(IntPtr topLevel)
        {
            if (topLevel == IntPtr.Zero)
            {
                return topLevel;
            }

            var threadId = NativeMethods.GetWindowThreadProcessId(topLevel, out _);
            var gui = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (threadId != 0 && NativeMethods.GetGUIThreadInfo(threadId, ref gui) && gui.hwndFocus != IntPtr.Zero)
            {
                return gui.hwndFocus;
            }

            return topLevel;
        }

        /// <summary>
        /// Screen-coordinate rectangle of the text caret in <paramref name="topLevel"/>'s
        /// thread, when the app exposes one (classic edit controls do; Chromium and
        /// Electron apps publish one for accessibility). Returns false when there is
        /// no system caret, so the caller can fall back to the mouse position.
        /// </summary>
        public static bool TryGetCaretRect(IntPtr topLevel, out System.Drawing.Rectangle rect)
        {
            rect = default;
            if (topLevel == IntPtr.Zero)
            {
                return false;
            }

            var threadId = NativeMethods.GetWindowThreadProcessId(topLevel, out _);
            var gui = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            if (threadId == 0 || !NativeMethods.GetGUIThreadInfo(threadId, ref gui) || gui.hwndCaret == IntPtr.Zero)
            {
                return false;
            }

            // rcCaret is in hwndCaret's client coordinates; convert both corners
            var topLeft = new System.Drawing.Point(gui.rcCaret.Left, gui.rcCaret.Top);
            var bottomRight = new System.Drawing.Point(gui.rcCaret.Right, gui.rcCaret.Bottom);
            if (!NativeMethods.ClientToScreen(gui.hwndCaret, ref topLeft) || !NativeMethods.ClientToScreen(gui.hwndCaret, ref bottomRight))
            {
                return false;
            }

            rect = System.Drawing.Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);

            // A real text caret has a line height. Some apps keep a system caret
            // parked/hidden at client (0,0) with an empty rect even when the visible
            // cursor is elsewhere; treat that as "no caret" so the picker anchors to
            // the mouse instead of the window's top-left corner.
            if (rect.Height <= 0)
            {
                rect = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Restores the captured target and focused control, validating both again
        /// immediately before input. Returns false for an unknown, changed or
        /// inaccessible recipient; the caller reports failure. Must be awaited on
        /// the UI thread. Cancellation prevents pending input, but clipboard
        /// restoration after an already-sent paste always completes.
        /// </summary>
        internal static async Task<bool> TryInsertAsync(Target target, string text, CancellationToken cancellationToken,
            Action<string>? trace = null)
        {
            trace?.Invoke("target-insert-enter");
            cancellationToken.ThrowIfCancellationRequested();
            if (!target.IsValid())
            {
                return false;
            }

            if (!SelfElevated && IsWindowElevated(target.Window))
            {
                Logger.Log("Insert target is elevated; refusing input");
                return false;
            }

            trace?.Invoke("programmatic-target-focus-begin");
            if (!NativeMethods.SetForegroundWindow(target.Window))
            {
                trace?.Invoke("programmatic-target-focus-rejected");
                return false;
            }

            // Restore focus to the exact control that had it; activating our picker
            // moves focus off edits like Explorer's Search box or address bar
            RestoreFocus(target.Window, target.Focus);
            trace?.Invoke("programmatic-target-focus-return");

            // Wait for the target to actually become foreground before typing -
            // usually one or two ticks - rather than a fixed worst-case delay.
            // Awaited (not slept) so the UI thread keeps pumping.
            var waited = 0;
            while (waited < 250 && NativeMethods.GetForegroundWindow() != target.Window)
            {
                await Task.Delay(15, cancellationToken);
                waited += 15;
            }

            // One extra tick for keyboard focus to settle inside the window
            await Task.Delay(15, cancellationToken);
            trace?.Invoke("target-focus-wait-ended");
            Logger.Log($"Insert: target foreground after ~{waited}ms");

            if (!target.HasFocus() || !ModifiersReleased())
            {
                return false; // recipient/focus changed, or a modifier is still held
            }

            // Simple emoji type reliably everywhere; joined graphemes (ZWJ
            // sequences, flags, skin-tone variants) split into pieces when
            // injected as separate synthetic keystrokes in some apps, so those
            // are pasted instead. The mode is user-configurable.
            var mode = Settings.Current.InsertMode;
            var usePaste = mode == EmojiInsertMode.Paste
                || (mode == EmojiInsertMode.Hybrid && IsMultiScalarGrapheme(text));

            if (usePaste)
            {
                return await PasteViaClipboardAsync(target, text, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var sent = target.HasFocus() && SendUnicodeKeystrokes(text);
            // Keep the recipient foreground while its input queue consumes the
            // injected UTF-16 events, before the picker reclaims keyboard focus.
            if (sent) { await Task.Delay(30); }
            return sent;
        }

        private static bool ModifiersReleased()
        {
            foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            {
                if ((NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0) { return false; }
            }
            return true;
        }

        private static bool SendUnicodeKeystrokes(string text)
        {
            // All key-downs first, then all key-ups: the two halves of a surrogate
            // pair must produce consecutive WM_CHAR messages or the receiving edit
            // control shows two broken characters instead of one emoji
            var inputs = new NativeMethods.INPUT[text.Length * 2];
            for (int i = 0; i < text.Length; i++)
            {
                inputs[i] = UnicodeKeyEvent(text[i], keyUp: false);
                inputs[text.Length + i] = UnicodeKeyEvent(text[i], keyUp: true);
            }

            return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) == (uint)inputs.Length;
        }

        /// <summary>
        /// Places <paramref name="text"/> on the clipboard, sends Ctrl+V so the
        /// target composes it as one string (which synthetic keystrokes fail to do
        /// for joined emoji in some apps), then restores the previous clipboard
        /// content. The full clipboard (all formats, incl. images/files) is
        /// snapshotted and restored, and both writes are tagged to stay out of
        /// Clipboard History (Win+V), Cloud Clipboard and third-party monitors, so
        /// the transient paste neither destroys existing content nor pollutes the
        /// history stack.
        /// </summary>
        private static async Task<bool> PasteViaClipboardAsync(Target target, string text, CancellationToken cancellationToken)
        {
            // Snapshot the ENTIRE clipboard (all formats) before overwriting, so a
            // copied image / file selection isn't destroyed by the paste. Grab the
            // text separately as a guaranteed-restorable fallback.
            var previous = CaptureClipboard();
            var previousText = TryGetText(previous);

            // Never replace clipboard data if it could not be fully snapshotted.
            if (previous == null || !target.HasFocus() || !ModifiersReleased() || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!SetClipboardTextExcluded(text))
            {
                return false;
            }

            var sequenceAfterSet = NativeMethods.GetClipboardSequenceNumber();
            Action restore = () =>
            {
                // Never overwrite a new user copy, including during app exit.
                if (NativeMethods.GetClipboardSequenceNumber() == sequenceAfterSet
                    && !RestoreClipboard(previous) && previousText != null)
                {
                    SetClipboardTextExcluded(previousText);
                }
            };
            pendingClipboardRestore = restore;
            try
            {
                var sent = !cancellationToken.IsCancellationRequested && target.HasFocus()
                    && ModifiersReleased() && SendCtrlV();
                // Restoration must finish even after dismissal/cancellation.
                // Slow/remote recipients can require a longer configurable delay.
                var delay = Math.Clamp(Settings.Current.PasteRestoreDelayMs, 50, 5000);
                await Task.Delay(delay);
                Logger.Log($"Paste: input accepted={sent}, restore after {delay}ms");
                return sent;
            }
            finally
            {
                if (ReferenceEquals(pendingClipboardRestore, restore))
                {
                    FinishPendingClipboardRestore();
                }
            }
        }

        /// <summary>
        /// Puts text on the clipboard tagged so Clipboard History (Win+V), Cloud
        /// Clipboard, and cooperating clipboard monitors ignore it.
        /// Returns false on failure.
        /// </summary>
        public static bool SetClipboardTextExcluded(string text)
        {
            try
            {
                var data = new System.Windows.DataObject();
                data.SetText(text);
                AddHistoryExclusion(data);
                System.Windows.Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Clipboard set failed ({ex.Message})");
                return false;
            }
        }

        /// <summary>
        /// Copies every native clipboard format into a detached DataObject while the
        /// clipboard is still intact, so it can be restored after the paste. Returns
        /// null if any format cannot be read; an empty clipboard has an empty snapshot.
        /// </summary>
        private static System.Windows.IDataObject? CaptureClipboard()
        {
            try
            {
                var current = System.Windows.Clipboard.GetDataObject();
                if (current == null)
                {
                    return new System.Windows.DataObject();
                }

                var snapshot = new System.Windows.DataObject();
                foreach (var format in current.GetFormats(autoConvert: false))
                {
                    try
                    {
                        var data = current.GetData(format, autoConvert: false);
                        if (data != null)
                        {
                            snapshot.SetData(format, data);
                        }
                        else { return null; }
                    }
                    catch (Exception)
                    {
                        return null; // don't destroy formats we could not snapshot
                    }
                }

                return snapshot;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string? TryGetText(System.Windows.IDataObject? snapshot)
        {
            try
            {
                if (snapshot != null && snapshot.GetDataPresent(System.Windows.DataFormats.UnicodeText))
                {
                    return snapshot.GetData(System.Windows.DataFormats.UnicodeText) as string;
                }
            }
            catch (Exception)
            {
                // No usable text; the fallback simply won't run
            }

            return null;
        }

        /// <summary>
        /// Restores a captured snapshot to the clipboard. Returns false if the whole
        /// restore failed (e.g. a handle-backed format went stale), so the caller can
        /// fall back to restoring the plain text.
        /// </summary>
        private static bool RestoreClipboard(System.Windows.IDataObject snapshot)
        {
            try
            {
                if (snapshot is System.Windows.DataObject data)
                {
                    // History-exclude the restore too, so putting the user's own
                    // content back doesn't add a duplicate Win+V entry.
                    AddHistoryExclusion(data);
                    System.Windows.Clipboard.SetDataObject(data, copy: true);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"Paste: full clipboard restore failed ({ex.Message}); trying text");
                return false;
            }
        }

        /// <summary>
        /// Tags a DataObject so Clipboard History, Cloud Clipboard, and clipboard
        /// monitors ignore it. Value 0 (DWORD) = exclude.
        /// </summary>
        private static void AddHistoryExclusion(System.Windows.DataObject data)
        {
            var excludeDword = new byte[] { 0, 0, 0, 0 };
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(excludeDword));
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(excludeDword));
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[] { 0 }));
        }

        /// <summary>
        /// True when the emoji is a joined grapheme cluster - more than one Unicode
        /// scalar once a trailing emoji-presentation selector (VS16) is discounted.
        /// Covers ZWJ sequences, flags, keycaps and skin-tone variants; false for a
        /// plain single emoji (with or without VS16).
        /// </summary>
        private static bool IsMultiScalarGrapheme(string text)
        {
            var count = 0;
            var lastValue = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                count++;
                lastValue = rune.Value;
            }

            if (lastValue == 0xFE0F)
            {
                count--; // a lone trailing VS16 just requests emoji presentation
            }

            return count > 1;
        }

        private static bool SendCtrlV()
        {
            var inputs = new[]
            {
                VirtualKeyEvent(VkControl, keyUp: false),
                VirtualKeyEvent(VkV, keyUp: false),
                VirtualKeyEvent(VkV, keyUp: true),
                VirtualKeyEvent(VkControl, keyUp: true),
            };
            return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) == (uint)inputs.Length;
        }

        private const ushort VkControl = 0x11;
        private const ushort VkV = 0x56;

        private static NativeMethods.INPUT VirtualKeyEvent(ushort virtualKey, bool keyUp)
        {
            return new NativeMethods.INPUT
            {
                type = NativeMethods.InputKeyboard,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = virtualKey,
                        dwFlags = keyUp ? NativeMethods.KeyEventKeyUp : 0,
                    },
                },
            };
        }

        private static void RestoreFocus(IntPtr targetWindow, IntPtr focusWindow)
        {
            if (focusWindow == IntPtr.Zero || focusWindow == targetWindow || !NativeMethods.IsWindow(focusWindow))
            {
                return;
            }

            var targetThread = NativeMethods.GetWindowThreadProcessId(targetWindow, out _);
            var thisThread = NativeMethods.GetCurrentThreadId();

            // Focus is per input-queue; attach to the target thread so SetFocus takes
            if (targetThread != 0 && targetThread != thisThread && NativeMethods.AttachThreadInput(thisThread, targetThread, true))
            {
                NativeMethods.SetFocus(focusWindow);
                NativeMethods.AttachThreadInput(thisThread, targetThread, false);
            }
            else
            {
                NativeMethods.SetFocus(focusWindow);
            }
        }

        private static bool IsSelfElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// True when the window's owning process runs elevated (or is so protected
        /// we can't even query it, which implies the same for injection purposes).
        /// </summary>
        private static bool IsWindowElevated(IntPtr window)
        {
            NativeMethods.GetWindowThreadProcessId(window, out var pid);
            if (pid == 0)
            {
                return true;
            }

            var process = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
            if (process == IntPtr.Zero)
            {
                return true; // can't query at all -> treat as protected
            }

            try
            {
                if (!NativeMethods.OpenProcessToken(process, NativeMethods.TokenQuery, out var token))
                {
                    return true;
                }

                try
                {
                    if (NativeMethods.GetTokenInformation(token, NativeMethods.TokenElevation,
                        out var elevated, sizeof(int), out _))
                    {
                        return elevated != 0;
                    }

                    return true;
                }
                finally
                {
                    NativeMethods.CloseHandle(token);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(process);
            }
        }

        private static NativeMethods.INPUT UnicodeKeyEvent(char codeUnit, bool keyUp)
        {
            return new NativeMethods.INPUT
            {
                type = NativeMethods.InputKeyboard,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wScan = codeUnit,
                        dwFlags = NativeMethods.KeyEventUnicode | (keyUp ? NativeMethods.KeyEventKeyUp : 0),
                    },
                },
            };
        }
    }
}

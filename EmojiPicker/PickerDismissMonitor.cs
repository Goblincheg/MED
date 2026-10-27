using System;
using System.Runtime.InteropServices;

namespace EmojiPicker
{
    // Only installed while the picker is open. Outside clicks must cancel even
    // while the target already owns focus during an asynchronous paste.
    internal sealed class PickerDismissMonitor : IDisposable
    {
        private readonly NativeMethods.LowLevelKeyboardProc mouseProc;
        private readonly NativeMethods.LowLevelKeyboardProc keyProc;
        private readonly IntPtr pickerWindow;
        private readonly Action<string> dismiss;
        private readonly Action<string>? trace;
        private readonly Action commit;
        private readonly Func<bool> inserting;
        private IntPtr mouseHook;
        private IntPtr keyHook;
        private bool enterHeld;
        private bool enterForwarded;
        private bool escapeHeld;

        internal PickerDismissMonitor(IntPtr pickerWindow, Action<string> dismiss, Action commit, Func<bool> inserting,
            Action<string>? trace = null)
        {
            this.pickerWindow = pickerWindow;
            this.dismiss = dismiss;
            this.trace = trace;
            this.commit = commit;
            this.inserting = inserting;
            mouseProc = MouseCallback;
            keyProc = KeyCallback;
            mouseHook = NativeMethods.SetWindowsHookEx(14, mouseProc, IntPtr.Zero, 0);
            keyHook = NativeMethods.SetWindowsHookEx(13, keyProc, IntPtr.Zero, 0);
            if (mouseHook == IntPtr.Zero || keyHook == IntPtr.Zero)
            {
                Dispose();
                throw new InvalidOperationException("Could not monitor outside clicks and Escape safely.");
            }
        }

        private IntPtr MouseCallback(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && (message.ToInt64() is 0x0201 or 0x0204 or 0x0207 or 0x020B))
            {
                var point = Marshal.PtrToStructure<NativeMethods.POINT>(data);
                var clicked = NativeMethods.WindowFromPoint(point);
                var root = NativeMethods.GetAncestor(clicked, 2);
                var boundsKnown = NativeMethods.GetWindowRect(pickerWindow, out var bounds);
                trace?.Invoke($"mouse-down message={message.ToInt64():X} x={point.X} y={point.Y} hit={clicked} root={root} " +
                    $"outside={root != pickerWindow} boundsKnown={boundsKnown} bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom} " +
                    $"mouseFlags={Marshal.ReadInt32(data, 12)}");
                if (root != pickerWindow)
                {
                    dismiss("outside-click"); // cancels synchronously; hiding is dispatched
                }
            }
            return NativeMethods.CallNextHookEx(mouseHook, code, message, data);
        }

        private IntPtr KeyCallback(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && (Marshal.ReadInt32(data, 8) & 0x10) == 0)
            {
                var key = Marshal.ReadInt32(data);
                var down = message.ToInt64() is 0x0100 or 0x0104;
                var up = message.ToInt64() is 0x0101 or 0x0105;
                if (key == 0x1B && down)
                {
                    escapeHeld = true;
                    dismiss("escape-hook");
                    return new IntPtr(1);
                }
                if (key == 0x1B && up && escapeHeld)
                {
                    escapeHeld = false;
                    return new IntPtr(1);
                }
                // Enter pressed during focus transfer belongs to the picker,
                // never to the target edit (where it would insert a newline).
                if (key == 0x0D && down && inserting())
                {
                    enterHeld = !enterForwarded;
                    commit();
                    return new IntPtr(1);
                }
                if (key == 0x0D && down)
                {
                    enterForwarded = true;
                    enterHeld = false;
                }
                if (key == 0x0D && up)
                {
                    var swallow = enterHeld && !enterForwarded;
                    enterHeld = false;
                    enterForwarded = false;
                    if (swallow) { return new IntPtr(1); }
                }
            }
            return NativeMethods.CallNextHookEx(keyHook, code, message, data);
        }

        public void Dispose()
        {
            if (mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            if (keyHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(keyHook); keyHook = IntPtr.Zero; }
        }
    }
}

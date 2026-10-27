using System;

namespace EmojiPicker
{
    internal enum ForegroundState
    {
        Unknown,
        Picker,
        Target,
        Foreign,
    }

    internal static class ForegroundPolicy
    {
        // NULL is a documented activation-transition state, not another window.
        // This classification only controls lifecycle/activation attempts. It
        // never authorises SendInput: TextInjector must still validate HasFocus.
        internal static ForegroundState Classify(IntPtr foreground, IntPtr picker, IntPtr target)
        {
            if (foreground == IntPtr.Zero) { return ForegroundState.Unknown; }
            if (foreground == picker) { return ForegroundState.Picker; }
            if (foreground == target) { return ForegroundState.Target; }
            return ForegroundState.Foreign;
        }

        internal static bool MayRestoreTarget(ForegroundState state) => state != ForegroundState.Foreign;
        internal static bool ShouldCloseAfterInsert(ForegroundState state) => state == ForegroundState.Foreign;
        internal static bool ShouldCloseWhenIdle(ForegroundState state) =>
            state == ForegroundState.Target || state == ForegroundState.Foreign;
    }
}

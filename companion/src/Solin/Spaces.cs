// Solin-owned; not derived from upstream.
using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Solin.Companion
{
    // Makes a window live on every macOS Space, above full-screen apps, so she follows when you swipe between desktops.
    internal static class Spaces
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern long GetNumber(IntPtr target, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SetNumber(IntPtr target, IntPtr selector, long value);

        private const long CanJoinAllSpaces = 1, MoveToActiveSpace = 2, Stationary = 16, FullScreenPrimary = 128, FullScreenAuxiliary = 256, FullScreenDisallowsTiling = 2048;
        private const long StatusLevel = 25;

        // Applies once the native window exists; safe to call before it is shown.
        internal static void Everywhere(Window window)
        {
            if (!OperatingSystem.IsMacOS()) return;
            void Apply()
            {
                try
                {
                    IntPtr ns = window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle mac ? mac.NSWindow : IntPtr.Zero;
                    if (ns == IntPtr.Zero) return;
                    long behaviour = GetNumber(ns, sel_registerName("collectionBehavior"));
                    behaviour = behaviour & ~(MoveToActiveSpace | FullScreenPrimary) | CanJoinAllSpaces | FullScreenAuxiliary | FullScreenDisallowsTiling;
                    SetNumber(ns, sel_registerName("setCollectionBehavior:"), behaviour);
                    long level = GetNumber(ns, sel_registerName("level"));
                    if (level < StatusLevel) SetNumber(ns, sel_registerName("setLevel:"), StatusLevel);
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOLIN_DEBUG_WINDOWS")))
                        Console.WriteLine("[spaces] " + window.Title + " behaviour " + GetNumber(ns, sel_registerName("collectionBehavior")) + " level " + GetNumber(ns, sel_registerName("level")));
                }
                catch (Exception) { }
            }
            window.Opened += delegate { Apply(); };
            if (window.IsVisible) Apply();
        }
    }
}

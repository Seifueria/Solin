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

        // Level and behaviour as the window server sees them now, for checking nothing has reset them.
        internal static string Describe(Window window)
        {
            if (!OperatingSystem.IsMacOS()) return "n/a";
            IntPtr ns = window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle mac ? mac.NSWindow : IntPtr.Zero;
            if (ns == IntPtr.Zero) return "no window";
            IntPtr app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            return "appActive " + GetNumber(app, sel_registerName("isActive")) + " canKey " + GetNumber(ns, sel_registerName("canBecomeKeyWindow")) + " key " + GetNumber(ns, sel_registerName("isKeyWindow")) + " level " + GetNumber(ns, sel_registerName("level")) + " behaviour " + GetNumber(ns, sel_registerName("collectionBehavior")) + " visible " + GetNumber(ns, sel_registerName("isVisible")) + " onActiveSpace " + GetNumber(ns, sel_registerName("isOnActiveSpace"));
        }

        private const string SkyLight = "/System/Library/PrivateFrameworks/SkyLight.framework/SkyLight";
        [DllImport(SkyLight)] private static extern int SLSMainConnectionID();
        [DllImport(SkyLight)] private static extern ulong SLSGetActiveSpace(int connection);
        [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr target, IntPtr selector);
        private static int connection = -1;

        // The number of the desktop on screen now, or 0 if it cannot be read. Changes the instant you swipe.
        internal static ulong ActiveSpace()
        {
            if (!OperatingSystem.IsMacOS()) return 0;
            try
            {
                if (connection < 0) connection = SLSMainConnectionID();
                return SLSGetActiveSpace(connection);
            }
            catch (Exception) { return 0; }
        }

        // Runs her as a background agent app: no Dock icon, and the only kind of app macOS lets draw over another app's
        // full-screen desktop.
        internal static void BeAgent()
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                IntPtr app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                if (app != IntPtr.Zero) SetNumber(app, sel_registerName("setActivationPolicy:"), 1);
            }
            catch (Exception) { }
        }

        private static IntPtr Native(Window window)
        {
            try { return window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle mac ? mac.NSWindow : IntPtr.Zero; }
            catch (Exception) { return IntPtr.Zero; }
        }

        // Puts the flags on now and again whenever it opens: set before the window is first on screen, macOS honours them
        // at once; set afterwards, only once the window is re-ordered.
        internal static void Everywhere(Window window)
        {
            if (!OperatingSystem.IsMacOS()) return;
            Reassert(window);
            window.Opened += delegate { Reassert(window); Front(window); };
        }

        // Joins every Space, sits over full-screen apps, at the status level. Cheap; safe to call often.
        internal static void Reassert(Window window)
        {
            IntPtr ns = Native(window);
            if (ns == IntPtr.Zero) return;
            try
            {
                long behaviour = GetNumber(ns, sel_registerName("collectionBehavior"));
                long want = behaviour & ~(MoveToActiveSpace | FullScreenPrimary) | CanJoinAllSpaces | FullScreenAuxiliary | FullScreenDisallowsTiling;
                if (want != behaviour) SetNumber(ns, sel_registerName("setCollectionBehavior:"), want);
                if (GetNumber(ns, sel_registerName("level")) < StatusLevel) SetNumber(ns, sel_registerName("setLevel:"), StatusLevel);
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SOLIN_DEBUG_WINDOWS")))
                    Console.WriteLine("[spaces] " + window.Title + " behaviour " + GetNumber(ns, sel_registerName("collectionBehavior")) + " level " + GetNumber(ns, sel_registerName("level")));
            }
            catch (Exception) { }
        }

        // To the front of its level without taking focus from whatever you are typing in.
        internal static void Front(Window window)
        {
            if (!OperatingSystem.IsMacOS() || !window.IsVisible) return;
            IntPtr ns = Native(window);
            if (ns == IntPtr.Zero) return;
            try { SetNumber(ns, sel_registerName("orderFrontRegardless"), 0); } catch (Exception) { }
        }

        // Brings this background app forward and makes the window key, so a text box in it gets the keyboard.
        internal static void TakeKeyboard(Window window)
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                IntPtr app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                if (app != IntPtr.Zero) SetNumber(app, sel_registerName("activateIgnoringOtherApps:"), 1);
                IntPtr ns = Native(window);
                if (ns != IntPtr.Zero) SetNumber(ns, sel_registerName("makeKeyAndOrderFront:"), 0);
            }
            catch (Exception) { }
        }

        // Clicks go straight through to whatever is underneath: for overlays that only draw.
        internal static void ClickThrough(Window window)
        {
            if (!OperatingSystem.IsMacOS()) return;
            void Apply() { IntPtr ns = Native(window); if (ns != IntPtr.Zero) try { SetNumber(ns, sel_registerName("setIgnoresMouseEvents:"), 1); } catch (Exception) { } }
            Apply();
            window.Opened += delegate { Apply(); };
        }
    }
}

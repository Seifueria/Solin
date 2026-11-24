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

        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        [DllImport(SkyLight)] private static extern int SLSMoveWindowsToManagedSpace(int connection, IntPtr windows, ulong space);
        [DllImport(CoreFoundation)] private static extern IntPtr CFNumberCreate(IntPtr allocator, long type, ref long value);
        [DllImport(CoreFoundation)] private static extern IntPtr CFArrayCreate(IntPtr allocator, IntPtr[] values, long count, IntPtr callbacks);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr thing);

        // Asks the window server to put the window on a given desktop.
        internal static void MoveTo(Window window, ulong space)
        {
            IntPtr ns = Native(window);
            if (ns == IntPtr.Zero) return;
            try
            {
                long number = GetNumber(ns, sel_registerName("windowNumber"));
                IntPtr boxed = CFNumberCreate(IntPtr.Zero, 4, ref number);
                IntPtr callbacks = NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), "kCFTypeArrayCallBacks");
                IntPtr list = CFArrayCreate(IntPtr.Zero, new[] { boxed }, 1, callbacks);
                SLSMoveWindowsToManagedSpace(connection < 0 ? (connection = SLSMainConnectionID()) : connection, list, space);
                CFRelease(list); CFRelease(boxed);
            }
            catch (Exception) { }
        }

        internal static bool OnActiveSpace(Window window)
        {
            IntPtr ns = Native(window);
            try { return ns != IntPtr.Zero && GetNumber(ns, sel_registerName("isOnActiveSpace")) != 0; } catch (Exception) { return true; }
        }

        // Carries a one-desktop window to the desktop on screen now. Tries the window server's own move, then (if it has not
        // taken a beat later) the join-all-and-leave trick; if neither works it gives up on one-desktop mode for that window,
        // so she is never stranded on a desktop you have left. done: told how it went, for debugging.
        internal static void ToActiveSpace(Window window, Action<string> done = null)
        {
            if (!OperatingSystem.IsMacOS() || !oneDesktop.Contains(window)) { done?.Invoke("n/a"); return; }
            if (Native(window) == IntPtr.Zero) { done?.Invoke("no window"); return; }
            if (OnActiveSpace(window)) { done?.Invoke("already"); return; }
            ulong space = ActiveSpace();
            if (space != 0) MoveTo(window, space);
            Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (OnActiveSpace(window)) { done?.Invoke("moved"); return; }
                IntPtr ns = Native(window);
                try
                {
                    long behaviour = GetNumber(ns, sel_registerName("collectionBehavior"));
                    SetNumber(ns, sel_registerName("setCollectionBehavior:"), behaviour | CanJoinAllSpaces);
                    SetNumber(ns, sel_registerName("orderFrontRegardless"), 0);
                    SetNumber(ns, sel_registerName("setCollectionBehavior:"), behaviour & ~CanJoinAllSpaces);
                }
                catch (Exception) { }
                Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                {
                    if (OnActiveSpace(window)) { done?.Invoke("rejoined"); return; }
                    oneDesktop.Remove(window);
                    Reassert(window);
                    done?.Invoke("gave up: on every desktop again");
                }, TimeSpan.FromMilliseconds(120));
            }, TimeSpan.FromMilliseconds(120));
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

        // The window server's number for a window, or 0.
        internal static long Number(Window window)
        {
            IntPtr ns = Native(window);
            try { return ns == IntPtr.Zero ? 0 : GetNumber(ns, sel_registerName("windowNumber")); } catch (Exception) { return 0; }
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
            // On the desktop you are on, carried to the next one: windows on "every desktop" are not put on a full-screen
            // app's space, which is why she could not be seen over full-screen apps.
            oneDesktop.Add(window);
            Reassert(window);
            window.Opened += delegate { Reassert(window); Front(window); };
            // Shown again later (a card, her bubble): onto the desktop you are on now, before it is seen elsewhere.
            window.PropertyChanged += delegate(object sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Avalonia.Visual.IsVisibleProperty && window.IsVisible && oneDesktop.Contains(window) && !OnActiveSpace(window))
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => Front(window));
            };
        }

        // Windows that live on one desktop at a time and are carried over on a swipe (her body), rather than being on all of
        // them at once: on a swipe she slides away with the old desktop, so the new one is empty until she arrives.
        private static readonly System.Collections.Generic.HashSet<Window> oneDesktop = new System.Collections.Generic.HashSet<Window>();
        internal static void OneDesktop(Window window) { oneDesktop.Add(window); Reassert(window); }
        internal static bool IsOneDesktop(Window window) { return oneDesktop.Contains(window); }

        // Joins every Space (or, for one-desktop windows, just this one), sits over full-screen apps, at the status level.
        // Cheap; safe to call often.
        internal static void Reassert(Window window)
        {
            IntPtr ns = Native(window);
            if (ns == IntPtr.Zero) return;
            try
            {
                long behaviour = GetNumber(ns, sel_registerName("collectionBehavior"));
                long want = oneDesktop.Contains(window)
                    ? behaviour & ~(MoveToActiveSpace | FullScreenPrimary | CanJoinAllSpaces) | FullScreenAuxiliary | FullScreenDisallowsTiling
                    : behaviour & ~(MoveToActiveSpace | FullScreenPrimary) | CanJoinAllSpaces | FullScreenAuxiliary | FullScreenDisallowsTiling;
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
            // A one-desktop window left on another desktop is carried here first.
            if (oneDesktop.Contains(window) && GetNumber(ns, sel_registerName("isOnActiveSpace")) == 0) { ulong here = ActiveSpace(); if (here != 0) MoveTo(window, here); }
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

        // Clicks through or not, switched while it is open: through her empty space, not her art.
        internal static void IgnoreMouse(Window window, bool through)
        {
            if (!OperatingSystem.IsMacOS()) return;
            IntPtr ns = Native(window);
            if (ns != IntPtr.Zero) try { SetNumber(ns, sel_registerName("setIgnoresMouseEvents:"), through ? 1 : 0); } catch (Exception) { }
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

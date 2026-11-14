// Derived from cross/Desk.cs (class MacDesk) in lachydotmcg/capri-desktop, commit 26224db. PolyForm Noncommercial 1.0.0.
// Trimmed for Solin: lists other apps' windows on screen; no moving them, no pointer (see Solin/Mouse.cs).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;

namespace Solin.Companion.Upstream
{
    // One window of some other app: its CoreGraphics number and where it is, in screen pixels.
    internal sealed class Spot
    {
        internal long Window;
        internal PixelRect Bounds;
        internal int Pid;
        internal string Owner;
    }

    internal sealed class MacDesk
    {
        private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const uint OnScreenOnly = 1, IncludingWindow = 8, ExcludeDesktop = 16;
        [StructLayout(LayoutKind.Sequential)] private struct Area { internal double X, Y, Width, Height; }
        [DllImport(Graphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeTo);
        [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dictionary, out Area rect);
        [DllImport(Foundation)] private static extern long CFArrayGetCount(IntPtr array);
        [DllImport(Foundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
        [DllImport(Foundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
        [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, long type, out long value);
        [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, long type, out double value);
        [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFBooleanGetValue(IntPtr boolean);
        [DllImport(Foundation)] private static extern void CFRelease(IntPtr value);
        [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFStringGetCString(IntPtr str, byte[] buffer, long size, uint encoding);
        private const uint Utf8 = 0x08000100;
        private const long SInt64Type = 4, DoubleType = 13;

        private readonly IntPtr number, layer, frame, owner, alpha, onscreen, ownerName;
        // Another copy of her, or her backend's windows, are never a seat.
        private static readonly string[] Ours = { "Solin.Companion", "Solin" };
        private readonly long self = Environment.ProcessId;
        // Screen pixels per CoreGraphics point.
        internal double Scale = 1;

        internal MacDesk()
        {
            IntPtr library = NativeLibrary.Load(Graphics);
            number = Key(library, "kCGWindowNumber");
            layer = Key(library, "kCGWindowLayer");
            frame = Key(library, "kCGWindowBounds");
            owner = Key(library, "kCGWindowOwnerPID");
            alpha = Key(library, "kCGWindowAlpha");
            onscreen = Key(library, "kCGWindowIsOnscreen");
            ownerName = Key(library, "kCGWindowOwnerName");
        }

        private static string Text(IntPtr info, IntPtr key)
        {
            IntPtr value = CFDictionaryGetValue(info, key);
            if (value == IntPtr.Zero) return "";
            byte[] buffer = new byte[256];
            return CFStringGetCString(value, buffer, buffer.Length, Utf8) ? System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0') : "";
        }

        private static IntPtr Key(IntPtr library, string name) { return Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name)); }

        // Every other app's window on screen, front to back.
        internal List<Spot> Windows() { return List(OnScreenOnly | ExcludeDesktop, 0, true); }

        // Every entry CoreGraphics returns, unfiltered, one per line: for finding out why a window is not a seat.
        internal string Dump()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            IntPtr array = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktop, 0);
            if (array == IntPtr.Zero) return "(no list)";
            try
            {
                long count = CFArrayGetCount(array);
                for (long i = 0; i < count; i++)
                {
                    IntPtr info = CFArrayGetValueAtIndex(array, i);
                    IntPtr shown = CFDictionaryGetValue(info, onscreen);
                    IntPtr box = CFDictionaryGetValue(info, frame);
                    Area area = default;
                    bool hasBox = box != IntPtr.Zero && CGRectMakeWithDictionaryRepresentation(box, out area);
                    sb.Append("\n  #" + Whole(info, number) + " " + Text(info, ownerName) + " layer " + Whole(info, layer) + " on " + (shown != IntPtr.Zero && CFBooleanGetValue(shown)) + " alpha " + Real(info, alpha) + (hasBox ? " " + area.X + "," + area.Y + " " + area.Width + "x" + area.Height : " nobox"));
                }
            }
            finally { CFRelease(array); }
            return sb.ToString();
        }

        // Where one window is now, or null when it has gone or is hidden.
        internal Spot Find(long window)
        {
            foreach (Spot spot in List(IncludingWindow, (uint)window, true)) if (spot.Window == window) return spot;
            return null;
        }

        private List<Spot> List(uint option, uint relative, bool showing)
        {
            List<Spot> found = new List<Spot>();
            IntPtr array = CGWindowListCopyWindowInfo(option, relative);
            if (array == IntPtr.Zero) return found;
            try
            {
                long count = CFArrayGetCount(array);
                for (long i = 0; i < count; i++)
                {
                    IntPtr info = CFArrayGetValueAtIndex(array, i);
                    long id = Whole(info, number);
                    long level = Whole(info, layer);
                    if (level < 0 || level > 3 || Whole(info, owner) == self || Array.IndexOf(Ours, Text(info, ownerName)) >= 0) continue;
                    IntPtr shown = CFDictionaryGetValue(info, onscreen);
                    if (showing && (shown == IntPtr.Zero || !CFBooleanGetValue(shown) || Real(info, alpha) <= 0.05)) continue;
                    IntPtr box = CFDictionaryGetValue(info, frame);
                    if (box == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(box, out Area area)) continue;
                    PixelRect bounds = new PixelRect((int)Math.Round(area.X * Scale), (int)Math.Round(area.Y * Scale), (int)Math.Round(area.Width * Scale), (int)Math.Round(area.Height * Scale));
                    // Menu bar items, tooltips and other slivers are no seat.
                    if (bounds.Width < 120 || bounds.Height < 60) continue;
                    found.Add(new Spot { Window = id, Bounds = bounds, Pid = (int)Whole(info, owner), Owner = Text(info, ownerName) });
                }
            }
            finally { CFRelease(array); }
            return found;
        }

        private static long Whole(IntPtr info, IntPtr key)
        {
            IntPtr value = CFDictionaryGetValue(info, key);
            return value != IntPtr.Zero && CFNumberGetValue(value, SInt64Type, out long whole) ? whole : -1;
        }

        private static double Real(IntPtr info, IntPtr key)
        {
            IntPtr value = CFDictionaryGetValue(info, key);
            return value != IntPtr.Zero && CFNumberGetValue(value, DoubleType, out double real) ? real : 1;
        }
    }
}

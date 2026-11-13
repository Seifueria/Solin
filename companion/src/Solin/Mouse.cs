// Solin-owned; not derived from upstream.
using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace Solin.Companion
{
    // Where the cursor is on screen, even when it is nowhere near her window. macOS only; elsewhere returns null.
    internal static class Mouse
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint { public double X, Y; }

        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr evt);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr cf);
        [DllImport(CoreGraphics)] private static extern double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);
        private const int CombinedSessionState = 1;
        private const uint AnyInput = 0xFFFFFFFF;

        // Seconds since the last key, click or mouse move anywhere on the Mac; null where it cannot be read.
        internal static double? IdleSeconds()
        {
            if (!OperatingSystem.IsMacOS()) return null;
            try { return CGEventSourceSecondsSinceLastEventType(CombinedSessionState, AnyInput); }
            catch (Exception) { return null; }
        }

        // In screen pixels, the same space as a window's Position; scaling maps CoreGraphics points onto it.
        internal static PixelPoint? Position(double scaling)
        {
            if (!OperatingSystem.IsMacOS()) return null;
            IntPtr evt = IntPtr.Zero;
            try
            {
                evt = CGEventCreate(IntPtr.Zero);
                if (evt == IntPtr.Zero) return null;
                CGPoint at = CGEventGetLocation(evt);
                return new PixelPoint((int)Math.Round(at.X * scaling), (int)Math.Round(at.Y * scaling));
            }
            catch (Exception) { return null; }
            finally { if (evt != IntPtr.Zero) CFRelease(evt); }
        }
    }
}

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

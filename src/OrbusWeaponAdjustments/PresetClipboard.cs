using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OrbusWeaponAdjustments;

/// <summary>
/// Windows CF_UNICODETEXT clipboard bridge for an explicit VR UI click.
/// No external executable, browser, download, network request or shared DLL.
/// All clipboard access is user-initiated and bounded.
/// </summary>
internal static class PresetClipboard
{
    private const uint GmemMoveable = 0x0002;
    private const uint CfUnicodeText = 13;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);

    internal static bool Copy(string code, out string message)
    {
        message = "The Windows clipboard is unavailable. Try again.";
        if (!OperatingSystem.IsWindows()) return false;
        if (code.Length is < 1 or > 160) return false;
        IntPtr allocated = IntPtr.Zero;
        bool opened = false;
        try
        {
            // After EmptyClipboard, Windows requires a real window owner
            // for SetClipboardData. Opening with NULL silently breaks Copy.
            IntPtr owner = Process.GetCurrentProcess().MainWindowHandle;
            if (owner == IntPtr.Zero) owner = GetActiveWindow();
            if (owner == IntPtr.Zero) owner = GetForegroundWindow();
            if (owner == IntPtr.Zero)
            {
                message = "Orbus's window isn't ready for clipboard copy.";
                return false;
            }
            opened = OpenClipboard(owner);
            if (!opened)
            {
                message = "Clipboard is busy. Close any clipboard manager and retry.";
                return false;
            }
            byte[] bytes = Encoding.Unicode.GetBytes(code + "\0");
            allocated = GlobalAlloc(GmemMoveable, new UIntPtr((uint)bytes.Length));
            if (allocated == IntPtr.Zero) return false;
            IntPtr target = GlobalLock(allocated);
            if (target == IntPtr.Zero) return false;
            try { Marshal.Copy(bytes, 0, target, bytes.Length); }
            finally { GlobalUnlock(allocated); }
            if (!EmptyClipboard()) return false;
            if (SetClipboardData(CfUnicodeText, allocated) == IntPtr.Zero) return false;
            allocated = IntPtr.Zero; // Windows now owns the handle.
            message = "Share code copied! Paste it into Discord or a message.";
            return true;
        }
        catch (Exception)
        {
            message = "Windows couldn't copy the code. The code is shown below.";
            return false;
        }
        finally
        {
            if (allocated != IntPtr.Zero) GlobalFree(allocated);
            if (opened) CloseClipboard();
        }
    }

    internal static bool Paste(out string text, out string message)
    {
        text = "";
        message = "Couldn't read the Windows clipboard.";
        if (!OperatingSystem.IsWindows()) return false;
        bool opened = false;
        try
        {
            opened = OpenClipboard(IntPtr.Zero);
            if (!opened) { message = "Clipboard is busy. Try again."; return false; }
            IntPtr handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero)
            {
                message = "Copy an OW1 share code first, then select Import Code.";
                return false;
            }
            ulong length = GlobalSize(handle).ToUInt64();
            if (length < 2 || length > 8192)
            {
                message = "Clipboard text is too large for a preset code.";
                return false;
            }
            IntPtr pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return false;
            try
            {
                int count = (int)(length / 2);
                text = (Marshal.PtrToStringUni(pointer, count) ?? "").Split('\0')[0];
            }
            finally { GlobalUnlock(handle); }
            if (text.Length > 160)
            {
                text = "";
                message = "Clipboard text is too large for a preset code.";
                return false;
            }
            message = "";
            return true;
        }
        catch (Exception)
        {
            message = "Windows clipboard couldn't be read. Copy the code and try again.";
            return false;
        }
        finally { if (opened) CloseClipboard(); }
    }
}

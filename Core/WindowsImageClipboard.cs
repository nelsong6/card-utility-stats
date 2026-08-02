using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;

namespace SpireLens.Core;

/// <summary>
/// Publishes a Godot image to the Windows clipboard without an external
/// process, temporary file, or additional runtime dependency.
/// </summary>
internal static class WindowsImageClipboard
{
    private const uint ClipboardFormatDib = 8;
    private const uint GlobalMemoryMoveable = 0x0002;
    private const int BitmapInfoHeaderSize = 40;
    private const int ClipboardOpenAttempts = 8;
    private const int ClipboardRetryDelayMilliseconds = 12;

    public static bool TrySetImage(Image image, out string error)
    {
        error = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            error = "Image clipboard export is currently available only on Windows.";
            return false;
        }

        if (image == null || image.GetWidth() <= 0 || image.GetHeight() <= 0)
        {
            error = "The captured stats image was empty.";
            return false;
        }

        try
        {
            image.Convert(Image.Format.Rgba8);
            var dib = BuildDib(
                image.GetWidth(),
                image.GetHeight(),
                image.GetData());
            return TrySetDib(dib, out error);
        }
        catch (Exception exception)
        {
            error = $"Could not prepare the stats image: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Builds a bottom-up, 32-bit CF_DIB payload. Windows owns the memory only
    /// after SetClipboardData succeeds; callers retain ordinary managed bytes.
    /// </summary>
    internal static byte[] BuildDib(
        int width,
        int height,
        ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var pixelByteCount = checked(width * height * 4);
        if (rgba.Length != pixelByteCount)
        {
            throw new ArgumentException(
                "RGBA data length does not match the supplied image dimensions.",
                nameof(rgba));
        }

        var dib = new byte[checked(BitmapInfoHeaderSize + pixelByteCount)];
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(0, 4), BitmapInfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8, 4), height);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(14, 2), 32);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(16, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(20, 4), pixelByteCount);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(24, 4), 3780);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(28, 4), 3780);

        var sourceStride = width * 4;
        for (var sourceY = 0; sourceY < height; sourceY++)
        {
            var destinationY = height - sourceY - 1;
            var sourceRow = sourceY * sourceStride;
            var destinationRow = BitmapInfoHeaderSize + destinationY * sourceStride;
            for (var x = 0; x < width; x++)
            {
                var source = sourceRow + x * 4;
                var destination = destinationRow + x * 4;
                dib[destination] = rgba[source + 2];
                dib[destination + 1] = rgba[source + 1];
                dib[destination + 2] = rgba[source];
                // CF_DIB with BI_RGB is most widely interoperable when every
                // copied pixel is explicitly opaque.
                dib[destination + 3] = byte.MaxValue;
            }
        }

        return dib;
    }

    private static bool TrySetDib(byte[] dib, out string error)
    {
        error = string.Empty;
        var memory = GlobalAlloc(GlobalMemoryMoveable, (nuint)dib.Length);
        if (memory == IntPtr.Zero)
        {
            error = LastWindowsError("Windows could not allocate clipboard memory");
            return false;
        }

        var clipboardOpen = false;
        try
        {
            var destination = GlobalLock(memory);
            if (destination == IntPtr.Zero)
            {
                error = LastWindowsError("Windows could not lock clipboard memory");
                return false;
            }

            try
            {
                Marshal.Copy(dib, 0, destination, dib.Length);
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }

            for (var attempt = 0; attempt < ClipboardOpenAttempts; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    clipboardOpen = true;
                    break;
                }

                Thread.Sleep(ClipboardRetryDelayMilliseconds);
            }

            if (!clipboardOpen)
            {
                error = LastWindowsError("The Windows clipboard is busy");
                return false;
            }

            if (!EmptyClipboard())
            {
                error = LastWindowsError("Windows could not clear the clipboard");
                return false;
            }

            if (SetClipboardData(ClipboardFormatDib, memory) == IntPtr.Zero)
            {
                error = LastWindowsError("Windows could not store the stats image");
                return false;
            }

            // SetClipboardData transfers ownership of this allocation to the
            // operating system. Do not release it after this point.
            memory = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (clipboardOpen)
                _ = CloseClipboard();
            if (memory != IntPtr.Zero)
                _ = GlobalFree(memory);
        }
    }

    private static string LastWindowsError(string message)
    {
        var errorCode = Marshal.GetLastWin32Error();
        return errorCode == 0
            ? message
            : $"{message}: {new Win32Exception(errorCode).Message}";
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}

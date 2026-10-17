using System.Runtime.InteropServices;
using System.Text;

namespace BatteryTray.Hid;

internal sealed class HidDevice : IDisposable
{
    private readonly IntPtr _handle;
    private readonly IntPtr _preparsed;
    private IntPtr _readHandle;
    private IntPtr _readEvent;
    private bool _disposed;

    public string Path { get; }
    public HidCaps Caps { get; }
    public bool CanWrite { get; private set; }
    public IntPtr PreparsedData => _preparsed;

    private HidDevice(string path, IntPtr handle, IntPtr preparsed, HidCaps caps, bool canWrite)
    {
        Path = path;
        _handle = handle;
        _preparsed = preparsed;
        Caps = caps;
        CanWrite = canWrite;
    }

    public static HidDevice Open(string path, bool write)
    {
        uint access = write ? HidNative.GENERIC_READ | HidNative.GENERIC_WRITE : 0;
        IntPtr handle = HidNative.CreateFileW(
            path, access, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
            IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);

        bool canWrite = write;
        if (IsInvalid(handle) && write)
        {
            handle = HidNative.CreateFileW(
                path, 0, HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
            canWrite = false;
        }

        if (IsInvalid(handle))
        {
            throw new IOException($"无法打开 HID 接口（错误 {HidNative.LastError}）");
        }

        IntPtr preparsed = IntPtr.Zero;
        HidCaps caps;
        try
        {
            HidNative.HidD_GetPreparsedData(handle, out preparsed);
            caps = ReadCaps(preparsed);
        }
        catch
        {
            if (preparsed != IntPtr.Zero)
            {
                HidNative.HidD_FreePreparsedData(preparsed);
            }
            HidNative.CloseHandle(handle);
            throw;
        }

        return new HidDevice(path, handle, preparsed, caps, canWrite);
    }

    private static HidCaps ReadCaps(IntPtr preparsed)
    {
        if (preparsed == IntPtr.Zero)
        {
            return new HidCaps(0, 0, 0, 0, 0, 0);
        }

        IntPtr buffer = Marshal.AllocHGlobal(128);
        try
        {
            Marshal.Copy(new byte[128], 0, buffer, 128);
            int status = HidNative.HidP_GetCaps(preparsed, buffer);
            if (status != HidNative.HIDP_STATUS_SUCCESS)
            {
                return new HidCaps(0, 0, 0, 0, 0, 0);
            }

            return new HidCaps(
                (ushort)Marshal.ReadInt16(buffer, 2),
                (ushort)Marshal.ReadInt16(buffer, 0),
                (ushort)Marshal.ReadInt16(buffer, 4),
                (ushort)Marshal.ReadInt16(buffer, 6),
                (ushort)Marshal.ReadInt16(buffer, 8),
                (ushort)Marshal.ReadInt16(buffer, 60));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Drain(int maxReports = 24, int timeoutMs = 6)
    {
        for (int i = 0; i < maxReports; i++)
        {
            if (ReadInput(timeoutMs) is null)
            {
                break;
            }
        }
    }

    public byte[]? ReadInput(int timeoutMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Caps.InputReportLength <= 0 || !EnsureReader())
        {
            return null;
        }

        int length = Math.Max(Caps.InputReportLength, 1);
        IntPtr buffer = Marshal.AllocHGlobal(length);
        IntPtr overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<HidNative.OVERLAPPED>());
        try
        {
            Marshal.Copy(new byte[length], 0, buffer, length);
            Marshal.Copy(new byte[Marshal.SizeOf<HidNative.OVERLAPPED>()], 0,
                overlapped, Marshal.SizeOf<HidNative.OVERLAPPED>());

            var ov = new HidNative.OVERLAPPED { hEvent = _readEvent };
            Marshal.StructureToPtr(ov, overlapped, false);
            HidNative.ResetEvent(_readEvent);

            bool ok = HidNative.ReadFile(
                _readHandle, buffer, (uint)length, out uint read, overlapped);
            if (ok)
            {
                byte[] result = new byte[read];
                Marshal.Copy(buffer, result, 0, (int)read);
                return result;
            }

            int error = HidNative.LastError;
            if (error != HidNative.ERROR_IO_PENDING)
            {
                return null;
            }

            uint wait = HidNative.WaitForSingleObject(
                _readEvent, (uint)Math.Clamp(timeoutMs, 1, 10_000));
            if (wait == HidNative.WAIT_TIMEOUT)
            {
                HidNative.CancelIoEx(_readHandle, overlapped);
                HidNative.WaitForSingleObject(_readEvent, 300);
                HidNative.GetOverlappedResult(_readHandle, overlapped, out _, false);
                return null;
            }

            if (wait != HidNative.WAIT_OBJECT_0)
            {
                return null;
            }

            if (!HidNative.GetOverlappedResult(_readHandle, overlapped, out read, false))
            {
                return null;
            }

            byte[] data = new byte[read];
            Marshal.Copy(buffer, data, 0, (int)read);
            return data;
        }
        finally
        {
            Marshal.FreeHGlobal(overlapped);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private bool EnsureReader()
    {
        if (_readHandle != IntPtr.Zero)
        {
            return true;
        }

        _readHandle = HidNative.CreateFileW(
            Path, HidNative.GENERIC_READ,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
            IntPtr.Zero, HidNative.OPEN_EXISTING, HidNative.FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);
        if (IsInvalid(_readHandle))
        {
            _readHandle = IntPtr.Zero;
            return false;
        }

        _readEvent = HidNative.CreateEventW(IntPtr.Zero, true, false, null);
        if (_readEvent == IntPtr.Zero)
        {
            HidNative.CloseHandle(_readHandle);
            _readHandle = IntPtr.Zero;
            return false;
        }

        return true;
    }

    public void WriteOutput(byte[] report, bool allowHidDeeFallback = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanWrite)
        {
            throw new IOException("HID 接口没有写入权限");
        }

        if (HidNative.WriteFile(_handle, report, (uint)report.Length, out _, IntPtr.Zero))
        {
            return;
        }

        int writeError = HidNative.LastError;
        if (allowHidDeeFallback &&
            HidNative.HidD_SetOutputReport(_handle, report, (uint)report.Length))
        {
            return;
        }

        throw new IOException($"写入 HID 报告失败（错误 {writeError}）");
    }

    public void SetFeature(byte[] report)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!HidNative.HidD_SetFeature(_handle, report, (uint)report.Length))
        {
            throw new IOException($"写入 Feature Report 失败（错误 {HidNative.LastError}）");
        }
    }

    public byte[] GetFeature(int reportId, int? length = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int size = length ?? Caps.FeatureReportLength;
        if (size <= 0)
        {
            throw new IOException("该接口没有 Feature Report");
        }

        byte[] buffer = new byte[Math.Max(size, 2)];
        buffer[0] = (byte)reportId;
        if (!HidNative.HidD_GetFeature(_handle, buffer, (uint)buffer.Length))
        {
            throw new IOException($"读取 Feature Report 0x{reportId:X2} 失败（错误 {HidNative.LastError}）");
        }

        return buffer;
    }

    public uint? GetUsageValue(int reportType, HidValueCap cap, byte[] report)
    {
        if (_preparsed == IntPtr.Zero)
        {
            return null;
        }

        int status = HidNative.HidP_GetUsageValue(
            reportType, cap.UsagePage, cap.LinkCollection, cap.Usage,
            out uint value, _preparsed, report, (uint)report.Length);
        return status == HidNative.HIDP_STATUS_SUCCESS ? value : null;
    }

    public IReadOnlyList<HidValueCap> GetValueCaps(int reportType)
    {
        if (_preparsed == IntPtr.Zero)
        {
            return Array.Empty<HidValueCap>();
        }

        ushort count = 0;
        HidNative.HidP_GetValueCaps(reportType, IntPtr.Zero, ref count, _preparsed);
        if (count == 0)
        {
            return Array.Empty<HidValueCap>();
        }

        const int valueCapSize = 72;
        IntPtr buffer = Marshal.AllocHGlobal(count * valueCapSize);
        try
        {
            Marshal.Copy(new byte[count * valueCapSize], 0, buffer, count * valueCapSize);
            int status = HidNative.HidP_GetValueCaps(reportType, buffer, ref count, _preparsed);
            if (status != HidNative.HIDP_STATUS_SUCCESS)
            {
                return Array.Empty<HidValueCap>();
            }

            var result = new List<HidValueCap>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = i * valueCapSize;
                bool isRange = Marshal.ReadByte(buffer, offset + 12) != 0;
                result.Add(new HidValueCap(
                    (ushort)Marshal.ReadInt16(buffer, offset),
                    (ushort)Marshal.ReadInt16(buffer, offset + 8),
                    Marshal.ReadByte(buffer, offset + 2),
                    isRange,
                    (ushort)Marshal.ReadInt16(buffer, offset + 56),
                    (ushort)Marshal.ReadInt16(buffer, offset + 58),
                    Marshal.ReadInt32(buffer, offset + 44),
                    Marshal.ReadInt32(buffer, offset + 40)));
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static string ReadWideString(
        Func<IntPtr, byte[], uint, bool> reader, IntPtr handle)
    {
        byte[] buffer = new byte[512];
        try
        {
            if (!reader(handle, buffer, (uint)buffer.Length))
            {
                return "";
            }

            string value = Encoding.Unicode.GetString(buffer);
            int nul = value.IndexOf('\0');
            return (nul >= 0 ? value[..nul] : value).Trim();
        }
        catch
        {
            return "";
        }
    }

    public static bool IsInvalid(IntPtr handle) =>
        handle == IntPtr.Zero || handle == HidNative.InvalidHandleValue;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_readHandle != IntPtr.Zero)
        {
            HidNative.CancelIoEx(_readHandle, IntPtr.Zero);
            HidNative.CloseHandle(_readHandle);
            _readHandle = IntPtr.Zero;
        }

        if (_readEvent != IntPtr.Zero)
        {
            HidNative.CloseHandle(_readEvent);
            _readEvent = IntPtr.Zero;
        }

        if (_preparsed != IntPtr.Zero)
        {
            HidNative.HidD_FreePreparsedData(_preparsed);
        }

        if (_handle != IntPtr.Zero)
        {
            HidNative.CloseHandle(_handle);
        }
    }
}

internal sealed record HidValueCap(
    ushort UsagePage,
    ushort LinkCollection,
    byte ReportId,
    bool IsRange,
    ushort Usage,
    ushort UsageMax,
    int LogicalMax,
    int LogicalMin);

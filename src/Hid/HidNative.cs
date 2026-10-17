using System.Runtime.InteropServices;

namespace BatteryTray.Hid;

internal static class HidNative
{
    internal const uint DIGCF_PRESENT = 0x02;
    internal const uint DIGCF_DEVICEINTERFACE = 0x10;
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x01;
    internal const uint FILE_SHARE_WRITE = 0x02;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    internal static readonly IntPtr InvalidHandleValue = new(-1);
    internal const int ERROR_IO_PENDING = 997;
    internal const uint WAIT_OBJECT_0 = 0;
    internal const uint WAIT_TIMEOUT = 0x102;

    internal const int HIDP_INPUT = 0;
    internal const int HIDP_OUTPUT = 1;
    internal const int HIDP_FEATURE = 2;
    internal const int HIDP_STATUS_SUCCESS = 0x00110000;

    internal static readonly Guid HidInterfaceGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr hEvent;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetailW(
        IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize,
        ref int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadFile(
        IntPtr file, IntPtr buffer, uint numberOfBytesToRead, out uint numberOfBytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteFile(
        IntPtr file, byte[] buffer, uint numberOfBytesToWrite, out uint numberOfBytesWritten,
        IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateEventW(
        IntPtr eventAttributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ResetEvent(IntPtr eventHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetOverlappedResult(
        IntPtr file, IntPtr overlapped, out uint numberOfBytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CancelIoEx(IntPtr file, IntPtr overlapped);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetAttributes(IntPtr device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetProductString(IntPtr device, byte[] buffer, uint bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetManufacturerString(IntPtr device, byte[] buffer, uint bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetSerialNumberString(IntPtr device, byte[] buffer, uint bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    internal static extern int HidP_GetCaps(IntPtr preparsedData, IntPtr capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    internal static extern int HidP_GetValueCaps(
        int reportType, IntPtr valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    internal static extern int HidP_GetUsageValue(
        int reportType, ushort usagePage, ushort linkCollection, ushort usage,
        out uint usageValue, IntPtr preparsedData, byte[] report, uint reportLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetFeature(IntPtr device, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_SetFeature(IntPtr device, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_SetOutputReport(IntPtr device, byte[] reportBuffer, uint reportBufferLength);

    internal static int LastError => Marshal.GetLastWin32Error();
}

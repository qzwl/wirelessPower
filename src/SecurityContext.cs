using System.Runtime.InteropServices;

namespace BatteryTray;

internal static class SecurityContext
{
    public static bool IsLowIntegrity()
    {
        IntPtr token = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token))
            {
                return false;
            }

            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out uint length);
            if (length == 0)
            {
                return false;
            }

            buffer = Marshal.AllocHGlobal((int)length);
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
            {
                return false;
            }

            IntPtr sid = Marshal.ReadIntPtr(buffer, 0);
            IntPtr countPointer = GetSidSubAuthorityCount(sid);
            if (countPointer == IntPtr.Zero)
            {
                return false;
            }

            // GetSidSubAuthorityCount 返回的是指向字节的指针，必须解引用读取，
            // 直接把返回值当 byte 会拿到指针自身的低位，导致随机误判。
            byte subAuthorityCount = Marshal.ReadByte(countPointer);
            if (subAuthorityCount == 0)
            {
                return false;
            }

            IntPtr subAuthority = GetSidSubAuthority(sid, (uint)(subAuthorityCount - 1));
            if (subAuthority == IntPtr.Zero)
            {
                return false;
            }

            int integrityRid = Marshal.ReadInt32(subAuthority);

            // 0x1000 = Low，0x2000 = Medium。只有真正的低完整性/不受信任才拦截，
            // 避免边界值把普通权限误判掉。
            return integrityRid < 0x1000;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }

    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
        uint tokenInformationLength, out uint returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthorityIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

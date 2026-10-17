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
            byte subAuthorityCount = GetSidSubAuthorityCount(sid);
            if (subAuthorityCount == 0)
            {
                return false;
            }

            IntPtr subAuthority = GetSidSubAuthority(sid, (uint)(subAuthorityCount - 1));
            int integrityRid = Marshal.ReadInt32(subAuthority);
            return integrityRid < 0x2000;
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
    private static extern byte GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthorityIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

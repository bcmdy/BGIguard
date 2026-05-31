using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace BGIguard;

internal static class ProcessService
{
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenUser = 1;
    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_USER
    {
        public SID_AND_ATTRIBUTES User;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        int dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool LookupAccountSid(string? lpSystemName, IntPtr Sid, StringBuilder lpName, ref int cchName, StringBuilder? lpReferencedDomainName, ref int cchReferencedDomainName, out int peUse);

    public static void ForEachProcessByName(string processName, Action<Process> action)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                action(process);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public static ProcessOwnerInfo GetProcessOwner(int processId)
    {
        IntPtr hProcess = IntPtr.Zero;
        IntPtr hToken = IntPtr.Zero;
        IntPtr tokenInfo = IntPtr.Zero;

        try
        {
            hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, false, processId);
            if (hProcess == IntPtr.Zero)
            {
                hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
                if (hProcess == IntPtr.Zero)
                    return default;
            }

            if (!OpenProcessToken(hProcess, TOKEN_QUERY, out hToken))
                return default;

            int returnLength = 0;
            GetTokenInformation(hToken, TokenUser, IntPtr.Zero, 0, out returnLength);
            if (returnLength == 0)
                return default;

            tokenInfo = Marshal.AllocHGlobal(returnLength);
            if (!GetTokenInformation(hToken, TokenUser, tokenInfo, returnLength, out returnLength))
                return default;

            var tokenUser = Marshal.PtrToStructure<TOKEN_USER>(tokenInfo);
            if (tokenUser.User.Sid == IntPtr.Zero)
                return default;

            string sid = new SecurityIdentifier(tokenUser.User.Sid).Value;

            int nameSize = 0;
            int domainSize = 0;
            if (!LookupAccountSid(null, tokenUser.User.Sid, null!, ref nameSize, null!, ref domainSize, out _))
            {
                if (nameSize == 0)
                    return new ProcessOwnerInfo("", sid);
            }

            var nameBuilder = new StringBuilder(nameSize);
            var domainBuilder = new StringBuilder(Math.Max(1, domainSize));
            if (!LookupAccountSid(null, tokenUser.User.Sid, nameBuilder, ref nameSize, domainBuilder, ref domainSize, out _))
                return new ProcessOwnerInfo("", sid);

            string name = nameBuilder.ToString();
            string domain = domainBuilder.ToString();
            string displayName = string.IsNullOrEmpty(domain) ? name : $@"{domain}\{name}";
            return new ProcessOwnerInfo(displayName, sid);
        }
        catch
        {
            return default;
        }
        finally
        {
            if (tokenInfo != IntPtr.Zero)
                Marshal.FreeHGlobal(tokenInfo);
            if (hToken != IntPtr.Zero)
                CloseHandle(hToken);
            if (hProcess != IntPtr.Zero)
                CloseHandle(hProcess);
        }
    }

    public static bool IsCurrentUserProcess(ProcessOwnerInfo owner, string currentUserSid, string currentUserName)
    {
        if (string.IsNullOrEmpty(currentUserSid))
            return string.Equals(owner.UserName, currentUserName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(owner.UserName, Environment.UserName, StringComparison.OrdinalIgnoreCase);

        return string.Equals(owner.Sid, currentUserSid, StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct ProcessOwnerInfo(string UserName, string Sid)
{
    public bool HasIdentity => !string.IsNullOrEmpty(Sid);
    public string Display => string.IsNullOrEmpty(UserName) ? $"SID:{Sid}" : $"用户:{UserName}, SID:{Sid}";
}

internal readonly record struct BetterGiProcessSnapshot(bool Exists, string? CommandLine, long MemoryMB);

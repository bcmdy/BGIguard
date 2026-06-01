using System.Security.Principal;

namespace BGIguard;

internal static class CurrentUserService
{
    public static string GetCurrentUserSid()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value ?? "";
        }
        catch
        {
            return "";
        }
    }

    public static string GetCurrentUserDisplayName()
    {
        string domain = Environment.UserDomainName;
        string user = Environment.UserName;
        return string.IsNullOrEmpty(domain) ? user : $@"{domain}\{user}";
    }
}

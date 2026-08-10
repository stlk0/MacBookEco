using System;
using System.Security.Principal;

namespace MacBookEco.Platform.Windows
{
    internal static class PrivilegeGuard
    {
        internal static void RequireAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (!principal.IsInRole(
                        WindowsBuiltInRole.Administrator))
                {
                    throw new UnauthorizedAccessException(
                        "Administrator privileges are required for this operation.");
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace MacBookEco.App
{
    internal static class StartupRegistration
    {
        private const string RunKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MacBookEco";

        internal static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    RunKeyPath,
                    false))
                {
                    string value = key == null
                        ? null
                        : key.GetValue(
                            ValueName,
                            null,
                            RegistryValueOptions.DoNotExpandEnvironmentNames)
                            as string;
                    return string.Equals(
                        value,
                        BuildCommand(),
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception exception)
                when (IsRegistryAccessFailure(exception))
            {
                return false;
            }
        }

        internal static bool TrySetEnabled(bool enabled, out string error)
        {
            try
            {
                if (enabled)
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                        RunKeyPath))
                    {
                        if (key == null)
                        {
                            throw new IOException(
                                "Windows did not open the startup registry key.");
                        }

                        key.SetValue(
                            ValueName,
                            BuildCommand(),
                            RegistryValueKind.String);
                    }
                }
                else
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                        RunKeyPath,
                        true))
                    {
                        if (key != null)
                        {
                            key.DeleteValue(ValueName, false);
                        }
                    }
                }

                error = null;
                return true;
            }
            catch (Exception exception)
                when (IsRegistryAccessFailure(exception))
            {
                error = exception.Message;
                return false;
            }
        }

        internal static string BuildCommand()
        {
            return "\"" + System.Windows.Forms.Application.ExecutablePath
                + "\" --background";
        }

        private static bool IsRegistryAccessFailure(Exception exception)
        {
            return exception is UnauthorizedAccessException
                || exception is SecurityException
                || exception is IOException;
        }
    }
}

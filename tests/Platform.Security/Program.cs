using System;
using MacBookEco.Platform.Windows;

namespace MacBookEco.Tests.PlatformSecurity
{
    /// <summary>
    /// Elevated half of the opt-in two-token NTFS suite. It uses the real
    /// fixed ProgramData state root and is intentionally never invoked by
    /// test-all; a disposable VM is mandatory.
    /// </summary>
    internal static class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitUsage = 2;
        private const int ExitFailure = 10;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args == null || args.Length != 1)
            {
                PrintUsage();
                return ExitUsage;
            }

            try
            {
                switch (args[0])
                {
                    case "create-clean-root":
                        CreateCleanRoot();
                        break;
                    case "create-edid-lock":
                        CreateEdidLock();
                        break;
                    case "expect-root-conflict":
                        ExpectRootConflict();
                        break;
                    case "expect-edid-lock-conflict":
                        ExpectLockConflict(SecureStateLockKind.Edid);
                        break;
                    case "expect-power-lock-conflict":
                        ExpectLockConflict(SecureStateLockKind.Power);
                        break;
                    default:
                        PrintUsage();
                        return ExitUsage;
                }

                return ExitSuccess;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    "FAIL: " + exception.GetType().Name + ": " +
                    exception.Message);
                return ExitFailure;
            }
        }

        private static void CreateCleanRoot()
        {
            using (SecureStateStore store =
                SecureStateStore.OpenOrCreateElevated())
            {
                Console.WriteLine(
                    "PASS: checked state root is ready: " +
                    SecureStateStore.FixedRootPath);
            }
        }

        private static void CreateEdidLock()
        {
            using (SecureStateStore store =
                SecureStateStore.OpenOrCreateElevated())
            {
                using (SecureStateLockHandle ignored =
                    store.AcquireEdidLock(TimeSpan.Zero))
                {
                    Console.WriteLine(
                        "PASS: checked EDID lock was created for hostile-link staging.");
                }
            }
        }

        private static void ExpectRootConflict()
        {
            try
            {
                using (SecureStateStore ignored =
                    SecureStateStore.OpenOrCreateElevated())
                {
                }
            }
            catch (SecureStateConflictException exception)
            {
                PrintExpectedConflict(exception);
                return;
            }

            throw new InvalidOperationException(
                "A hostile state root was accepted by the elevated store.");
        }

        private static void ExpectLockConflict(SecureStateLockKind kind)
        {
            try
            {
                using (SecureStateStore store =
                    SecureStateStore.OpenOrCreateElevated())
                {
                    using (SecureStateLockHandle ignored =
                        kind == SecureStateLockKind.Edid
                            ? store.AcquireEdidLock(TimeSpan.Zero)
                            : store.AcquirePowerLock(TimeSpan.Zero))
                    {
                    }
                }
            }
            catch (SecureStateConflictException exception)
            {
                PrintExpectedConflict(exception);
                return;
            }

            throw new InvalidOperationException(
                "A hostile lock file was accepted by the elevated store.");
        }

        private static void PrintExpectedConflict(
            SecureStateConflictException exception)
        {
            Console.WriteLine(
                "PASS: expected SecureStateConflictException: " +
                exception.Message);
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine(
                "Usage: PlatformSecurityTests.exe " +
                "create-clean-root|create-edid-lock|expect-root-conflict|" +
                "expect-edid-lock-conflict|expect-power-lock-conflict");
        }
    }
}

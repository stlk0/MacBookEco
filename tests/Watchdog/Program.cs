using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MacBookEco.Core;
using MacBookEco.DisplaySafety;

namespace MacBookEco.Tests.Watchdog
{
    internal static class Program
    {
        private static int Main()
        {
            TestCase[] tests =
            {
                new TestCase(
                    "Test-root hook rejects the production directory",
                    AssertTestRootCannotUseProductionDirectory),
                new TestCase(
                    "Protocol rejects invalid tokens",
                    TestInvalidToken),
                new TestCase(
                    "Session and commit signal round-trip under the lock",
                    delegate { RunInIsolatedRoot(TestSessionRoundTrip); }),
                new TestCase(
                    "Session enumeration accepts only canonical files",
                    delegate { RunInIsolatedRoot(TestSessionEnumeration); }),
                new TestCase(
                    "Malformed sessions fail closed",
                    delegate { RunInIsolatedRoot(AssertMalformedSessionFailsClosed); }),
                new TestCase(
                    "Non-canonical refresh rationals fail closed",
                    delegate { RunInIsolatedRoot(TestNonCanonicalRational); }),
                new TestCase(
                    "Rollback marker wins the persistence-lock race",
                    delegate { RunInIsolatedRoot(TestRollbackMarkerWinsLockRace); })
            };

            return TestSuite.Run("Watchdog protocol tests", tests);
        }

        private static void AssertTestRootCannotUseProductionDirectory()
        {
            string productionRoot = Path.Combine(
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "MacBookEco"),
                "DisplayWatchdog");

            Check.Throws<ArgumentException>(
                delegate
                {
                    IDisposable scope = null;
                    try
                    {
                        scope = DisplayWatchdogProtocol.UseTestRootForTesting(
                            productionRoot);
                    }
                    finally
                    {
                        if (scope != null)
                        {
                            scope.Dispose();
                        }
                    }
                },
                "The test-root hook must reject the production watchdog directory.");
        }

        private static void TestInvalidToken()
        {
            Check.Throws<ArgumentException>(
                delegate
                {
                    DisplayWatchdogProtocol.ValidateToken("not-a-token");
                },
                "Invalid tokens must be rejected.");
        }

        private static void RunInIsolatedRoot(Action<string> test)
        {
            string testRoot = CreateIsolatedTestRoot();
            try
            {
                Directory.CreateDirectory(testRoot);
                using (DisplayWatchdogProtocol.UseTestRootForTesting(testRoot))
                {
                    test(testRoot);
                }
            }
            finally
            {
                CleanupIsolatedTestRoot(testRoot);
            }
        }

        private static void TestSessionRoundTrip(string testRoot)
        {
            string tokenToClean = null;
            try
            {
                MonitorIdentity targetIdentity = CreateTargetIdentity();
                DisplayModeKey originalMode = CreateOriginalMode(59);
                DisplayWatchdogSessionState state =
                    DisplayWatchdogProtocol.CreateSession(
                        targetIdentity,
                        originalMode,
                        TimeSpan.FromSeconds(10));
                tokenToClean = state.Token;
                Check.That(
                    File.Exists(Path.Combine(testRoot, state.Token + ".session")),
                    "The watchdog session was not written to the isolated test root.");
                AssertSessionContainsNoDisplayEndpoint(
                    testRoot,
                    state.Token,
                    originalMode);
                DisplayWatchdogSessionState roundTrip =
                    DisplayWatchdogProtocol.ReadSession(state.Token);
                Check.That(roundTrip.Token == state.Token, "Token round-trip failed.");
                Check.That(
                    roundTrip.TargetIdentity.Equals(state.TargetIdentity),
                    "Monitor identity round-trip failed.");
                Check.That(
                    roundTrip.OriginalMode.Equals(state.OriginalMode),
                    "Full original-mode round-trip failed.");
                Check.That(
                    DisplayWatchdogProtocol.ReadSignal(state.Token)
                        == DisplayWatchdogSignal.None,
                    "A new session must have no signal.");

                DisplayWatchdogProtocol.WriteReady(state.Token);
                Check.That(
                    DisplayWatchdogProtocol.IsReady(state.Token),
                    "Ready marker round-trip failed.");
                using (FileStream trayLock =
                    DisplayWatchdogProtocol.AcquirePersistenceLock(
                        state.Token,
                        TimeSpan.FromSeconds(1)))
                {
                    Check.That(
                        DisplayWatchdogProtocol.ReadSignal(state.Token)
                            == DisplayWatchdogSignal.None,
                        "The happy-path session was not clean under the tray lock.");
                    DisplayWatchdogProtocol.WriteSignal(
                        state.Token,
                        DisplayWatchdogSignal.Commit);
                }

                using (FileStream watchdogLock =
                    DisplayWatchdogProtocol.AcquirePersistenceLock(
                        state.Token,
                        TimeSpan.FromSeconds(1)))
                {
                    Check.That(
                        DisplayWatchdogProtocol.ReadSignal(state.Token)
                            == DisplayWatchdogSignal.Commit,
                        "The watchdog recheck did not observe the locked commit.");
                }

                DisplayWatchdogProtocol.Cleanup(state.Token);
                tokenToClean = null;
            }
            finally
            {
                if (tokenToClean != null)
                {
                    DisplayWatchdogProtocol.Cleanup(tokenToClean);
                }
            }
        }

        private static void TestSessionEnumeration(string testRoot)
        {
            MonitorIdentity targetIdentity = CreateTargetIdentity();
            DisplayModeKey originalMode = CreateOriginalMode();
            DisplayWatchdogSessionState first =
                DisplayWatchdogProtocol.CreateSession(
                    targetIdentity,
                    originalMode,
                    TimeSpan.FromSeconds(10));
            DisplayWatchdogSessionState second =
                DisplayWatchdogProtocol.CreateSession(
                    targetIdentity,
                    originalMode,
                    TimeSpan.FromSeconds(10));
            try
            {
                DisplayWatchdogProtocol.WriteReady(first.Token);
                System.Collections.Generic.IList<string> tokens =
                    DisplayWatchdogProtocol.ListSessionTokens();
                Check.That(tokens.Count == 2,
                    "Startup enumeration must return every session and no marker files.");
                Check.That(
                    tokens.Contains(first.Token)
                    && tokens.Contains(second.Token),
                    "Startup enumeration lost a canonical session token.");

                string malformed = Path.Combine(testRoot, "bad.session");
                File.WriteAllText(malformed, "invalid", Encoding.UTF8);
                try
                {
                    Check.Throws<InvalidDataException>(
                        delegate
                        {
                            DisplayWatchdogProtocol.ListSessionTokens();
                        },
                        "A non-canonical session file name must fail closed.");
                }
                finally
                {
                    File.Delete(malformed);
                }
            }
            finally
            {
                DisplayWatchdogProtocol.Cleanup(first.Token);
                DisplayWatchdogProtocol.Cleanup(second.Token);
            }
        }

        private static void TestNonCanonicalRational(string testRoot)
        {
            AssertNonCanonicalRationalFailsClosed(
                testRoot,
                CreateTargetIdentity());
        }

        private static void TestRollbackMarkerWinsLockRace(string testRoot)
        {
            AssertRollbackMarkerWinsLockRace(
                CreateTargetIdentity(),
                new DisplayModeKey(
                    3072,
                    1920,
                    32,
                    48,
                    0,
                    0,
                    0,
                    48,
                    1));
        }

        private static string CreateIsolatedTestRoot()
        {
            string parent = Path.Combine(
                Path.GetTempPath(),
                "MacBookEco.WatchdogProtocolTests");
            return Path.Combine(parent, Guid.NewGuid().ToString("N"));
        }

        private static void CleanupIsolatedTestRoot(string testRoot)
        {
            try
            {
                string parent = Path.GetFullPath(Path.Combine(
                    Path.GetTempPath(),
                    "MacBookEco.WatchdogProtocolTests"));
                string root = Path.GetFullPath(testRoot);
                string parentWithSeparator = parent.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (!root.StartsWith(
                        parentWithSeparator,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Refusing to clean a watchdog test root outside the temporary test directory.");
                }

                if (Directory.Exists(root))
                {
                    if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new UnauthorizedAccessException(
                            "Refusing to recursively delete a reparse-point test root.");
                    }

                    Directory.Delete(root, true);
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Watchdog test root cleanup deferred: " + exception.Message);
            }
        }

        private static MonitorIdentity CreateTargetIdentity()
        {
            return new MonitorIdentity(
                @"DISPLAY\APPA044\5&12345678&0&UID0000",
                "APPA044",
                "APP",
                Sha256Digest.ParseCanonical(
                    "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF"));
        }

        private static DisplayModeKey CreateOriginalMode(int refreshRate = 60)
        {
            return new DisplayModeKey(
                3072,
                1920,
                32,
                refreshRate,
                0,
                0,
                0,
                refreshRate == 60 ? 60000U : (uint)refreshRate,
                refreshRate == 60 ? 1001U : 1U);
        }

        private static void AssertSessionContainsNoDisplayEndpoint(
            string testRoot,
            string token,
            DisplayModeKey expectedMode)
        {
            string content = File.ReadAllText(
                Path.Combine(testRoot, token + ".session"));
            Check.That(
                content.StartsWith("format=watchdog\n", StringComparison.Ordinal),
                "The watchdog session must use the canonical format marker.");
            Check.That(
                content.IndexOf(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase) < 0,
                "A watchdog session must not persist a DISPLAYn endpoint.");
            Check.That(
                content.IndexOf(
                    "refreshNumerator=" +
                    expectedMode.RefreshRateNumerator.ToString(
                        CultureInfo.InvariantCulture) +
                    "\n",
                    StringComparison.Ordinal) >= 0 &&
                content.IndexOf(
                    "refreshDenominator=" +
                    expectedMode.RefreshRateDenominator.ToString(
                        CultureInfo.InvariantCulture) +
                    "\n",
                    StringComparison.Ordinal) >= 0,
                "A watchdog session must persist the exact rational refresh.");
        }

        private static void AssertMalformedSessionFailsClosed(string testRoot)
        {
            string token = new string('a', 64);
            string sessionPath = Path.Combine(testRoot, token + ".session");
            try
            {
                File.WriteAllText(
                    sessionPath,
                    "format=unexpected\n"
                    + "token=" + token + "\n"
                    + "device64="
                    + Convert.ToBase64String(
                        Encoding.UTF8.GetBytes(@"\\.\DISPLAY1"))
                    + "\noriginalRefresh=60\n"
                    + "deadlineUtcTicks="
                    + DateTime.UtcNow.Ticks.ToString()
                    + "\n",
                    new UTF8Encoding(false));
                Check.Throws<InvalidDataException>(
                    delegate
                    {
                        DisplayWatchdogProtocol.ReadSession(token);
                    },
                    "A malformed watchdog session must fail closed.");
            }
            finally
            {
                if (File.Exists(sessionPath))
                {
                    File.Delete(sessionPath);
                }
            }
        }

        private static void AssertNonCanonicalRationalFailsClosed(
            string testRoot,
            MonitorIdentity targetIdentity)
        {
            DisplayWatchdogSessionState state =
                DisplayWatchdogProtocol.CreateSession(
                    targetIdentity,
                    new DisplayModeKey(
                        3072,
                        1920,
                        32,
                        60,
                        0,
                        0,
                        0,
                        60,
                        1),
                    TimeSpan.FromSeconds(10));
            try
            {
                string sessionPath = Path.Combine(
                    testRoot,
                    state.Token + ".session");
                string content = File.ReadAllText(sessionPath);
                File.WriteAllText(
                    sessionPath,
                    content.Replace(
                        "refreshNumerator=60\n",
                        "refreshNumerator=120\n").Replace(
                        "refreshDenominator=1\n",
                        "refreshDenominator=2\n"),
                    new UTF8Encoding(false));
                Check.Throws<InvalidDataException>(
                    delegate
                    {
                        DisplayWatchdogProtocol.ReadSession(state.Token);
                    },
                    "A non-canonical watchdog rational must fail closed.");
            }
            finally
            {
                DisplayWatchdogProtocol.Cleanup(state.Token);
            }
        }

        private static void AssertRollbackMarkerWinsLockRace(
            MonitorIdentity targetIdentity,
            DisplayModeKey originalMode)
        {
            DisplayWatchdogSessionState state =
                DisplayWatchdogProtocol.CreateSession(
                    targetIdentity,
                    originalMode,
                    TimeSpan.FromSeconds(10));
            FileStream watchdogLock = null;
            ManualResetEventSlim trayStarted = new ManualResetEventSlim(false);
            try
            {
                watchdogLock =
                    DisplayWatchdogProtocol.AcquirePersistenceLock(
                        state.Token,
                        TimeSpan.FromSeconds(1));
                Task<DisplayWatchdogSignal> trayAttempt =
                    Task.Factory.StartNew(
                        delegate
                        {
                            trayStarted.Set();
                            using (FileStream trayLock =
                                DisplayWatchdogProtocol.AcquirePersistenceLock(
                                    state.Token,
                                    TimeSpan.FromSeconds(2)))
                            {
                                return DisplayWatchdogProtocol.ReadSignal(
                                    state.Token);
                            }
                        });

                Check.That(
                    trayStarted.Wait(TimeSpan.FromSeconds(1)),
                    "The simulated tray did not enter the lock race.");
                Thread.Sleep(100);
                Check.That(
                    !trayAttempt.IsCompleted,
                    "The tray bypassed the watchdog persistence lock.");

                Check.That(
                    DisplayWatchdogProtocol.ReadSignal(state.Token)
                        == DisplayWatchdogSignal.None,
                    "The timeout owner did not observe a clean session.");
                DisplayWatchdogProtocol.WriteRollback(state.Token);
                watchdogLock.Dispose();
                watchdogLock = null;

                Check.That(
                    trayAttempt.Wait(TimeSpan.FromSeconds(2)),
                    "The tray did not resume after watchdog lock release.");
                Check.That(
                    trayAttempt.Result == DisplayWatchdogSignal.Rollback,
                    "The tray failed to observe the authenticated rollback marker.");
            }
            finally
            {
                if (watchdogLock != null)
                {
                    watchdogLock.Dispose();
                }

                trayStarted.Dispose();
                DisplayWatchdogProtocol.Cleanup(state.Token);
            }
        }

    }
}

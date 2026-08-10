using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Diagnostics;
using MacBookEco.Core;

namespace MacBookEco.DisplaySafety
{
    internal enum DisplayWatchdogSignal
    {
        None,
        Commit,
        Cancel,
        Rollback,
        Conflict
    }

    internal sealed class DisplayWatchdogSessionState
    {
        internal string Token { get; set; }

        /// <summary>
        /// Durable monitor identity. It deliberately has no CCD endpoint or
        /// GDI DISPLAYn name: the watchdog resolves a fresh endpoint before
        /// it attempts rollback.
        /// </summary>
        internal MonitorIdentity TargetIdentity { get; set; }

        /// <summary>
        /// The complete original mode, including CCD's canonical rational
        /// refresh. A bare refresh-rate rollback is not safe.
        /// </summary>
        internal DisplayModeKey OriginalMode { get; set; }

        internal DateTime DeadlineUtc { get; set; }
    }

    /// <summary>
    /// A deliberately small file protocol shared by the tray application and
    /// the watchdog. The command line carries only an unpredictable token;
    /// display data is read from a current-user-only directory.
    /// </summary>
    internal static class DisplayWatchdogProtocol
    {
        private const int TokenByteCount = 32;
        private const int MaximumStateBytes = 2048;
        private const string DirectoryName = "DisplayWatchdog";

        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly object RootLock = new object();
        private static string verifiedRoot;
        private static string testRoot;

        /// <summary>
        /// Routes this process's watchdog protocol files to an isolated
        /// temporary directory until the returned scope is disposed. This is
        /// intentionally internal and test-only: production always resolves
        /// the current user's LocalApplicationData location and no environment
        /// variable can override it.
        /// </summary>
        internal static IDisposable UseTestRootForTesting(string root)
        {
            string fullRoot = ValidateTestRoot(root);
            lock (RootLock)
            {
                if (testRoot != null || verifiedRoot != null)
                {
                    throw new InvalidOperationException(
                        "The watchdog protocol root was already initialized.");
                }

                testRoot = fullRoot;
                return new TestRootScope(fullRoot);
            }
        }

        internal static DisplayWatchdogSessionState CreateSession(
            MonitorIdentity targetIdentity,
            DisplayModeKey originalMode,
            TimeSpan timeout)
        {
            ValidateSessionInputs(targetIdentity, originalMode, timeout);

            string root = EnsureSecureRootDirectory();
            for (int attempt = 0; attempt < 4; attempt++)
            {
                string token = GenerateToken();
                DisplayWatchdogSessionState state =
                    new DisplayWatchdogSessionState();
                state.Token = token;
                state.TargetIdentity = targetIdentity;
                state.OriginalMode = originalMode;
                state.DeadlineUtc = DateTime.UtcNow.Add(timeout);

                string path = SessionPath(root, token);
                try
                {
                    WriteNewFile(path, Serialize(state));
                    return state;
                }
                catch (IOException)
                {
                    // A cryptographic token collision is extraordinarily
                    // unlikely, but CreateNew keeps it harmless.
                }
            }

            throw new IOException("Could not allocate a unique watchdog session.");
        }

        internal static DisplayWatchdogSessionState ReadSession(string token)
        {
            ValidateToken(token);
            string root = VerifySecureRootDirectory();
            string path = SessionPath(root, token);
            SessionFields fields = SessionFields.Parse(ReadSmallFile(path));

            string storedToken = fields[SessionKeys.Token];
            if (!FixedTimeComparer.AreEqual(token, storedToken))
            {
                throw new InvalidDataException("The watchdog session token does not match.");
            }

            string monitorInstanceId;
            try
            {
                byte[] monitorInstanceBytes = Convert.FromBase64String(
                    fields[SessionKeys.MonitorInstance]);
                monitorInstanceId = Utf8.GetString(monitorInstanceBytes);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "The watchdog monitor instance identity is invalid.",
                    exception);
            }

            try
            {
                MonitorIdentity identity = new MonitorIdentity(
                    monitorInstanceId,
                    fields[SessionKeys.PanelHardwareId],
                    fields[SessionKeys.ManufacturerCode],
                    Sha256Digest.ParseCanonical(
                        fields[SessionKeys.EdidFingerprint]));
                uint refreshNumerator = fields.UnsignedInt32(
                    SessionKeys.RefreshNumerator);
                uint refreshDenominator = fields.UnsignedInt32(
                    SessionKeys.RefreshDenominator);
                DisplayModeKey originalMode = new DisplayModeKey(
                    fields.UnsignedInt(SessionKeys.Width),
                    fields.UnsignedInt(SessionKeys.Height),
                    fields.UnsignedInt(SessionKeys.BitsPerPixel),
                    fields.UnsignedInt(SessionKeys.RefreshRate),
                    fields.UnsignedInt(SessionKeys.Orientation),
                    fields.UnsignedInt(SessionKeys.FixedOutput),
                    fields.SignedInt(SessionKeys.DisplayFlags),
                    refreshNumerator,
                    refreshDenominator);

                // DisplayModeKey reduces the rational on construction. A stored
                // pair that changes when reduced was not canonical, which means
                // the file was not written by this version and must not be
                // trusted to describe the mode we would restore.
                if (originalMode.RefreshRateNumerator != refreshNumerator
                    || originalMode.RefreshRateDenominator != refreshDenominator)
                {
                    throw new InvalidDataException(
                        "The watchdog refresh rational is not canonical.");
                }

                ValidateModeKey(originalMode);

                DateTime deadlineUtc = ParseDeadlineUtc(
                    fields[SessionKeys.DeadlineUtcTicks]);
                ValidateDeadline(deadlineUtc);

                DisplayWatchdogSessionState state =
                    new DisplayWatchdogSessionState();
                state.Token = storedToken;
                state.TargetIdentity = identity;
                state.OriginalMode = originalMode;
                state.DeadlineUtc = deadlineUtc;
                return state;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "The watchdog target identity or original mode is invalid.",
                    exception);
            }
        }

        internal static IList<string> ListSessionTokens()
        {
            string configuredRoot = GetConfiguredRootDirectory();
            if (!Directory.Exists(configuredRoot))
            {
                return new List<string>().AsReadOnly();
            }

            string root = VerifySecureRootDirectory();
            string[] files = Directory.GetFiles(
                root,
                "*.session",
                SearchOption.TopDirectoryOnly);
            if (files.Length > 16)
            {
                throw new InvalidDataException(
                    "The watchdog directory contains too many pending sessions.");
            }

            List<string> tokens = new List<string>();
            int index;
            for (index = 0; index < files.Length; index++)
            {
                if ((File.GetAttributes(files[index]) &
                    FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "A watchdog session must not be a reparse point.");
                }

                string fileName = Path.GetFileName(files[index]);
                const string suffix = ".session";
                if (fileName.Length != TokenByteCount * 2 + suffix.Length ||
                    !fileName.EndsWith(
                        suffix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "A watchdog session has a non-canonical file name.");
                }

                string token = fileName.Substring(
                    0,
                    fileName.Length - suffix.Length);
                try
                {
                    ValidateToken(token);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException(
                        "A watchdog session file name has an invalid token.",
                        exception);
                }

                string expected = NormalizePath(SessionPath(root, token));
                string actual = NormalizePath(files[index]);
                if (!PathsEqual(expected, actual))
                {
                    throw new InvalidDataException(
                        "A watchdog session resolved outside its canonical path.");
                }

                tokens.Add(token);
            }

            tokens.Sort(StringComparer.OrdinalIgnoreCase);
            return tokens.AsReadOnly();
        }

        internal static void WriteReady(string token)
        {
            WriteMarker(token, ".ready");
        }

        internal static bool IsReady(string token)
        {
            return ReadMarker(token, ".ready") == MarkerState.Valid;
        }

        internal static void WriteSignal(
            string token,
            DisplayWatchdogSignal signal)
        {
            if (signal != DisplayWatchdogSignal.Commit
                && signal != DisplayWatchdogSignal.Cancel)
            {
                throw new ArgumentOutOfRangeException(nameof(signal));
            }

            WriteMarker(
                token,
                signal == DisplayWatchdogSignal.Commit
                    ? ".commit"
                    : ".cancel");
        }

        internal static void WriteRollback(string token)
        {
            WriteMarker(token, ".rollback");
        }

        internal static DisplayWatchdogSignal ReadSignal(string token)
        {
            MarkerState commit = ReadMarker(token, ".commit");
            MarkerState cancel = ReadMarker(token, ".cancel");
            MarkerState rollback = ReadMarker(token, ".rollback");
            if (commit == MarkerState.Invalid
                || cancel == MarkerState.Invalid
                || rollback == MarkerState.Invalid
                || CountValid(commit, cancel, rollback) > 1)
            {
                return DisplayWatchdogSignal.Conflict;
            }

            if (commit == MarkerState.Valid)
            {
                return DisplayWatchdogSignal.Commit;
            }

            if (cancel == MarkerState.Valid)
            {
                return DisplayWatchdogSignal.Cancel;
            }

            if (rollback == MarkerState.Valid)
            {
                return DisplayWatchdogSignal.Rollback;
            }

            return DisplayWatchdogSignal.None;
        }

        internal static FileStream AcquirePersistenceLock(
            string token,
            TimeSpan timeout)
        {
            ValidateToken(token);
            if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromMinutes(2))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "The persistence-lock timeout must be between zero and 2 minutes.");
            }

            string root = VerifySecureRootDirectory();
            string path = SessionPath(root, token);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The watchdog session no longer exists.",
                    path);
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException(
                    "The watchdog session file must not be a reparse point.");
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    return new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        1,
                        FileOptions.None);
                }
                catch (FileNotFoundException)
                {
                    throw;
                }
                catch (DirectoryNotFoundException)
                {
                    throw;
                }
                catch (IOException)
                {
                    if (stopwatch.Elapsed >= timeout)
                    {
                        throw new TimeoutException(
                            "Timed out waiting for the watchdog persistence lock.");
                    }

                    Thread.Sleep(10);
                }
            }
        }

        internal static void Cleanup(string token)
        {
            ValidateToken(token);
            string root;
            try
            {
                root = VerifySecureRootDirectory();
            }
            catch
            {
                return;
            }

            TryDelete(SessionPath(root, token));
            TryDelete(MarkerPath(root, token, ".ready"));
            TryDelete(MarkerPath(root, token, ".commit"));
            TryDelete(MarkerPath(root, token, ".cancel"));
            TryDelete(MarkerPath(root, token, ".rollback"));
        }

        internal static void ValidateToken(string token)
        {
            if (token == null || token.Length != TokenByteCount * 2)
            {
                throw new ArgumentException(
                    "A 256-bit hexadecimal watchdog token is required.",
                    nameof(token));
            }

            for (int index = 0; index < token.Length; index++)
            {
                char character = token[index];
                bool hexadecimal =
                    (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F');
                if (!hexadecimal)
                {
                    throw new ArgumentException(
                        "The watchdog token contains a non-hexadecimal character.",
                        nameof(token));
                }
            }
        }

        private static string Serialize(DisplayWatchdogSessionState state)
        {
            if (state == null || state.TargetIdentity == null)
            {
                throw new ArgumentException(
                    "A durable watchdog monitor identity is required.",
                    nameof(state));
            }

            ValidateModeKey(state.OriginalMode);
            DisplayModeKey mode = state.OriginalMode;
            MonitorIdentity identity = state.TargetIdentity;

            // The base64 wrapper keeps the devnode instance ID, which contains
            // backslashes and ampersands, on a single unambiguous line.
            SessionWriter writer = new SessionWriter();
            writer.Add(SessionKeys.Format, SessionKeys.FormatValue);
            writer.Add(SessionKeys.Token, state.Token);
            writer.Add(
                SessionKeys.MonitorInstance,
                Convert.ToBase64String(Utf8.GetBytes(identity.MonitorInstanceId)));
            writer.Add(SessionKeys.PanelHardwareId, identity.PanelHardwareId);
            writer.Add(SessionKeys.ManufacturerCode, identity.ManufacturerCode);
            writer.Add(
                SessionKeys.EdidFingerprint,
                identity.EdidFingerprint.ToString());
            writer.Add(SessionKeys.Width, Number(mode.Width));
            writer.Add(SessionKeys.Height, Number(mode.Height));
            writer.Add(SessionKeys.BitsPerPixel, Number(mode.BitsPerPixel));
            writer.Add(SessionKeys.RefreshRate, Number(mode.RefreshRate));
            writer.Add(
                SessionKeys.RefreshNumerator,
                Number(mode.RefreshRateNumerator));
            writer.Add(
                SessionKeys.RefreshDenominator,
                Number(mode.RefreshRateDenominator));
            writer.Add(SessionKeys.Orientation, Number(mode.Orientation));
            writer.Add(SessionKeys.FixedOutput, Number(mode.FixedOutput));
            writer.Add(SessionKeys.DisplayFlags, Number(mode.DisplayFlags));
            writer.Add(
                SessionKeys.DeadlineUtcTicks,
                state.DeadlineUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            return writer.ToSessionText();
        }

        private static string Number(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Number(uint value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static void ValidateSessionInputs(
            MonitorIdentity targetIdentity,
            DisplayModeKey originalMode,
            TimeSpan timeout)
        {
            if (targetIdentity == null)
            {
                throw new ArgumentNullException(nameof(targetIdentity));
            }

            if (originalMode == null)
            {
                throw new ArgumentNullException(nameof(originalMode));
            }

            ValidateModeKey(originalMode);
            if (timeout < TimeSpan.FromSeconds(5)
                || timeout > TimeSpan.FromMinutes(1))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "The watchdog timeout must be between 5 seconds and 1 minute.");
            }
        }

        private static void ValidateModeKey(DisplayModeKey originalMode)
        {
            if (originalMode.Width <= 0
                || originalMode.Height <= 0
                || originalMode.BitsPerPixel <= 0
                || (originalMode.RefreshRate != 48 && originalMode.RefreshRate != 60)
                || originalMode.RefreshRateNumerator == 0
                || originalMode.RefreshRateDenominator == 0)
            {
                throw new ArgumentException(
                    "A complete reviewed 48/60 Hz display mode key is required.",
                    nameof(originalMode));
            }
        }

        private static DateTime ParseDeadlineUtc(string value)
        {
            long ticks;
            if (!long.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out ticks))
            {
                throw new InvalidDataException("The watchdog deadline is invalid.");
            }

            try
            {
                return new DateTime(ticks, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new InvalidDataException(
                    "The watchdog deadline is outside the supported range.",
                    exception);
            }
        }

        private static void ValidateDeadline(DateTime deadlineUtc)
        {
            DateTime now = DateTime.UtcNow;
            if (deadlineUtc > now.AddMinutes(2)
                || deadlineUtc < now.AddDays(-1))
            {
                throw new InvalidDataException(
                    "The watchdog deadline is outside the accepted window.");
            }
        }

        private static void WriteMarker(string token, string suffix)
        {
            ValidateToken(token);
            string root = VerifySecureRootDirectory();
            string path = MarkerPath(root, token, suffix);
            try
            {
                WriteNewFile(path, token + "\n");
            }
            catch (IOException)
            {
                if (ReadMarker(token, suffix) != MarkerState.Valid)
                {
                    throw;
                }
            }
        }

        private static MarkerState ReadMarker(string token, string suffix)
        {
            ValidateToken(token);
            string root = VerifySecureRootDirectory();
            string path = MarkerPath(root, token, suffix);
            if (!File.Exists(path))
            {
                return MarkerState.Missing;
            }

            try
            {
                string[] lines = ReadSmallFile(path);
                return lines.Length == 1 && FixedTimeComparer.AreEqual(token, lines[0])
                    ? MarkerState.Valid
                    : MarkerState.Invalid;
            }
            catch
            {
                return MarkerState.Invalid;
            }
        }

        private static string EnsureSecureRootDirectory()
        {
            lock (RootLock)
            {
                if (verifiedRoot != null)
                {
                    return verifiedRoot;
                }

                string root = GetConfiguredRootDirectory();
                DirectoryInfo directory = Directory.CreateDirectory(root);
                RejectReparsePoint(directory);

                if (testRoot != null)
                {
                    // The scoped root is created under the test process's
                    // temporary directory. Protocol tests exercise file
                    // format and locking here; production ACL enforcement
                    // remains unchanged below for the real state directory.
                    verifiedRoot = directory.FullName;
                    return verifiedRoot;
                }

                SecurityIdentifier user = CurrentUserSid();
                DirectorySecurity security = new DirectorySecurity();
                security.SetAccessRuleProtection(true, false);
                security.SetOwner(user);
                security.AddAccessRule(new FileSystemAccessRule(
                    user,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                directory.SetAccessControl(security);
                VerifyDirectoryAcl(directory, user);
                verifiedRoot = directory.FullName;
                return verifiedRoot;
            }
        }

        private static string VerifySecureRootDirectory()
        {
            lock (RootLock)
            {
                if (verifiedRoot != null)
                {
                    return verifiedRoot;
                }

                DirectoryInfo directory = new DirectoryInfo(
                    GetConfiguredRootDirectory());
                if (!directory.Exists)
                {
                    throw new DirectoryNotFoundException(
                        "The watchdog state directory does not exist.");
                }

                RejectReparsePoint(directory);
                if (testRoot != null)
                {
                    verifiedRoot = directory.FullName;
                    return verifiedRoot;
                }

                VerifyDirectoryAcl(directory, CurrentUserSid());
                verifiedRoot = directory.FullName;
                return verifiedRoot;
            }
        }

        private static string GetConfiguredRootDirectory()
        {
            return testRoot ?? GetProductionRootDirectory();
        }

        private static string GetProductionRootDirectory()
        {
            string localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException(
                    "The current user's LocalApplicationData path is unavailable.");
            }

            return Path.Combine(
                // Not the spaced product name. This directory holds pending
                // rollback sessions, and renaming it would strand any session
                // written by an earlier build exactly when recovery needs it.
                Path.Combine(localApplicationData, "MacBookEco"),
                DirectoryName);
        }

        private static string ValidateTestRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException(
                    "An isolated temporary watchdog test root is required.",
                    nameof(root));
            }

            string fullRoot = NormalizePath(root);
            string temporaryRoot = NormalizePath(Path.GetTempPath());
            if (!IsChildPath(fullRoot, temporaryRoot))
            {
                throw new ArgumentException(
                    "The watchdog test root must be beneath the process temporary directory.",
                    nameof(root));
            }

            if (PathsEqual(fullRoot, GetProductionRootDirectory()))
            {
                throw new ArgumentException(
                    "The watchdog test root must not be the production state directory.",
                    nameof(root));
            }

            return fullRoot;
        }

        private static bool IsChildPath(string path, string parent)
        {
            string parentWithSeparator = parent + Path.DirectorySeparatorChar;
            return path.StartsWith(
                parentWithSeparator,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                NormalizePath(left),
                NormalizePath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath);
            if (!string.Equals(
                    fullPath,
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                fullPath = fullPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }

            return fullPath;
        }

        private static void VerifyDirectoryAcl(
            DirectoryInfo directory,
            SecurityIdentifier user)
        {
            DirectorySecurity security = directory.GetAccessControl(
                AccessControlSections.Access | AccessControlSections.Owner);
            IdentityReference owner = security.GetOwner(
                typeof(SecurityIdentifier));
            if (!user.Equals(owner) || !security.AreAccessRulesProtected)
            {
                throw new UnauthorizedAccessException(
                    "The watchdog directory is not owned and protected by the current user.");
            }

            AuthorizationRuleCollection rules = security.GetAccessRules(
                true,
                true,
                typeof(SecurityIdentifier));
            foreach (AuthorizationRule authorizationRule in rules)
            {
                FileSystemAccessRule rule =
                    authorizationRule as FileSystemAccessRule;
                if (rule == null)
                {
                    continue;
                }

                SecurityIdentifier identity =
                    rule.IdentityReference as SecurityIdentifier;
                if (rule.AccessControlType == AccessControlType.Allow
                    && (identity == null || !identity.Equals(user)))
                {
                    throw new UnauthorizedAccessException(
                        "The watchdog directory grants access to another identity.");
                }
            }
        }

        private static SecurityIdentifier CurrentUserSid()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                if (identity.User == null)
                {
                    throw new InvalidOperationException(
                        "The current Windows user SID is unavailable.");
                }

                return identity.User;
            }
        }

        private static void RejectReparsePoint(DirectoryInfo directory)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException(
                    "The watchdog directory must not be a reparse point.");
            }
        }

        private static string GenerateToken()
        {
            byte[] bytes = new byte[TokenByteCount];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string[] ReadSmallFile(string path)
        {
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                if (stream.Length <= 0 || stream.Length > MaximumStateBytes)
                {
                    throw new InvalidDataException(
                        "The watchdog protocol file has an invalid size.");
                }

                using (StreamReader reader = new StreamReader(
                    stream,
                    Utf8,
                    true,
                    256))
                {
                    string content = reader.ReadToEnd();
                    return content.Replace("\r\n", "\n")
                        .TrimEnd('\n')
                        .Split(new[] { '\n' }, StringSplitOptions.None);
                }
            }
        }

        private static void WriteNewFile(string path, string content)
        {
            // Publish protocol files atomically. The watchdog polls markers,
            // so exposing the final filename while token bytes are only
            // partially written could otherwise be mistaken for tampering.
            string temporaryPath = path
                + "."
                + Guid.NewGuid().ToString("N")
                + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    512,
                    FileOptions.WriteThrough))
                using (StreamWriter writer = new StreamWriter(stream, Utf8))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(true);
                }

                File.Move(temporaryPath, path);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static string SessionPath(string root, string token)
        {
            return Path.Combine(root, token + ".session");
        }

        private static string MarkerPath(
            string root,
            string token,
            string suffix)
        {
            return Path.Combine(root, token + suffix);
        }

        private static int CountValid(
            MarkerState first,
            MarkerState second,
            MarkerState third)
        {
            int count = 0;
            if (first == MarkerState.Valid)
            {
                count++;
            }

            if (second == MarkerState.Valid)
            {
                count++;
            }

            if (third == MarkerState.Valid)
            {
                count++;
            }

            return count;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private sealed class TestRootScope : IDisposable
        {
            private readonly string root;
            private bool disposed;

            internal TestRootScope(string root)
            {
                this.root = root;
            }

            public void Dispose()
            {
                lock (RootLock)
                {
                    if (disposed)
                    {
                        return;
                    }

                    if (!PathsEqual(testRoot, root))
                    {
                        throw new InvalidOperationException(
                            "The watchdog test root scope was replaced unexpectedly.");
                    }

                    verifiedRoot = null;
                    testRoot = null;
                    disposed = true;
                }
            }
        }

        private enum MarkerState
        {
            Missing,
            Valid,
            Invalid
        }

        /// <summary>
        /// The session file's field names, in the exact order they appear.
        /// Serialization walks this array and parsing requires the same order,
        /// so the reader and the writer cannot describe different formats.
        /// </summary>
        private static class SessionKeys
        {
            internal const string Format = "format";
            internal const string Token = "token";
            internal const string MonitorInstance = "monitorInstance64";
            internal const string PanelHardwareId = "panelHardwareId";
            internal const string ManufacturerCode = "manufacturerCode";
            internal const string EdidFingerprint = "edidFingerprint";
            internal const string Width = "width";
            internal const string Height = "height";
            internal const string BitsPerPixel = "bitsPerPixel";
            internal const string RefreshRate = "refreshRate";
            internal const string RefreshNumerator = "refreshNumerator";
            internal const string RefreshDenominator = "refreshDenominator";
            internal const string Orientation = "orientation";
            internal const string FixedOutput = "fixedOutput";
            internal const string DisplayFlags = "displayFlags";
            internal const string DeadlineUtcTicks = "deadlineUtcTicks";

            internal static readonly string[] Ordered =
            {
                Format,
                Token,
                MonitorInstance,
                PanelHardwareId,
                ManufacturerCode,
                EdidFingerprint,
                Width,
                Height,
                BitsPerPixel,
                RefreshRate,
                RefreshNumerator,
                RefreshDenominator,
                Orientation,
                FixedOutput,
                DisplayFlags,
                DeadlineUtcTicks
            };

            internal const string FormatValue = "watchdog";
        }

        /// <summary>
        /// The writing half of the session format.
        ///
        /// The reader looks a value up by the key it was written under; the
        /// writer used to zip a bare array of values against
        /// <see cref="SessionKeys.Ordered"/> by position, so adding a key to
        /// one list without the other silently shifted every following value
        /// into the wrong name. Naming each value at the point it is written,
        /// and checking that name against the declared order, closes that.
        /// </summary>
        private sealed class SessionWriter
        {
            private readonly StringBuilder _text = new StringBuilder();
            private int _written;

            internal void Add(string key, string value)
            {
                if (_written >= SessionKeys.Ordered.Length ||
                    !string.Equals(
                        SessionKeys.Ordered[_written],
                        key,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The watchdog session writer does not follow the declared field order.");
                }

                _text.Append(key).Append('=').Append(value).Append('\n');
                _written++;
            }

            internal string ToSessionText()
            {
                if (_written != SessionKeys.Ordered.Length)
                {
                    throw new InvalidOperationException(
                        "The watchdog session writer did not write every declared field.");
                }

                return _text.ToString();
            }
        }

        /// <summary>
        /// A strict reader for the session file.
        ///
        /// This replaced a sixteen-clause StartsWith condition followed by
        /// sixteen Substring calls indexed by hand. It is exactly as strict:
        /// the field count, their order and their names must all match, and a
        /// value is only ever read through the key it was written under, so a
        /// field cannot silently be read from the wrong line.
        /// </summary>
        private sealed class SessionFields
        {
            private readonly Dictionary<string, string> _values;

            private SessionFields(Dictionary<string, string> values)
            {
                _values = values;
            }

            internal string this[string key]
            {
                get
                {
                    string value;
                    if (!_values.TryGetValue(key, out value))
                    {
                        throw new InvalidDataException(
                            "The watchdog session is missing '" + key + "'.");
                    }

                    return value;
                }
            }

            internal static SessionFields Parse(string[] lines)
            {
                if (lines == null || lines.Length != SessionKeys.Ordered.Length)
                {
                    throw new InvalidDataException(
                        "The watchdog session format is invalid.");
                }

                Dictionary<string, string> values =
                    new Dictionary<string, string>(StringComparer.Ordinal);
                for (int index = 0; index < SessionKeys.Ordered.Length; index++)
                {
                    string expectedKey = SessionKeys.Ordered[index];
                    string prefix = expectedKey + "=";
                    if (!lines[index].StartsWith(prefix, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "The watchdog session format is invalid.");
                    }

                    values.Add(expectedKey, lines[index].Substring(prefix.Length));
                }

                if (!string.Equals(
                    values[SessionKeys.Format],
                    SessionKeys.FormatValue,
                    StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The watchdog session format is invalid.");
                }

                return new SessionFields(values);
            }

            internal int UnsignedInt(string key)
            {
                int value;
                if (!int.TryParse(
                        this[key],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    throw new InvalidDataException(
                        "The watchdog " + key + " value is invalid.");
                }

                return value;
            }

            internal int SignedInt(string key)
            {
                int value;
                if (!int.TryParse(
                        this[key],
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    throw new InvalidDataException(
                        "The watchdog " + key + " value is invalid.");
                }

                return value;
            }

            internal uint UnsignedInt32(string key)
            {
                uint value;
                if (!uint.TryParse(
                        this[key],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out value))
                {
                    throw new InvalidDataException(
                        "The watchdog " + key + " value is invalid.");
                }

                return value;
            }
        }
    }
}

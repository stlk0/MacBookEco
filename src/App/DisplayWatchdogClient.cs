using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MacBookEco.Core;
using MacBookEco.DisplaySafety;

namespace MacBookEco.App
{
    internal sealed class DisplayWatchdogClient : IDisposable
    {
        private readonly Process _process;
        private readonly DisplayWatchdogSessionState _state;
        private bool _disposed;

        private DisplayWatchdogClient(
            Process process,
            DisplayWatchdogSessionState state)
        {
            _process = process;
            _state = state;
        }

        internal static DisplayWatchdogClient Start(
            string executablePath,
            MonitorIdentity targetIdentity,
            DisplayModeKey originalMode,
            TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException(
                    "A watchdog executable path is required.",
                    nameof(executablePath));
            }

            DisplayWatchdogSessionState state =
                DisplayWatchdogProtocol.CreateSession(
                    targetIdentity,
                    originalMode,
                    timeout);
            Process process = null;
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = executablePath;
                startInfo.Arguments = "watch " + state.Token;
                startInfo.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;

                process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException(
                        "Windows did not start the display watchdog.");
                }

                DisplayWatchdogClient client =
                    new DisplayWatchdogClient(process, state);
                if (!client.WaitUntilReady(TimeSpan.FromSeconds(3)))
                {
                    client.TrySignal(DisplayWatchdogSignal.Cancel);
                    client.Dispose();
                    throw new InvalidOperationException(
                        "The display watchdog did not become ready.");
                }

                return client;
            }
            catch
            {
                if (process != null)
                {
                    process.Dispose();
                }

                DisplayWatchdogProtocol.Cleanup(state.Token);
                throw;
            }
        }

        internal DisplayWatchdogPersistenceGuard AcquirePersistenceGuard()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("DisplayWatchdogClient");
            }

            return new DisplayWatchdogPersistenceGuard(
                _process,
                _state.Token);
        }

        internal bool WaitForCommitAcknowledgement()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("DisplayWatchdogClient");
            }

            if (!_process.HasExited && !_process.WaitForExit(5000))
            {
                return false;
            }

            return FinishExitedProcess();
        }

        internal bool CancelAndWait()
        {
            return SignalAndWait(DisplayWatchdogSignal.Cancel);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _process.Dispose();
        }

        private bool WaitUntilReady(TimeSpan timeout)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                if (_process.HasExited)
                {
                    return false;
                }

                if (DisplayWatchdogProtocol.IsReady(_state.Token))
                {
                    return true;
                }

                Thread.Sleep(25);
            }

            return false;
        }

        private bool SignalAndWait(DisplayWatchdogSignal signal)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("DisplayWatchdogClient");
            }

            DisplayWatchdogSignal current =
                DisplayWatchdogProtocol.ReadSignal(_state.Token);
            if (current == DisplayWatchdogSignal.Rollback)
            {
                if (!_process.HasExited)
                {
                    _process.WaitForExit(5000);
                }

                if (_process.HasExited
                    && _process.ExitCode
                        == DisplayWatchdogExitCodes.RollbackPerformed)
                {
                    DisplayWatchdogProtocol.Cleanup(_state.Token);
                    return true;
                }

                return false;
            }

            if (current != DisplayWatchdogSignal.None
                && current != signal)
            {
                return false;
            }

            if (_process.HasExited)
            {
                return FinishExitedProcess();
            }

            DisplayWatchdogProtocol.WriteSignal(_state.Token, signal);
            if (!_process.WaitForExit(5000))
            {
                return false;
            }

            return FinishExitedProcess();
        }

        private bool FinishExitedProcess()
        {
            bool completed = _process.ExitCode
                == DisplayWatchdogExitCodes.Completed;
            if (completed)
            {
                DisplayWatchdogProtocol.Cleanup(_state.Token);
            }

            return completed;
        }

        private void TrySignal(DisplayWatchdogSignal signal)
        {
            try
            {
                DisplayWatchdogProtocol.WriteSignal(_state.Token, signal);
                _process.WaitForExit(1000);
            }
            catch
            {
            }
        }
    }

    internal sealed class DisplayWatchdogPersistenceGuard : IDisposable
    {
        private readonly Process _watchdogProcess;
        private readonly string _token;
        private FileStream _persistenceLock;
        private bool _committed;

        internal DisplayWatchdogPersistenceGuard(
            Process watchdogProcess,
            string token)
        {
            if (watchdogProcess == null)
            {
                throw new ArgumentNullException(nameof(watchdogProcess));
            }

            _watchdogProcess = watchdogProcess;
            _token = token;
            _persistenceLock =
                DisplayWatchdogProtocol.AcquirePersistenceLock(
                    token,
                    TimeSpan.FromSeconds(5));
            try
            {
                VerifyCanPersist();
            }
            catch
            {
                _persistenceLock.Dispose();
                _persistenceLock = null;
                throw;
            }
        }

        /// <summary>
        /// Checked on construction and again immediately before the commit
        /// signal, so a caller that holds this guard does not need to ask.
        /// </summary>
        private void VerifyCanPersist()
        {
            ThrowIfDisposed();
            DisplayWatchdogSignal signal =
                DisplayWatchdogProtocol.ReadSignal(_token);
            if (signal == DisplayWatchdogSignal.Rollback)
            {
                throw new InvalidOperationException(
                    "The watchdog already won the deadline race and requested rollback.");
            }

            if (signal != DisplayWatchdogSignal.None)
            {
                throw new InvalidOperationException(
                    "The watchdog session is no longer eligible for persistence.");
            }

            if (_watchdogProcess.HasExited)
            {
                throw new InvalidOperationException(
                    "The watchdog exited before target persistence began.");
            }
        }

        internal void Commit()
        {
            VerifyCanPersist();
            DisplayWatchdogProtocol.WriteSignal(
                _token,
                DisplayWatchdogSignal.Commit);
            _committed = true;
        }

        public void Dispose()
        {
            FileStream persistenceLock = _persistenceLock;
            _persistenceLock = null;
            if (persistenceLock != null)
            {
                persistenceLock.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_persistenceLock == null)
            {
                throw new ObjectDisposedException(
                    "DisplayWatchdogPersistenceGuard");
            }

            if (_committed)
            {
                throw new InvalidOperationException(
                    "The watchdog persistence transaction is already committed.");
            }
        }
    }
}

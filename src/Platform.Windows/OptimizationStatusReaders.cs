using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MacBookEco.AppPolicy;
using MacBookEco.Core;

namespace MacBookEco.Platform.Windows
{
    public enum ManagedResourceState
    {
        NotInstalled,
        RecoveryRequired,
        Installed,
        Restored,
        Conflict
    }

    public sealed class DisplayOverrideStatus
    {
        public ManagedResourceState State { get; internal set; }
        public string ProfileId { get; internal set; }
    }

    public sealed class PowerSchemeStatus
    {
        public ManagedResourceState State { get; internal set; }
        public PowerPreset Preset { get; internal set; }
        public Guid ActiveScheme { get; internal set; }
        public Guid OriginalScheme { get; internal set; }
        public Guid OwnedScheme { get; internal set; }
        public bool OwnedSchemeRetained { get; internal set; }
    }

    public sealed class EdidStatusReader
    {
        private readonly InternalDisplayTargetResolver _targetResolver;

        public EdidStatusReader()
            : this(new InternalDisplayTargetResolver())
        {
        }

        internal EdidStatusReader(InternalDisplayTargetResolver targetResolver)
        {
            if (targetResolver == null)
                throw new ArgumentNullException(nameof(targetResolver));
            _targetResolver = targetResolver;
        }

        public DisplayOverrideStatus Read()
        {
            DisplayOverrideStatus status = new DisplayOverrideStatus();
            try
            {
                EdidJournal journal = JournalStore.ReadEdidStatus();
                if (journal == null)
                {
                    status.State = ManagedResourceState.NotInstalled;
                    return status;
                }

                status.State = Map(journal.State);
                if (journal.Payload != null && journal.Payload.Target != null)
                {
                    status.ProfileId = journal.Payload.Target.ProfileId;
                }

                if (journal.State == EdidJournalState.Installed ||
                    journal.State == EdidJournalState.Restored)
                {
                    status.State = ReadTerminalLiveState(journal);
                }

                return status;
            }
            catch (SecureStateException)
            {
                status.State = ManagedResourceState.Conflict;
                return status;
            }
            catch (JournalFormatException)
            {
                status.State = ManagedResourceState.Conflict;
                return status;
            }
        }

        private ManagedResourceState ReadTerminalLiveState(EdidJournal journal)
        {
            try
            {
                if (journal.Payload == null ||
                    journal.Payload.Target == null ||
                    journal.Payload.Target.Monitor == null)
                {
                    return ManagedResourceState.Conflict;
                }

                DisplayProfile profile = ProfileCatalog.GetById(
                    journal.Payload.Target.ProfileId);
                ResolvedMonitorTarget target =
                    _targetResolver.ResolveStoredForRestore(
                        journal.Payload.Target.Monitor,
                        journal.Payload.OwnedOverrideHash);
                if (!target.MatchesIdentity(
                        journal.Payload.Target.Monitor,
                        journal.Payload.OwnedOverrideHash))
                    return ManagedResourceState.Conflict;

                if (profile == null)
                {
                    return ClassifyTerminalState(
                        journal.State,
                        EdidRecoveryPolicy.ClassifyProtectedJournalOverride(
                            target.ReadOverride(),
                            journal.Payload.OwnedOverrideHash));
                }

                EdidBaseBlock originalEdid;
                if (!target.TryResolveOriginalBaseEdid(
                        journal.Payload.Target.Monitor,
                        out originalEdid))
                {
                    return ManagedResourceState.Conflict;
                }

                if (!string.Equals(
                        profile.PanelHardwareId,
                        target.HardwareId,
                        StringComparison.Ordinal) ||
                    !profile.NormalizedEdidSignature.Equals(
                        originalEdid.NormalizedSignature) ||
                    !profile.NativeTiming.Equals(originalEdid.PreferredTiming))
                {
                    return ManagedResourceState.Conflict;
                }

                byte[] expected = target.BaseEdidHash.Equals(
                    journal.Payload.OwnedOverrideHash)
                        ? target.BaseEdid
                        : profile.BuildOverride(originalEdid).ToByteArray();
                if (!Sha256Digest.Compute(expected).Equals(
                        journal.Payload.OwnedOverrideHash))
                {
                    return ManagedResourceState.Conflict;
                }

                return ClassifyTerminalState(
                    journal.State,
                    target.ClassifyOverride(expected));
            }
            catch (SecureStateConflictException)
            {
                return ManagedResourceState.Conflict;
            }
            catch (Exception)
            {
                return ManagedResourceState.RecoveryRequired;
            }
        }

        internal static ManagedResourceState ClassifyTerminalState(
            EdidJournalState journalState,
            EdidLiveOverrideState liveState)
        {
            if (journalState == EdidJournalState.Installed &&
                liveState == EdidLiveOverrideState.ExactOwned)
            {
                return ManagedResourceState.Installed;
            }

            if (journalState == EdidJournalState.Restored &&
                liveState == EdidLiveOverrideState.Absent)
            {
                return ManagedResourceState.Restored;
            }

            return ManagedResourceState.Conflict;
        }

        private static ManagedResourceState Map(EdidJournalState state)
        {
            switch (state)
            {
                case EdidJournalState.NotInstalled:
                    return ManagedResourceState.NotInstalled;
                case EdidJournalState.InstallPending:
                case EdidJournalState.RestorePending:
                    return ManagedResourceState.RecoveryRequired;
                case EdidJournalState.Installed:
                    return ManagedResourceState.Installed;
                case EdidJournalState.Restored:
                    return ManagedResourceState.Restored;
                default:
                    return ManagedResourceState.Conflict;
            }
        }
    }

    public sealed class PowerStatusReader
    {
        public PowerSchemeStatus Read()
        {
            PowerSchemeStatus status = new PowerSchemeStatus();
            try
            {
                // Inside the try: this runs in the unelevated tray process,
                // where live read failures degrade to a fail-closed status
                // rather than escaping into the state monitor.
                status.ActiveScheme = PowerSchemeNative.ReadActiveScheme();
                PowerJournal journal = JournalStore.ReadPowerStatus();
                if (journal == null)
                {
                    status.State = ManagedResourceState.NotInstalled;
                    return status;
                }

                status.State = Map(journal.State);
                if (journal.Payload != null && journal.Payload.Target != null)
                {
                    PowerTargetIdentity target = journal.Payload.Target;
                    status.Preset = ToPreset(target.Preset);
                    status.OriginalScheme = target.OriginalSchemeId;
                    status.OwnedScheme = target.OwnedSchemeId;
                    status.OwnedSchemeRetained =
                        journal.State == PowerJournalState.InactiveRetained;
                    if (journal.State == PowerJournalState.Applied ||
                        journal.State == PowerJournalState.InactiveRetained)
                    {
                        status.State = ClassifyTerminalState(
                            journal.State,
                            PowerManagedSettings.ClassifyOwnedScheme(target),
                            status.ActiveScheme == target.OwnedSchemeId);
                    }
                }
                else if (journal.State == PowerJournalState.Applied ||
                    journal.State == PowerJournalState.InactiveRetained)
                {
                    status.State = ManagedResourceState.Conflict;
                }

                return status;
            }
            catch (SecureStateException)
            {
                status.State = ManagedResourceState.Conflict;
                return status;
            }
            catch (JournalFormatException)
            {
                status.State = ManagedResourceState.Conflict;
                return status;
            }
            catch (Win32Exception)
            {
                status.State = ManagedResourceState.RecoveryRequired;
                return status;
            }
            catch (IOException)
            {
                status.State = ManagedResourceState.RecoveryRequired;
                return status;
            }
        }

        internal static ManagedResourceState ClassifyTerminalState(
            PowerJournalState journalState,
            PowerOwnedSchemeState ownedState,
            bool activeIsOwned)
        {
            if (ownedState != PowerOwnedSchemeState.ExactOwned)
            {
                return ManagedResourceState.Conflict;
            }

            if (journalState == PowerJournalState.Applied)
            {
                return activeIsOwned
                    ? ManagedResourceState.Installed
                    : ManagedResourceState.RecoveryRequired;
            }

            if (journalState == PowerJournalState.InactiveRetained)
            {
                return activeIsOwned
                    ? ManagedResourceState.RecoveryRequired
                    : ManagedResourceState.Restored;
            }

            return ManagedResourceState.Conflict;
        }

        private static PowerPreset ToPreset(PowerPresetId preset)
        {
            switch (preset)
            {
                case PowerPresetId.Normal:
                    return PowerPreset.Normal;
                case PowerPresetId.Cool:
                    return PowerPreset.Cool;
                case PowerPresetId.MaximumBattery:
                    return PowerPreset.MaximumBattery;
                default:
                    throw new SecureStateConflictException(
                        "The trusted power journal has an unknown preset.");
            }
        }

        private static ManagedResourceState Map(PowerJournalState state)
        {
            switch (state)
            {
                case PowerJournalState.NotManaged:
                    return ManagedResourceState.NotInstalled;
                case PowerJournalState.Creating:
                case PowerJournalState.RestorePending:
                    return ManagedResourceState.RecoveryRequired;
                case PowerJournalState.Applied:
                    return ManagedResourceState.Installed;
                case PowerJournalState.InactiveRetained:
                    return ManagedResourceState.Restored;
                default:
                    return ManagedResourceState.Conflict;
            }
        }
    }
}

using System;
using System.ComponentModel;
using System.Globalization;
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
        public bool ExperimentalProfile { get; internal set; }
        internal MonitorIdentity TargetMonitorIdentity { get; set; }
        internal Sha256Digest OwnedOverrideHash { get; set; }
        internal Sha256Digest SourceEdidSignature { get; set; }
    }

    public sealed class DisplayProfileCandidateStatus
    {
        public bool Available { get; internal set; }
        public bool EligibleForInstall { get; internal set; }
        public bool Experimental { get; internal set; }
        public string SafeSummary { get; internal set; }
        public string ReviewedProfileId { get; internal set; }
        internal string ExperimentalAcknowledgementToken { get; set; }
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
                    status.TargetMonitorIdentity =
                        journal.Payload.Target.Monitor;
                    status.OwnedOverrideHash =
                        journal.Payload.OwnedOverrideHash;
                    status.SourceEdidSignature =
                        journal.Payload.SourceEdidSignature;
                    status.ExperimentalProfile =
                        Experimental48HzProfileGenerator.IsExperimentalProfileId(
                            status.ProfileId);
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

                ResolvedMonitorTarget target =
                    _targetResolver.ResolveStoredForRestore(
                        journal.Payload.Target.Monitor,
                        journal.Payload.OwnedOverrideHash);
                if (!target.MatchesIdentity(
                        journal.Payload.Target.Monitor,
                        journal.Payload.OwnedOverrideHash))
                    return ManagedResourceState.Conflict;

                EdidBaseBlock baseEdid = new EdidBaseBlock(target.BaseEdid);
                bool experimental =
                    Experimental48HzProfileGenerator.IsExperimentalProfileId(
                        journal.Payload.Target.ProfileId);
                if (experimental)
                {
                    if (journal.Payload.SourceEdidSignature == null)
                    {
                        return ManagedResourceState.Conflict;
                    }

                    byte[] sourceEdid = target.SourceEdid;
                    if (EdidBaseBlock.HasValidCompleteDocument(sourceEdid))
                    {
                        if (!journal.Payload.SourceEdidSignature.Equals(
                                EdidBaseBlock
                                    .ComputeNormalizedDocumentSignature(
                                        sourceEdid)))
                        {
                            return ManagedResourceState.Conflict;
                        }
                    }
                    else if (sourceEdid == null ||
                        sourceEdid.Length != EdidBaseBlock.Length ||
                        !target.BaseEdidHash.Equals(
                            journal.Payload.OwnedOverrideHash))
                    {
                        return ManagedResourceState.Conflict;
                    }
                }

                DisplayProfile profile = ProfileCatalog.GetById(
                    journal.Payload.Target.ProfileId);
                if (profile == null)
                {
                    SmbiosIdentity identity = SmbiosReader.ReadIdentity();
                    if (identity == null || !string.Equals(
                            identity.Manufacturer,
                            "Apple Inc.",
                            StringComparison.Ordinal))
                    {
                        return ManagedResourceState.Conflict;
                    }

                    ExperimentalProfileGenerationResult generated =
                        Experimental48HzProfileGenerator.ResolveForRecovery(
                            journal.Payload.Target.ProfileId,
                            identity.ProductName,
                            target.HardwareId,
                            baseEdid,
                            journal.Payload.SourceEdidSignature);
                    if (!generated.Succeeded)
                    {
                        return ManagedResourceState.Conflict;
                    }

                    profile = generated.Profile;
                }

                if (!string.Equals(
                        profile.PanelHardwareId,
                        target.HardwareId,
                        StringComparison.Ordinal) ||
                    !profile.NormalizedEdidSignature.Equals(
                        baseEdid.NormalizedSignature) ||
                    !profile.NativeTiming.Equals(baseEdid.PreferredTiming))
                {
                    return ManagedResourceState.Conflict;
                }

                byte[] expected = profile
                    .CompileOverride(baseEdid)
                    .ToByteArray();
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

    /// <summary>
    /// Builds the bounded display-profile presentation and independently proves
    /// the read-only platform gates used to enable a fresh install. It exposes
    /// no raw EDID, monitor instance, registry path, or per-unit fingerprint.
    /// </summary>
    public sealed class DisplayProfileCandidateReader
    {
        private readonly HardwareDiscoveryService _discovery;
        private readonly InternalDisplayTargetResolver _targetResolver;

        public DisplayProfileCandidateReader()
            : this(
                new HardwareDiscoveryService(),
                new InternalDisplayTargetResolver())
        {
        }

        internal DisplayProfileCandidateReader(
            HardwareDiscoveryService discovery,
            InternalDisplayTargetResolver targetResolver)
        {
            if (discovery == null)
            {
                throw new ArgumentNullException(nameof(discovery));
            }

            if (targetResolver == null)
            {
                throw new ArgumentNullException(nameof(targetResolver));
            }

            _discovery = discovery;
            _targetResolver = targetResolver;
        }

        public DisplayProfileCandidateStatus Read(string storedProfileId)
        {
            DisplayProfileCandidateStatus status =
                new DisplayProfileCandidateStatus();
            status.SafeSummary = string.Empty;
            try
            {
                WindowsHardwareSnapshot snapshot = _discovery.Discover();
                HardwareSnapshot hardware = snapshot.ToCoreSnapshot();
                bool fresh = string.IsNullOrEmpty(storedProfileId);
                DisplayProfile profile;
                if (fresh)
                {
                    profile = SelectReviewedProfile(hardware);
                    if (profile == null)
                    {
                        if (!PassesExperimentalPlatformGates(
                                snapshot,
                                hardware))
                        {
                            return status;
                        }

                        ExperimentalProfileGenerationResult generated =
                            Experimental48HzProfileGenerator.GenerateFallback(
                                hardware);
                        if (!generated.Succeeded)
                        {
                            return status;
                        }

                        profile = generated.Profile;
                    }
                }
                else
                {
                    profile = ResolveStoredProfile(
                        storedProfileId,
                        hardware);
                }

                if (profile == null)
                {
                    return status;
                }

                DisplayProfileMatch match = profile.Match(hardware);
                if (!match.HardwareSupported)
                {
                    return status;
                }

                status.Available = true;
                status.Experimental = profile.IsExperimental;
                status.ReviewedProfileId = profile.IsExperimental
                    ? string.Empty
                    : profile.Id;
                status.SafeSummary = BuildSafeSummary(hardware, profile);
                status.EligibleForInstall = IsEligibleForFreshInstall(
                    snapshot,
                    hardware,
                    profile,
                    match);
                status.ExperimentalAcknowledgementToken =
                    profile.IsExperimental && status.EligibleForInstall
                        ? Experimental48HzProfileGenerator
                            .CreateAcknowledgementToken(profile.Id)
                        : null;
                return status;
            }
            catch (Exception)
            {
                return status;
            }
        }

        private static DisplayProfile ResolveStoredProfile(
            string storedProfileId,
            HardwareSnapshot hardware)
        {
            DisplayProfile reviewed = ProfileCatalog.GetById(storedProfileId);
            if (reviewed != null)
            {
                return reviewed;
            }

            if (Experimental48HzProfileGenerator.IsExperimentalProfileId(
                    storedProfileId))
            {
                if (!string.Equals(
                        hardware.SystemManufacturer,
                        "Apple Inc.",
                        StringComparison.Ordinal))
                {
                    return null;
                }

                ExperimentalProfileGenerationResult recovered =
                    Experimental48HzProfileGenerator.ResolveForRecovery(
                        storedProfileId,
                        hardware.SystemModel,
                        hardware.PanelHardwareId,
                        hardware.Edid,
                        ResolveStoredSourceSignature(
                            storedProfileId,
                            hardware.NormalizedSourceEdidSignature));
                return recovered.Succeeded ? recovered.Profile : null;
            }

            return null;
        }

        private static Sha256Digest ResolveStoredSourceSignature(
            string storedProfileId,
            Sha256Digest currentSignature)
        {
            if (currentSignature != null)
            {
                return currentSignature;
            }

            EdidJournal journal = JournalStore.ReadEdidStatus();
            if (journal == null || journal.Payload == null ||
                journal.Payload.Target == null ||
                !string.Equals(
                    journal.Payload.Target.ProfileId,
                    storedProfileId,
                    StringComparison.Ordinal))
            {
                return null;
            }

            return journal.Payload.SourceEdidSignature;
        }

        private static DisplayProfile SelectReviewedProfile(
            HardwareSnapshot hardware)
        {
            if (hardware == null ||
                !hardware.Edid.IsDetailedTimingDescriptor(0))
            {
                return null;
            }

            ProfileSelectionResult selection = ProfileCatalog.Select(hardware);
            return selection.HardwareSupported ? selection.Profile : null;
        }

        private bool PassesExperimentalPlatformGates(
            WindowsHardwareSnapshot snapshot,
            HardwareSnapshot hardware)
        {
            if (snapshot == null || snapshot.InternalDisplay == null ||
                string.IsNullOrEmpty(
                    snapshot.InternalDisplay.DeviceInstanceId) ||
                snapshot.ActiveDisplayCount != 1 ||
                snapshot.CurrentDisplayMode == null ||
                snapshot.CurrentDisplayMode.RefreshRate != 60 ||
                !snapshot.InternalDisplay.ExistingEdidOverrideReadSucceeded ||
                snapshot.InternalDisplay.ExistingEdidOverride != null ||
                hardware == null || !hardware.CompleteEdidIsValid ||
                hardware.NormalizedSourceEdidSignature == null)
            {
                return false;
            }

            ResolvedMonitorTarget target = _targetResolver.ResolveActive();
            return string.Equals(
                    NormalizeInstanceId(
                        snapshot.InternalDisplay.DeviceInstanceId),
                    target.DeviceInstanceId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    hardware.PanelHardwareId,
                    target.HardwareId,
                    StringComparison.Ordinal) &&
                snapshot.InternalDisplay.Endpoint != null &&
                target.Endpoint != null &&
                snapshot.InternalDisplay.Endpoint.Equals(target.Endpoint) &&
                FixedTimeComparer.AreEqual(
                    hardware.Edid.ToByteArray(),
                    target.BaseEdid) &&
                EdidBaseBlock.HasValidCompleteDocument(target.SourceEdid) &&
                hardware.NormalizedSourceEdidSignature.Equals(
                    EdidBaseBlock.ComputeNormalizedDocumentSignature(
                        target.SourceEdid)) &&
                target.ReadOverride() == null;
        }

        private bool IsEligibleForFreshInstall(
            WindowsHardwareSnapshot snapshot,
            HardwareSnapshot hardware,
            DisplayProfile profile,
            DisplayProfileMatch match)
        {
            if (snapshot == null || snapshot.InternalDisplay == null ||
                snapshot.ActiveDisplayCount != 1 ||
                snapshot.CurrentDisplayMode == null ||
                snapshot.CurrentDisplayMode.RefreshRate != 60 ||
                !match.CanInstall ||
                (profile.IsExperimental &&
                    !snapshot.InternalDisplay.ExistingEdidOverrideReadSucceeded) ||
                (profile.IsExperimental &&
                    snapshot.InternalDisplay.ExistingEdidOverride != null) ||
                (profile.IsExperimental && !hardware.CompleteEdidIsValid) ||
                (profile.IsExperimental &&
                    hardware.Edid.ContainsDetailedTiming(profile.TargetTiming)))
            {
                return false;
            }

            ResolvedMonitorTarget target = _targetResolver.ResolveActive();
            if (!string.Equals(
                    NormalizeInstanceId(
                        snapshot.InternalDisplay.DeviceInstanceId),
                    target.DeviceInstanceId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    hardware.PanelHardwareId,
                    target.HardwareId,
                    StringComparison.Ordinal) ||
                snapshot.InternalDisplay.Endpoint == null ||
                target.Endpoint == null ||
                !snapshot.InternalDisplay.Endpoint.Equals(target.Endpoint) ||
                !FixedTimeComparer.AreEqual(
                    hardware.Edid.ToByteArray(),
                    target.BaseEdid) ||
                target.ReadOverride() != null)
            {
                return false;
            }

            return true;
        }

        private static string BuildSafeSummary(
            HardwareSnapshot hardware,
            DisplayProfile profile)
        {
            DetailedTiming native = profile.NativeTiming;
            DetailedTiming target = profile.TargetTiming;
            return string.Format(
                CultureInfo.InvariantCulture,
                "Model {0}; panel {1}; controlling GPU {2}; native "
                    + "{3}x{4} @ {5:0.###} Hz, {6:0.00} MHz, totals {7}x{8}; "
                    + "{9} 48 Hz: {10:0.00} MHz, totals {11}x{12}.",
                hardware.SystemModel,
                hardware.PanelHardwareId,
                profile.VerifiedGpuDeviceIdPrefix,
                native.HorizontalActive,
                native.VerticalActive,
                native.RefreshRateHertz,
                native.PixelClockMegahertz,
                native.HorizontalTotal,
                native.VerticalTotal,
                profile.IsExperimental ? "calculated" : "reviewed",
                target.PixelClockMegahertz,
                target.HorizontalTotal,
                target.VerticalTotal);
        }

        private static string NormalizeInstanceId(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
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

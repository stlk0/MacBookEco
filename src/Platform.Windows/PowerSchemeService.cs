using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using MacBookEco.AppPolicy;
using MacBookEco.Core;

namespace MacBookEco.Platform.Windows
{
    /// <summary>
    /// Creates an app-owned copy of the active scheme. All durable state is
    /// fixed-name, strict, handle-validated journal data.
    /// </summary>
    public sealed class PowerSchemeService
    {
        private readonly Func<SmbiosIdentity> _readSmbiosIdentity;
        public PowerSchemeService()
            : this(SmbiosReader.ReadIdentity)
        {
        }

        internal PowerSchemeService(Func<SmbiosIdentity> readSmbiosIdentity)
        {
            if (readSmbiosIdentity == null)
            {
                throw new ArgumentNullException(nameof(readSmbiosIdentity));
            }

            _readSmbiosIdentity = readSmbiosIdentity;
        }

        public PowerSchemeOperationResult ApplyPreset(PowerPreset preset)
        {
            PrivilegeGuard.RequireAdministrator();
            PowerPresetCatalog.Get(preset);
            RequireSupportedHardware();

            using (JournalStore journals = JournalStore.OpenPowerMutation())
            {
                PowerJournal existing = journals.ReadPower();
                if (existing == null ||
                    existing.State == PowerJournalState.NotManaged)
                {
                    return StartNewApply(journals, existing, preset);
                }

                ValidateJournalPolicy(existing);
                switch (existing.State)
                {
                    case PowerJournalState.Creating:
                    {
                        PowerSchemeOperationResult resumed =
                            ContinueCreating(journals, existing);
                        if (!resumed.Succeeded ||
                            existing.Payload.Target.Preset == ToJournalPreset(preset))
                        {
                            return resumed;
                        }

                        PowerJournal completed = journals.ReadPower();
                        if (completed == null ||
                            completed.State != PowerJournalState.Applied)
                        {
                            return PowerSchemeOperationResult.Indeterminate(
                                "The earlier durable power request completed, but its applied state could not be reloaded for preset switching.",
                                resumed.OriginalScheme,
                                resumed.OwnedScheme,
                                resumed.SettingResults,
                                false);
                        }

                        ValidateJournalPolicy(completed);
                        return BeginPresetChange(journals, completed, preset);
                    }
                    case PowerJournalState.Applied:
                        return ApplyFromApplied(journals, existing, preset);
                    case PowerJournalState.InactiveRetained:
                        return ReactivateRetained(journals, existing, preset);
                    case PowerJournalState.RestorePending:
                        return PowerSchemeOperationResult.Failed(
                            "A durable restore is pending. Run restore-power to reconcile it first.",
                            existing.Payload.Target.OriginalSchemeId,
                            existing.Payload.Target.OwnedSchemeId,
                            EmptySettingResults(),
                            true);
                    case PowerJournalState.Conflict:
                        return PowerSchemeOperationResult.Failed(
                            "The trusted power journal is conflicted and will not be changed automatically.",
                            existing.Payload.Target.OriginalSchemeId,
                            existing.Payload.Target.OwnedSchemeId,
                            EmptySettingResults(),
                            true);
                    default:
                        return PowerSchemeOperationResult.Failed(
                            "The trusted power journal has an unsupported state.",
                            Guid.Empty,
                            Guid.Empty,
                            EmptySettingResults(),
                            false);
                }
            }
        }

        private void RequireSupportedHardware()
        {
            SmbiosIdentity identity = _readSmbiosIdentity();
            CpuHardwareSupportStatus support = CpuHardwareSupportPolicy.Classify(
                identity == null ? null : identity.Manufacturer,
                identity == null ? null : identity.ProductName);
            if (support != CpuHardwareSupportStatus.Supported)
            {
                throw new NotSupportedException(
                    CpuHardwareSupportPolicy.UserMessage(support));
            }
        }

        public PowerSchemeOperationResult RestoreOriginal()
        {
            PrivilegeGuard.RequireAdministrator();
            using (JournalStore journals = JournalStore.OpenPowerMutation())
            {
                PowerJournal journal = journals.ReadPower();
                if (journal == null || journal.State == PowerJournalState.NotManaged)
                {
                    throw new InvalidOperationException(
                        "No trusted MacBook Eco power transaction exists.");
                }

                if (journal.State == PowerJournalState.Conflict)
                {
                    return PowerSchemeOperationResult.Failed(
                        "The trusted power journal is conflicted and will not be changed automatically.",
                        journal.Payload.Target.OriginalSchemeId,
                        journal.Payload.Target.OwnedSchemeId,
                        EmptySettingResults(),
                        true);
                }

                ValidateJournalPolicy(journal);
                if (journal.State == PowerJournalState.Creating)
                {
                    PowerSchemeOperationResult resumed =
                        ContinueCreating(journals, journal);
                    if (!resumed.Succeeded)
                        return resumed;

                    journal = journals.ReadPower();
                    if (journal == null || journal.State != PowerJournalState.Applied)
                    {
                        return PowerSchemeOperationResult.Indeterminate(
                            "The pending power request was reconciled, but its durable final state could not be confirmed.",
                            resumed.OriginalScheme,
                            resumed.OwnedScheme,
                            resumed.SettingResults,
                            false);
                    }

                    ValidateJournalPolicy(journal);
                }

                PowerTargetIdentity target = journal.Payload.Target;
                if (journal.State == PowerJournalState.InactiveRetained)
                {
                    if (PowerManagedSettings.ClassifyOwnedScheme(
                            target) != PowerOwnedSchemeState.ExactOwned)
                    {
                        return SaveConflict(
                            journals,
                            journal,
                            "The retained app-owned power scheme no longer matches its trusted identity.",
                            false);
                    }

                    Guid retainedActive = PowerSchemeNative.ReadActiveScheme();
                    PowerActiveSchemeRelation retainedRelation =
                        ClassifyActiveRelation(target, retainedActive);
                    if (retainedRelation == PowerActiveSchemeRelation.Original)
                    {
                        return PowerSchemeOperationResult.Success(
                            "The original scheme is already active. The app-owned scheme was retained.",
                            target.OriginalSchemeId,
                            target.OwnedSchemeId,
                            EmptySettingResults(),
                            true);
                    }

                    if (retainedRelation == PowerActiveSchemeRelation.External)
                    {
                        return PowerSchemeOperationResult.Success(
                            "Another application or the user selected a different power scheme. "
                                + "MacBook Eco left that selection unchanged.",
                            target.OriginalSchemeId,
                            target.OwnedSchemeId,
                            EmptySettingResults(),
                            true);
                    }

                    journal = journals.SavePower(journal.TransitionTo(
                        PowerJournalState.RestorePending,
                        new PowerJournalPayload(
                            target,
                            PowerInactiveReason.None),
                        journal.Generation.Next(),
                        DateTime.UtcNow));
                }

                Guid active = PowerSchemeNative.ReadActiveScheme();
                if (journal.State == PowerJournalState.Applied)
                {
                    PowerReconciliationAction action =
                        PowerRecoveryPolicy.ForApplied(
                            PowerManagedSettings.ClassifyOwnedScheme(target),
                            PowerSchemeNative.SchemeExists(target.OriginalSchemeId),
                            ClassifyActiveRelation(target, active));
                    if (action == PowerReconciliationAction.Conflict)
                    {
                        return SaveConflict(
                            journals,
                            journal,
                            "The trusted power journal and live schemes disagree. " +
                            "MacBook Eco left Windows power selection unchanged.",
                            false);
                    }

                    if (action == PowerReconciliationAction.RetainOriginalSelection)
                    {
                        return CompleteInactiveRetained(
                            journals,
                            journal,
                            PowerInactiveReason.OriginalAlreadyActive,
                            "The original scheme is already active. The app-owned scheme was retained.",
                            false);
                    }

                    if (action == PowerReconciliationAction.RetainExternalSelection)
                    {
                        return CompleteInactiveRetained(
                            journals,
                            journal,
                            PowerInactiveReason.ExternalSelection,
                            "Another application or the user selected a different power scheme. " +
                            "MacBook Eco left that selection unchanged.",
                            false);
                    }

                    journal = journals.SavePower(journal.TransitionTo(
                        PowerJournalState.RestorePending,
                        journal.Payload,
                        journal.Generation.Next(),
                        DateTime.UtcNow));
                }

                target = journal.Payload.Target;
                active = PowerSchemeNative.ReadActiveScheme();
                PowerReconciliationAction restoreAction =
                    PowerRecoveryPolicy.ForRestorePending(
                        PowerManagedSettings.ClassifyOwnedScheme(target),
                        PowerSchemeNative.SchemeExists(target.OriginalSchemeId),
                        ClassifyActiveRelation(target, active));
                if (restoreAction == PowerReconciliationAction.Conflict)
                {
                    return SaveConflict(
                        journals,
                        journal,
                        "The durable restore request no longer matches the live power schemes.",
                        false);
                }

                if (restoreAction == PowerReconciliationAction.RetainExternalSelection)
                {
                    return CompleteInactiveRetained(
                        journals,
                        journal,
                        PowerInactiveReason.ExternalSelection,
                        "Another application or the user selected a different power scheme. " +
                        "MacBook Eco left that selection unchanged.",
                        false);
                }

                if (restoreAction == PowerReconciliationAction.CompleteRetainedOriginal)
                {
                    return CompleteInactiveRetained(
                        journals,
                        journal,
                        PowerInactiveReason.OriginalAlreadyActive,
                        "The original scheme is active. The app-owned scheme was retained for manual cleanup.",
                        false);
                }

                try
                {
                    PowerSchemeNative.SetActiveScheme(target.OriginalSchemeId);
                    if (PowerSchemeNative.ReadActiveScheme() != target.OriginalSchemeId)
                    {
                        return PowerSchemeOperationResult.Indeterminate(
                            "The original power scheme was selected, but active-scheme read-back did not confirm it. " +
                            "The durable restore request remains for retry.",
                            target.OriginalSchemeId,
                            target.OwnedSchemeId,
                            EmptySettingResults(),
                            false);
                    }
                }
                catch (Exception exception)
                {
                    return PowerSchemeOperationResult.Indeterminate(
                        "The original power scheme could not be verified after activation: " +
                        SafeExceptionMessage(exception),
                        target.OriginalSchemeId,
                        target.OwnedSchemeId,
                        EmptySettingResults(),
                        false);
                }

                return CompleteInactiveRetained(
                    journals,
                    journal,
                    PowerInactiveReason.OriginalAlreadyActive,
                    "The original scheme is active. The app-owned scheme was retained for manual cleanup.",
                    true);
            }
        }

        private static PowerSchemeOperationResult StartNewApply(
            JournalStore journals,
            PowerJournal previous,
            PowerPreset preset)
        {
            Guid original = PowerSchemeNative.ReadActiveScheme();
            if (!PowerSchemeNative.SchemeExists(original))
            {
                return PowerSchemeOperationResult.Failed(
                    "The active Windows power scheme no longer exists.",
                    original,
                    Guid.Empty,
                    EmptySettingResults(),
                    false);
            }

            Guid owned = AllocateUnusedSchemeGuid();
            PowerTargetIdentity target = new PowerTargetIdentity(
                original,
                owned,
                ToJournalPreset(preset),
                PowerManagedSettings.ComputeManagedSettingsHash(preset));
            DateTime now = DateTime.UtcNow;
            PowerJournal creating = new PowerJournal(
                JournalOperationId.NewId(),
                NextGeneration(previous),
                now,
                now,
                PowerJournalState.Creating,
                new PowerJournalPayload(target, PowerInactiveReason.None));

            // This verified save is intentionally before PowerDuplicateScheme,
            // friendly-name write, setting write, or activation.  A failed
            // intent persistence therefore has no native side effect.
            creating = journals.SavePower(creating);
            return ContinueCreating(journals, creating);
        }

        private static PowerSchemeOperationResult ApplyFromApplied(
            JournalStore journals,
            PowerJournal journal,
            PowerPreset requestedPreset)
        {
            PowerTargetIdentity target = journal.Payload.Target;
            PowerReconciliationAction action = PowerRecoveryPolicy.ForApplied(
                PowerManagedSettings.ClassifyOwnedScheme(target),
                PowerSchemeNative.SchemeExists(target.OriginalSchemeId),
                ClassifyActiveRelation(target, PowerSchemeNative.ReadActiveScheme()));
            if (action == PowerReconciliationAction.Conflict)
            {
                return SaveConflict(
                    journals,
                    journal,
                    "The trusted applied power transaction no longer matches the live schemes.",
                    false);
            }

            if (action == PowerReconciliationAction.ConfirmApplied)
            {
                if (target.Preset != ToJournalPreset(requestedPreset))
                {
                    return BeginPresetChange(
                        journals,
                        journal,
                        requestedPreset);
                }

                return ConfigureActivateAndComplete(
                    journals,
                    journal,
                    "The managed power settings were reapplied and verified.");
            }

            PowerInactiveReason reason =
                action == PowerReconciliationAction.RetainOriginalSelection
                    ? PowerInactiveReason.OriginalAlreadyActive
                    : PowerInactiveReason.ExternalSelection;
            PowerJournal retained;
            try
            {
                retained = TransitionToInactiveRetained(
                    journals,
                    journal,
                    reason);
            }
            catch (Exception exception)
            {
                return PowerSchemeOperationResult.Failed(
                    "The current external power selection was observed, but could not be recorded: " +
                    SafeExceptionMessage(exception),
                    target.OriginalSchemeId,
                    target.OwnedSchemeId,
                    EmptySettingResults(),
                    true);
            }

            if (target.Preset != ToJournalPreset(requestedPreset))
            {
                return BeginPresetChange(
                    journals,
                    retained,
                    requestedPreset);
            }

            return ReactivateRetained(journals, retained, requestedPreset);
        }

        private static PowerSchemeOperationResult ReactivateRetained(
            JournalStore journals,
            PowerJournal journal,
            PowerPreset requestedPreset)
        {
            PowerTargetIdentity target = journal.Payload.Target;
            Guid active = PowerSchemeNative.ReadActiveScheme();
            PowerReconciliationAction action =
                PowerRecoveryPolicy.ForInactiveRetained(
                    PowerManagedSettings.ClassifyOwnedScheme(target),
                    active == target.OwnedSchemeId);
            if (action == PowerReconciliationAction.Conflict)
            {
                return SaveConflict(
                    journals,
                    journal,
                    "The retained app-owned scheme is missing or no longer matches its trusted identity.",
                    false);
            }

            if (target.Preset != ToJournalPreset(requestedPreset))
            {
                return BeginPresetChange(
                    journals,
                    journal,
                    requestedPreset);
            }

            if (action == PowerReconciliationAction.ConfigureAndActivate)
            {
                if (!PowerSchemeNative.SchemeExists(target.OriginalSchemeId))
                {
                    return SaveConflict(
                        journals,
                        journal,
                        "The recorded return power scheme no longer exists.",
                        false);
                }

                return ConfigureActivateAndComplete(
                    journals,
                    journal,
                    "The earlier activation was confirmed and the managed settings were verified.");
            }

            // Capture the exact live return target before any managed setting
            // is touched.  Creating then gives a crash after partial setting
            // work the same safe reconciliation path as a first apply.
            if (!PowerSchemeNative.SchemeExists(active))
            {
                return SaveConflict(
                    journals,
                    journal,
                    "The currently active return power scheme no longer exists.",
                    false);
            }

            PowerTargetIdentity updatedTarget = target.WithOriginalScheme(active);
            PowerJournal creating = journal.TransitionTo(
                PowerJournalState.Creating,
                new PowerJournalPayload(updatedTarget, PowerInactiveReason.None),
                journal.Generation.Next(),
                DateTime.UtcNow);
            try
            {
                creating = journals.SavePower(creating);
            }
            catch (Exception exception)
            {
                return PowerSchemeOperationResult.Failed(
                    "The new power-session return target could not be recorded before mutation: " +
                    SafeExceptionMessage(exception),
                    target.OriginalSchemeId,
                    target.OwnedSchemeId,
                    EmptySettingResults(),
                    true);
            }

            return ContinueCreating(journals, creating);
        }

        private static PowerSchemeOperationResult BeginPresetChange(
            JournalStore journals,
            PowerJournal journal,
            PowerPreset requestedPreset)
        {
            PowerTargetIdentity current = journal.Payload.Target;
            if (PowerManagedSettings.ClassifyOwnedScheme(current) !=
                PowerOwnedSchemeState.ExactOwned)
            {
                return SaveConflict(
                    journals,
                    journal,
                    "The app-owned scheme no longer matches its trusted identity and cannot be reconfigured.",
                    false);
            }

            Guid returnScheme = current.OriginalSchemeId;
            Guid active = PowerSchemeNative.ReadActiveScheme();
            if (active != current.OwnedSchemeId)
            {
                returnScheme = active;
            }

            if (!PowerSchemeNative.SchemeExists(returnScheme))
            {
                return SaveConflict(
                    journals,
                    journal,
                    "The return power scheme for the requested preset no longer exists.",
                    false);
            }

            PowerTargetIdentity requested = new PowerTargetIdentity(
                returnScheme,
                current.OwnedSchemeId,
                ToJournalPreset(requestedPreset),
                PowerManagedSettings.ComputeManagedSettingsHash(requestedPreset));
            PowerJournal creating = journal.TransitionTo(
                PowerJournalState.Creating,
                new PowerJournalPayload(
                    requested,
                    PowerInactiveReason.None),
                journal.Generation.Next(),
                DateTime.UtcNow);
            try
            {
                creating = journals.SavePower(creating);
            }
            catch (Exception exception)
            {
                return PowerSchemeOperationResult.Failed(
                    "The requested preset switch could not be recorded before mutation: "
                    + SafeExceptionMessage(exception),
                    current.OriginalSchemeId,
                    current.OwnedSchemeId,
                    EmptySettingResults(),
                    journal.State == PowerJournalState.InactiveRetained);
            }

            return ContinueCreating(journals, creating);
        }

        private static PowerSchemeOperationResult ContinueCreating(
            JournalStore journals,
            PowerJournal journal)
        {
            ValidateJournalPolicy(journal);
            PowerTargetIdentity target = journal.Payload.Target;
            PowerPreset preset = FromJournalPreset(target.Preset);
            PowerOwnedSchemeState destination =
                ClassifyOwnedSchemeForCreating(target);
            PowerReconciliationAction action = PowerRecoveryPolicy.ForCreating(
                destination,
                PowerSchemeNative.SchemeExists(target.OriginalSchemeId));
            if (action == PowerReconciliationAction.Conflict)
            {
                return SaveConflict(
                    journals,
                    journal,
                    "The durable creating request cannot prove ownership of its destination scheme.",
                    false);
            }

            if (action == PowerReconciliationAction.DuplicateWithRecordedGuid)
            {
                try
                {
                    PowerSchemeNative.DuplicateScheme(
                        target.OriginalSchemeId,
                        target.OwnedSchemeId);
                }
                catch (Win32Exception exception)
                {
                    // ERROR_ALREADY_EXISTS or an interruption can race the
                    // native call.  Re-read under the same journal lock; a
                    // retry is never authorized from a swallowed API error.
                    destination = PowerManagedSettings.ClassifyOwnedMarker(target);
                    if (destination == PowerOwnedSchemeState.ExactOwned)
                    {
                        action = PowerReconciliationAction.ConfigureAndActivate;
                    }
                    else if (destination == PowerOwnedSchemeState.ForeignOrDiverged)
                    {
                        return SaveConflict(
                            journals,
                            journal,
                            "A scheme appeared at the recorded destination GUID but ownership is ambiguous.",
                            true);
                    }
                    else
                    {
                        return PowerSchemeOperationResult.Failed(
                            "PowerDuplicateScheme failed before a recoverable destination was observed: " +
                            SafeExceptionMessage(exception),
                            target.OriginalSchemeId,
                            target.OwnedSchemeId,
                            EmptySettingResults(),
                            false);
                    }
                }

                if (action == PowerReconciliationAction.DuplicateWithRecordedGuid)
                {
                    if (!PowerSchemeNative.SchemeExists(target.OwnedSchemeId))
                    {
                        return PowerSchemeOperationResult.Indeterminate(
                            "PowerDuplicateScheme returned, but the recorded destination could not be read back. " +
                            "The Creating journal remains for retry.",
                            target.OriginalSchemeId,
                            target.OwnedSchemeId,
                            EmptySettingResults(),
                            false);
                    }

                    PowerSchemeNative.WriteFriendlyName(
                        target.OwnedSchemeId,
                        PowerSchemeNaming.OwnedFriendlyName(preset, target.OwnedSchemeId));
                    destination = PowerManagedSettings.ClassifyOwnedMarker(target);
                    if (destination != PowerOwnedSchemeState.ExactOwned)
                    {
                        return SaveConflict(
                            journals,
                            journal,
                            "The duplicated power scheme did not retain its exact app-owned identity marker.",
                            true);
                    }
                }
            }

            if (PowerManagedSettings.ClassifyOwnedMarker(target) !=
                PowerOwnedSchemeState.ExactOwned)
            {
                if (destination != PowerOwnedSchemeState.ExactOwned &&
                    destination != PowerOwnedSchemeState.UnmarkedDuplicate)
                {
                    return SaveConflict(
                        journals,
                        journal,
                        "The creating request found a power scheme that is not a recognized app-owned preset.",
                        true);
                }

                PowerSchemeNative.WriteFriendlyName(
                    target.OwnedSchemeId,
                    PowerSchemeNaming.OwnedFriendlyName(preset, target.OwnedSchemeId));
                if (PowerManagedSettings.ClassifyOwnedMarker(target) !=
                    PowerOwnedSchemeState.ExactOwned)
                {
                    return SaveConflict(
                        journals,
                        journal,
                        "The reconfigured power scheme did not retain its exact app-owned identity marker.",
                        true);
                }
            }

            return ConfigureActivateAndComplete(
                journals,
                journal,
                "The app-owned " + PresetName(preset) +
                " power scheme is active.");
        }

        private static PowerSchemeOperationResult ConfigureActivateAndComplete(
            JournalStore journals,
            PowerJournal journal,
            string successMessage)
        {
            PowerTargetIdentity target = journal.Payload.Target;
            PowerPreset preset = FromJournalPreset(target.Preset);
            PowerSettingsConfigurationResult configuration =
                ConfigureOwnedScheme(target.OwnedSchemeId, preset);
            if (!configuration.Succeeded)
            {
                return PowerSchemeOperationResult.Failed(
                    "A managed power setting could not be verified. The durable power state remains for retry.",
                    target.OriginalSchemeId,
                    target.OwnedSchemeId,
                    configuration.Results,
                    false);
            }

            try
            {
                PowerSchemeNative.SetActiveScheme(target.OwnedSchemeId);
                if (PowerSchemeNative.ReadActiveScheme() != target.OwnedSchemeId)
                {
                    return PowerSchemeOperationResult.Indeterminate(
                        "The app-owned scheme was selected, but active-scheme read-back did not confirm it. " +
                        "The durable power state remains for retry.",
                        target.OriginalSchemeId,
                        target.OwnedSchemeId,
                        configuration.Results,
                        false);
                }
            }
            catch (Exception exception)
            {
                return PowerSchemeOperationResult.Indeterminate(
                    "The app-owned scheme could not be verified after activation: " +
                    SafeExceptionMessage(exception),
                    target.OriginalSchemeId,
                    target.OwnedSchemeId,
                    configuration.Results,
                    false);
            }

            return CompleteApplied(
                journals,
                journal,
                configuration.Results,
                successMessage);
        }

        private static PowerSchemeOperationResult CompleteApplied(
            JournalStore journals,
            PowerJournal journal,
            IList<PowerSettingOperationResult> settings,
            string message)
        {
            PowerTargetIdentity target = journal.Payload.Target;
            PowerJournal applied = journal.TransitionTo(
                PowerJournalState.Applied,
                new PowerJournalPayload(target, PowerInactiveReason.None),
                journal.Generation.Next(),
                DateTime.UtcNow);
            try
            {
                applied = journals.SavePower(applied);
            }
            catch (Exception exception)
            {
                return PowerSchemeOperationResult.Indeterminate(
                    "The power scheme is active, but the terminal journal save could not be verified: " +
                    SafeExceptionMessage(exception),
                    target.OriginalSchemeId,
                    target.OwnedSchemeId,
                    settings,
                    false);
            }

            return PowerSchemeOperationResult.Success(
                message,
                applied.Payload.Target.OriginalSchemeId,
                applied.Payload.Target.OwnedSchemeId,
                settings,
                false);
        }

        private static PowerJournal TransitionToInactiveRetained(
            JournalStore journals,
            PowerJournal journal,
            PowerInactiveReason reason)
        {
            PowerJournalPayload payload = new PowerJournalPayload(
                journal.Payload.Target,
                reason);
            PowerJournal retained = journal.TransitionTo(
                PowerJournalState.InactiveRetained,
                payload,
                journal.Generation.Next(),
                DateTime.UtcNow);
            return journals.SavePower(retained);
        }

        private static PowerSchemeOperationResult CompleteInactiveRetained(
            JournalStore journals,
            PowerJournal journal,
            PowerInactiveReason reason,
            string message,
            bool nativeMutationAlreadyOccurred)
        {
            PowerTargetIdentity target = journal.Payload.Target;
            try
            {
                PowerJournal retained = TransitionToInactiveRetained(
                    journals,
                    journal,
                    reason);
                return PowerSchemeOperationResult.Success(
                    message,
                    retained.Payload.Target.OriginalSchemeId,
                    retained.Payload.Target.OwnedSchemeId,
                    EmptySettingResults(),
                    true);
            }
            catch (Exception exception)
            {
                string detail = "The retained power state could not be durably recorded: " +
                    SafeExceptionMessage(exception);
                return nativeMutationAlreadyOccurred
                    ? PowerSchemeOperationResult.Indeterminate(
                        detail,
                        target.OriginalSchemeId,
                        target.OwnedSchemeId,
                        EmptySettingResults(),
                        true)
                    : PowerSchemeOperationResult.Failed(
                        detail,
                        target.OriginalSchemeId,
                        target.OwnedSchemeId,
                        EmptySettingResults(),
                        true);
            }
        }

        private static PowerSchemeOperationResult SaveConflict(
            JournalStore journals,
            PowerJournal journal,
            string message,
            bool nativeMutationMayHaveOccurred)
        {
            PowerTargetIdentity target = journal.Payload.Target;
            PowerJournal conflict = journal.TransitionTo(
                PowerJournalState.Conflict,
                new PowerJournalPayload(target, PowerInactiveReason.None),
                journal.Generation.Next(),
                DateTime.UtcNow);
            try
            {
                conflict = journals.SavePower(conflict);
            }
            catch (Exception exception)
            {
                string detail = message + " Durable conflict persistence failed: " +
                    SafeExceptionMessage(exception);
                return nativeMutationMayHaveOccurred
                    ? PowerSchemeOperationResult.Indeterminate(
                        detail,
                        target.OriginalSchemeId,
                        target.OwnedSchemeId,
                        EmptySettingResults(),
                        false)
                    : PowerSchemeOperationResult.Failed(
                        detail,
                        target.OriginalSchemeId,
                        target.OwnedSchemeId,
                        EmptySettingResults(),
                        false);
            }

            return PowerSchemeOperationResult.Failed(
                message,
                conflict.Payload.Target.OriginalSchemeId,
                conflict.Payload.Target.OwnedSchemeId,
                EmptySettingResults(),
                false);
        }

        private static void ValidateJournalPolicy(PowerJournal journal)
        {
            if (journal == null || journal.Payload == null ||
                journal.Payload.Target == null)
            {
                throw new SecureStateConflictException(
                    "The trusted power journal has no typed target.");
            }

            PowerPreset preset = FromJournalPreset(
                journal.Payload.Target.Preset);
            if (!PowerManagedSettings.ComputeManagedSettingsHash(preset).Equals(
                    journal.Payload.Target.ManagedSettingsHash))
            {
                throw new SecureStateConflictException(
                    "The trusted power journal does not match the compiled preset policy.");
            }
        }

        private static PowerSettingsConfigurationResult ConfigureOwnedScheme(
            Guid owned,
            PowerPreset preset)
        {
            IList<DesiredPowerSetting> desired = PowerManagedSettings.BuildPreset(preset);
            List<PowerSettingOperationResult> results =
                new List<PowerSettingOperationResult>();
            int index;
            for (index = 0; index < desired.Count; index++)
            {
                try
                {
                    uint existingAc;
                    uint existingDc;
                    if (!PowerManagedSettings.TryReadValues(
                            owned,
                            desired[index].SettingGuid,
                            out existingAc,
                            out existingDc))
                    {
                        results.Add(PowerSettingOperationResult.Unsupported(
                            desired[index].Name));
                        continue;
                    }

                    WriteAndVerify(owned, desired[index]);
                    results.Add(PowerSettingOperationResult.Applied(
                        desired[index].Name));
                }
                catch (Win32Exception exception)
                {
                    results.Add(PowerSettingOperationResult.Failed(
                        desired[index].Name,
                        SafeExceptionMessage(exception)));
                    return new PowerSettingsConfigurationResult(results, false);
                }
                catch (IOException exception)
                {
                    results.Add(PowerSettingOperationResult.Failed(
                        desired[index].Name,
                        SafeExceptionMessage(exception)));
                    return new PowerSettingsConfigurationResult(results, false);
                }
            }

            return new PowerSettingsConfigurationResult(results, true);
        }

        private static void WriteAndVerify(
            Guid scheme,
            DesiredPowerSetting settingEntry)
        {
            Guid subgroup = PowerManagedSettings.ProcessorSubgroup;
            Guid setting = settingEntry.SettingGuid;
            uint error = NativeMethods.PowerWriteACValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                settingEntry.AcValue);
            if (error != NativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception(
                    (int)error,
                    "PowerWriteACValueIndex failed for " + settingEntry.Name + ".");
            }

            error = NativeMethods.PowerWriteDCValueIndex(
                IntPtr.Zero,
                ref scheme,
                ref subgroup,
                ref setting,
                settingEntry.DcValue);
            if (error != NativeMethods.ERROR_SUCCESS)
            {
                throw new Win32Exception(
                    (int)error,
                    "PowerWriteDCValueIndex failed for " + settingEntry.Name + ".");
            }

            uint readAc;
            uint readDc;
            if (!PowerManagedSettings.TryReadValues(
                    scheme,
                    settingEntry.SettingGuid,
                    out readAc,
                    out readDc) ||
                readAc != settingEntry.AcValue ||
                readDc != settingEntry.DcValue)
            {
                throw new IOException(
                    "Power-setting read-back failed for " + settingEntry.Name + ".");
            }
        }

        private static PowerOwnedSchemeState ClassifyOwnedSchemeForCreating(
            PowerTargetIdentity target)
        {
            string actualName;
            if (!PowerSchemeNative.TryReadFriendlyName(target.OwnedSchemeId, out actualName))
            {
                return PowerOwnedSchemeState.Missing;
            }

            PowerPreset[] presets = new PowerPreset[]
            {
                PowerPreset.Normal,
                PowerPreset.Cool,
                PowerPreset.MaximumBattery
            };
            int index;
            for (index = 0; index < presets.Length; index++)
            {
                if (string.Equals(
                        actualName,
                        PowerSchemeNaming.OwnedFriendlyName(
                            presets[index],
                            target.OwnedSchemeId),
                        StringComparison.Ordinal))
                {
                    return PowerOwnedSchemeState.ExactOwned;
                }
            }

            string originalName;
            if (PowerSchemeNative.TryReadFriendlyName(
                    target.OriginalSchemeId,
                    out originalName) &&
                string.Equals(
                    actualName,
                    originalName,
                    StringComparison.Ordinal) &&
                HasSameManagedSettingValues(
                    target.OriginalSchemeId,
                    target.OwnedSchemeId,
                    FromJournalPreset(target.Preset)))
            {
                // PowerDuplicateScheme initially preserves the source name.
                // A durable Creating journal plus this exact source-name
                // match is the recoverable crash boundary before the
                // app-owned marker is written.
                return PowerOwnedSchemeState.UnmarkedDuplicate;
            }

            return PowerOwnedSchemeState.ForeignOrDiverged;
        }

        private static bool HasSameManagedSettingValues(
            Guid first,
            Guid second,
            PowerPreset preset)
        {
            IList<DesiredPowerSetting> managed = PowerManagedSettings.BuildPreset(preset);
            int index;
            for (index = 0; index < managed.Count; index++)
            {
                uint firstAc;
                uint firstDc;
                uint secondAc;
                uint secondDc;
                bool firstSupported = PowerManagedSettings.TryReadValues(
                    first,
                    managed[index].SettingGuid,
                    out firstAc,
                    out firstDc);
                bool secondSupported = PowerManagedSettings.TryReadValues(
                    second,
                    managed[index].SettingGuid,
                    out secondAc,
                    out secondDc);
                if (firstSupported != secondSupported ||
                    (firstSupported &&
                        (firstAc != secondAc || firstDc != secondDc)))
                {
                    return false;
                }
            }

            return true;
        }

        private static PowerActiveSchemeRelation ClassifyActiveRelation(
            PowerTargetIdentity target,
            Guid active)
        {
            if (active == target.OwnedSchemeId)
                return PowerActiveSchemeRelation.Owned;
            if (active == target.OriginalSchemeId)
                return PowerActiveSchemeRelation.Original;
            return PowerActiveSchemeRelation.External;
        }

        private static Guid AllocateUnusedSchemeGuid()
        {
            const int MaxAttempts = 16;
            int attempt;
            for (attempt = 0; attempt < MaxAttempts; attempt++)
            {
                Guid candidate = Guid.NewGuid();
                if (!PowerSchemeNative.SchemeExists(candidate))
                    return candidate;
            }

            throw new SecureStateConflictException(
                "Could not allocate an unused app-owned power-scheme GUID.");
        }

        private static PowerPresetId ToJournalPreset(PowerPreset preset)
        {
            switch (preset)
            {
                case PowerPreset.Normal:
                    return PowerPresetId.Normal;
                case PowerPreset.Cool:
                    return PowerPresetId.Cool;
                case PowerPreset.MaximumBattery:
                    return PowerPresetId.MaximumBattery;
                default:
                    throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }

        private static PowerPreset FromJournalPreset(PowerPresetId preset)
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

        private static JournalGeneration NextGeneration(PowerJournal previous)
        {
            return previous == null
                ? new JournalGeneration(1)
                : previous.Generation.Next();
        }

        private static IList<PowerSettingOperationResult> EmptySettingResults()
        {
            return new List<PowerSettingOperationResult>().AsReadOnly();
        }

        private static string SafeExceptionMessage(Exception exception)
        {
            if (exception == null || string.IsNullOrWhiteSpace(exception.Message))
                return "unknown error";

            return exception.Message.Trim();
        }

        private static string PresetName(PowerPreset preset)
        {
            return PowerPresetCatalog.Get(preset).DisplayName;
        }
    }

}

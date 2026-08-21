using System;
using MacBookEco.Core;
using MacBookEco.Platform.Windows;

namespace MacBookEco.Tests.Platform
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                WindowsHardwareSnapshot snapshot =
                    new HardwareDiscoveryService().Discover();

                Check.That(snapshot != null, "Discovery returned no snapshot.");
                if (!snapshot.IsAppleHardware)
                {
                    Console.WriteLine(
                        "Platform diagnostics: DEFERRED (this host is not Apple hardware)");
                    return 2;
                }

                Check.That(
                    !string.IsNullOrWhiteSpace(snapshot.AppleModel),
                    "The Apple SMBIOS model is unavailable.");
                Check.That(
                    snapshot.InternalDisplay != null,
                    "The active internal display was not discovered.");
                Check.That(
                    snapshot.InternalDisplay.Edid != null,
                    "The internal display EDID is unavailable.");
                Check.That(
                    snapshot.CurrentDisplayMode != null,
                    "The current internal display mode is unavailable.");

                EdidBaseBlock edid = new EdidBaseBlock(
                    CopyBaseBlock(snapshot.InternalDisplay.Edid));
                HardwareSnapshot coreSnapshot = new HardwareSnapshot(
                    snapshot.SystemManufacturer,
                    snapshot.AppleModel,
                    snapshot.InternalDisplay.IsInternal,
                    snapshot.InternalDisplay.HardwareId,
                    edid,
                    snapshot.DisplayAdapter == null
                        ? null
                        : snapshot.DisplayAdapter.Description,
                    snapshot.DisplayAdapter == null
                        ? null
                        : snapshot.DisplayAdapter.DeviceInstanceId,
                    snapshot.DisplayAdapter == null
                        ? null
                        : snapshot.DisplayAdapter.DriverVersion);
                ProfileSelectionResult selection = ProfileCatalog.Select(coreSnapshot);

                InternalDisplayTargetResolver resolver =
                    new InternalDisplayTargetResolver();
                ResolvedMonitorTarget activeTarget = resolver.ResolveActive();
                Check.That(activeTarget.Endpoint != null,
                    "Active resolver did not return an ephemeral display endpoint.");
                Check.That(snapshot.InternalDisplay.Endpoint != null &&
                    snapshot.InternalDisplay.Endpoint.Equals(activeTarget.Endpoint),
                    "Discovery and active resolver returned different display endpoints.");
                Check.That(activeTarget.MatchesIdentity(activeTarget.MonitorIdentity),
                    "Active resolver did not prove its durable monitor identity.");
                StableDisplayTarget stableTarget =
                    new StableDisplayTargetResolver().ResolveActive(
                        activeTarget.MonitorIdentity);
                Check.That(stableTarget.Identity.Equals(activeTarget.MonitorIdentity),
                    "Narrow watchdog resolver did not prove the durable monitor identity.");
                Check.That(stableTarget.Endpoint.Equals(activeTarget.Endpoint),
                    "Narrow watchdog resolver returned a different live endpoint.");
                Check.That(stableTarget.RefreshRateNumerator != 0
                    && stableTarget.RefreshRateDenominator != 0,
                    "Narrow watchdog resolver did not return CCD rational refresh.");
                ResolvedMonitorTarget storedTarget =
                    resolver.ResolveStoredForRestore(activeTarget.MonitorIdentity);
                Check.That(storedTarget.Endpoint == null,
                    "Offline restore resolution unexpectedly retained an active endpoint.");
                Check.That(storedTarget.MatchesIdentity(activeTarget.MonitorIdentity),
                    "Stored monitor resolver did not return the exact active panel identity.");
                Check.That(
                    FixedTimeComparer.AreEqual(
                        activeTarget.BaseEdid,
                        storedTarget.BaseEdid),
                    "Active and stored resolver paths returned different base EDID bytes.");

                Console.WriteLine("MacBook Eco platform diagnostics");
                Console.WriteLine("Apple model: " + snapshot.AppleModel);
                Console.WriteLine(
                    "Panel: " +
                    snapshot.InternalDisplay.EdidManufacturerCode +
                    snapshot.InternalDisplay.EdidProductCode.ToString("X4"));
                Console.WriteLine(
                    "Native EDID: " +
                    edid.PreferredTiming.HorizontalActive +
                    "x" +
                    edid.PreferredTiming.VerticalActive +
                    " @ " +
                    edid.PreferredTiming.RefreshRateHertz.ToString("0.###") +
                    " Hz");
                Console.WriteLine("Current GDI mode: " + snapshot.CurrentDisplayMode);
                Console.WriteLine(
                    "Existing override: " +
                    (snapshot.InternalDisplay.ExistingEdidOverride != null));
                Console.WriteLine(
                    "Active endpoint: " +
                    activeTarget.Endpoint.GdiDeviceName +
                    " source=" + activeTarget.Endpoint.SourceId +
                    " target=" + activeTarget.Endpoint.TargetId);
                Console.WriteLine(
                    "Stored monitor identity: " +
                    storedTarget.MonitorIdentity.MonitorInstanceId);
                Console.WriteLine(
                    "Verified profile: " +
                    (selection.HardwareSupported ? selection.Profile.Id : "unsupported"));

                EdidJournal displayJournal = JournalStore.ReadEdidStatus();
                if (displayJournal != null && displayJournal.Payload != null)
                {
                    DisplayProfile journalProfile = ProfileCatalog.GetById(
                        displayJournal.Payload.Target.ProfileId);
                    byte[] expectedOverride = journalProfile == null
                        ? null
                        : journalProfile.BuildOverride(
                            new EdidBaseBlock(activeTarget.BaseEdid))
                            .ToByteArray();
                    Console.WriteLine(
                        "Display journal generation: " +
                        displayJournal.Generation.Value);
                    Console.WriteLine(
                        "Journal target matches active/or exact owned EDID: " +
                        activeTarget.MatchesIdentity(
                            displayJournal.Payload.Target.Monitor,
                            displayJournal.Payload.OwnedOverrideHash));
                    Console.WriteLine(
                        "Journal/active instance: " +
                        displayJournal.Payload.Target.Monitor.MonitorInstanceId +
                        " / " +
                        activeTarget.DeviceInstanceId);
                    Console.WriteLine(
                        "Journal/active hardware ID: " +
                        displayJournal.Payload.Target.Monitor.PanelHardwareId +
                        " / " +
                        activeTarget.HardwareId);
                    Console.WriteLine(
                        "Journal/active manufacturer: " +
                        displayJournal.Payload.Target.Monitor.ManufacturerCode +
                        " / " +
                        activeTarget.ManufacturerCode);
                    Console.WriteLine(
                        "Journal/active EDID fingerprint: " +
                        displayJournal.Payload.Target.Monitor.EdidFingerprint +
                        " / " +
                        activeTarget.BaseEdidHash);
                    Console.WriteLine(
                        "Journal profile matches hardware: " +
                        (journalProfile != null &&
                         journalProfile.Match(coreSnapshot).HardwareSupported));
                    Console.WriteLine(
                        "Journal ownership hash matches compiled override: " +
                        (expectedOverride != null &&
                         Sha256Digest.Compute(expectedOverride).Equals(
                             displayJournal.Payload.OwnedOverrideHash)));
                    Console.WriteLine(
                        "Live override classification: " +
                        (expectedOverride == null
                            ? "Unavailable"
                            : activeTarget.ClassifyOverride(expectedOverride).ToString()));
                }

                DisplayOverrideStatus displayStatus =
                    new EdidStatusReader().Read();
                PowerSchemeStatus powerStatus =
                    new PowerStatusReader().Read();
                Console.WriteLine("Display journal: " + displayStatus.State);
                Console.WriteLine("Power journal: " + powerStatus.State);
                Console.WriteLine("Warnings: " + snapshot.Warnings.Count);
                Console.WriteLine("Platform diagnostics: PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Platform diagnostics: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static byte[] CopyBaseBlock(byte[] value)
        {
            if (value == null || value.Length < EdidBaseBlock.Length)
            {
                throw new InvalidOperationException(
                    "The discovered EDID does not contain a complete base block.");
            }

            byte[] result = new byte[EdidBaseBlock.Length];
            Array.Copy(value, result, result.Length);
            return result;
        }

    }
}

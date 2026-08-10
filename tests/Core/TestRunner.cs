using System;
using MacBookEco.AppPolicy;
using MacBookEco.Core;
using MacBookEco.Platform.Windows;

namespace MacBookEco.Tests.Core
{
    internal static class TestRunner
    {
        // Reviewed synthetic fixture. Per-unit serial and manufacture-week
        // bytes are cleared; a fixed valid year byte is used and the checksum
        // is recomputed. The normalized profile signature and both reviewed
        // timings are unchanged.
        private const string ReviewedAppa044Edid =
            "00 FF FF FF FF FF FF 00 06 10 44 A0 00 00 00 00 " +
            "00 10 01 04 B5 22 16 78 02 0F B1 AE 52 43 B0 26 " +
            "0D 50 54 00 00 00 01 01 01 01 01 01 01 01 01 01 " +
            "01 01 01 01 01 01 E7 91 00 50 C0 80 37 70 08 20 " +
            "98 08 59 D7 10 00 00 1A 00 00 00 FC 00 43 6F 6C " +
            "6F 72 20 4C 43 44 0A 20 20 20 00 00 00 10 00 00 " +
            "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 10 " +
            "00 00 00 00 00 00 00 00 00 00 00 00 00 00 01 AC";

        private const string Exact48Dtd =
            "DC 91 00 50 C0 80 24 72 08 20 98 08 59 D7 10 00 00 1A";

        internal static TestCase[] CreateCases()
        {
            return new[]
            {
                Test(
                    "Reviewed APPA044 fixture parses as exact native 60 Hz",
                    ParseOriginalEdid),
                Test("Detailed timing round-trips byte for byte", DetailedTimingRoundTrip),
                Test("Exact 48 Hz DTD is inserted without replacing native 60 Hz", InsertExact48),
                Test(
                    "Normalized signature is stable after managed insertion",
                    NormalizedSignature),
                Test("Known hardware selects the reviewed profile", KnownProfileMatches),
                Test(
                    "Generated 48 Hz timing follows the bounded golden formula",
                    GeneratedTimingMatchesGoldenFormula),
                Test(
                    "Generated pixel clock uses half-up rounding at 5000",
                    GeneratedPixelClockUsesHalfUpRounding),
                Test(
                    "Reviewed profiles retain priority over generated candidates",
                    ReviewedProfileRetainsPriority),
                Test(
                    "Experimental fallback is deterministic and canonical",
                    ExperimentalFallbackIsDeterministic),
                Test(
                    "Generator rejects identity and GPU allowlist mismatches",
                    GeneratorRejectsIdentityAndGpuMismatches),
                Test(
                    "Complete EDID validation gates generated profiles",
                    CompleteEdidValidationGatesGeneration),
                Test(
                    "Generator rejects unsafe preferred timing and layout",
                    GeneratorRejectsUnsafeTimingAndLayout),
                Test(
                    "Native refresh allowlist uses inclusive 59-61 Hz bounds",
                    NativeRefreshBoundsAreInclusive),
                Test(
                    "Generated recovery re-proves deterministic identity",
                    GeneratedRecoveryReprovesIdentity),
                Test(
                    "Generated journal payload carries identity but no DTD bytes",
                    GeneratedJournalOmitsTimingBytes),
                Test("Unknown hardware and unknown layout are rejected", UnknownProfileRejected),
                Test("Occupied descriptor layout refuses insertion", OccupiedLayoutRejected),
                Test("Hardware support is distinct from install readiness", CapabilitySplit),
                Test("Monitor identity and endpoint contracts stay separate", IdentityContracts),
                Test(
                    "EDID recovery policy covers every durable retry boundary",
                    RecoveryPolicyMatrix),
                Test(
                    "EDID journal transition matrix is exhaustive",
                    EdidTransitionMatrix),
                Test(
                    "EDID recovery policy rejects unknown inputs",
                    RecoveryPolicyRejectsUnknownInputs),
                Test(
                    "Refresh-only mode selection preserves the complete current key",
                    RefreshOnlyModeSelectionPreservesCurrentKey),
                Test(
                    "Refresh-only mode selection rejects changed display properties",
                    RefreshOnlyModeSelectionRejectsChangedDisplayProperties),
                Test(
                    "CPU preset catalog exposes immutable reviewed policy",
                    PowerPresetCatalogIsImmutable),
                Test(
                    "Power recovery policy covers every durable retry boundary",
                    PowerRecoveryPolicyMatrix),
                Test(
                    "Power fault recovery retains one resource across a full cycle",
                    PowerFaultRecoveryRetainsSingleResource),
                Test(
                    "Power preset switching durably reuses the owned scheme",
                    PowerPresetSwitchReusesOwnedScheme),
                Test(
                    "Power recovery policy fails closed on unknown live facts",
                    PowerRecoveryPolicyFailsClosed),
                Test(
                    "Terminal status readers require exact live managed state",
                    TerminalStatusReadersRequireExactLiveState),
                Test(
                    "Power ownership includes every supported live setting",
                    PowerOwnershipIncludesLiveSettings),
                Test("Invalid checksum is rejected", InvalidChecksumRejected)
            };
        }

        private static TestCase Test(string name, Action body)
        {
            return new TestCase("Core: " + name, body);
        }

        private static void ParseOriginalEdid()
        {
            var edid = CreateOriginal();
            var native = edid.PreferredTiming;

            Check.Equal("APP", edid.ManufacturerCode);
            Check.Equal((ushort)0xA044, edid.ProductCode);
            Check.Equal("APPA044", edid.HardwareId);
            Check.Equal(1, (int)edid.ExtensionBlockCount);
            Check.Equal(3072, native.HorizontalActive);
            Check.Equal(1920, native.VerticalActive);
            Check.Equal(3152, native.HorizontalTotal);
            Check.Equal(1975, native.VerticalTotal);
            Check.Equal(8, native.HorizontalSyncOffset);
            Check.Equal(32, native.HorizontalSyncPulseWidth);
            Check.Equal(40, native.HorizontalBackPorch);
            Check.Equal(41, native.VerticalSyncOffset);
            Check.Equal(8, native.VerticalSyncPulseWidth);
            Check.Equal(6, native.VerticalBackPorch);
            Check.Near(373.51, native.PixelClockMegahertz, 0.001);
            Check.Near(59.999679, native.RefreshRateHertz, 0.00001);
            Check.Equal(2, edid.FindFreeDescriptor());
            Check.True(EdidBaseBlock.HasValidChecksum(edid.ToByteArray()));
        }

        private static void DetailedTimingRoundTrip()
        {
            var nativeBytes = HexCodec.Parse(
                "E7 91 00 50 C0 80 37 70 08 20 98 08 59 D7 10 00 00 1A");
            var native = DetailedTiming.Parse(nativeBytes);
            Check.BytesEqual(nativeBytes, native.ToByteArray());

            var targetBytes = HexCodec.Parse(Exact48Dtd);
            var target = DetailedTiming.Parse(targetBytes);
            Check.BytesEqual(targetBytes, target.ToByteArray());
            Check.Equal(3152, target.HorizontalTotal);
            Check.Equal(2468, target.VerticalTotal);
            Check.Equal(499, target.VerticalBackPorch);
            Check.Near(373.40, target.PixelClockMegahertz, 0.001);
            Check.Near(48.000189, target.RefreshRateHertz, 0.00001);
        }

        private static void InsertExact48()
        {
            var original = CreateOriginal();
            var originalBytes = original.ToByteArray();
            var target = DetailedTiming.ParseHex(Exact48Dtd);
            var modified = original.InsertDetailedTiming(target);

            Check.BytesEqual(
                original.PreferredTiming.ToByteArray(),
                modified.PreferredTiming.ToByteArray());
            Check.BytesEqual(target.ToByteArray(), modified.GetDetailedTiming(2).ToByteArray());
            Check.True(modified.ContainsDetailedTiming(target));
            Check.True(EdidBaseBlock.HasValidChecksum(modified.ToByteArray()));
            Check.Equal(1, (int)modified.ExtensionBlockCount);
            Check.BytesEqual(originalBytes, original.ToByteArray());

            // Insertion is deliberately idempotent for crash/retry recovery.
            Check.BytesEqual(
                modified.ToByteArray(),
                modified.InsertDetailedTiming(target).ToByteArray());
        }

        private static void NormalizedSignature()
        {
            var original = CreateOriginal();
            var modified = original.InsertDetailedTiming(DetailedTiming.ParseHex(Exact48Dtd));

            Check.Equal(
                Sha256Digest.ParseCanonical(
                    "CDA0E18080DE8CAC744C66A5374A53CBBA1999115FA5FE2DBD949980649AF3F5"),
                original.NormalizedSignature);
            Check.Equal(original.NormalizedSignature, modified.NormalizedSignature);
            Check.Equal(
                ProfileCatalog.All[0].NormalizedEdidSignature,
                original.NormalizedSignature);
            Check.Equal(
                original.NormalizedSignature,
                Sha256Digest.ParseHex(
                    "CD:A0:E1:80:80:DE:8C:AC:74:4C:66:A5:37:4A:53:CB:"
                    + "BA:19:99:11:5F:A5:FE:2D:BD:94:99:80:64:9A:F3:F5"));

            byte[] anotherUnit = original.ToByteArray();
            anotherUnit[12] = 0x12;
            anotherUnit[13] = 0x34;
            anotherUnit[14] = 0x56;
            anotherUnit[15] = 0x78;
            anotherUnit[16] = 0x2A;
            anotherUnit[17] = 0x21;
            anotherUnit[127] = 0;
            int checksum = 0;
            for (int index = 0; index < 127; index++)
            {
                checksum = (checksum + anotherUnit[index]) & 0xFF;
            }
            anotherUnit[127] = (byte)((256 - checksum) & 0xFF);

            EdidBaseBlock samePanelAnotherUnit =
                new EdidBaseBlock(anotherUnit);
            Check.Equal(
                original.NormalizedSignature,
                samePanelAnotherUnit.NormalizedSignature);
            Check.That(
                !Sha256Digest.Compute(original.ToByteArray()).Equals(
                    Sha256Digest.Compute(anotherUnit)),
                "exact EDID fingerprints must remain unit-specific");
        }

        private static void KnownProfileMatches()
        {
            var hardware = CreateKnownHardware(CreateOriginal());
            var selected = ProfileCatalog.Select(hardware);

            Check.True(selected.HardwareSupported);
            Check.NotNull(selected.Profile);
            Check.Equal(
                ProfileCatalog.MacBookPro161Appa044ProfileId,
                selected.Profile.Id);
            Check.Equal(0, selected.ClosestMatch.RejectionReasons.Count);
            Check.Equal(0, selected.ClosestMatch.Warnings.Count);

            var installed = selected.Profile.BuildOverride(hardware);
            Check.True(installed.ContainsDetailedTiming(selected.Profile.TargetTiming));

            var otherDriver = new HardwareSnapshot(
                "Apple Inc.",
                "MacBookPro16,1",
                true,
                "MONITOR\\APPA044\\REDACTED",
                CreateOriginal(),
                "AMD Radeon Pro 5300M",
                "PCI\\VEN_1002&DEV_7340&SUBSYS_REDACTED",
                "99.0.0.0");
            var warningMatch = selected.Profile.Match(otherDriver);
            Check.True(warningMatch.HardwareSupported);
            Check.Equal(1, warningMatch.Warnings.Count);
        }

        private static void GeneratedTimingMatchesGoldenFormula()
        {
            EdidBaseBlock original = CreateOriginal();
            ExperimentalProfileGenerationResult generated =
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(original));

            Check.True(generated.Succeeded);
            Check.NotNull(generated.Profile);
            Check.Equal(DisplayProfileKind.Experimental, generated.Profile.Kind);
            Check.BytesEqual(
                HexCodec.Parse(Exact48Dtd),
                generated.Profile.TargetTiming.ToByteArray());
            Check.Equal(
                "exp48-v1-mbp161-7340-" +
                    "0d248c3f0d1f6bea6b207f50f9e7cd66" +
                    "9e518ac7dfe22c9184ad04801080b8da",
                generated.Profile.Id);

            DetailedTiming native = generated.Profile.NativeTiming;
            DetailedTiming target = generated.Profile.TargetTiming;
            long expectedVerticalTotal =
                (native.PixelClock10Khz * 10000L) /
                (native.HorizontalTotal * 48L);
            long expectedPixelClock10Khz =
                ((native.HorizontalTotal * expectedVerticalTotal * 48L) + 5000L) /
                10000L;

            Check.Equal((int)expectedVerticalTotal, target.VerticalTotal);
            Check.Equal((int)expectedPixelClock10Khz, target.PixelClock10Khz);
            Check.That(
                target.PixelClock10Khz <= native.PixelClock10Khz,
                "Generated pixel clock must not exceed the native clock.");
            Check.Near(48.0, target.RefreshRateHertz, 0.01);

            Check.Equal(native.HorizontalActive, target.HorizontalActive);
            Check.Equal(native.HorizontalBlanking, target.HorizontalBlanking);
            Check.Equal(native.HorizontalSyncOffset, target.HorizontalSyncOffset);
            Check.Equal(
                native.HorizontalSyncPulseWidth,
                target.HorizontalSyncPulseWidth);
            Check.Equal(native.VerticalActive, target.VerticalActive);
            Check.Equal(native.VerticalSyncOffset, target.VerticalSyncOffset);
            Check.Equal(
                native.VerticalSyncPulseWidth,
                target.VerticalSyncPulseWidth);
            Check.Equal(
                native.HorizontalImageSizeMillimeters,
                target.HorizontalImageSizeMillimeters);
            Check.Equal(
                native.VerticalImageSizeMillimeters,
                target.VerticalImageSizeMillimeters);
            Check.Equal(
                native.HorizontalBorderPixels,
                target.HorizontalBorderPixels);
            Check.Equal(native.VerticalBorderLines, target.VerticalBorderLines);
            Check.Equal(native.Flags, target.Flags);
            Check.Equal(
                target.VerticalBlanking - native.VerticalBlanking,
                target.VerticalBackPorch - native.VerticalBackPorch);
        }

        private static void GeneratedPixelClockUsesHalfUpRounding()
        {
            EdidBaseBlock belowHalf = CreateRoundingFixture(2527, 80);
            EdidBaseBlock aboveHalf = CreateRoundingFixture(2473, 55);
            ExperimentalProfileGenerationResult belowGenerated =
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(belowHalf));
            ExperimentalProfileGenerationResult aboveGenerated =
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(aboveHalf));

            Check.True(belowGenerated.Succeeded);
            Check.True(aboveGenerated.Succeeded);
            Check.Equal(2527, belowGenerated.Profile.TargetTiming.VerticalTotal);
            Check.Equal(2473, aboveGenerated.Profile.TargetTiming.VerticalTotal);

            long belowNumerator =
                belowGenerated.Profile.TargetTiming.HorizontalTotal *
                (long)belowGenerated.Profile.TargetTiming.VerticalTotal *
                48L;
            long aboveNumerator =
                aboveGenerated.Profile.TargetTiming.HorizontalTotal *
                (long)aboveGenerated.Profile.TargetTiming.VerticalTotal *
                48L;
            Check.Equal(4992L, belowNumerator % 10000L);
            Check.Equal(5008L, aboveNumerator % 10000L);
            Check.Equal(
                (int)(belowNumerator / 10000L),
                belowGenerated.Profile.TargetTiming.PixelClock10Khz);
            Check.Equal(
                (int)((aboveNumerator / 10000L) + 1L),
                aboveGenerated.Profile.TargetTiming.PixelClock10Khz);
        }

        private static void ReviewedProfileRetainsPriority()
        {
            HardwareSnapshot hardware =
                CreateGeneratorHardware(CreateOriginal());
            ExperimentalProfileGenerationResult generated =
                Experimental48HzProfileGenerator.Generate(hardware);
            ExperimentalProfileGenerationResult fallback =
                Experimental48HzProfileGenerator.GenerateFallback(hardware);
            ProfileSelectionResult selected = ProfileCatalog.Select(hardware);

            Check.True(generated.Succeeded);
            Check.True(generated.Profile.IsExperimental);
            Check.False(fallback.Succeeded);
            Check.NotNull(selected.Profile);
            Check.Equal(
                ProfileCatalog.MacBookPro161Appa044ProfileId,
                selected.Profile.Id);
            Check.Equal(DisplayProfileKind.Verified, selected.Profile.Kind);
        }

        private static void ExperimentalFallbackIsDeterministic()
        {
            EdidBaseBlock panelVariant = CreatePanelVariant(0xA045);
            HardwareSnapshot hardware = CreateGeneratorHardware(panelVariant);

            Check.False(ProfileCatalog.Select(hardware).HardwareSupported);
            ExperimentalProfileGenerationResult first =
                Experimental48HzProfileGenerator.GenerateFallback(hardware);
            ExperimentalProfileGenerationResult second =
                Experimental48HzProfileGenerator.GenerateFallback(hardware);

            Check.True(first.Succeeded);
            Check.True(second.Succeeded);
            DisplayProfileMatch firstMatch = first.Profile.Match(hardware);
            Check.True(firstMatch.HardwareSupported);
            Check.True(firstMatch.CanInstall);
            Check.True(first.Profile.IsExperimental);
            Check.Equal("APPA045", first.Profile.PanelHardwareId);
            Check.Equal(first.Profile.Id, second.Profile.Id);
            Check.BytesEqual(
                first.Profile.TargetTiming.ToByteArray(),
                second.Profile.TargetTiming.ToByteArray());
            Check.That(
                first.Profile.Id.StartsWith(
                    "exp48-v1-mbp161-7340-",
                    StringComparison.Ordinal),
                "Generated profile ID must carry the allowlist key.");
            Check.That(
                first.Profile.Id.Length <= 96,
                "Generated profile ID must fit the journal wire bound.");
            Check.Equal(first.Profile.Id.ToLowerInvariant(), first.Profile.Id);
            Check.True(
                Experimental48HzProfileGenerator.IsExperimentalProfileId(
                    first.Profile.Id));
            string acknowledgementToken =
                Experimental48HzProfileGenerator.CreateAcknowledgementToken(
                    first.Profile.Id);
            Sha256Digest parsedAcknowledgementToken;
            Check.True(Sha256Digest.TryParseCanonical(
                acknowledgementToken,
                out parsedAcknowledgementToken));
            Check.Equal(
                acknowledgementToken,
                Experimental48HzProfileGenerator.CreateAcknowledgementToken(
                    second.Profile.Id));
            Check.True(
                Experimental48HzProfileGenerator.AcknowledgementTokenMatches(
                    first.Profile.Id,
                    acknowledgementToken));
            Check.False(
                Experimental48HzProfileGenerator.AcknowledgementTokenMatches(
                    first.Profile.Id,
                    acknowledgementToken.ToLowerInvariant()));
            string otherProfileId = first.Profile.Id.Substring(
                0,
                first.Profile.Id.Length - 1) +
                (first.Profile.Id.EndsWith("0", StringComparison.Ordinal)
                    ? "1"
                    : "0");
            Check.That(
                !string.Equals(
                    acknowledgementToken,
                    Experimental48HzProfileGenerator
                        .CreateAcknowledgementToken(otherProfileId),
                    StringComparison.Ordinal),
                "Different generated profiles must have different consent tokens.");
            Check.False(
                Experimental48HzProfileGenerator.AcknowledgementTokenMatches(
                    otherProfileId,
                    acknowledgementToken));
            Check.Throws<ArgumentException>(delegate
            {
                Experimental48HzProfileGenerator.CreateAcknowledgementToken(
                    ProfileCatalog.MacBookPro161Appa044ProfileId);
            });

            byte[] anotherUnitBytes = panelVariant.ToByteArray();
            anotherUnitBytes[12] = 0x12;
            anotherUnitBytes[13] = 0x34;
            anotherUnitBytes[14] = 0x56;
            anotherUnitBytes[15] = 0x78;
            anotherUnitBytes[16] = 0x2A;
            anotherUnitBytes[17] = 0x21;
            EdidBaseBlock.UpdateChecksum(anotherUnitBytes);
            EdidBaseBlock anotherUnit = new EdidBaseBlock(anotherUnitBytes);
            ExperimentalProfileGenerationResult anotherUnitSelection =
                Experimental48HzProfileGenerator.GenerateFallback(
                    CreateGeneratorHardware(anotherUnit));
            Check.True(anotherUnitSelection.Succeeded);
            Check.Equal(
                panelVariant.NormalizedSignature,
                anotherUnit.NormalizedSignature);
            Check.That(
                !Sha256Digest.Compute(panelVariant.ToByteArray()).Equals(
                    Sha256Digest.Compute(anotherUnit.ToByteArray())),
                "Per-unit EDID bytes must retain a distinct exact fingerprint.");
            Check.Equal(first.Profile.Id, anotherUnitSelection.Profile.Id);
            Check.BytesEqual(
                first.Profile.TargetTiming.ToByteArray(),
                anotherUnitSelection.Profile.TargetTiming.ToByteArray());
        }

        private static void GeneratorRejectsIdentityAndGpuMismatches()
        {
            EdidBaseBlock panelVariant = CreatePanelVariant(0xA045);
            Check.True(
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(panelVariant)).Succeeded);
            Check.True(
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(
                        panelVariant,
                        "Apple Inc.",
                        "MacBookPro16,4",
                        true,
                        "APPA045",
                        "PCI\\VEN_1002&DEV_7360",
                        true)).Succeeded);

            CheckGenerationRejected(CreateGeneratorHardware(
                panelVariant,
                "Apple Computer, Inc.",
                "MacBookPro16,1",
                true,
                "APPA045",
                "PCI\\VEN_1002&DEV_7340",
                true));
            CheckGenerationRejected(CreateGeneratorHardware(
                panelVariant,
                "Apple Inc.",
                "MacBookPro16,2",
                true,
                "APPA045",
                "PCI\\VEN_1002&DEV_7340",
                true));
            CheckGenerationRejected(CreateGeneratorHardware(
                panelVariant,
                "Apple Inc.",
                "MacBookPro16,1",
                true,
                "APPA045",
                "PCI\\VEN_8086&DEV_3E9B",
                true));
            CheckGenerationRejected(CreateGeneratorHardware(
                panelVariant,
                "Apple Inc.",
                "MacBookPro16,1",
                true,
                "APPA045",
                "PCI\\VEN_1002&DEV_7340EXTRA",
                true));
            CheckGenerationRejected(CreateGeneratorHardware(
                panelVariant,
                "Apple Inc.",
                "MacBookPro16,4",
                true,
                "APPA045",
                "PCI\\VEN_1002&DEV_7340",
                true));
            CheckGenerationRejected(CreateGeneratorHardware(
                panelVariant,
                "Apple Inc.",
                "MacBookPro16,1",
                false,
                "APPA045",
                "PCI\\VEN_1002&DEV_7340",
                true));
            CheckGenerationRejected(CreateGeneratorHardware(
                panelVariant,
                "Apple Inc.",
                "MacBookPro16,1",
                true,
                "APPA046",
                "PCI\\VEN_1002&DEV_7340",
                true));
        }

        private static void CompleteEdidValidationGatesGeneration()
        {
            byte[] validDocument = CreateCompleteEdidDocument();
            Check.True(EdidBaseBlock.HasValidCompleteDocument(validDocument));
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    CreateOriginal().ToByteArray()));
            Check.False(EdidBaseBlock.HasValidCompleteDocument(new byte[129]));

            byte[] badExtensionChecksum = (byte[])validDocument.Clone();
            badExtensionChecksum[EdidBaseBlock.Length + 12] ^= 0x01;
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(badExtensionChecksum));

            byte[] trailingBlock = new byte[EdidBaseBlock.Length * 3];
            Buffer.BlockCopy(
                validDocument,
                0,
                trailingBlock,
                0,
                validDocument.Length);
            Check.False(EdidBaseBlock.HasValidCompleteDocument(trailingBlock));

            int extensionOffset = EdidBaseBlock.Length;
            byte[] badNativeCount = (byte[])validDocument.Clone();
            badNativeCount[extensionOffset + 3] = 0x0F;
            UpdateDocumentBlockChecksum(badNativeCount, 1);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(badNativeCount));

            byte[] reservedDataBlock = (byte[])validDocument.Clone();
            reservedDataBlock[extensionOffset + 2] = 6;
            reservedDataBlock[extensionOffset + 4] = 0x01;
            UpdateDocumentBlockChecksum(reservedDataBlock, 1);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(reservedDataBlock));

            byte[] emptyExtendedBlock = (byte[])validDocument.Clone();
            emptyExtendedBlock[extensionOffset + 2] = 5;
            emptyExtendedBlock[extensionOffset + 4] = 0xE0;
            UpdateDocumentBlockChecksum(emptyExtendedBlock, 1);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(emptyExtendedBlock));

            byte[] unknownExtendedBlock = (byte[])validDocument.Clone();
            unknownExtendedBlock[extensionOffset + 2] = 7;
            unknownExtendedBlock[extensionOffset + 4] = 0xE2;
            unknownExtendedBlock[extensionOffset + 5] = 0x7F;
            UpdateDocumentBlockChecksum(unknownExtendedBlock, 1);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(unknownExtendedBlock));

            byte[] unsupportedExtension = (byte[])validDocument.Clone();
            unsupportedExtension[extensionOffset] = 0x70;
            UpdateDocumentBlockChecksum(unsupportedExtension, 1);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(unsupportedExtension));

            byte[] unsupportedCtaRevision = (byte[])validDocument.Clone();
            unsupportedCtaRevision[extensionOffset + 1] = 4;
            UpdateDocumentBlockChecksum(unsupportedCtaRevision, 1);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedCtaRevision));

            byte[] unsupportedLegacyCta = (byte[])validDocument.Clone();
            unsupportedLegacyCta[extensionOffset + 1] = 1;
            UpdateDocumentBlockChecksum(unsupportedLegacyCta, 1);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedLegacyCta));

            unsupportedLegacyCta[extensionOffset + 1] = 2;
            UpdateDocumentBlockChecksum(unsupportedLegacyCta, 1);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedLegacyCta));

            byte[] unsupportedBaseVersion = (byte[])validDocument.Clone();
            unsupportedBaseVersion[18] = 2;
            unsupportedBaseVersion[19] = 0;
            UpdateDocumentBlockChecksum(unsupportedBaseVersion, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedBaseVersion));

            byte[] unsupportedBaseRevision = (byte[])validDocument.Clone();
            unsupportedBaseRevision[19] = 5;
            UpdateDocumentBlockChecksum(unsupportedBaseRevision, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedBaseRevision));

            byte[] unsupportedEdid13 = (byte[])validDocument.Clone();
            unsupportedEdid13[19] = 3;
            UpdateDocumentBlockChecksum(unsupportedEdid13, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(unsupportedEdid13));

            byte[] unsupportedContinuousFrequency =
                (byte[])validDocument.Clone();
            unsupportedContinuousFrequency[24] |= 0x01;
            UpdateDocumentBlockChecksum(unsupportedContinuousFrequency, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedContinuousFrequency));

            byte[] reservedDigitalInterface = (byte[])validDocument.Clone();
            reservedDigitalInterface[20] = 0xBF;
            UpdateDocumentBlockChecksum(reservedDigitalInterface, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    reservedDigitalInterface));

            byte[] reservedDigitalBitDepth = (byte[])validDocument.Clone();
            reservedDigitalBitDepth[20] = 0xF5;
            UpdateDocumentBlockChecksum(reservedDigitalBitDepth, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    reservedDigitalBitDepth));

            byte[] reservedManufacturerBit = (byte[])validDocument.Clone();
            reservedManufacturerBit[8] |= 0x80;
            UpdateDocumentBlockChecksum(reservedManufacturerBit, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    reservedManufacturerBit));

            byte[] reservedManufactureWeek = (byte[])validDocument.Clone();
            reservedManufactureWeek[16] = 0x37;
            UpdateDocumentBlockChecksum(reservedManufactureWeek, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    reservedManufactureWeek));

            byte[] reservedManufactureYear = (byte[])validDocument.Clone();
            reservedManufactureYear[17] = 0x0F;
            UpdateDocumentBlockChecksum(reservedManufactureYear, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    reservedManufactureYear));

            byte[] unsupportedAnalogInput = (byte[])validDocument.Clone();
            unsupportedAnalogInput[20] &= 0x7F;
            UpdateDocumentBlockChecksum(unsupportedAnalogInput, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedAnalogInput));

            byte[] unsupportedExtensionGamma = (byte[])validDocument.Clone();
            unsupportedExtensionGamma[23] = 0xFF;
            UpdateDocumentBlockChecksum(unsupportedExtensionGamma, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedExtensionGamma));

            byte[] unsupportedSrgbDeclaration = (byte[])validDocument.Clone();
            unsupportedSrgbDeclaration[24] |= 0x04;
            UpdateDocumentBlockChecksum(unsupportedSrgbDeclaration, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedSrgbDeclaration));

            byte[] unsupportedManufacturerTiming =
                (byte[])validDocument.Clone();
            unsupportedManufacturerTiming[37] |= 0x01;
            UpdateDocumentBlockChecksum(unsupportedManufacturerTiming, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedManufacturerTiming));

            byte[] appleEstablishedTiming = (byte[])validDocument.Clone();
            appleEstablishedTiming[37] |= 0x80;
            UpdateDocumentBlockChecksum(appleEstablishedTiming, 0);
            Check.True(
                EdidBaseBlock.HasValidCompleteDocument(
                    appleEstablishedTiming));

            byte[] malformedStandardTiming = (byte[])validDocument.Clone();
            malformedStandardTiming[38] = 0x00;
            malformedStandardTiming[39] = 0x02;
            UpdateDocumentBlockChecksum(malformedStandardTiming, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    malformedStandardTiming));

            byte[] minimumStandardTiming = (byte[])validDocument.Clone();
            minimumStandardTiming[38] = 0x01;
            minimumStandardTiming[39] = 0x02;
            UpdateDocumentBlockChecksum(minimumStandardTiming, 0);
            Check.True(
                EdidBaseBlock.HasValidCompleteDocument(
                    minimumStandardTiming));

            byte[] invalidManufacturer = (byte[])validDocument.Clone();
            invalidManufacturer[8] = 0;
            invalidManufacturer[9] = 0;
            UpdateDocumentBlockChecksum(invalidManufacturer, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(invalidManufacturer));

            byte[] malformedMonitorDescriptor = (byte[])validDocument.Clone();
            int descriptorOffset = EdidBaseBlock.FirstDescriptorOffset +
                (2 * DetailedTiming.EncodedLength);
            malformedMonitorDescriptor[descriptorOffset + 2] = 1;
            UpdateDocumentBlockChecksum(malformedMonitorDescriptor, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    malformedMonitorDescriptor));

            byte[] missingRequiredFirstDtd = (byte[])validDocument.Clone();
            SetOccupiedMonitorDescriptor(missingRequiredFirstDtd, 0, 0xFC);
            UpdateDocumentBlockChecksum(missingRequiredFirstDtd, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    missingRequiredFirstDtd));

            byte[] allZeroBaseDescriptor = (byte[])validDocument.Clone();
            Array.Clear(
                allZeroBaseDescriptor,
                descriptorOffset,
                DetailedTiming.EncodedLength);
            UpdateDocumentBlockChecksum(allZeroBaseDescriptor, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    allZeroBaseDescriptor));

            byte[] dtdAfterMonitorDescriptor = (byte[])validDocument.Clone();
            Buffer.BlockCopy(
                dtdAfterMonitorDescriptor,
                EdidBaseBlock.FirstDescriptorOffset,
                dtdAfterMonitorDescriptor,
                descriptorOffset,
                DetailedTiming.EncodedLength);
            UpdateDocumentBlockChecksum(dtdAfterMonitorDescriptor, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    dtdAfterMonitorDescriptor));

            byte[] malformedTextPadding = (byte[])validDocument.Clone();
            int textDescriptorOffset = EdidBaseBlock.FirstDescriptorOffset +
                DetailedTiming.EncodedLength;
            malformedTextPadding[textDescriptorOffset + 17] = (byte)'X';
            UpdateDocumentBlockChecksum(malformedTextPadding, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    malformedTextPadding));

            byte[] unsupportedRangeLimits = (byte[])validDocument.Clone();
            SetOccupiedMonitorDescriptor(unsupportedRangeLimits, 2, 0xFD);
            UpdateDocumentBlockChecksum(unsupportedRangeLimits, 0);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedRangeLimits));

            byte[] unsupportedAudioBlock = (byte[])validDocument.Clone();
            unsupportedAudioBlock[extensionOffset + 2] = 8;
            unsupportedAudioBlock[extensionOffset + 4] = 0x23;
            UpdateDocumentBlockChecksum(unsupportedAudioBlock, 1);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    unsupportedAudioBlock));

            byte[] anotherUnit = (byte[])validDocument.Clone();
            anotherUnit[12] = 0x12;
            anotherUnit[13] = 0x34;
            anotherUnit[16] = 0x20;
            anotherUnit[17] = 0x22;
            UpdateDocumentBlockChecksum(anotherUnit, 0);
            Check.Equal(
                EdidBaseBlock.ComputeNormalizedDocumentSignature(validDocument),
                EdidBaseBlock.ComputeNormalizedDocumentSignature(anotherUnit));

            var multipleCtaExtensions =
                new byte[EdidBaseBlock.Length * 3];
            Buffer.BlockCopy(
                validDocument,
                0,
                multipleCtaExtensions,
                0,
                EdidBaseBlock.Length * 2);
            Buffer.BlockCopy(
                validDocument,
                EdidBaseBlock.Length,
                multipleCtaExtensions,
                EdidBaseBlock.Length * 2,
                EdidBaseBlock.Length);
            multipleCtaExtensions[126] = 2;
            UpdateDocumentBlockChecksum(multipleCtaExtensions, 0);
            Check.True(
                EdidBaseBlock.HasValidCompleteDocument(
                    multipleCtaExtensions));

            byte[] inconsistentCtaCapabilities =
                (byte[])multipleCtaExtensions.Clone();
            inconsistentCtaCapabilities[
                (EdidBaseBlock.Length * 2) + 3] = 0x80;
            UpdateDocumentBlockChecksum(inconsistentCtaCapabilities, 2);
            Check.False(
                EdidBaseBlock.HasValidCompleteDocument(
                    inconsistentCtaCapabilities));

            byte[] changedExtension = (byte[])validDocument.Clone();
            changedExtension[extensionOffset + 3] = 0x80;
            UpdateDocumentBlockChecksum(changedExtension, 1);
            Check.True(
                EdidBaseBlock.HasValidCompleteDocument(changedExtension));
            Sha256Digest originalSourceSignature =
                EdidBaseBlock.ComputeNormalizedDocumentSignature(validDocument);
            Sha256Digest changedSourceSignature =
                EdidBaseBlock.ComputeNormalizedDocumentSignature(
                    changedExtension);
            Check.That(
                !originalSourceSignature.Equals(changedSourceSignature),
                "A changed extension block must change source identity.");

            ExperimentalProfileGenerationResult originalGenerated =
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(CreateOriginal()));
            var changedSourceHardware = new HardwareSnapshot(
                "Apple Inc.",
                "MacBookPro16,1",
                true,
                "APPA044",
                CreateOriginal(),
                "AMD Radeon Pro",
                "PCI\\VEN_1002&DEV_7340",
                string.Empty,
                true,
                changedSourceSignature);
            ExperimentalProfileGenerationResult changedGenerated =
                Experimental48HzProfileGenerator.Generate(
                    changedSourceHardware);
            Check.True(originalGenerated.Succeeded);
            Check.True(changedGenerated.Succeeded);
            Check.That(
                !string.Equals(
                    originalGenerated.Profile.Id,
                    changedGenerated.Profile.Id,
                    StringComparison.Ordinal),
                "The generated ID must bind every source EDID extension.");
            Check.False(
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    originalGenerated.Profile.Id,
                    "MacBookPro16,1",
                    "APPA044",
                    CreateOriginal(),
                    changedSourceSignature).Succeeded);

            EdidBaseBlock panelVariant = CreatePanelVariant(0xA045);
            CheckGenerationRejected(new HardwareSnapshot(
                "Apple Inc.",
                "MacBookPro16,1",
                true,
                panelVariant.HardwareId,
                panelVariant,
                "AMD Radeon Pro",
                "PCI\\VEN_1002&DEV_7340",
                string.Empty));
            CheckGenerationRejected(CreateGeneratorHardware(
                panelVariant,
                "Apple Inc.",
                "MacBookPro16,1",
                true,
                "APPA045",
                "PCI\\VEN_1002&DEV_7340",
                false));
            Check.True(
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(panelVariant)).Succeeded);
        }

        private static void GeneratorRejectsUnsafeTimingAndLayout()
        {
            byte[] noPreferredFlag = CreateOriginal().ToByteArray();
            noPreferredFlag[24] = (byte)(noPreferredFlag[24] & ~0x02);
            EdidBaseBlock.UpdateChecksum(noPreferredFlag);
            CheckGenerationRejected(CreateGeneratorHardware(
                new EdidBaseBlock(noPreferredFlag)));

            byte[] nonDtd = CreateOriginal().ToByteArray();
            SetOccupiedMonitorDescriptor(nonDtd, 0, 0xFC);
            EdidBaseBlock.UpdateChecksum(nonDtd);
            CheckGenerationRejected(CreateGeneratorHardware(
                new EdidBaseBlock(nonDtd)));

            byte[] interlaced = CreateOriginal().ToByteArray();
            interlaced[
                EdidBaseBlock.FirstDescriptorOffset +
                DetailedTiming.EncodedLength -
                1] |= 0x80;
            EdidBaseBlock.UpdateChecksum(interlaced);
            CheckGenerationRejected(CreateGeneratorHardware(
                new EdidBaseBlock(interlaced)));

            byte[] zeroSyncWidth = CreateOriginal().ToByteArray();
            int preferredOffset = EdidBaseBlock.FirstDescriptorOffset;
            zeroSyncWidth[preferredOffset + 9] = 0;
            zeroSyncWidth[preferredOffset + 11] =
                (byte)(zeroSyncWidth[preferredOffset + 11] & 0xCF);
            EdidBaseBlock.UpdateChecksum(zeroSyncWidth);
            CheckGenerationRejected(CreateGeneratorHardware(
                new EdidBaseBlock(zeroSyncWidth)));

            byte[] occupied = CreateOriginal().ToByteArray();
            SetOccupiedMonitorDescriptor(occupied, 2, 0xFC);
            SetOccupiedMonitorDescriptor(occupied, 3, 0xFE);
            EdidBaseBlock.UpdateChecksum(occupied);
            CheckGenerationRejected(CreateGeneratorHardware(
                new EdidBaseBlock(occupied)));

            EdidBaseBlock existingTarget = CreateOriginal().InsertDetailedTiming(
                DetailedTiming.ParseHex(Exact48Dtd));
            CheckGenerationRejected(CreateGeneratorHardware(existingTarget));
        }

        private static void NativeRefreshBoundsAreInclusive()
        {
            DetailedTiming native = CreateOriginal().PreferredTiming;
            long totalPixels =
                native.HorizontalTotal * (long)native.VerticalTotal;
            int minimumAllowedClock = (int)(
                ((totalPixels * 59L) + 9999L) / 10000L);
            int maximumAllowedClock = (int)(
                (totalPixels * 61L) / 10000L);

            EdidBaseBlock minimumAllowed = WithPreferredPixelClock(
                CreateOriginal(),
                minimumAllowedClock);
            EdidBaseBlock maximumAllowed = WithPreferredPixelClock(
                CreateOriginal(),
                maximumAllowedClock);
            Check.That(
                minimumAllowed.PreferredTiming.RefreshRateHertz >= 59.0,
                "The encoded minimum fixture must be inside the inclusive gate.");
            Check.That(
                maximumAllowed.PreferredTiming.RefreshRateHertz <= 61.0,
                "The encoded maximum fixture must be inside the inclusive gate.");
            Check.True(
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(minimumAllowed)).Succeeded);
            Check.True(
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(maximumAllowed)).Succeeded);

            CheckGenerationRejected(CreateGeneratorHardware(
                WithPreferredPixelClock(
                    CreateOriginal(),
                    minimumAllowedClock - 1)));
            CheckGenerationRejected(CreateGeneratorHardware(
                WithPreferredPixelClock(
                    CreateOriginal(),
                    maximumAllowedClock + 1)));
        }

        private static void GeneratedRecoveryReprovesIdentity()
        {
            EdidBaseBlock original = CreatePanelVariant(0xA045);
            ExperimentalProfileGenerationResult generated =
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(original));
            Check.True(generated.Succeeded);
            Check.Equal("mbp161-7340", generated.HardwareKey);

            ExperimentalProfileGenerationResult originalRecovery =
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    generated.Profile.Id,
                    "MacBookPro16,1",
                    "APPA045",
                    original,
                    generated.Profile.SourceEdidSignature);
            Check.True(originalRecovery.Succeeded);
            Check.Equal(generated.Profile.Id, originalRecovery.Profile.Id);
            Check.False(
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    generated.Profile.Id,
                    "MacBookPro16,1",
                    "APPA045",
                    original,
                    null).Succeeded);

            byte[] ownedBytes = original.InsertDetailedTiming(
                generated.Profile.TargetTiming).ToByteArray();
            SetOccupiedMonitorDescriptor(ownedBytes, 3, 0xFE);
            EdidBaseBlock.UpdateChecksum(ownedBytes);
            EdidBaseBlock ownedWithoutFreeSlot = new EdidBaseBlock(ownedBytes);
            Check.Equal(-1, ownedWithoutFreeSlot.FindFreeDescriptor());
            Check.True(
                ownedWithoutFreeSlot.ContainsDetailedTiming(
                    generated.Profile.TargetTiming));
            ExperimentalProfileGenerationResult ownedRecovery =
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    generated.Profile.Id,
                    "mbp161-7340",
                    "MacBookPro16,1",
                    "APPA045",
                    ownedWithoutFreeSlot,
                    generated.Profile.SourceEdidSignature);
            Check.True(ownedRecovery.Succeeded);
            Check.BytesEqual(
                generated.Profile.TargetTiming.ToByteArray(),
                ownedRecovery.Profile.TargetTiming.ToByteArray());
            CheckGenerationRejected(CreateGeneratorHardware(
                ownedWithoutFreeSlot));

            string tamperedId = generated.Profile.Id.Substring(
                0,
                generated.Profile.Id.Length - 1) +
                (generated.Profile.Id.EndsWith("0", StringComparison.Ordinal)
                    ? "1"
                    : "0");
            Check.False(
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    tamperedId,
                    "MacBookPro16,1",
                    "APPA045",
                    original,
                    generated.Profile.SourceEdidSignature).Succeeded);
            Check.False(
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    generated.Profile.Id,
                    "MacBookPro16,4",
                    "APPA045",
                    original,
                    generated.Profile.SourceEdidSignature).Succeeded);
            Check.False(
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    generated.Profile.Id,
                    "mbp164-7360",
                    "MacBookPro16,1",
                    "APPA045",
                    original,
                    generated.Profile.SourceEdidSignature).Succeeded);
            Check.False(
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    generated.Profile.Id,
                    "MacBookPro16,1",
                    "APPA046",
                    original,
                    generated.Profile.SourceEdidSignature).Succeeded);
            Check.False(
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    generated.Profile.Id,
                    "MacBookPro16,1",
                    "APPA046",
                    CreatePanelVariant(0xA046),
                    generated.Profile.SourceEdidSignature).Succeeded);
            Check.False(
                Experimental48HzProfileGenerator.ResolveForRecovery(
                    generated.Profile.Id,
                    "MacBookPro16,1",
                    "APPA045",
                    WithPreferredPixelClock(
                        original,
                        original.PreferredTiming.PixelClock10Khz - 1),
                    generated.Profile.SourceEdidSignature).Succeeded);
        }

        private static void GeneratedJournalOmitsTimingBytes()
        {
            EdidBaseBlock original = CreatePanelVariant(0xA045);
            ExperimentalProfileGenerationResult generated =
                Experimental48HzProfileGenerator.Generate(
                    CreateGeneratorHardware(original));
            Check.True(generated.Succeeded);

            byte[] ownedOverride = original.InsertDetailedTiming(
                generated.Profile.TargetTiming).ToByteArray();
            var target = new EdidTargetIdentity(
                generated.Profile.Id,
                "DISPLAY\\APPA045\\4&REDACTED&0&UID0000",
                "APPA045",
                "APP",
                Sha256Digest.Compute(original.ToByteArray()));
            var payload = new EdidJournalPayload(
                target,
                Sha256Digest.Compute(ownedOverride),
                generated.Profile.SourceEdidSignature);
            DateTime now = new DateTime(
                2026,
                8,
                10,
                12,
                0,
                0,
                DateTimeKind.Utc);
            var journal = new EdidJournal(
                JournalOperationId.NewId(),
                new JournalGeneration(1),
                now,
                now,
                EdidJournalState.InstallPending,
                payload);

            byte[] serialized = JournalCodec.Serialize(journal);
            Check.False(ContainsBytes(
                serialized,
                generated.Profile.TargetTiming.ToByteArray()));
            EdidJournal parsed = JournalCodec.Parse(serialized) as EdidJournal;
            Check.NotNull(parsed);
            Check.Equal(
                generated.Profile.Id,
                parsed.Payload.Target.ProfileId);
            Check.Equal(
                payload.OwnedOverrideHash,
                parsed.Payload.OwnedOverrideHash);
            Check.Equal(
                payload.Target.NormalizedEdidHash,
                parsed.Payload.Target.NormalizedEdidHash);
            Check.Equal(
                payload.SourceEdidSignature,
                parsed.Payload.SourceEdidSignature);
            Check.Throws<ArgumentException>(delegate
            {
                parsed.TransitionTo(
                    EdidJournalState.Installed,
                    new EdidJournalPayload(
                        payload.Target,
                        payload.OwnedOverrideHash,
                        Sha256Digest.Compute(new byte[] { 1 })),
                    parsed.Generation.Next(),
                    now.AddSeconds(1));
            });
        }

        private static void UnknownProfileRejected()
        {
            var wrongModel = new HardwareSnapshot(
                "Apple Inc.",
                "MacBookPro16,2",
                true,
                "APPA044",
                CreateOriginal(),
                "AMD Radeon Pro 5300M",
                "PCI\\VEN_1002&DEV_7340",
                "30.0.13045.22003");
            Check.False(ProfileCatalog.Select(wrongModel).HardwareSupported);

            var almostApple = new HardwareSnapshot(
                "Apple Computer, Inc.",
                "MacBookPro16,1",
                true,
                "APPA044",
                CreateOriginal(),
                "AMD Radeon Pro 5300M",
                "PCI\\VEN_1002&DEV_7340",
                "30.0.13045.22003");
            Check.False(ProfileCatalog.Select(almostApple).HardwareSupported);

            var unknownLayoutBytes = CreateOriginal().ToByteArray();
            unknownLayoutBytes[24] ^= 0x01;
            EdidBaseBlock.UpdateChecksum(unknownLayoutBytes);
            var unknownLayout = CreateKnownHardware(new EdidBaseBlock(unknownLayoutBytes));
            var selection = ProfileCatalog.Select(unknownLayout);
            Check.False(selection.HardwareSupported);
            Check.True(selection.ClosestMatch.RejectionReasons.Count > 0);

            var external = new HardwareSnapshot(
                "Apple Inc.",
                "MacBookPro16,1",
                false,
                "APPA044",
                CreateOriginal(),
                "AMD Radeon Pro 5300M",
                "PCI\\VEN_1002&DEV_7340",
                "30.0.13045.22003");
            Check.False(ProfileCatalog.Select(external).HardwareSupported);

            var otherGpu = new HardwareSnapshot(
                "Apple Inc.",
                "MacBookPro16,1",
                true,
                "APPA044",
                CreateOriginal(),
                "AMD Radeon Pro 5500M",
                "PCI\\VEN_1002&DEV_7341",
                "30.0.13045.22003");
            Check.False(ProfileCatalog.Select(otherGpu).HardwareSupported);
        }

        private static void OccupiedLayoutRejected()
        {
            var occupiedBytes = CreateOriginal().ToByteArray();
            SetOccupiedMonitorDescriptor(occupiedBytes, 2, 0xFC);
            SetOccupiedMonitorDescriptor(occupiedBytes, 3, 0xFE);
            EdidBaseBlock.UpdateChecksum(occupiedBytes);

            var occupied = new EdidBaseBlock(occupiedBytes);
            Check.Equal(-1, occupied.FindFreeDescriptor());
            Check.Throws<InvalidOperationException>(
                delegate
                {
                    occupied.InsertDetailedTiming(DetailedTiming.ParseHex(Exact48Dtd));
                });
        }

        private static void CapabilitySplit()
        {
            var occupiedBytes = CreateOriginal().ToByteArray();
            SetOccupiedMonitorDescriptor(occupiedBytes, 2, 0xFC);
            SetOccupiedMonitorDescriptor(occupiedBytes, 3, 0xFE);
            EdidBaseBlock.UpdateChecksum(occupiedBytes);

            var occupiedHardware = CreateKnownHardware(
                new EdidBaseBlock(occupiedBytes));
            var selection = ProfileCatalog.Select(occupiedHardware);
            var match = selection.Profile.Match(occupiedHardware);

            Check.True(match.HardwareSupported);
            Check.False(match.CanInstall);
            Check.Equal(1, match.InstallBlockers.Count);
            Check.True(selection.HardwareSupported);
            Check.False(selection.CanInstall);
            Check.Throws<InvalidOperationException>(
                delegate
                {
                    selection.Profile.BuildOverride(occupiedHardware);
                });
        }

        private static void IdentityContracts()
        {
            var original = CreateOriginal();
            var identity = MonitorIdentity.FromNormalizedEdid(
                "display\\appa044\\4&abcd1234&0&uid00000000",
                "monitor\\appa044\\example-instance",
                original);
            var equivalent = new MonitorIdentity(
                "DISPLAY\\APPA044\\4&ABCD1234&0&UID00000000",
                "APPA044",
                "APP",
                original.NormalizedSignature);

            Check.Equal(
                "DISPLAY\\APPA044\\4&ABCD1234&0&UID00000000",
                identity.MonitorInstanceId);
            Check.Equal("APPA044", identity.PanelHardwareId);
            Check.Equal("APP", identity.ManufacturerCode);
            Check.True(identity.Equals(equivalent));

            var target = new EdidTargetIdentity(
                "macbookpro16-1-appa044-48hz",
                "DISPLAY\\APPA044\\4&ABCD1234&0&UID00000000",
                "APPA044",
                "APP",
                Sha256Digest.Compute(original.ToByteArray()));
            Check.Equal(identity.MonitorInstanceId, target.Monitor.MonitorInstanceId);
            Check.Equal(target.NormalizedEdidHash, target.Monitor.EdidFingerprint);
            Check.Throws<ArgumentException>(
                delegate
                {
                    new MonitorIdentity(
                        "HKLM\\SYSTEM\\CurrentControlSet",
                        "APPA044",
                        "APP",
                        original.NormalizedSignature);
                });

            var endpoint = new DisplayEndpoint(
                0x0102030405060708UL,
                3,
                7,
                @"\\.\display1");
            var equivalentEndpoint = new DisplayEndpoint(
                0x0102030405060708UL,
                3,
                7,
                @"\\.\DISPLAY1");

            Check.Equal(@"\\.\DISPLAY1", endpoint.GdiDeviceName);
            Check.True(endpoint.Equals(equivalentEndpoint));
            Check.Throws<ArgumentException>(
                delegate
                {
                    new DisplayEndpoint(0, 0, 0, @"\\.\DISPLAY1\\registry");
                });
        }

        private static void RecoveryPolicyMatrix()
        {
            EdidJournalState[] states =
            {
                EdidJournalState.NotInstalled,
                EdidJournalState.InstallPending,
                EdidJournalState.Installed,
                EdidJournalState.RestorePending,
                EdidJournalState.Restored,
                EdidJournalState.Conflict
            };
            EdidLiveOverrideState[] liveStates =
            {
                EdidLiveOverrideState.Absent,
                EdidLiveOverrideState.ExactOwned,
                EdidLiveOverrideState.ForeignOrInvalid
            };
            const EdidReconciliationAction StartNew =
                EdidReconciliationAction.StartNewInstall;
            const EdidReconciliationAction Write =
                EdidReconciliationAction.WriteOwnedOverride;
            const EdidReconciliationAction MarkInstalled =
                EdidReconciliationAction.MarkInstalled;
            const EdidReconciliationAction ConfirmInstalled =
                EdidReconciliationAction.ConfirmInstalled;
            const EdidReconciliationAction ReconcileRestore =
                EdidReconciliationAction.ReconcileRestoreFirst;
            const EdidReconciliationAction StartRestore =
                EdidReconciliationAction.StartRestore;
            const EdidReconciliationAction Delete =
                EdidReconciliationAction.DeleteOwnedOverride;
            const EdidReconciliationAction MarkRestored =
                EdidReconciliationAction.MarkRestored;
            const EdidReconciliationAction ConfirmRestored =
                EdidReconciliationAction.ConfirmRestored;
            const EdidReconciliationAction Conflict =
                EdidReconciliationAction.Conflict;
            const EdidReconciliationAction Blocked =
                EdidReconciliationAction.Blocked;
            EdidReconciliationAction[,] installExpected =
            {
                { StartNew, StartNew, StartNew },
                { Write, MarkInstalled, Conflict },
                { Conflict, ConfirmInstalled, Conflict },
                { ReconcileRestore, ReconcileRestore, ReconcileRestore },
                { StartNew, StartNew, StartNew },
                { Blocked, MarkInstalled, Blocked }
            };
            EdidReconciliationAction[,] restoreExpected =
            {
                { Blocked, Blocked, Blocked },
                { StartRestore, StartRestore, Conflict },
                { Conflict, StartRestore, Conflict },
                { MarkRestored, Delete, Conflict },
                { ConfirmRestored, Conflict, Conflict },
                { Blocked, Blocked, Blocked }
            };

            for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
            {
                for (int liveIndex = 0; liveIndex < liveStates.Length; liveIndex++)
                {
                    AssertEdidPolicyAction(
                        "install",
                        states[stateIndex],
                        liveStates[liveIndex],
                        installExpected[stateIndex, liveIndex],
                        EdidRecoveryPolicy.ForInstall(
                            states[stateIndex],
                            liveStates[liveIndex]));
                    AssertEdidPolicyAction(
                        "restore",
                        states[stateIndex],
                        liveStates[liveIndex],
                        restoreExpected[stateIndex, liveIndex],
                        EdidRecoveryPolicy.ForRestore(
                            states[stateIndex],
                            liveStates[liveIndex]));
                }
            }
        }

        private static void EdidTransitionMatrix()
        {
            EdidJournalState[] states =
            {
                EdidJournalState.NotInstalled,
                EdidJournalState.InstallPending,
                EdidJournalState.Installed,
                EdidJournalState.RestorePending,
                EdidJournalState.Restored,
                EdidJournalState.Conflict
            };
            bool[,] expected =
            {
                { false, true, false, false, false, false },
                { false, true, true, true, false, true },
                { false, false, true, true, false, true },
                { false, false, false, true, true, true },
                { false, false, false, false, true, false },
                { false, false, true, false, false, true }
            };

            for (int currentIndex = 0; currentIndex < states.Length; currentIndex++)
            {
                for (int nextIndex = 0; nextIndex < states.Length; nextIndex++)
                {
                    Check.That(
                        EdidJournal.CanTransition(
                            states[currentIndex],
                            states[nextIndex]) == expected[currentIndex, nextIndex],
                        string.Format(
                            "Unexpected EDID transition result for {0} -> {1}.",
                            states[currentIndex],
                            states[nextIndex]));

                    bool expectedNewOperation =
                        (states[currentIndex] == EdidJournalState.NotInstalled ||
                         states[currentIndex] == EdidJournalState.Restored) &&
                        states[nextIndex] == EdidJournalState.InstallPending;
                    Check.That(
                        EdidJournal.CanStartNewOperation(
                            states[currentIndex],
                            states[nextIndex]) == expectedNewOperation,
                        string.Format(
                            "Unexpected EDID new-operation result for {0} -> {1}.",
                            states[currentIndex],
                            states[nextIndex]));
                }
            }
        }

        private static void RecoveryPolicyRejectsUnknownInputs()
        {
            Check.Throws<ArgumentOutOfRangeException>(
                delegate
                {
                    EdidRecoveryPolicy.ForInstall(
                        (EdidJournalState)255,
                        EdidLiveOverrideState.Absent);
                });
            Check.Throws<ArgumentOutOfRangeException>(
                delegate
                {
                    EdidRecoveryPolicy.ForRestore(
                        EdidJournalState.RestorePending,
                        (EdidLiveOverrideState)255);
                });
        }

        private static void AssertEdidPolicyAction(
            string operation,
            EdidJournalState state,
            EdidLiveOverrideState liveState,
            EdidReconciliationAction expected,
            EdidReconciliationAction actual)
        {
            Check.That(
                expected == actual,
                string.Format(
                    "EDID {0} policy for {1}/{2}: expected {3}, received {4}.",
                    operation,
                    state,
                    liveState,
                    expected,
                    actual));
        }

        private static void RefreshOnlyModeSelectionPreservesCurrentKey()
        {
            var current = new DisplayModeKey(
                3072,
                1920,
                24,
                60,
                0,
                0,
                0);
            var exact48 = new DisplayModeKey(
                3072,
                1920,
                24,
                48,
                0,
                0,
                0);

            Check.True(
                DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    current,
                    exact48,
                    48));
            Check.False(
                DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    current,
                    exact48,
                    60));

            var exactNativeRate = current.WithRefreshRateRational(
                120000,
                2002);
            var canonicalNativeRate = new DisplayModeKey(
                3072,
                1920,
                24,
                60,
                0,
                0,
                0,
                60000,
                1001);
            Check.Equal((uint)60000, exactNativeRate.RefreshRateNumerator);
            Check.Equal((uint)1001, exactNativeRate.RefreshRateDenominator);
            Check.True(exactNativeRate.Equals(canonicalNativeRate));
            Check.Equal(
                exactNativeRate.GetHashCode(),
                canonicalNativeRate.GetHashCode());

            Check.False(
                DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    current,
                    null,
                    48));
            Check.Throws<ArgumentNullException>(
                delegate
                {
                    DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                        null,
                        exact48,
                        48);
                });
            Check.Throws<ArgumentOutOfRangeException>(
                delegate
                {
                    new DisplayModeKey(3072, 1920, 24, 60, 0, 0, 0, 0, 1);
                });
            Check.Throws<ArgumentOutOfRangeException>(
                delegate
                {
                    new DisplayModeKey(3072, 1920, 24, 60, 0, 0, 0, 1, 0);
                });
        }

        private static void RefreshOnlyModeSelectionRejectsChangedDisplayProperties()
        {
            var current = new DisplayModeKey(
                3072,
                1920,
                24,
                60,
                0,
                0,
                0);

            Check.False(
                DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    current,
                    new DisplayModeKey(3072, 1920, 32, 48, 0, 0, 0),
                    48));
            Check.False(
                DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    current,
                    new DisplayModeKey(2880, 1920, 24, 48, 0, 0, 0),
                    48));
            Check.False(
                DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    current,
                    new DisplayModeKey(3072, 1824, 24, 48, 0, 0, 0),
                    48));
            Check.False(
                DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    current,
                    new DisplayModeKey(3072, 1920, 24, 48, 1, 0, 0),
                    48));
            Check.False(
                DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    current,
                    new DisplayModeKey(3072, 1920, 24, 48, 0, 1, 0),
                    48));

            // DM_INTERLACED lives in dmDisplayFlags.  It is not a preferred
            // variant of the same 48 Hz mode; it is a different mode.
            Check.False(
                DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    current,
                    new DisplayModeKey(3072, 1920, 24, 48, 0, 0, 0x2),
                    48));

            // A persisted full target key cannot smuggle a colour-depth
            // change into the refresh-only overload either.
            Check.False(
                DisplayModeSelectionPolicy.IsExactRefreshOnlyCandidate(
                    current,
                    new DisplayModeKey(3072, 1920, 24, 48, 0, 0, 0),
                    new DisplayModeKey(3072, 1920, 32, 48, 0, 0, 0)));
        }

        private static void PowerPresetCatalogIsImmutable()
        {
            PowerPresetDefinition normal =
                PowerPresetCatalog.Get(PowerPreset.Normal);

            Check.Equal(3, PowerPresetCatalog.All.Count);
            Check.Equal(PowerPreset.Normal, normal.Preset);
            Check.Equal("Everyday", normal.DisplayName);
            Check.Equal((uint)5, normal.MinimumProcessorAc);
            Check.Equal((uint)90, normal.MaximumProcessorDc);
            Check.Equal(
                ProcessorPerformanceBoostMode.Enabled,
                normal.BoostModeAc);
            Check.Throws<NotSupportedException>(
                delegate
                {
                    PowerPresetCatalog.All.Add(normal);
                });
            Check.Throws<ArgumentOutOfRangeException>(
                delegate
                {
                    PowerPresetCatalog.Get((PowerPreset)255);
                });
        }

        private static void PowerRecoveryPolicyMatrix()
        {
            PowerOwnedSchemeState[] ownedStates =
            {
                PowerOwnedSchemeState.Missing,
                PowerOwnedSchemeState.ExactOwned,
                PowerOwnedSchemeState.UnmarkedDuplicate,
                PowerOwnedSchemeState.ForeignOrDiverged
            };
            PowerActiveSchemeRelation[] activeRelations =
            {
                PowerActiveSchemeRelation.Owned,
                PowerActiveSchemeRelation.Original,
                PowerActiveSchemeRelation.External
            };
            PowerReconciliationAction[] appliedWhenOwned =
            {
                PowerReconciliationAction.ConfirmApplied,
                PowerReconciliationAction.RetainOriginalSelection,
                PowerReconciliationAction.RetainExternalSelection
            };
            PowerReconciliationAction[] restoreWhenOwned =
            {
                PowerReconciliationAction.ActivateOriginal,
                PowerReconciliationAction.CompleteRetainedOriginal,
                PowerReconciliationAction.RetainExternalSelection
            };

            for (int ownedIndex = 0; ownedIndex < ownedStates.Length; ownedIndex++)
            {
                PowerOwnedSchemeState owned = ownedStates[ownedIndex];
                for (int existsIndex = 0; existsIndex < 2; existsIndex++)
                {
                    bool originalExists = existsIndex == 1;
                    PowerReconciliationAction creatingExpected =
                        !originalExists ||
                        owned == PowerOwnedSchemeState.ForeignOrDiverged
                            ? PowerReconciliationAction.Conflict
                            : owned == PowerOwnedSchemeState.Missing
                                ? PowerReconciliationAction.DuplicateWithRecordedGuid
                                : PowerReconciliationAction.ConfigureAndActivate;
                    AssertPowerPolicyAction(
                        "creating",
                        owned,
                        originalExists,
                        null,
                        creatingExpected,
                        PowerRecoveryPolicy.ForCreating(owned, originalExists));

                    for (int activeIndex = 0;
                        activeIndex < activeRelations.Length;
                        activeIndex++)
                    {
                        PowerActiveSchemeRelation active =
                            activeRelations[activeIndex];
                        bool canUseOwned = originalExists &&
                            owned == PowerOwnedSchemeState.ExactOwned;
                        AssertPowerPolicyAction(
                            "applied",
                            owned,
                            originalExists,
                            active,
                            canUseOwned
                                ? appliedWhenOwned[activeIndex]
                                : PowerReconciliationAction.Conflict,
                            PowerRecoveryPolicy.ForApplied(
                                owned,
                                originalExists,
                                active));
                        AssertPowerPolicyAction(
                            "restore-pending",
                            owned,
                            originalExists,
                            active,
                            canUseOwned
                                ? restoreWhenOwned[activeIndex]
                                : PowerReconciliationAction.Conflict,
                            PowerRecoveryPolicy.ForRestorePending(
                                owned,
                                originalExists,
                                active));
                    }
                }

                for (int activeIsOwnedIndex = 0;
                    activeIsOwnedIndex < 2;
                    activeIsOwnedIndex++)
                {
                    bool activeIsOwned = activeIsOwnedIndex == 1;
                    PowerReconciliationAction expected =
                        owned != PowerOwnedSchemeState.ExactOwned
                            ? PowerReconciliationAction.Conflict
                            : activeIsOwned
                                ? PowerReconciliationAction.ConfigureAndActivate
                                : PowerReconciliationAction.ReactivateRetained;
                    AssertPowerPolicyAction(
                        "inactive-retained",
                        owned,
                        true,
                        activeIsOwned
                            ? PowerActiveSchemeRelation.Owned
                            : PowerActiveSchemeRelation.External,
                        expected,
                        PowerRecoveryPolicy.ForInactiveRetained(
                            owned,
                            activeIsOwned));
                }
            }
        }

        private static void AssertPowerPolicyAction(
            string operation,
            PowerOwnedSchemeState owned,
            bool originalExists,
            PowerActiveSchemeRelation? active,
            PowerReconciliationAction expected,
            PowerReconciliationAction actual)
        {
            Check.That(
                expected == actual,
                string.Format(
                    "Power {0} policy for {1}/original={2}/active={3}: " +
                        "expected {4}, received {5}.",
                    operation,
                    owned,
                    originalExists,
                    active.HasValue ? active.Value.ToString() : "n/a",
                    expected,
                    actual));
        }

        private static void PowerFaultRecoveryRetainsSingleResource()
        {
            // This is a host-safe model of every durable boundary in a full
            // apply -> restore -> apply cycle.  It uses the production journal
            // value objects and reconciliation policy only; it does not call a
            // live power API or mutate a Windows power scheme.
            DateTime started = new DateTime(
                2026,
                7,
                30,
                12,
                0,
                0,
                DateTimeKind.Utc);
            Guid original = Guid.Parse("EA93F2E7-B985-4F7D-A3D1-1EA337B11CD1");
            Guid owned = Guid.Parse("0F555C01-3A9B-4912-8A90-6EBA504D9A10");
            Guid external = Guid.Parse("447D3A4E-E5B5-4A55-BDB9-A67FC16FD31C");
            Sha256Digest policyHash = Sha256Digest.Compute(new byte[]
            {
                0x50, 0x57, 0x52, 0x2D, 0x54, 0x45, 0x53, 0x54
            });
            PowerTargetIdentity target = new PowerTargetIdentity(
                original,
                owned,
                PowerPresetId.Cool,
                policyHash);
            PowerJournal creating = new PowerJournal(
                new JournalOperationId(Guid.Parse(
                    "7820070D-73A8-42FA-AE3F-5B840B5D9B9C")),
                new JournalGeneration(1),
                started,
                started,
                PowerJournalState.Creating,
                new PowerJournalPayload(target, PowerInactiveReason.None));

            // Durable Creating is the recoverable intent that must exist
            // before duplicate.  A crash directly after duplicate reloads the
            // exact GUID and resumes configuration instead of allocating one.
            PowerJournal afterDuplicateFault = JournalCodec.Parse(
                JournalCodec.Serialize(creating)) as PowerJournal;
            Check.NotNull(afterDuplicateFault);
            Check.Equal(PowerJournalState.Creating, afterDuplicateFault.State);
            Check.Equal((ulong)1, afterDuplicateFault.Generation.Value);
            Check.Equal(original, afterDuplicateFault.Payload.Target.OriginalSchemeId);
            Check.Equal(owned, afterDuplicateFault.Payload.Target.OwnedSchemeId);
            Check.Equal(
                PowerReconciliationAction.DuplicateWithRecordedGuid,
                PowerRecoveryPolicy.ForCreating(
                    PowerOwnedSchemeState.Missing,
                    true));
            Check.Equal(
                PowerReconciliationAction.ConfigureAndActivate,
                PowerRecoveryPolicy.ForCreating(
                    PowerOwnedSchemeState.UnmarkedDuplicate,
                    true));
            Check.Equal(
                PowerReconciliationAction.Conflict,
                PowerRecoveryPolicy.ForCreating(
                    PowerOwnedSchemeState.ForeignOrDiverged,
                    true));

            PowerJournal applied = afterDuplicateFault.TransitionTo(
                PowerJournalState.Applied,
                new PowerJournalPayload(
                    afterDuplicateFault.Payload.Target,
                    PowerInactiveReason.None),
                afterDuplicateFault.Generation.Next(),
                started.AddMinutes(1));
            Check.Equal((ulong)2, applied.Generation.Value);
            Check.Equal(
                PowerReconciliationAction.RetainOriginalSelection,
                PowerRecoveryPolicy.ForApplied(
                    PowerOwnedSchemeState.ExactOwned,
                    true,
                    PowerActiveSchemeRelation.Original));
            Check.Equal(
                PowerReconciliationAction.RetainExternalSelection,
                PowerRecoveryPolicy.ForApplied(
                    PowerOwnedSchemeState.ExactOwned,
                    true,
                    PowerActiveSchemeRelation.External));

            // An external selection leaves the owned GUID intact.  The next
            // Apply records that exact active scheme as the new return target,
            // but it cannot repurpose another owned GUID or settings policy.
            PowerJournal retained = applied.TransitionTo(
                PowerJournalState.InactiveRetained,
                new PowerJournalPayload(
                    applied.Payload.Target,
                    PowerInactiveReason.ExternalSelection),
                applied.Generation.Next(),
                started.AddMinutes(2));
            PowerTargetIdentity reactivationTarget =
                retained.Payload.Target.WithOriginalScheme(external);
            PowerJournal reactivationIntent = retained.TransitionTo(
                PowerJournalState.Creating,
                new PowerJournalPayload(
                    reactivationTarget,
                    PowerInactiveReason.None),
                retained.Generation.Next(),
                started.AddMinutes(3));
            Check.Equal((ulong)4, reactivationIntent.Generation.Value);
            Check.Equal(external, reactivationIntent.Payload.Target.OriginalSchemeId);
            Check.Equal(owned, reactivationIntent.Payload.Target.OwnedSchemeId);
            Check.True(reactivationIntent.Payload.Target.HasSameOwnedResource(
                retained.Payload.Target));
            Check.Equal(
                PowerReconciliationAction.ReactivateRetained,
                PowerRecoveryPolicy.ForInactiveRetained(
                    PowerOwnedSchemeState.ExactOwned,
                    false));

            PowerTargetIdentity replacementOwnedResource =
                new PowerTargetIdentity(
                    external,
                    Guid.Parse("1D2B4E6B-088B-42D1-8296-277E59932E88"),
                    PowerPresetId.Cool,
                    policyHash);
            Check.Throws<ArgumentException>(
                delegate
                {
                    retained.TransitionTo(
                        PowerJournalState.Creating,
                        new PowerJournalPayload(
                            replacementOwnedResource,
                            PowerInactiveReason.None),
                        retained.Generation.Next(),
                        started.AddMinutes(3));
                });

            PowerJournal reapplied = reactivationIntent.TransitionTo(
                PowerJournalState.Applied,
                new PowerJournalPayload(
                    reactivationIntent.Payload.Target,
                    PowerInactiveReason.None),
                reactivationIntent.Generation.Next(),
                started.AddMinutes(4));
            PowerJournal restorePending = reapplied.TransitionTo(
                PowerJournalState.RestorePending,
                new PowerJournalPayload(
                    reapplied.Payload.Target,
                    PowerInactiveReason.None),
                reapplied.Generation.Next(),
                started.AddMinutes(5));
            Check.Equal((ulong)6, restorePending.Generation.Value);
            Check.Equal(
                PowerReconciliationAction.ActivateOriginal,
                PowerRecoveryPolicy.ForRestorePending(
                    PowerOwnedSchemeState.ExactOwned,
                    true,
                    PowerActiveSchemeRelation.Owned));
            Check.Equal(
                PowerReconciliationAction.CompleteRetainedOriginal,
                PowerRecoveryPolicy.ForRestorePending(
                    PowerOwnedSchemeState.ExactOwned,
                    true,
                    PowerActiveSchemeRelation.Original));
            Check.Equal(
                PowerReconciliationAction.RetainExternalSelection,
                PowerRecoveryPolicy.ForRestorePending(
                    PowerOwnedSchemeState.ExactOwned,
                    true,
                    PowerActiveSchemeRelation.External));

            PowerJournal restored = restorePending.TransitionTo(
                PowerJournalState.InactiveRetained,
                new PowerJournalPayload(
                    restorePending.Payload.Target,
                    PowerInactiveReason.OriginalAlreadyActive),
                restorePending.Generation.Next(),
                started.AddMinutes(6));
            Check.Equal((ulong)7, restored.Generation.Value);
            Check.Equal(owned, restored.Payload.Target.OwnedSchemeId);
            Check.Equal(
                PowerInactiveReason.OriginalAlreadyActive,
                restored.Payload.InactiveReason);
        }

        private static void PowerRecoveryPolicyFailsClosed()
        {
            Check.Equal(
                PowerReconciliationAction.Conflict,
                PowerRecoveryPolicy.ForCreating(
                    (PowerOwnedSchemeState)255,
                    true));
            Check.Equal(
                PowerReconciliationAction.Conflict,
                PowerRecoveryPolicy.ForApplied(
                    PowerOwnedSchemeState.ExactOwned,
                    true,
                    (PowerActiveSchemeRelation)255));
            Check.Equal(
                PowerReconciliationAction.Conflict,
                PowerRecoveryPolicy.ForRestorePending(
                    PowerOwnedSchemeState.ForeignOrDiverged,
                    true,
                    PowerActiveSchemeRelation.Owned));
        }

        private static void TerminalStatusReadersRequireExactLiveState()
        {
            Check.Equal(
                ManagedResourceState.Installed,
                EdidStatusReader.ClassifyTerminalState(
                    EdidJournalState.Installed,
                    EdidLiveOverrideState.ExactOwned));
            Check.Equal(
                ManagedResourceState.Conflict,
                EdidStatusReader.ClassifyTerminalState(
                    EdidJournalState.Installed,
                    EdidLiveOverrideState.Absent));
            Check.Equal(
                ManagedResourceState.Conflict,
                EdidStatusReader.ClassifyTerminalState(
                    EdidJournalState.Installed,
                    EdidLiveOverrideState.ForeignOrInvalid));
            Check.Equal(
                ManagedResourceState.Restored,
                EdidStatusReader.ClassifyTerminalState(
                    EdidJournalState.Restored,
                    EdidLiveOverrideState.Absent));
            Check.Equal(
                ManagedResourceState.Conflict,
                EdidStatusReader.ClassifyTerminalState(
                    EdidJournalState.Restored,
                    EdidLiveOverrideState.ExactOwned));

            Check.Equal(
                ManagedResourceState.Installed,
                PowerStatusReader.ClassifyTerminalState(
                    PowerJournalState.Applied,
                    PowerOwnedSchemeState.ExactOwned,
                    true));
            Check.Equal(
                ManagedResourceState.RecoveryRequired,
                PowerStatusReader.ClassifyTerminalState(
                    PowerJournalState.Applied,
                    PowerOwnedSchemeState.ExactOwned,
                    false));
            Check.Equal(
                ManagedResourceState.Conflict,
                PowerStatusReader.ClassifyTerminalState(
                    PowerJournalState.Applied,
                    PowerOwnedSchemeState.ForeignOrDiverged,
                    true));
            Check.Equal(
                ManagedResourceState.Restored,
                PowerStatusReader.ClassifyTerminalState(
                    PowerJournalState.InactiveRetained,
                    PowerOwnedSchemeState.ExactOwned,
                    false));
            Check.Equal(
                ManagedResourceState.RecoveryRequired,
                PowerStatusReader.ClassifyTerminalState(
                    PowerJournalState.InactiveRetained,
                    PowerOwnedSchemeState.ExactOwned,
                    true));
        }

        private static void PowerOwnershipIncludesLiveSettings()
        {
            Guid original =
                Guid.Parse("C9B9AE0E-D75A-4F22-AE4D-146BA8264CD1");
            Guid owned =
                Guid.Parse("AD7D28E2-7E8F-4B38-A35B-A84DA9239900");
            PowerTargetIdentity target = new PowerTargetIdentity(
                original,
                owned,
                PowerPresetId.Cool,
                PowerManagedSettings.ComputeManagedSettingsHash(PowerPreset.Cool));
            PowerPresetDefinition preset =
                PowerPresetCatalog.Get(PowerPreset.Cool);
            uint[] expectedAc =
            {
                preset.MinimumProcessorAc,
                preset.MaximumProcessorAc,
                (uint)preset.BoostModeAc,
                preset.EnergyPreferenceAc,
                preset.CoolingPolicyAc
            };
            uint[] expectedDc =
            {
                preset.MinimumProcessorDc,
                preset.MaximumProcessorDc,
                (uint)preset.BoostModeDc,
                preset.EnergyPreferenceDc,
                preset.CoolingPolicyDc
            };
            int settingIndex = 0;

            TryReadPowerSchemeName readName = delegate(
                Guid scheme,
                out string name)
            {
                Check.Equal(owned, scheme);
                name = PowerSchemeNaming.OwnedFriendlyName(
                    PowerPreset.Cool,
                    owned);
                return true;
            };
            TryReadPowerSettingValues readExactSetting = delegate(
                Guid scheme,
                Guid setting,
                out uint ac,
                out uint dc)
            {
                Check.Equal(owned, scheme);
                Check.That(setting != Guid.Empty, "Managed setting GUID is empty.");
                ac = expectedAc[settingIndex];
                dc = expectedDc[settingIndex];
                settingIndex++;
                return true;
            };

            Check.Equal(
                PowerOwnedSchemeState.ExactOwned,
                PowerManagedSettings.ClassifyOwnedScheme(
                    target,
                    readName,
                    readExactSetting));
            Check.Equal(expectedAc.Length, settingIndex);

            settingIndex = 0;
            TryReadPowerSettingValues readDivergedSetting = delegate(
                Guid scheme,
                Guid setting,
                out uint ac,
                out uint dc)
            {
                ac = expectedAc[settingIndex];
                dc = expectedDc[settingIndex];
                if (settingIndex == 2)
                {
                    dc++;
                }
                settingIndex++;
                return true;
            };
            Check.Equal(
                PowerOwnedSchemeState.ForeignOrDiverged,
                PowerManagedSettings.ClassifyOwnedScheme(
                    target,
                    readName,
                    readDivergedSetting));
            Check.Equal(3, settingIndex);

            Check.Equal(
                PowerOwnedSchemeState.Missing,
                PowerManagedSettings.ClassifyOwnedScheme(
                    target,
                    delegate(Guid scheme, out string name)
                    {
                        name = null;
                        return false;
                    },
                    readExactSetting));

            Check.Equal(
                PowerOwnedSchemeState.ForeignOrDiverged,
                PowerManagedSettings.ClassifyOwnedScheme(
                    target,
                    delegate(Guid scheme, out string name)
                    {
                        name = "Foreign scheme";
                        return true;
                    },
                    readExactSetting));

            PowerTargetIdentity stalePolicy = new PowerTargetIdentity(
                original,
                owned,
                PowerPresetId.Cool,
                Sha256Digest.Compute(new byte[] { 1, 2, 3 }));
            Check.Equal(
                PowerOwnedSchemeState.ForeignOrDiverged,
                PowerManagedSettings.ClassifyOwnedScheme(
                    stalePolicy,
                    readName,
                    readExactSetting));
        }

        private static void PowerPresetSwitchReusesOwnedScheme()
        {
            DateTime now = new DateTime(
                2026,
                7,
                30,
                13,
                0,
                0,
                DateTimeKind.Utc);
            Guid original =
                Guid.Parse("A4C8EAA3-D4B7-49E4-A61C-92208D0929C1");
            Guid owned =
                Guid.Parse("7B3226C0-E27D-4F6B-8DB4-204F5D71FA92");
            PowerTargetIdentity cool = new PowerTargetIdentity(
                original,
                owned,
                PowerPresetId.Cool,
                Sha256Digest.Compute(new byte[] { 1, 2, 3 }));
            PowerJournal applied = new PowerJournal(
                JournalOperationId.NewId(),
                new JournalGeneration(4),
                now,
                now,
                PowerJournalState.Applied,
                new PowerJournalPayload(
                    cool,
                    PowerInactiveReason.None));
            PowerTargetIdentity normal = new PowerTargetIdentity(
                original,
                owned,
                PowerPresetId.Normal,
                Sha256Digest.Compute(new byte[] { 4, 5, 6 }));

            PowerJournal switching = applied.TransitionTo(
                PowerJournalState.Creating,
                new PowerJournalPayload(
                    normal,
                    PowerInactiveReason.None),
                applied.Generation.Next(),
                now.AddSeconds(1));
            Check.Equal(PowerJournalState.Creating, switching.State);
            Check.Equal(owned, switching.Payload.Target.OwnedSchemeId);
            Check.Equal(
                PowerPresetId.Normal,
                switching.Payload.Target.Preset);

            PowerJournal switched = switching.TransitionTo(
                PowerJournalState.Applied,
                switching.Payload,
                switching.Generation.Next(),
                now.AddSeconds(2));
            Check.Equal(PowerJournalState.Applied, switched.State);
            Check.Equal(
                PowerPresetId.Normal,
                switched.Payload.Target.Preset);

            PowerTargetIdentity replacement = new PowerTargetIdentity(
                original,
                Guid.Parse("674301A9-DB0D-43F9-8672-E16EF4EA4302"),
                PowerPresetId.MaximumBattery,
                Sha256Digest.Compute(new byte[] { 7, 8, 9 }));
            Check.Throws<ArgumentException>(
                delegate
                {
                    applied.TransitionTo(
                        PowerJournalState.Creating,
                        new PowerJournalPayload(
                            replacement,
                            PowerInactiveReason.None),
                        applied.Generation.Next(),
                        now.AddSeconds(1));
                });
        }

        private static void InvalidChecksumRejected()
        {
            var invalid = CreateOriginal().ToByteArray();
            invalid[127] ^= 0x01;
            Check.False(EdidBaseBlock.HasValidChecksum(invalid));
            Check.Throws<FormatException>(
                delegate
                {
                    new EdidBaseBlock(invalid);
                });
        }

        private static void CheckGenerationRejected(HardwareSnapshot hardware)
        {
            ExperimentalProfileGenerationResult generated =
                Experimental48HzProfileGenerator.Generate(hardware);
            Check.False(generated.Succeeded);
            Check.That(
                generated.Profile == null,
                "A rejected generated profile must not expose a candidate.");
            Check.That(
                generated.RejectionReasons.Count > 0,
                "A rejected generated profile must explain its failed gate.");
        }

        private static HardwareSnapshot CreateGeneratorHardware(
            EdidBaseBlock edid)
        {
            return CreateGeneratorHardware(
                edid,
                "Apple Inc.",
                "MacBookPro16,1",
                true,
                edid.HardwareId,
                "PCI\\VEN_1002&DEV_7340&SUBSYS_REDACTED",
                true);
        }

        private static HardwareSnapshot CreateGeneratorHardware(
            EdidBaseBlock edid,
            string manufacturer,
            string systemModel,
            bool isInternalDisplay,
            string panelHardwareId,
            string gpuDeviceId,
            bool completeEdidIsValid)
        {
            return new HardwareSnapshot(
                manufacturer,
                systemModel,
                isInternalDisplay,
                panelHardwareId,
                edid,
                "AMD Radeon Pro",
                gpuDeviceId,
                string.Empty,
                completeEdidIsValid,
                completeEdidIsValid
                    ? EdidBaseBlock.ComputeNormalizedDocumentSignature(
                        CreateCompleteEdidDocument(edid))
                    : null);
        }

        private static EdidBaseBlock CreatePanelVariant(ushort productCode)
        {
            byte[] bytes = CreateOriginal().ToByteArray();
            bytes[10] = (byte)(productCode & 0xFF);
            bytes[11] = (byte)((productCode >> 8) & 0xFF);
            EdidBaseBlock.UpdateChecksum(bytes);
            return new EdidBaseBlock(bytes);
        }

        private static EdidBaseBlock WithPreferredPixelClock(
            EdidBaseBlock source,
            int pixelClock10Khz)
        {
            DetailedTiming timing = source.PreferredTiming;
            return WithPreferredTiming(
                source,
                pixelClock10Khz,
                timing.VerticalBlanking);
        }

        private static EdidBaseBlock CreateRoundingFixture(
            int targetVerticalTotal,
            int nativeVerticalBlanking)
        {
            EdidBaseBlock source = CreateOriginal();
            DetailedTiming timing = source.PreferredTiming;
            long minimumNativeClockHertz =
                timing.HorizontalTotal *
                (long)targetVerticalTotal *
                48L;
            int nativePixelClock10Khz = (int)(
                (minimumNativeClockHertz + 9999L) / 10000L);
            return WithPreferredTiming(
                source,
                nativePixelClock10Khz,
                nativeVerticalBlanking);
        }

        private static EdidBaseBlock WithPreferredTiming(
            EdidBaseBlock source,
            int pixelClock10Khz,
            int verticalBlanking)
        {
            DetailedTiming timing = source.PreferredTiming;
            var replacement = new DetailedTiming(
                pixelClock10Khz,
                timing.HorizontalActive,
                timing.HorizontalBlanking,
                timing.VerticalActive,
                verticalBlanking,
                timing.HorizontalSyncOffset,
                timing.HorizontalSyncPulseWidth,
                timing.VerticalSyncOffset,
                timing.VerticalSyncPulseWidth,
                timing.HorizontalImageSizeMillimeters,
                timing.VerticalImageSizeMillimeters,
                timing.HorizontalBorderPixels,
                timing.VerticalBorderLines,
                timing.Flags);
            byte[] bytes = source.ToByteArray();
            replacement.WriteTo(bytes, EdidBaseBlock.FirstDescriptorOffset);
            EdidBaseBlock.UpdateChecksum(bytes);
            return new EdidBaseBlock(bytes);
        }

        private static byte[] CreateCompleteEdidDocument()
        {
            return CreateCompleteEdidDocument(CreateOriginal());
        }

        private static byte[] CreateCompleteEdidDocument(
            EdidBaseBlock source)
        {
            byte[] baseBlock = source.ToByteArray();
            var extensionBlock = new byte[EdidBaseBlock.Length];
            extensionBlock[0] = 0x02;
            extensionBlock[1] = 0x03;
            extensionBlock[2] = 0x04;
            EdidBaseBlock.UpdateChecksum(extensionBlock);

            var document = new byte[EdidBaseBlock.Length * 2];
            Buffer.BlockCopy(
                baseBlock,
                0,
                document,
                0,
                baseBlock.Length);
            Buffer.BlockCopy(
                extensionBlock,
                0,
                document,
                EdidBaseBlock.Length,
                extensionBlock.Length);
            return document;
        }

        private static void UpdateDocumentBlockChecksum(
            byte[] document,
            int blockIndex)
        {
            var block = new byte[EdidBaseBlock.Length];
            Buffer.BlockCopy(
                document,
                blockIndex * EdidBaseBlock.Length,
                block,
                0,
                block.Length);
            EdidBaseBlock.UpdateChecksum(block);
            Buffer.BlockCopy(
                block,
                0,
                document,
                blockIndex * EdidBaseBlock.Length,
                block.Length);
        }

        private static bool ContainsBytes(byte[] value, byte[] candidate)
        {
            if (
                value == null ||
                candidate == null ||
                candidate.Length == 0 ||
                candidate.Length > value.Length)
            {
                return false;
            }

            for (int offset = 0; offset <= value.Length - candidate.Length; offset++)
            {
                bool match = true;
                for (int index = 0; index < candidate.Length; index++)
                {
                    if (value[offset + index] != candidate[index])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return true;
                }
            }

            return false;
        }

        private static HardwareSnapshot CreateKnownHardware(EdidBaseBlock edid)
        {
            return new HardwareSnapshot(
                "Apple Inc.",
                "MacBookPro16,1",
                true,
                "MONITOR\\APPA044\\REDACTED",
                edid,
                "AMD Radeon Pro 5300M",
                "PCI\\VEN_1002&DEV_7340&SUBSYS_REDACTED",
                "30.0.13045.22003");
        }

        private static EdidBaseBlock CreateOriginal()
        {
            return EdidBaseBlock.ParseHex(ReviewedAppa044Edid);
        }

        private static void SetOccupiedMonitorDescriptor(
            byte[] edid,
            int descriptorIndex,
            byte tag)
        {
            var offset = EdidBaseBlock.FirstDescriptorOffset +
                (descriptorIndex * DetailedTiming.EncodedLength);
            Array.Clear(edid, offset, DetailedTiming.EncodedLength);
            edid[offset + 3] = tag;
            if (tag == 0xFC || tag == 0xFE || tag == 0xFF)
            {
                for (int index = offset + 5;
                    index < offset + DetailedTiming.EncodedLength;
                    index++)
                {
                    edid[index] = 0x20;
                }

                edid[offset + 5] = (byte)'T';
                edid[offset + 6] = 0x0A;
            }
        }
    }
}

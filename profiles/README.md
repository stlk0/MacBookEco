# Display profile authoring

The JSON files in this directory are the reviewed source for the built-in
display catalog. The application does not read JSON, accept profile paths, or
generate timings at runtime. `tools/ProfileAuthoring.ps1` validates the
manifests and produces the committed
`src/DisplayProfiles/ProfileCatalog.Generated.cs` file used by both the app and
the elevated helper.

## Create a proposal

Run the offline authoring utility on Windows with a complete binary EDID file:

```powershell
.\tools\ProfileAuthoring.ps1 -Propose `
    -EdidPath .\private\display.edid `
    -SystemModel MacBookPro16,1 `
    -GpuDeviceIdPrefix 'PCI\VEN_1002&DEV_7340' `
    -GpuName 'AMD Radeon Pro 5300M' `
    -DriverVersion '30.0.13045.22003' `
    -OutputPath .\private\profile-proposal.json
```

The utility is read-only: it does not inspect the registry, install an EDID
override, select a display mode, or require elevation. It validates every EDID
block checksum, requires a preferred native DTD near 60 Hz and a free
descriptor, and calculates the 48 Hz DTD by changing only the pixel clock and
vertical back porch. It emits reduced profile facts rather than the raw EDID.

A generated proposal is not supported hardware. Before adding it here, review
the original evidence and complete the display-profile hardware acceptance
process. Never commit the input EDID, serial number, full device-instance ID,
registry export, or private acceptance output.

## Update the compiled catalog

After a profile is accepted, copy the reviewed proposal to this directory and
run:

```powershell
.\tools\ProfileAuthoring.ps1 -Generate
```

Commit the manifest and generated C# file together. The normal Windows build
runs the same utility with `-Check` and fails if the generated catalog is stale
or a manifest violates the timing and identity rules.

Keep one profile per file. A manifest contains only the exact SMBIOS model,
panel product ID, normalized EDID signature, native and target DTDs, and the
GPU/driver facts used during review. Public research that is not sufficient for
a runtime profile belongs in
[`docs/INTEL_DGPU_MACBOOK_CATALOG.md`](../docs/INTEL_DGPU_MACBOOK_CATALOG.md).

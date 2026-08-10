# Supported hardware

The support matrix contains only reviewed, hardware-verified profiles. A local
experimental candidate may be offered on other narrowly eligible Intel MacBooks,
but generation does not make that hardware supported or verified.

## MacBook Pro 16-inch, 2019

| Field | Verified value |
|---|---|
| SMBIOS model | `MacBookPro16,1` |
| Panel hardware ID | `APPA044` |
| Native mode | `3072x1920 @ 60 Hz` |
| GPU | AMD Radeon Pro 5300M (`VEN_1002&DEV_7340`) |
| Tested driver | `30.0.13045.22003` |
| Added mode | 48 Hz |
| 48 Hz DTD | `DC 91 00 50 C0 80 24 72 08 20 98 08 59 D7 10 00 00 1A` |
| Pixel clock | `373.40 MHz` |

The model, panel, normalized original EDID, native timing, dimensions, and GPU
device ID must match. A different driver version produces a warning rather than
changing the reviewed timing.

This exact static profile always takes priority over experimental generation and
keeps its existing install, recovery, and removal behavior.

## Experimental generator (not supported hardware)

MacBook Eco may calculate one local 48 Hz candidate only when all of these facts
are proven at the same time:

- SMBIOS manufacturer is exactly `Apple Inc.` and the model is an allowlisted
  Intel MacBook with a compiled model/GPU pair (currently two exact 16-inch AMD
  pairs; NVIDIA models remain catalog research only);
- one internal panel is identified without topology or durable-identity
  ambiguity, and its controlling adapter maps to that exact compiled model/GPU
  pair (the current runtime allowlist contains AMD pairs only);
- the complete EDID document has valid block checksums and passes the
  conservative supported base/extension parser; unparsed base descriptor types
  and CTA data-block collections fail closed; its preferred descriptor is a valid
  native DTD between 59 and 61 Hz inclusive, and a nonpreferred descriptor is
  free;
- no pre-existing third-party EDID override exists;
- the calculated DTD is fully encodable, keeps sync inside blanking, does not
  increase the native pixel clock, and encodes a refresh within 0.01 Hz of
  48 Hz.

The candidate preserves the native active dimensions, horizontal timing,
physical dimensions, borders, flags, vertical front porch, and vertical sync
width. Native 60 Hz remains preferred. One free dummy descriptor supplies the
capacity for 48 Hz; existing monitor-descriptor payloads may move to later slots
so every DTD remains before them, but their bytes and relative order are
preserved. Failure of any check leaves the machine read-only.

The best-effort model, GPU, panel, and timing evidence is maintained separately
in the [Intel dGPU MacBook research catalog](docs/INTEL_DGPU_MACBOOK_CATALOG.md).
A catalog entry is evidence, not an eligibility promise, and a marketing model
does not prove which panel or EDID revision is installed.

CPU presets are currently enabled only for SMBIOS `Apple Inc.` /
`MacBookPro16,1`. Unsupported Windows power settings are skipped and reported.

On hardware that matches neither the reviewed profile nor every experimental
gate, MacBook Eco shows only discovery information and read-only telemetry.

No generated candidate has been accepted on real hardware as part of this
change. Windows, hostile-state VM, reboot, visual rollback, cold-boot, and
hardware acceptance remain deferred and are not reported as passed.

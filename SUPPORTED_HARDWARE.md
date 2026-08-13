# Supported hardware

Display changes are enabled only for hardware that matches a reviewed profile
exactly. Other systems remain diagnostic-only.

## MacBook Pro 16-inch, 2019

| Field | Verified value |
|---|---|
| SMBIOS model | `MacBookPro16,1` |
| Panel hardware ID | `APPA044` |
| Normalized EDID | `CDA0E18080DE8CAC744C66A5374A53CBBA1999115FA5FE2DBD949980649AF3F5` |
| Alternate normalized EDID | `FAF4A9C16A6B394896D75DAA3280D84A61744EA07ED2F7CC21E6CFBCF1B4D2DF` |
| Native mode | `3072x1920 @ 60 Hz` |
| GPU | AMD Radeon Pro 5300M (`VEN_1002&DEV_7340`) |
| Tested driver (primary EDID) | `30.0.13045.22003` |
| Added mode | 48 Hz |
| 48 Hz DTD | `DC 91 00 50 C0 80 24 72 08 20 98 08 59 D7 10 00 00 1A` |
| Pixel clock | `373.40 MHz` |

The model, panel, normalized original EDID, native timing, dimensions, and GPU
device ID must match. A different driver version produces a warning rather than
changing the reviewed timing.

CPU presets are currently enabled only for SMBIOS `Apple Inc.` /
`MacBookPro16,1`. Unsupported Windows power settings are skipped and reported.

On other hardware, MacBook Eco may show discovery information and read-only
telemetry, but it will not install or generate a display timing.

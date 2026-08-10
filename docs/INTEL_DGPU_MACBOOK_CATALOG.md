# Intel discrete-GPU MacBook research catalog

> **Best-effort, non-exhaustive research.** This catalog records public evidence
> available during development. It is not the support matrix, a hardware
> acceptance result, or permission to mutate a display. Missing models, panel
> revisions, and conflicting public inventories are possible. The current
> reviewed support matrix remains [SUPPORTED_HARDWARE.md](../SUPPORTED_HARDWARE.md).

The literal Intel `MacBook` and `MacBookAir` product lines did not use a true
discrete GPU. NVIDIA GeForce 9400M and 320M configurations used shared memory
and are treated as integrated graphics here. The relevant portable Macs are
therefore Intel `MacBookPro` models with a separate AMD/ATI or NVIDIA adapter.

Apple's [MacBook Pro identification page][apple-identify] is the primary source
for the marketing-model and SMBIOS mapping. Apple technical specifications are
preferred for GPU options and advertised native resolution. Public DMI, PCI,
System Information, `ioreg`, EDID, and hardware-probe reports supply facts that
Apple does not publish, especially PCI vendor/device pairs and panel IDs.

## Confidence legend

- **High**: the exact SMBIOS model and PCI vendor/device pair occur together in
  a raw or structured hardware probe, or a tested-unit panel report contains the
  model and panel identifier together.
- **Medium**: Apple confirms the exact model/GPU configuration, but the public
  PCI evidence is an uploaded driver inventory, a user report, or a closely
  related association rather than a strong raw probe.
- **Inferred**: the marketing GPU is documented, but the PCI device ID is
  transferred from the same GPU in a related model. Inferred pairs are catalog
  context only.
- **Unknown**: no defensible public source was found. Unknown does not mean zero,
  absent, or safe to estimate.

An advertised native resolution is not EDID evidence. A PCI pair does not prove
which adapter controls the internal panel, and an SMBIOS model does not prove a
panel product ID or timing.

## Model catalog

| SMBIOS model | Marketing generation | Discrete GPU and PCI pair | Advertised built-in resolution | PCI confidence | Apple/spec source |
|---|---|---|---|---|---|
| `MacBookPro2,2` | 15-inch, Late 2006 | ATI Mobility Radeon X1600, `1002:71C5` | 1440x900 | High | [Apple 2006 announcement][apple-2006-news], [older ID map][everymac-identifiers] |
| `MacBookPro2,1` | 17-inch, Late 2006 | ATI Mobility Radeon X1600, `1002:71C5` | 1680x1050 | Inferred | [Apple 2006 announcement][apple-2006-news], [older ID map][everymac-identifiers] |
| `MacBookPro3,1` | 15/17-inch, Mid 2007 and later CPU refresh | NVIDIA GeForce 8600M GT, `10DE:0407` | 1440x900; 1680x1050 or CTO 1920x1200 | High | [Apple 2007 announcement][apple-2007-news], [older ID map][everymac-identifiers] |
| `MacBookPro4,1` | 15/17-inch, Early 2008; continued 17-inch Late 2008 | NVIDIA GeForce 8600M GT, `10DE:0407` | 1440x900; 17-inch variants through 1920x1200 | High | [Apple model catalog][apple-identify], [older ID map][everymac-identifiers] |
| `MacBookPro5,1` | 15-inch, Late 2008 | NVIDIA GeForce 9600M GT, `10DE:0647` | 1440x900 | High | [Apple model catalog][apple-identify], [older ID map][everymac-identifiers] |
| `MacBookPro5,2` | 17-inch, Early/Mid 2009 | NVIDIA GeForce 9600M GT, `10DE:0647` | 1920x1200 | Medium | [Early 2009 specs][apple-17-early-2009], [Mid 2009 specs][apple-17-mid-2009] |
| `MacBookPro5,3` | 15-inch, Mid 2009 higher configurations | NVIDIA GeForce 9600M GT, `10DE:0647` | 1440x900 | High | [Apple specifications][apple-15-mid-2009] |
| `MacBookPro6,1` | 17-inch, Mid 2010 | NVIDIA GeForce GT 330M, `10DE:0A29` | 1920x1200 | Medium | [Apple specifications][apple-17-mid-2010] |
| `MacBookPro6,2` | 15-inch, Mid 2010 | NVIDIA GeForce GT 330M, `10DE:0A29` | 1440x900; optional 1680x1050 | High | [Apple specifications][apple-15-mid-2010] |
| `MacBookPro8,2` | 15-inch, Early/Late 2011 | Radeon HD 6490M, `1002:6760`; 6750M, `1002:6741`; 6770M, `1002:6740` | 1440x900; optional 1680x1050 | High for 6760/6741; medium for 6740 | [Early 2011 specs][apple-15-early-2011], [Late 2011 specs][apple-15-late-2011] |
| `MacBookPro8,3` | 17-inch, Early/Late 2011 | Radeon HD 6750M, `1002:6741`; 6770M, `1002:6740` | 1920x1200 | High | [Early 2011 specs][apple-17-early-2011], [Late 2011 specs][apple-17-late-2011] |
| `MacBookPro9,1` | 15-inch, Mid 2012, non-Retina | NVIDIA GeForce GT 650M, `10DE:0FD5` | 1440x900; optional 1680x1050 | High | [Apple specifications][apple-15-mid-2012] |
| `MacBookPro10,1` | Retina 15-inch, Mid 2012/Early 2013 | NVIDIA GeForce GT 650M, `10DE:0FD5` | 2880x1800 | High | [Mid 2012 specs][apple-retina-mid-2012], [Early 2013 specs][apple-retina-early-2013] |
| `MacBookPro11,3` | Retina 15-inch, Late 2013/Mid 2014 dGPU configurations | NVIDIA GeForce GT 750M, `10DE:0FE9` | 2880x1800 | High | [Late 2013 specs][apple-retina-late-2013], [Mid 2014 specs][apple-retina-mid-2014] |
| `MacBookPro11,5` | Retina 15-inch, Mid 2015 dGPU configuration | Radeon R9 M370X, `1002:6821` | 2880x1800 | High | [Apple specifications][apple-retina-mid-2015] |
| `MacBookPro13,3` | 15-inch, 2016 | Radeon Pro 450/455/460, `1002:67EF` | 2880x1800 | High | [Apple specifications][apple-15-2016] |
| `MacBookPro14,3` | 15-inch, 2017 | Radeon Pro 555/560, `1002:67EF` | 2880x1800 | High | [Apple specifications][apple-15-2017] |
| `MacBookPro15,1` | 15-inch, 2018/2019 | Radeon Pro 555X/560X, `1002:67EF` | 2880x1800 | High | [2018 specs][apple-15-2018], [2019 specs][apple-15-2019] |
| `MacBookPro15,3` | 15-inch, 2018/2019 Vega configurations | Radeon Pro Vega 16/20, `1002:69AF` | 2880x1800 | High | [2018 specs][apple-15-2018], [2019 specs][apple-15-2019] |
| `MacBookPro16,1` | 16-inch, 2019 | Radeon Pro 5300M/5500M, `1002:7340` | 3072x1920 | High | [Apple specifications][apple-16-2019] |
| `MacBookPro16,4` | 16-inch, 2019/2020 5600M configuration | Radeon Pro 5600M, `1002:7360` | 3072x1920 | Medium | [Apple model catalog][apple-identify], [16-inch specifications][apple-16-2019] |

Some SMBIOS identifiers cover several processor, GPU-memory, display-finish,
or build-to-order configurations. Older 15/17-inch resolutions in particular
must not be collapsed into one assumed panel.

### Historical, x64-ineligible predecessors

The early-2006 `MacBookPro1,1` and `MacBookPro1,2` are retained here only to
make the catalog boundary explicit. Apple shipped that generation with Intel
Core Duo processors, which do not support 64-bit operating systems. They cannot
run this Windows x64 product and are not application candidates.

| SMBIOS model | Marketing generation | Discrete GPU and PCI pair | Advertised built-in resolution | PCI confidence | Sources |
|---|---|---|---|---|---|
| `MacBookPro1,1` | 15-inch, Early 2006 | ATI Mobility Radeon X1600, `1002:71C5` | 1440x900 | Inferred | [Apple Core Duo lineup][apple-core-duo-2006], [older ID map][everymac-identifiers], [Core Duo architecture][core-duo-32-bit] |
| `MacBookPro1,2` | 17-inch, Early 2006 | ATI Mobility Radeon X1600, `1002:71C5` | 1680x1050 | Inferred | [Apple Core Duo lineup][apple-core-duo-2006], [older ID map][everymac-identifiers], [Core Duo architecture][core-duo-32-bit] |

The following PCI IDs are shared by multiple Apple marketing names and cannot
identify the SKU by themselves:

- `1002:67EF`: Radeon Pro 450, 455, 460, 555, 560, 555X, or 560X;
- `1002:69AF`: Radeon Pro Vega 16 or Vega 20;
- `1002:7340`: Radeon Pro 5300M or 5500M.

Friendly names, subsystem values, and revisions can help diagnostics but cannot
replace the exact model, panel, EDID, and live-topology gates.

## Exact PCI evidence ledger

Apple's specifications establish the marketed GPU. These independent sources
put an SMBIOS model and PCI pair together in an observed inventory.

| SMBIOS and pair | Evidence | Assessment |
|---|---|---|
| `MacBookPro2,2`, `1002:71C5` | [Ubuntu DMI and lspci report][pci-2-2] | High |
| `MacBookPro3,1`, `10DE:0407` | [Red Hat hardware report][pci-3-1] | High |
| `MacBookPro4,1`, `10DE:0407` | [Red Hat hardware report][pci-4-1] | High |
| `MacBookPro5,1`, `10DE:0647` | [Ubuntu hardware report][pci-5-1] | High |
| `MacBookPro5,2`, `10DE:0647` | [Uploaded Windows driver inventory][pci-5-2] | Medium; retain outside runtime policy |
| `MacBookPro5,3`, `10DE:0647` | [Debian installation inventory][pci-5-3] | High |
| `MacBookPro6,1`, `10DE:0A29` | [Uploaded Windows hardware inventory][pci-6-1] | Medium; exact uploaded inventory, not a raw probe |
| `MacBookPro6,2`, `10DE:0A29` | [Published lspci inventory][pci-6-2] | High |
| `MacBookPro8,2`, `1002:6760` | [Ubuntu hardware inventory][pci-8-2-6760] | High |
| `MacBookPro8,2`, `1002:6741` | [Red Hat hardware report][pci-8-2-6741] | High |
| `MacBookPro8,2`, `1002:6740` | Apple documents the 6770M configuration; no equally strong same-model raw PCI probe was found | Medium; retain outside runtime policy |
| `MacBookPro8,3`, `1002:6741` | [Linux hardware report][pci-8-3-6741] | High |
| `MacBookPro8,3`, `1002:6740` | [BSD Hardware probe][pci-8-3-6740] | High |
| `MacBookPro9,1`, `10DE:0FD5` | [Ubuntu hardware inventory][pci-9-1] | High |
| `MacBookPro10,1`, `10DE:0FD5` | [Linux Hardware aggregate][pci-10-1] | High |
| `MacBookPro11,3`, `10DE:0FE9` | [Ubuntu hardware report][pci-11-3] | High |
| `MacBookPro11,5`, `1002:6821` | [Published macOS System Information][pci-11-5] | High |
| `MacBookPro13,3`, `1002:67EF` | [ArchWiki hardware inventory][pci-13-3] | High |
| `MacBookPro14,3`, `1002:67EF` | [Ubuntu hardware inventory][pci-14-3] | High |
| `MacBookPro15,1`, `1002:67EF` | [Linux hardware report][pci-15-1] | High |
| `MacBookPro15,3`, `1002:69AF` | [Linux hardware report][pci-15-3] | High |
| `MacBookPro16,1`, `1002:7340` | [Linux Hardware probe][pci-16-1] | High |
| `MacBookPro16,4`, `1002:7360` | [Uploaded Windows inventory][pci-16-4], corroborated by a [Linux installation report][pci-16-4-linux] | Medium; exact uploaded inventory corroborated by a user report |

The `1002:71C5` association for `MacBookPro1,1`, `MacBookPro1,2`, and
`MacBookPro2,1` is inferred from the documented X1600 configuration and the
exact `MacBookPro2,2` probe. It is not an exact same-model observation here.

## Known panel, EDID, and native-timing observations

These are observations of individual machines. They do not establish that all
machines with the same SMBIOS identifier use that panel.

| Observed model/configuration | Reported internal panel | Evidence | Timing status |
|---|---|---|---|
| `MacBookPro11,3`, GT 750M | `APPA022`, 2880x1800 | [Notebookcheck tested unit][panel-11-3] | Exact native DTD not found |
| `MacBookPro11,5`, R9 M370X | `APPA02E`, 2880x1800 | [Notebookcheck tested unit][panel-11-5] | Exact native DTD not found |
| `MacBookPro13,3` | `APPA030`, 2880x1800 | [Published on-device EDID and SwitchResX report][panel-13-3-timing]; [tested 450/460 unit][panel-13-3-a030] | Linked source reports the complete base EDID; native DTD reproduced below |
| `MacBookPro13,3` | `APPA031`, 2880x1800 | [Notebookcheck 455 unit][panel-13-3-a031] | Exact native DTD not found |
| `MacBookPro14,3`, Radeon Pro 555 | `APPA031`, 2880x1800 | [Notebookcheck tested unit][panel-14-3] | Exact native DTD not found |
| `MacBookPro15,1`, Radeon Pro 560X | `APPA040`, 2880x1800 | [Notebookcheck tested unit][panel-15-1]; [uploaded Windows inventory][panel-15-1-inventory] | Exact native DTD not found |
| `MacBookPro15,3`, Radeon Pro Vega 20 | `APPA040`, 2880x1800 | [Notebookcheck tested unit][panel-15-3]; [uploaded Windows inventory][panel-15-3-inventory] | Exact native DTD not found |
| `MacBookPro16,1` | `APPA044`, 3072x1920, 345x215 mm | [Linux Hardware probe][pci-16-1]; [Notebookcheck tested unit][panel-16-1] | Reviewed repository profile contains the native DTD below |
| `MacBookPro16,4`, Radeon Pro 5600M | `APPA044`, 3072x1920 | [Uploaded Windows inventory][pci-16-4] | Exact native DTD not independently recovered for this unit |

### `APPA030` observed native DTD

The published on-device report identifies the author's `MacBookPro13,3` with a
Radeon Pro 460 and includes its complete 128-byte `APPA030` base EDID. Its
preferred native DTD is:

```text
7C 80 40 50 B0 08 34 70 08 20 68 08 4B CF 10 00 00 1A
```

The accompanying SwitchResX observation decodes it as:

```text
Pixel clock: 328.92 MHz
Horizontal: 2880 + 8 + 32 + 40 = 2960
Vertical:   1800 + 38 + 8 + 6 = 1852
Sync:       +H / -V
Refresh:    approximately 60.001 Hz
Image size: 331 x 207 mm
```

These bytes come from one observed `MacBookPro13,3` / `APPA030` unit. They do
not establish a catalog-wide profile for every machine or panel revision.

### `APPA044` reviewed native timing

The existing reviewed `MacBookPro16,1` / `APPA044` profile records this original
native DTD:

```text
E7 91 00 50 C0 80 37 70 08 20 98 08 59 D7 10 00 00 1A
```

It decodes to:

```text
Pixel clock: 373.51 MHz
Horizontal: 3072 + 8 + 32 + 40 = 3152
Vertical:   1920 + 41 + 8 + 6 = 1975
Sync:       +H / -V
Refresh:    60 Hz
Image size: 345 x 215 mm
```

The reviewed added 48 Hz DTD remains documented in
[SUPPORTED_HARDWARE.md](../SUPPORTED_HARDWARE.md). It is a static reviewed
profile and is the only display profile enabled for this panel identity.

Apple display override resource names need careful interpretation. Raw
`DisplayVendorID` `0x0610` represents manufacturer `APP`, and a resource such as
`DisplayProductID-a030` describes product `0xA030`. The existence of that file
does not prove which Mac model or panel revision contains it. A public example
of those raw values and the resource path is the [APPA030 report][panel-a030-raw].

## Explicit unknowns and limits

- Exact panel manufacturer/product IDs and native DTDs remain unknown for every
  catalog row not listed in the observation table.
- Even listed models may contain other panel suppliers or revisions.
  `MacBookPro13,3` alone has public observations of both `APPA030` and
  `APPA031`.
- No panel or DTD is inferred from year, chassis, marketing resolution, GPU,
  PCI ID, a macOS override resource, or another machine's dump.
- A discrete GPU's presence does not prove that it currently controls the
  internal panel on a switchable-graphics system. Live Windows topology remains
  authoritative.
- The public sources do not establish a uniform Boot Camp driver version,
  descriptor layout, free DTD slot, or pristine override state.
- Apple's advertised 48 Hz modes on the 16-inch model establish intended panel
  capability under Apple's supported software. They do not prove that Windows
  exposes 48 Hz, that a particular EDID has room for another DTD, or that a
  calculated override is safe.
- No calculated proposal was validated on Windows, in the hostile-state VM
  matrix, or on real hardware as part of this research.

## Exclusions

This catalog intentionally excludes:

- all Intel `MacBook` and `MacBookAir` models, including NVIDIA 9400M/320M
  shared-memory configurations;
- 32-bit-only `MacBookPro1,1` and `MacBookPro1,2`, which cannot run the Windows
  x64 product;
- integrated-only MacBook Pro identifiers `MacBookPro5,4`, `5,5`, `7,1`,
  `8,1`, `9,2`, `10,2`, `11,1`, `11,2`, `11,4`, `12,1`, `13,1`, `13,2`,
  `14,1`, `14,2`, `15,2`, `16,2`, and `16,3`;
- Apple silicon models;
- external displays, eGPUs, replacement panels without exact live identity,
  and machines whose SMBIOS identity or display topology is ambiguous.

## Runtime policy

This research catalog is intentionally broader than the executable profile
catalog. Public model, GPU, resolution, or panel-family evidence is not enough
to enable a display mutation. MacBook Eco has no runtime timing generator and
does not load profiles from disk.

The only runtime profiles are reviewed manifests compiled into a release. Each
must bind the exact SMBIOS model, panel product ID, normalized original EDID,
native DTD, controlling GPU device ID, and reviewed 48 Hz DTD. The installed app
then applies the existing fail-closed topology, ownership, intent-before-write,
read-back, rollback, and recovery rules.

The offline utility described in
[`profiles/README.md`](../profiles/README.md) can reduce a complete EDID to a
review proposal and calculate a candidate DTD. Its output is neither installed
nor treated as supported hardware. A profile becomes eligible only after its
evidence and hardware acceptance are reviewed and the manifest is compiled into
a later build.

At present, only the existing `MacBookPro16,1` / `APPA044` profile meets that
bar. The `MacBookPro16,4` and all other catalog rows remain read-only until an
exact panel/EDID profile completes the same review.

## Privacy and source hygiene

Public hardware reports can contain serial numbers, full device-instance
suffixes, UUIDs, raw EDIDs, storage identifiers, or other machine-specific
data. Do not copy those values into this repository. Future evidence should be
reduced to the minimum non-unique facts needed for review: SMBIOS model, PCI
vendor/device pair, non-unique panel product ID, timing tuple, and a public
source URL. Raw acceptance output and private user submissions must stay out of
version control.

[apple-identify]: https://support.apple.com/en-us/108052
[apple-core-duo-2006]: https://www.apple.com/newsroom/2006/05/16Apple-Unveils-New-MacBook-Featuring-Intel-Core-Duo-Processors/
[apple-2006-news]: https://www.apple.com/newsroom/2006/10/24Apple-MacBook-Pro-Notebooks-Now-with-Intel-Core-2-Duo-Processors/
[apple-2007-news]: https://www.apple.com/newsroom/2007/06/05Apple-Updates-MacBook-Pro/
[apple-17-early-2009]: https://support.apple.com/en-us/112526
[apple-17-mid-2009]: https://support.apple.com/en-us/112473
[apple-15-mid-2009]: https://support.apple.com/en-us/112624
[apple-17-mid-2010]: https://support.apple.com/en-us/112606
[apple-15-mid-2010]: https://support.apple.com/en-us/112605
[apple-15-early-2011]: https://support.apple.com/en-us/112599
[apple-15-late-2011]: https://support.apple.com/en-us/112586
[apple-17-early-2011]: https://support.apple.com/en-us/112598
[apple-17-late-2011]: https://support.apple.com/en-us/112418
[apple-15-mid-2012]: https://support.apple.com/en-us/112568
[apple-retina-mid-2012]: https://support.apple.com/en-us/112576
[apple-retina-early-2013]: https://support.apple.com/en-us/118465
[apple-retina-late-2013]: https://support.apple.com/en-us/111971
[apple-retina-mid-2014]: https://support.apple.com/en-us/111935
[apple-retina-mid-2015]: https://support.apple.com/en-us/111955
[apple-15-2016]: https://support.apple.com/en-us/111975
[apple-15-2017]: https://support.apple.com/en-us/111947
[apple-15-2018]: https://support.apple.com/en-us/111949
[apple-15-2019]: https://support.apple.com/en-us/111941
[apple-16-2019]: https://support.apple.com/en-ie/111932
[everymac-identifiers]: https://everymac.com/systems/by-identifier/all-macbook-pro-model-identifiers.html
[core-duo-32-bit]: https://www.notebookcheck.net/Intel-Core-Duo-T2600-Notebook-Processor.35155.0.html
[pci-2-2]: https://answers.launchpad.net/ubuntu/%2Bsource/alsa-driver/%2Bquestion/691576
[pci-3-1]: https://bugzilla.redhat.com/show_bug.cgi?id=751147
[pci-4-1]: https://bugzilla.redhat.com/show_bug.cgi?id=1030695
[pci-5-1]: https://bugs.launchpad.net/bugs/490704
[pci-5-2]: https://www.driveridentifier.com/scan/apple-macbookpro52-driver/desktop/DE88D5DAED264DA49DD0F36C0109BEB2
[pci-5-3]: https://wiki.debian.org/InstallingDebianOn/Apple/MacBookPro/5-3
[pci-6-1]: https://www.driveridentifier.com/scan/nvidia-geforce-gt-330m/driver-detail/1510F9374A27428D9A28FA190A23C71D/1719518/232d26f581a3a24c0b7fe7356bfd275c/337585351/PCI%5CVEN_10DE%26DEV_0A29%26SUBSYS_00C8106B
[pci-6-2]: https://gist.github.com/1601562
[pci-8-2-6760]: https://www.mail-archive.com/desktop-packages%40lists.launchpad.net/msg572965.html
[pci-8-2-6741]: https://bugzilla.redhat.com/show_bug.cgi?id=1336959
[pci-8-3-6741]: https://forum.garudalinux.org/t/macbookpro8-3-issues-with-radeon-hd-6750m-and-stuck-on-plymouth-boot-screen/7408
[pci-8-3-6740]: https://bsd-hardware.info/?probe=593b4b2237
[pci-9-1]: https://www.mail-archive.com/desktop-packages%40lists.launchpad.net/msg569584.html
[pci-10-1]: https://linux-hardware.org/?id=pci%3A10de-0fd5-106b-00f2
[pci-11-3]: https://bugs.launchpad.net/bugs/1280658
[pci-11-5]: https://forum.blackmagicdesign.com/viewtopic.php?f=21&t=73046
[pci-13-3]: https://wiki.archlinux.org/title/MacBookPro13%2C3
[pci-14-3]: https://www.mail-archive.com/desktop-packages%40lists.launchpad.net/msg553772.html
[pci-15-1]: https://forum.garudalinux.org/t/garuda-gaming-edition-2018-macbook-pro-touchbar/19179
[pci-15-3]: https://discuss.cachyos.org/t/macbook-15-1-with-discrete-amd-gpu/23794
[pci-16-1]: https://linux-hardware.org/?probe=124425a1ed
[pci-16-4]: https://www.driveridentifier.com/scan/apple-macbookpro164-driver/desktop/28B9B2C966C641DDA96CF94109C7035A
[pci-16-4-linux]: https://www.reddit.com/r/linux_on_mac/comments/1v05dvn/2026_guide_installing_ubuntu_2604_from_scratch_on/
[panel-11-3]: https://www.notebookcheck.net/Apple-MacBook-Pro-Retina-15-Late-2013-Notebook-Review.120330.0.html
[panel-11-5]: https://www.notebookcheck.net/Apple-MacBook-Pro-Retina-15-Mid-2015-Review.144402.0.html
[panel-13-3-timing]: https://pikeralpha.wordpress.com/2017/01/19/apple-igpu-saved-config-data/
[panel-13-3-a030]: https://www.notebookcheck.net/Apple-MacBook-Pro-15-Late-2016-2-9-GHz-460-Notebook-Review.195702.0.html
[panel-13-3-a031]: https://www.notebookcheck.net/Apple-MacBook-Pro-15-Late-2016-2-7-GHz-455-Notebook-Review.197826.0.html
[panel-14-3]: https://www.notebookcheck.net/Apple-MacBook-Pro-15-2017-2-8-GHz-555-Laptop-Review.230096.0.html
[panel-15-1]: https://www.notebookcheck.net/Apple-MacBook-Pro-15-2018-2-6-GHz-560X-Laptop-Review.317358.0.html
[panel-15-1-inventory]: https://www.driveridentifier.com/scan/apple-macbookpro151-driver/desktop/6B05DDD9807A4E7EA4337FC4A2442D2E
[panel-15-3]: https://www.notebookcheck.net/Apple-MacBook-Pro-15-2018-2-9-GHz-i9-Vega-20-Laptop-Review.423029.0.html
[panel-15-3-inventory]: https://www.driveridentifier.com/scan/apple-macbookpro153-driver/desktop/776FF2EF8C9F4B5FB5F325FD4094BB75
[panel-16-1]: https://www.notebookcheck.net/Apple-MacBook-Pro-16-2019-Laptop-Review-A-convincing-Core-i9-9880H-and-Radeon-Pro-5500M-powered-multimedia-laptop.445902.0.html
[panel-a030-raw]: https://ji0vwl.net/index.php/2018/07/31/1337/

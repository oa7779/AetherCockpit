# Third-party notices

The MIT license in [LICENSE](./LICENSE) applies to AETHER-authored code and
documentation only. It does not replace the terms of third-party components
redistributed with AETHER.

## Components bundled in `tools`

- **smartmontools 7.5** (`smartctl.exe`, `drivedb.h`, and
  `smartmontools-7.5-source.tar.gz`): GNU General Public License, version 2.
  The license text is in [`tools/COPYING.txt`](./tools/COPYING.txt); source and
  provenance details are included alongside the binaries.
- **PresentMon 2.6.0** (`PresentMon.exe`): MIT License. The copyright and
  permission notice are in [`tools/PresentMon-LICENSE.txt`](./tools/PresentMon-LICENSE.txt).

## NuGet dependencies

- **LibreHardwareMonitorLib 0.9.6**: Mozilla Public License 2.0 (MPL-2.0).
  See the package metadata and license at
  <https://licenses.nuget.org/MPL-2.0>; source:
  <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor>.
- Transitive NuGet dependencies retain the licenses declared in their
  respective package metadata. The self-contained .NET runtime distribution
  includes its own third-party notices.

The listed versions describe the current project and bundled tools; consult
package metadata and the adjacent provenance file when updating components.

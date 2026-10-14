# Third-Party Notices

## Vanta NET 0.5.24 and Three.js r134 (MIT)

The animated network background bundles these libraries locally in
`src/VictusControl.App/Assets/Network/`:

- Vanta by Teng Bao: https://github.com/tengbao/vanta (version 0.5.24).
  Full license: `Assets/Network/VANTA-LICENSE.txt`.
- Three.js by the Three.js authors: https://github.com/mrdoob/three.js (r134).
  Full license: `Assets/Network/THREE-LICENSE.txt`.

Microsoft.Web.WebView2 hosts the background in a WPF composition control.
Package and license information: https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3537.50.

## OmenMon (GPL-3.0)

HP does not publish the WMI/BIOS command interface used to control fan speed,
GPU mode, and CPU/GPU power limits on Omen/Victus laptops. The command IDs,
byte layouts, and enum values in `src/VictusControl.Core/Bios/` were
reverse-engineered by the OmenMon project:

- Project: https://omenmon.github.io/
- Repository: https://github.com/OmenMon/OmenMon
- Copyright (c) 2023 Piotr Szczepański
- License: GNU General Public License v3.0

The files `HpBiosData.cs`, `HpBios.cs`, and `HpBiosControl.cs` in this
repository are a trimmed, re-namespaced adaptation of OmenMon's
`Hardware/BiosData.cs`, `Hardware/Bios.cs`, and `Hardware/BiosCtl.cs`,
carrying forward the reverse-engineered protocol values under the terms of
the GPL-3.0. Because this is a derivative work, VictusControl.Core (the
project containing these files) must also be distributed under GPL-3.0 if
you redistribute it. See https://www.gnu.org/licenses/gpl-3.0.en.html for
the full license text.

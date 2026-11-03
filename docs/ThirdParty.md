# Third-party sources and notices

OneWare.CyanCalc is an independent integration project. The names ONE WARE, CyanCalc and IronCalc
identify the software it integrates with; this repository does not represent endorsement by those projects.

| Component | Source and role | License / notices |
| --- | --- | --- |
| OneWare Studio | [Pinned source](https://github.com/one-ware/OneWare/tree/4c1b510fa4c2410cc462a2bff549b1d2b5590f5a); plugin APIs and host integration conventions | Apache-2.0; [upstream license](https://github.com/one-ware/OneWare/blob/4c1b510fa4c2410cc462a2bff549b1d2b5590f5a/LICENSE) |
| CyanCalc | [Pinned source](https://github.com/spnvt63995/CyanCalc/tree/76a3530c60294fc6446218c033eb32a7e1330137); .NET binding and linked Avalonia workbook UI | Preserve its [third-party notices](https://github.com/spnvt63995/CyanCalc/blob/76a3530c60294fc6446218c033eb32a7e1330137/docs/cyancalc/THIRD_PARTY.md) and included licenses |
| IronCalc | Engine and workbook formats through CyanCalc; v0.9.0 baseline | MIT OR Apache-2.0; both notice texts accompany the native binding |
| Lucide / Feather | Functional workbook icons, including the Explorer Table2 icon | ISC / MIT; full notices in `licenses/Lucide.ISC-and-Feather.MIT.txt` |
| Inter | Fonts embedded by the linked workbook UI | SIL OFL 1.1, included in the runtime license folder |
| react-colorful | Colour-picker behaviour ported by CyanCalc | MIT, included in the runtime license folder |
| Avalonia and other managed dependencies | Provided by OneWare or referenced by the source projects | See the pinned project package references and each upstream package's license |

The runtime staging script preserves the complete `licenses/` directory emitted by the CyanCalc binding,
and adds this plugin's LICENSE and NOTICE. Preserve those files when redistributing a staged package.
The screenshot workbooks and sample generator use fictional data created for this repository.
The AI Chat shown in the screenshots belongs to OneWare Studio and is not supplied by this plugin.

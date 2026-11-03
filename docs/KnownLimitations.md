# Known limitations

This is an experimental source preview. Work on copies of your files.

- Native tab splitting, floating, redocking and contextual toolbar transitions remain under investigation.
  Layout can need a window resize and toolbar transitions can flicker. Do not assume every docking sequence is reliable.
- The toolbar adapter currently expects OneWare Studio 1.0.45's named shell controls. Other shell layouts
  may use the document's inline toolbar instead. Floating windows use the inline toolbar.
- The broad headless toolbar/rehosting harness is a diagnostic tool and currently fails its assertion
  that floating a workbook restores one inline toolbar and releases the shell toolbar.
  Focused project, feedback and input-state groups are documented separately; their success is not full GUI coverage.
- macOS Apple Silicon is the current GUI verification platform. Linux font availability, CJK fallback,
  IME differences and Windows behaviour require further validation. No new font-substitution service is provided.
- Workbook import/export and formula support follow the pinned CyanCalc/IronCalc revisions.
  This is not a claim of full Excel compatibility or lossless round-tripping for arbitrary workbooks.
- OneWare's AI Chat is separate. Showing it beside a workbook does not add workbook-specific AI commands.
- There is no marketplace installation manifest, signed installer or supported binary release yet.
  The source build and local module loading flow are the supported evaluation path for this preview.

When reporting a problem, include the host version, plugin commit, platform and a small fictional workbook
or reproduction sequence. Keep private workbooks, account details and personal paths out of public reports.

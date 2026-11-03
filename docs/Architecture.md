# Integration architecture

## Structure

- `src/OneWare.CyanCalc`: project registration, document lifetime, save/undo,
  shell menus, exports and the contextual OneWare toolbar.
- `src/Workbook`: a plugin-only build of CyanCalc's reusable Avalonia sources,
  with an additional partial class for toolbar hosting. Shared sources are linked
  read-only from `CyanCalcRoot`; they are not edited or copied into this project.
- `tests/OneWare.CyanCalc.Checks`: project, layout, document switching, save and export
  checks using the real workbook UI and engine with a controlled host.

The plugin UI keeps the `CyanCalc.Avalonia` assembly identity because the shared
resources use that identity. Load this plugin variant **instead of** the standalone
UI assembly in a OneWare process. This does not change the standalone build.

## Toolbar sizing

The workbook row content is 40 logical pixels high (48 including the shell's
existing top and bottom margins), with 28-pixel buttons and 16-pixel icons.
OneWare's existing row has a 28-pixel outer panel and a 24-pixel toolbar
style, so changing icon size alone cannot accommodate the workbook controls.

While active, the plugin raises its owning shell row to 40 and reserves the right
shell actions before measuring the workbook contribution. Overflow uses the
workbook's existing horizontal scroll arrows. Leaving the workbook project
restores the row height and child order, and returns the toolbar to its document.

This prototype depends on the host's named `IconTray` DockPanel and its left
StackPanel slot. A different shell layout needs an adapter; it falls back to the
inline workbook toolbar when that slot is unavailable. Floating document windows
keep the inline toolbar and its Undo/Redo buttons. The toolbar stays hidden until
its initial host is known, so opening a project does not briefly show it inside
the document before moving it to the shell.

When a document floats or redocks, the plugin releases its previous presenter and
toolbar host before attaching the same workbook to the destination window. This
preserves the workbook session, pending cell input, unsaved state and history.
Presenter cleanup waits for visual detachment to finish so inherited theme
updates cannot rebuild controls while Dock is still removing their visual tree.

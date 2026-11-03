# Workbook projects and commands

## Workbook projects

A `.wbproj` describes a folder of workbooks. Like IronCalc's workbook list, each
entry is an independent workbook; each workbook can contain multiple worksheets.
OneWare's Explorer lists the files, document tabs open several workbooks at once,
and the existing sheet tabs navigate worksheets inside each workbook.

Use **File → New → Workbook project…** to choose a folder and name for an empty
project. If that project file already exists, choose whether to open it instead.
Then use **Workbook → New workbook…**, or **Add → Workbook…** on a
project folder in Explorer. New files contain a valid `.ic` workbook.
As with OneWare's New File command, an existing name gets a numeric suffix
(`Workbook1.ic`, `Workbook2.ic`, etc.); existing files are never overwritten.
**Workbook → Import workbooks…** uses OneWare's file import to copy existing
`.ic` and `.xlsx` documents into the active project. Explorer provides the normal
open, rename, copy and delete operations. **Workbook → Open all workbooks** opens
the included workbook files in document tabs.

For a hand-written project, all fields are optional. This example also opens two
workbooks when the project is activated:

```json
{
  "startupWorkbooks": ["Budget.ic", "Reports/Forecast.xlsx"],
  "include": ["*.ic", "*.xlsx"],
  "exclude": []
}
```

Open it through **File → Open → Workbook project…**, or pass its path to OneWare.
The default include patterns cover root and nested workbook files using OneWare's
simple-expression matching. Workbook paths in `startupWorkbooks` must be relative
and stay inside the project. Missing startup files are reported in OneWare's Output while
the remaining files and project stay available. The earlier `"workbook": "Budget.ic"`
form remains supported as a single startup entry when `startupWorkbooks` is absent.

An already selected workbook in that project keeps its current tab and unsaved
edits. The toolbar follows the active workbook document within the project.
`.wbproj` contains project metadata; workbook content and its worksheets remain
in each `.ic` or `.xlsx` file. Each document has its own save state and Undo/Redo
history. OneWare owns restoration of open document tabs.

Workbook files, document tabs and the project root use IronCalc's existing Lucide
`Table2` icon in the host's accent color. The original vector shapes and stroke
are preserved; the packaged Lucide/Feather license notices apply.

Project and file operation failures use OneWare's logging and Output service.
Rejected creation/import input also uses the host's owned error dialog, as in its
FPGA creator. The existing host import and save-picker confirmation flows remain
in use. Workbook feedback uses one document-owned callout with information,
warning and error categories; diagnostics go to Output when needed. See the
[feedback policy](Feedback.md) for locations, lifetimes and collision behavior.

Choose an already-open project in OneWare's project selector to activate it.
While its workbook toolbar is hosted in the shell, use OneWare's existing Undo
and Redo buttons; the duplicate workbook buttons and their divider are hidden.
The shell delegates to the active document's workbook commands and reflects its
available history and read-only state. Each workbook retains its own engine
history. Returning the toolbar to its document restores the inline buttons.

## Shell menus

With a workbook document active, the top-level **Workbook** menu opens the existing name
manager, named styles, conditional formatting, workbook themes and regional
settings panels inside that document. OneWare also makes these menu commands
available through **View → Find Actions**. Switching to another document updates
their target and availability. Read-only workbooks retain the panels' existing
inspection mode.

**File → Export** provides **Workbook as XLSX…** and **Selection as PNG…** using
the owning window's save dialog. The existing PNG toolbar button uses the same
export action. Export commits pending cell input and captures the current
workbook or selection before opening the dialog. It writes a copy without
renaming the open document or marking it saved; cancelling writes no file.
PNG export retains CyanCalc's existing canvas and selection behavior.

The panel and export commands are available for opened `.ic` and `.xlsx` documents
even without an active workbook project. Creation, import, open-all and the
contextual toolbar use the selected workbook project.

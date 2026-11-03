# Workbook feedback in OneWare

Feedback belongs to the operation and document that produced it. The plugin uses
OneWare's existing Output, message dialogs and callout styles.
Project creation uses a folder picker followed by an owned name input; it avoids
a native Save panel's overwrite confirmation for a file that will never be overwritten.

| Situation | Severity and location |
| --- | --- |
| Invalid reference, ambiguous name, hidden-sheet destination, or an untyped engine command rejection | Warning in the workbook's bottom callout. No global toast or automatic Output panel. |
| Read-only link editing, unfinished cell edit, or a HYPERLINK editing limitation | Information in that workbook's callout. |
| Clipboard copy failure or a missing window required for a workbook action | Error in that workbook's callout, with a document-named diagnostic in Output. |
| Save or export failure, including conversion failure | Error summary in that workbook's callout; operation, document and original diagnostic in Output. Preserve unsaved state. |
| Workbook load failure | The failed document's existing load-error view plus a named Output diagnostic. |
| Missing or failed startup workbooks | Continue opening the remaining files. One Output summary identifies the project and each failed path. No dialog for each file. |
| Invalid project/workbook creation or import input | OneWare's owned Error dialog and Output. Background startup failures only use Output. |
| Existing new workbook filename | Create the next numbered name, as OneWare's New File does. |
| Existing project manifest | Ask whether to open the existing project. Yes reuses an open project or loads it; No cancels creation. Never overwrite it. |
| Picker cancellation or operation cancellation | No error feedback. |
| Formula error values and validation already shown in a panel/dialog | Keep their existing presentation; do not also raise a global notification. |

Each workbook has one replaceable callout. Information and warnings expire after
eight seconds, except while keyboard focus is inside the callout; a successful
document edit also clears them. Errors remain until dismissed, replaced, a
successful save/export, or the document is closed/reloaded. Repeated identical
failures while the notice remains visible do not add duplicate Output entries.
Displaying feedback does not focus a control. Dismissal from its button returns
focus to the workbook. The callout travels with its workbook during tab and
window transfers, including while another workbook has keyboard focus.

Known localized messages use their resource identities, not comparisons against
translated sentences. The shared UI also emits untyped engine messages; those
are treated as command rejections (warnings), without guessing a more specific
cause from the message text. File conversion exceptions similarly retain the
engine's actual diagnosis rather than inventing a format-loss explanation.

The plugin UI adds the host callout slot and exposes message resource identities
in its own assembly. The standalone CyanCalc source and OneWare host are unchanged.

## Input states

Workbook dialogs and panels retain IronCalc's validation and confirmation flow.
Disabled workbook action buttons use neutral host colors instead of the active
accent fill. Invalid link fields, defined-name fields and conditional formulas
show an error border while their existing validation message is present, including
when the input has focus. Correcting the value clears the border through the same
validation state that enables Save or Apply.

These styles are limited to workbook controls. The plugin observes existing error
labels for name and conditional-formula fields; it does not introduce new parsing,
change validation timing or send panel validation to Output or a global notice.
Subscriptions reconnect when a field moves between windows and are released when
the field leaves the visual tree.

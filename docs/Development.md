# Development

## Public source dependencies

`dependencies.json` pins CyanCalc and OneWare to public commit IDs. Run
`python3 tools/fetch-dependencies.py` before opening the solution for the first time.
Run `python3 tools/fetch-dependencies.py --verify` to check those revisions without downloading.
The plugin links CyanCalc's Avalonia sources into a plugin-only UI assembly and references
OneWare's Essentials and ProjectSystem projects. It does not build the OneWare desktop shell.

The solution includes the pinned dependency projects explicitly. For deliberate local development
against other checkouts, build the plugin or checks project directly with
`-p:CyanCalcRoot=/path/to/CyanCalc` and `-p:OneWareRoot=/path/to/OneWare`,
or place those properties in ignored `.local/Dependencies.props`.
Results with overrides are not results for the pinned public baseline.

## Build and checks

```sh
dotnet build OneWare.CyanCalc.slnx -c Release
dotnet run --project tests/OneWare.CyanCalc.Checks -c Release --no-build -- --file-actions
dotnet run --project tests/OneWare.CyanCalc.Checks -c Release --no-build -- --feedback
dotnet run --project tests/OneWare.CyanCalc.Checks -c Release --no-build -- --input-states
```

These groups cover project and file operations, document feedback, and validation visuals.
Checks generate fictional workbooks in the temporary directory printed by the runner.
An optional positional directory lets you keep the output somewhere explicit.
Running without a group selector exercises the wider toolbar and rehosting harness;
see the known limitations before treating that run as a release gate.

## Sample workbooks

```sh
dotnet run --project tools/SampleWorkbook -c Release --no-build -- examples/QuarterlyPlan
```

The generator writes new sample files and refuses to overwrite existing ones.
To regenerate, use a new empty directory and review the generated results before replacing the examples.

## Packaging and running

`python3 tools/package.py --output artifacts/OneWare.CyanCalc` stages the Release build by default.
Use `--configuration Debug` for a Debug build. Build for the operating system on which the plugin will run;
the native IronCalc bridge is platform-specific. The script ships only plugin-owned assemblies,
the native engine, compatibility metadata and license notices. OneWare supplies host dependencies.

For Rider, open `OneWare.CyanCalc.slnx`, build, then create a .NET Executable configuration
pointing at your installed OneWare Studio executable. Pass `--modules` with the staged folder and
use separate evaluation data directories as shown in the README. Run configurations and profiles stay local.

## Updating dependencies

Update the public commit in `dependencies.json` intentionally. Keep existing modified checkouts;
move them aside before bootstrapping the replacement. Review source and host API changes, build,
run the focused checks and perform native GUI verification. Update `compatibility.txt` only after
checking the actual host assembly versions. Do not bundle the host's Avalonia or OneWare assemblies.

## Repository layout

```text
src/OneWare.CyanCalc/        OneWare project, document, menu and toolbar integration
src/Workbook/               Plugin-only additions to the linked CyanCalc UI
tests/                      Synthetic regression checks
tools/                      Dependency bootstrap, package staging and sample generator
examples/QuarterlyPlan/     Fictional workbook project used in the preview
docs/                       Usage, architecture, limitations and screenshots
.deps/                      Ignored, pinned public source checkouts
```

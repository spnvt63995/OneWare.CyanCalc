#!/usr/bin/env python3
"""Stage the CyanGraph module for OneWare Studio from an existing build."""
import argparse
from pathlib import Path
import shutil

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--configuration", default="Release")
parser.add_argument("--cyan-graph-root", type=Path, required=True, help="Shared source tree used for the build")
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
build = root / "src/OneWare.CyanGraph/bin" / args.configuration / "net10.0"
files = [build / name for name in ("OneWare.CyanGraph.dll", "CyanGraph.Avalonia.dll",
         "CyanGraph.Neo4j.dll", "CyanCalc.Grid.dll", "Neo4j.Driver.dll", "compatibility.txt")]
licenses = [args.cyan_graph_root / name for name in ("LICENSE-Apache-2.0", "LICENSE-MIT")]
grid_notice = args.cyan_graph_root / "ui/grid/NOTICE"
for path in files + licenses + [grid_notice]:
    if not path.is_file():
        parser.error(f"Missing build output: {path}")
args.output.mkdir(parents=True, exist_ok=False)
for path in files:
    shutil.copy2(path, args.output / path.name)
shutil.copy2(root / "LICENSE", args.output / "LICENSE")
shutil.copy2(grid_notice, args.output / "CyanCalc.Grid.NOTICE.txt")
for path in licenses:
    shutil.copy2(path, args.output / ("CyanCalc." + path.name + ".txt"))
(args.output / "NOTICE").write_text(
    "CyanGraph for OneWare. Independent and unofficial.\n"
    "Shared CyanCalc/IronCalc grid and UI sources retain their original license notices.\n"
    "Neo4j.Driver 6.5.1, Neo4j contributors. Apache License 2.0.\n"
    "https://github.com/neo4j/neo4j-dotnet-driver\n", encoding="utf-8")

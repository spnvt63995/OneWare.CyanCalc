#!/usr/bin/env python3
"""Stage only plugin-owned assemblies; OneWare supplies its own dependencies."""
import argparse
import pathlib
import shutil

parser = argparse.ArgumentParser()
parser.add_argument('--configuration', default='Release')
parser.add_argument('--output', type=pathlib.Path, required=True)
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
source = root / 'src/OneWare.CyanCalc/bin' / args.configuration / 'net10.0'
files = ['OneWare.CyanCalc.dll', 'CyanCalc.Avalonia.dll', 'CyanCalc.dll', 'compatibility.txt']
native = [source / name for name in ('libcyancalc_native.dylib', 'libcyancalc_native.so', 'cyancalc_native.dll') if (source / name).exists()]
if len(native) != 1:
    raise SystemExit('Build for exactly one native platform before packaging.')
for name in files:
    if not (source / name).is_file():
        raise SystemExit(f'Missing build output: {name}')
args.output.mkdir(parents=True, exist_ok=True)
for path in [*(source / name for name in files), *native]:
    shutil.copy2(path, args.output / path.name)
if (source / 'licenses').exists():
    shutil.copytree(source / 'licenses', args.output / 'licenses', dirs_exist_ok=True)
else:
    raise SystemExit('Missing third-party license notices in build output.')
for name in ('LICENSE', 'NOTICE'):
    shutil.copy2(root / name, args.output / name)
print(f'Plugin staged at {args.output}')

#!/usr/bin/env python3
"""Exporta as peças do paper doll dos arquivos .aseprite para o formato do jogo.

Uso:
    ASEPRITE=/caminho/para/aseprite python tools/export_aseprite.py --src art/src --out godot/assets

Entrada:
    <src>/<slot>/<piece_id>.aseprite      camadas de topo: shade, mask e (opcional) line
    <src>/<slot>/<piece_id>.meta.json     metadados escritos à mão

Saída:
    <out>/<slot>/<piece_id>/shade.png     sprite sheet horizontal, um frame ao lado do outro
    <out>/<slot>/<piece_id>/mask.png
    <out>/<slot>/<piece_id>/line.png      só se a camada existir
    <out>/<slot>/<piece_id>/meta.json

Camadas cujo nome começa com "_" são guias e nunca são exportadas
(o exportador pede cada camada pelo nome, então as guias ficam de fora sozinhas).
Depois de exportar, rode tools/validate_assets.py.
"""

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

REQUIRED_LAYERS = ("shade", "mask")
OPTIONAL_LAYERS = ("line",)


def aseprite_binary() -> str:
    binary = os.environ.get("ASEPRITE")
    if not binary:
        raise SystemExit("defina a variável de ambiente ASEPRITE com o caminho do executável do Aseprite")
    return binary


def list_layers(binary: str, source: Path) -> set:
    result = subprocess.run([binary, "-b", "--list-layers", str(source)],
                            check=True, capture_output=True, text=True)
    return {line.strip() for line in result.stdout.splitlines() if line.strip()}


def export_layer(binary: str, source: Path, layer: str, target: Path) -> None:
    subprocess.run([binary, "-b", "--layer", layer, str(source),
                    "--sheet", str(target), "--sheet-type", "horizontal"],
                   check=True)


def export_piece(binary: str, source: Path, out_root: Path) -> None:
    slot = source.parent.name
    piece_id = source.stem
    meta_source = source.with_suffix(".meta.json")
    if not meta_source.exists():
        raise SystemExit(f"{source}: falta o arquivo de metadados {meta_source.name}")

    layers = list_layers(binary, source)
    missing = [layer for layer in REQUIRED_LAYERS if layer not in layers]
    if missing:
        raise SystemExit(f"{source}: camadas obrigatórias ausentes {missing} (encontradas: {sorted(layers)})")

    target_dir = out_root / slot / piece_id
    target_dir.mkdir(parents=True, exist_ok=True)

    for layer in REQUIRED_LAYERS + OPTIONAL_LAYERS:
        target = target_dir / f"{layer}.png"
        if layer in layers:
            export_layer(binary, source, layer, target)
        elif target.exists():
            # A camada opcional saiu do arquivo-fonte: remove o export antigo.
            target.unlink()

    shutil.copyfile(meta_source, target_dir / "meta.json")
    print(f"exportado {slot}/{piece_id}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--src", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    binary = aseprite_binary()
    sources = sorted(args.src.glob("*/*.aseprite"))
    if not sources:
        raise SystemExit(f"nenhum .aseprite encontrado em {args.src}/<slot>/")

    for source in sources:
        export_piece(binary, source, args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())

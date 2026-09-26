#!/usr/bin/env python3
"""Valida as peças exportadas do paper doll contra o contrato de arte.

Uso:
    python tools/validate_assets.py --contract godot/data/contract.json --assets godot/assets

Estrutura esperada:
    <assets>/<slot>/<piece_id>/shade.png
                               mask.png
                               meta.json
                               line.png   (opcional)

Sai com código 1 se qualquer peça violar o contrato. Todas as violações de
todas as peças são listadas antes de sair, para que um único run mostre tudo.
"""

import argparse
import json
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image

PIECE_ID = re.compile(r"^[a-z0-9_]+$")
HEX_COLOR = re.compile(r"^#[0-9a-fA-F]{6}$")
MASK_CHANNELS = ("r", "g", "b")
REGION_KEYS = ("base",) + MASK_CHANNELS
PIECE_FILES = {"shade.png", "mask.png", "meta.json", "line.png"}
META_KEYS = {"id", "regions", "decal_zones", "tags", "hides_slots", "requires_tags"}
DECAL_ZONE_KEYS = {"name", "rect", "frame_offsets"}


# ---------------------------------------------------------------- contrato

def load_contract(path: Path) -> dict:
    contract = json.loads(path.read_text(encoding="utf-8"))

    canvas = contract["canvas"]
    for key in ("frame_width", "frame_height", "frames"):
        if not isinstance(canvas[key], int) or canvas[key] <= 0:
            raise SystemExit(f"contrato: canvas.{key} precisa ser inteiro positivo")

    for name, category in contract["categories"].items():
        regions = category["regions"]
        if "base" not in regions:
            raise SystemExit(f"contrato: categoria '{name}' não declara a região 'base'")
        unknown = set(regions) - set(REGION_KEYS)
        if unknown:
            raise SystemExit(f"contrato: categoria '{name}' tem regiões inválidas {sorted(unknown)}")
        if not isinstance(category["mask_gradients"], bool):
            raise SystemExit(f"contrato: categoria '{name}'.mask_gradients precisa ser true/false")

    z_orders = {}
    for name, slot in contract["slots"].items():
        if slot["category"] not in contract["categories"]:
            raise SystemExit(f"contrato: slot '{name}' usa categoria inexistente '{slot['category']}'")
        z = slot["z_order"]
        if z in z_orders:
            raise SystemExit(f"contrato: slots '{z_orders[z]}' e '{name}' têm o mesmo z_order {z}")
        z_orders[z] = name

    return contract


# ---------------------------------------------------------------- imagens

def load_rgba(path: Path, width: int, height: int, errors: list) -> np.ndarray | None:
    image = Image.open(path)
    if image.size != (width, height):
        errors.append(f"{path.name}: tamanho {image.size[0]}x{image.size[1]}, esperado {width}x{height}")
        return None
    return np.asarray(image.convert("RGBA"), dtype=np.int32)


def locate(mask: np.ndarray, frame_width: int) -> str:
    """Descreve onde está o primeiro pixel marcado: frame e coordenada dentro do frame."""
    ys, xs = np.nonzero(mask)
    x, y = int(xs[0]), int(ys[0])
    return f"frame {x // frame_width}, ({x % frame_width}, {y})"


def check_shade(shade: np.ndarray, contract: dict, errors: list) -> None:
    frame_width = contract["canvas"]["frame_width"]
    alpha = shade[..., 3]
    opaque = alpha > 0

    if not opaque.any():
        errors.append("shade.png: está vazio (nenhum pixel visível)")
        return
    if (alpha == 255).all():
        errors.append("shade.png: não tem transparência; a silhueta precisa vir do alpha")
        return

    rgb = shade[..., :3]
    saturation = rgb.max(axis=2) - rgb.min(axis=2)
    limit = contract["shade"]["max_saturation"]
    saturated = opaque & (saturation > limit)
    if saturated.any():
        errors.append(
            f"shade.png: {int(saturated.sum())} pixels com cor (saturação até "
            f"{int(saturation[opaque].max())} > {limit}); primeiro em {locate(saturated, frame_width)}"
        )

    mean_value = float(rgb.max(axis=2)[opaque].mean())
    minimum = contract["shade"]["min_mean_value"]
    if mean_value < minimum:
        errors.append(
            f"shade.png: valor médio {mean_value:.0f} < {minimum}; o shade está escuro demais "
            f"para aceitar cores claras por multiplicação"
        )


def check_mask(mask: np.ndarray, shade: np.ndarray, category: dict, declared: set,
               contract: dict, errors: list) -> None:
    frame_width = contract["canvas"]["frame_width"]
    # Peso efetivo de cada região: rgb × alpha. Pixel transparente = sem região.
    weights = mask[..., :3] * mask[..., 3:4] // 255
    painted = weights.sum(axis=2)

    for index, channel in enumerate(MASK_CHANNELS):
        used = bool(weights[..., index].any())
        if used and channel not in declared:
            errors.append(f"mask.png: canal {channel.upper()} está pintado mas não foi declarado em meta.regions")
        if channel in declared and not used:
            errors.append(f"mask.png: canal {channel.upper()} declarado em meta.regions mas não está pintado")

    tolerance = contract["mask"]["sum_tolerance"]
    overflow = painted > 255 + tolerance
    if overflow.any():
        errors.append(
            f"mask.png: {int(overflow.sum())} pixels com R+G+B acima de 255 (regiões sobrepostas); "
            f"primeiro em {locate(overflow, frame_width)}"
        )

    outside = (painted > 0) & (shade[..., 3] == 0)
    if outside.any():
        errors.append(
            f"mask.png: {int(outside.sum())} pixels pintados fora da silhueta do shade; "
            f"primeiro em {locate(outside, frame_width)}"
        )

    if not category["mask_gradients"]:
        soft = (weights > 0) & (weights < 255)
        if soft.any():
            errors.append(
                f"mask.png: {int(soft.any(axis=2).sum())} pixels com valor intermediário, mas esta "
                f"categoria exige bordas duras (0 ou 255); primeiro em {locate(soft.any(axis=2), frame_width)}"
            )


# ---------------------------------------------------------------- meta.json

def check_meta(meta: dict, piece_id: str, category: dict, contract: dict, errors: list) -> set:
    """Valida meta.json e devolve os canais de máscara declarados."""
    missing = META_KEYS - set(meta)
    extra = set(meta) - META_KEYS
    if missing:
        errors.append(f"meta.json: campos ausentes {sorted(missing)}")
    if extra:
        errors.append(f"meta.json: campos desconhecidos {sorted(extra)}")
    if missing:
        return set()

    if meta["id"] != piece_id:
        errors.append(f"meta.json: id '{meta['id']}' difere do nome da pasta '{piece_id}'")

    regions = meta["regions"]
    if "base" not in regions:
        errors.append("meta.json: regions precisa ter 'base'")
    for key, region in regions.items():
        if key not in category["regions"]:
            errors.append(f"meta.json: região '{key}' não existe nesta categoria "
                          f"(permitidas: {sorted(category['regions'])})")
            continue
        if set(region) != {"default"} or not HEX_COLOR.match(str(region["default"])):
            errors.append(f"meta.json: regions.{key} precisa ser {{\"default\": \"#RRGGBB\"}}")

    check_decal_zones(meta["decal_zones"], contract, errors)

    known_tags = set(contract["tags"])
    for field in ("tags", "requires_tags"):
        unknown = set(meta[field]) - known_tags
        if unknown:
            errors.append(f"meta.json: {field} tem tags fora do contrato {sorted(unknown)}")
    unknown_slots = set(meta["hides_slots"]) - set(contract["slots"])
    if unknown_slots:
        errors.append(f"meta.json: hides_slots tem slots inexistentes {sorted(unknown_slots)}")

    return {key for key in regions if key in MASK_CHANNELS}


def check_decal_zones(zones: list, contract: dict, errors: list) -> None:
    """Cada zona tem um retângulo no frame e um deslocamento por frame ([dx, dy] ou null = oculta)."""
    frame_width = contract["canvas"]["frame_width"]
    frame_height = contract["canvas"]["frame_height"]
    frames = contract["canvas"]["frames"]
    names = set()
    for zone in zones:
        if set(zone) != DECAL_ZONE_KEYS:
            errors.append(f"meta.json: decal_zone precisa ter exatamente {sorted(DECAL_ZONE_KEYS)}, tem {sorted(zone)}")
            continue
        name = zone["name"]
        if name in names:
            errors.append(f"meta.json: decal_zone '{name}' repetida")
        names.add(name)

        x, y, w, h = zone["rect"]
        if w <= 0 or h <= 0:
            errors.append(f"meta.json: decal_zone '{name}' tem tamanho inválido {zone['rect']}")
            continue

        offsets = zone["frame_offsets"]
        if len(offsets) != frames:
            errors.append(f"meta.json: decal_zone '{name}' tem {len(offsets)} frame_offsets, esperado {frames}")
            continue
        for frame, offset in enumerate(offsets):
            if offset is None:
                continue
            dx, dy = offset
            if x + dx < 0 or y + dy < 0 or x + dx + w > frame_width or y + dy + h > frame_height:
                errors.append(f"meta.json: decal_zone '{name}' sai do frame {frame} "
                              f"({frame_width}x{frame_height}) com offset {offset}")


# ---------------------------------------------------------------- peça

def validate_piece(piece_dir: Path, slot_name: str, contract: dict) -> list:
    errors = []
    piece_id = piece_dir.name
    category = contract["categories"][contract["slots"][slot_name]["category"]]

    if not PIECE_ID.match(piece_id):
        errors.append(f"nome da pasta '{piece_id}' fora do padrão [a-z0-9_]")

    # Arquivos .import são gerados pelo Godot quando os assets ficam dentro do projeto.
    files = {p.name for p in piece_dir.iterdir() if p.suffix != ".import"}
    for required in ("shade.png", "mask.png", "meta.json"):
        if required not in files:
            errors.append(f"arquivo obrigatório ausente: {required}")
    unexpected = files - PIECE_FILES
    if unexpected:
        errors.append(f"arquivos inesperados: {sorted(unexpected)}")
    if errors:
        return errors

    meta = json.loads((piece_dir / "meta.json").read_text(encoding="utf-8"))
    declared = check_meta(meta, piece_id, category, contract, errors)

    canvas = contract["canvas"]
    width = canvas["frame_width"] * canvas["frames"]
    height = canvas["frame_height"]
    shade = load_rgba(piece_dir / "shade.png", width, height, errors)
    mask = load_rgba(piece_dir / "mask.png", width, height, errors)
    if "line.png" in files:
        load_rgba(piece_dir / "line.png", width, height, errors)

    if shade is not None:
        check_shade(shade, contract, errors)
    if shade is not None and mask is not None:
        check_mask(mask, shade, category, declared, contract, errors)

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--contract", type=Path, required=True)
    parser.add_argument("--assets", type=Path, required=True)
    args = parser.parse_args()

    contract = load_contract(args.contract)
    if not args.assets.is_dir():
        raise SystemExit(f"pasta de assets não encontrada: {args.assets}")

    failures = 0
    checked = 0
    for slot_dir in sorted(p for p in args.assets.iterdir() if p.is_dir()):
        if slot_dir.name not in contract["slots"]:
            print(f"✗ {slot_dir.name}/: slot não existe no contrato")
            failures += 1
            continue
        for piece_dir in sorted(p for p in slot_dir.iterdir() if p.is_dir()):
            checked += 1
            errors = validate_piece(piece_dir, slot_dir.name, contract)
            label = f"{slot_dir.name}/{piece_dir.name}"
            if errors:
                failures += 1
                print(f"✗ {label}")
                for error in errors:
                    print(f"    - {error}")
            else:
                print(f"✓ {label}")

    print(f"\n{checked} peças verificadas, {failures} itens (peças ou slots) com problemas.")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())

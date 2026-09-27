#!/usr/bin/env python3
"""Gera peças de exemplo (formas simples) que respeitam o contrato, para testar o compositor sem arte real.

Uso:
    python tools/make_sample_assets.py --contract godot/data/contract.json --godot godot

Escreve:
    <godot>/assets/<slot>/<piece_id>/shade.png, mask.png, meta.json
    <godot>/decals/estrela.png

O personagem "respira": o tronco e a cabeça descem 1 px em metade dos frames. A zona de
decalque da camisa acompanha esse movimento via frame_offsets.
"""

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image


class Sheet:
    def __init__(self, canvas: dict):
        self.fw = canvas["frame_width"]
        self.fh = canvas["frame_height"]
        self.frames = canvas["frames"]
        self.yy, self.xx = np.mgrid[0:self.fh, 0:self.fw * self.frames]
        self.frame = self.xx // self.fw
        self.lx = self.xx % self.fw  # x local dentro do frame

    def bob(self, frame):
        return 1 if frame % 4 in (1, 2) else 0

    def dy(self):
        """Deslocamento vertical por pixel, conforme o frame em que o pixel está."""
        return np.vectorize(self.bob)(self.frame)

    def ellipse(self, cx, cy, rx, ry, moves=True):
        oy = self.dy() if moves else 0
        return ((self.lx - cx) / rx) ** 2 + ((self.yy - cy - oy) / ry) ** 2 <= 1

    def rect(self, x0, y0, x1, y1, moves=True):
        oy = self.dy() if moves else 0
        return (self.lx >= x0) & (self.lx < x1) & (self.yy - oy >= y0) & (self.yy - oy < y1)


def shade_from(sheet: Sheet, silhouette: np.ndarray, value: int = 225) -> np.ndarray:
    """Cinza com leve gradiente vertical e um contorno de 1 px mais escuro."""
    image = np.zeros(silhouette.shape + (4,), np.uint8)
    gradient = value - 25 * (sheet.yy % sheet.fh) / sheet.fh
    inner = silhouette.copy()
    inner[1:, :] &= silhouette[:-1, :]
    inner[:-1, :] &= silhouette[1:, :]
    inner[:, 1:] &= silhouette[:, :-1]
    inner[:, :-1] &= silhouette[:, 1:]
    rim = silhouette & ~inner
    v = np.where(rim, 140, gradient).astype(np.uint8)
    image[..., 0] = image[..., 1] = image[..., 2] = np.where(silhouette, v, 0)
    image[..., 3] = np.where(silhouette, 255, 0)
    return image


def mask_from(r=None, g=None, b=None, shape=None) -> np.ndarray:
    image = np.zeros(shape + (4,), np.uint8)
    for index, channel in enumerate((r, g, b)):
        if channel is not None:
            image[..., index] = channel
    image[..., 3] = np.where(image[..., :3].any(axis=2), 255, 0)
    return image


def write_piece(root: Path, slot: str, piece_id: str, shade, mask, meta: dict) -> None:
    target = root / "assets" / slot / piece_id
    target.mkdir(parents=True, exist_ok=True)
    Image.fromarray(shade).save(target / "shade.png")
    Image.fromarray(mask).save(target / "mask.png")
    meta = {"id": piece_id, "decal_zones": [], "tags": [], "hides_slots": [], "requires_tags": [], **meta}
    (target / "meta.json").write_text(json.dumps(meta, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--contract", type=Path, required=True)
    parser.add_argument("--godot", type=Path, required=True)
    args = parser.parse_args()

    contract = json.loads(args.contract.read_text(encoding="utf-8"))
    s = Sheet(contract["canvas"])
    shape = s.yy.shape
    full = lambda m: np.where(m, 255, 0).astype(np.uint8)

    # Corpo (pele): cabeça, pescoço, tronco, braços e pernas.
    body = (s.ellipse(32, 26, 13, 14) | s.rect(29, 38, 35, 44) | s.rect(22, 42, 42, 72)
            | s.rect(17, 44, 22, 66) | s.rect(42, 44, 47, 66) | s.rect(24, 72, 31, 94, moves=False)
            | s.rect(33, 72, 40, 94, moves=False))
    blush = s.ellipse(25, 31, 3, 2) | s.ellipse(39, 31, 3, 2)
    lips = s.rect(30, 34, 34, 36)
    write_piece(args.godot, "body", "body_01", shade_from(s, body, 235),
                mask_from(r=full(blush), g=full(lips), shape=shape),
                {"regions": {"base": {"default": "#e8b89a"}, "r": {"default": "#e0907e"},
                             "g": {"default": "#b5525b"}},
                 "tags": ["body_humanoid"]})

    # Olhos: esclera (base) + íris (R).
    eyes = s.rect(24, 23, 29, 28) | s.rect(35, 23, 40, 28)
    iris = s.rect(26, 23, 28, 28) | s.rect(37, 23, 39, 28)
    write_piece(args.godot, "eyes", "eyes_01", shade_from(s, eyes, 245),
                mask_from(r=full(iris), shape=shape),
                {"regions": {"base": {"default": "#ffffff"}, "r": {"default": "#3b6ea5"}},
                 "requires_tags": ["body_humanoid"]})

    # Cabelo: raiz (R) em cima, cor base no meio, pontas (B) embaixo, com transições em gradiente.
    hair = ((s.ellipse(32, 19, 15, 10) & s.rect(0, 0, 64, 24)) | s.rect(17, 16, 21, 40) | s.rect(43, 16, 47, 40))
    t = np.clip(((s.yy - s.dy()) - 9) / 31, 0, 1)
    root_w = (np.clip(1 - 2 * t, 0, 1) * 255).round()
    tips_w = (np.clip(2 * t - 1, 0, 1) * 255).round()
    write_piece(args.godot, "hair_front", "hair_03", shade_from(s, hair, 230),
                mask_from(r=np.where(hair, root_w, 0).astype(np.uint8),
                          b=np.where(hair, tips_w, 0).astype(np.uint8), shape=shape),
                {"regions": {"base": {"default": "#6b4a2f"}, "r": {"default": "#3a2616"},
                             "b": {"default": "#c9a36b"}},
                 "tags": ["hair_long"], "requires_tags": ["body_humanoid"]})

    # Camisa: primária (base) + gola (R), com zona de decalque no peito que acompanha a respiração.
    shirt = s.rect(21, 43, 43, 71) | s.rect(16, 44, 21, 55) | s.rect(43, 44, 48, 55)
    collar = s.rect(27, 43, 37, 46)
    frames = contract["canvas"]["frames"]
    write_piece(args.godot, "torso", "shirt_02", shade_from(s, shirt, 235),
                mask_from(r=full(collar), shape=shape),
                {"regions": {"base": {"default": "#2255aa"}, "r": {"default": "#f2f2f2"}},
                 "decal_zones": [{"name": "peito", "rect": [26, 50, 12, 10],
                                  "frame_offsets": [[0, s.bob(f)] for f in range(frames)]}],
                 "requires_tags": ["body_humanoid"]})

    # Calça: só cor primária (nenhum canal de máscara).
    pants = s.rect(22, 70, 42, 75, moves=False) | s.rect(23, 75, 31, 94, moves=False) | s.rect(33, 75, 41, 94, moves=False)
    write_piece(args.godot, "legs", "pants_01", shade_from(s, pants, 225), mask_from(shape=shape),
                {"regions": {"base": {"default": "#3d3d46"}}, "requires_tags": ["body_humanoid"]})

    # Variações para o editor ter o que trocar em cada slot.
    short_hair = s.ellipse(32, 19, 15, 10) & s.rect(0, 0, 64, 25)
    write_piece(args.godot, "hair_front", "hair_01", shade_from(s, short_hair, 230),
                mask_from(r=np.where(short_hair, root_w, 0).astype(np.uint8), shape=shape),
                {"regions": {"base": {"default": "#1f1b18"}, "r": {"default": "#0d0b0a"}},
                 "requires_tags": ["body_humanoid"]})

    long_back = s.rect(18, 18, 47, 60)
    back_t = np.clip(((s.yy - s.dy()) - 18) / 42, 0, 1)
    write_piece(args.godot, "hair_back", "hair_back_01", shade_from(s, long_back, 215),
                mask_from(r=np.where(long_back, (np.clip(1 - 2 * back_t, 0, 1) * 255).round(), 0).astype(np.uint8),
                          b=np.where(long_back, (np.clip(2 * back_t - 1, 0, 1) * 255).round(), 0).astype(np.uint8),
                          shape=shape),
                {"regions": {"base": {"default": "#6b4a2f"}, "r": {"default": "#3a2616"},
                             "b": {"default": "#c9a36b"}},
                 "tags": ["hair_long"], "requires_tags": ["body_humanoid"]})

    tank = s.rect(22, 44, 42, 71)
    stripes = tank & ((s.yy - s.dy()) % 5 == 0)
    write_piece(args.godot, "torso", "shirt_01", shade_from(s, tank, 235),
                mask_from(g=full(stripes), shape=shape),
                {"regions": {"base": {"default": "#f2f2f2"}, "g": {"default": "#c0392b"}},
                 "decal_zones": [{"name": "peito", "rect": [26, 50, 12, 10],
                                  "frame_offsets": [[0, s.bob(f)] for f in range(frames)]}],
                 "requires_tags": ["body_humanoid"]})

    beanie = s.ellipse(32, 18, 16, 11) & s.rect(0, 0, 64, 22)
    brim = beanie & s.rect(0, 18, 64, 22)
    write_piece(args.godot, "hat", "beanie_01", shade_from(s, beanie, 230),
                mask_from(r=full(brim), shape=shape),
                {"regions": {"base": {"default": "#d35400"}, "r": {"default": "#a04000"}},
                 "tags": ["hat_tall"], "requires_tags": ["body_humanoid"]})

    # Decalque colorido 7x7 (estrela simples). Decalques carregam cor própria e recebem o sombreamento da peça.
    star = np.zeros((7, 7, 4), np.uint8)
    pattern = ["...#...", "..###..", "#######", ".#####.", "..###..", ".##.##.", "##...##"]
    for y, row in enumerate(pattern):
        for x, cell in enumerate(row):
            if cell == "#":
                star[y, x] = (255, 214, 64, 255)
    (args.godot / "decals").mkdir(parents=True, exist_ok=True)
    Image.fromarray(star).save(args.godot / "decals" / "estrela.png")
    print(f"peças de exemplo escritas em {args.godot / 'assets'}")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Create deterministic synthetic image fixtures. Requires Pillow; never reads user photos."""
import argparse
import hashlib
import json
from pathlib import Path
import random
from PIL import Image, ImageDraw, __version__ as pillow_version


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    rng = random.Random(42)
    records = []

    def save(image, name, **kwargs):
        path = args.output / name
        image.save(path, **kwargs)
        data = path.read_bytes()
        records.append({"file": name, "size": len(data), "width": image.width, "height": image.height,
                        "sha256": hashlib.sha256(data).hexdigest()})

    for name, size in [("portrait.jpg", (1440, 2560)), ("landscape.jpg", (2560, 1440)),
                       ("large-4k.jpg", (3840, 2160)), ("tiny.jpg", (32, 24))]:
        # Texture and edges exercise compression, but do not replace real-photo visual acceptance.
        image = Image.frombytes("RGB", size, rng.randbytes(size[0] * size[1] * 3))
        draw = ImageDraw.Draw(image)
        draw.rectangle((size[0] // 4, size[1] // 4, size[0] * 3 // 4, size[1] * 3 // 4), fill="brown")
        save(image, name, quality=92)
    alpha = Image.new("RGBA", (768, 768), (0, 0, 0, 0))
    ImageDraw.Draw(alpha).ellipse((64, 64, 704, 704), fill=(140, 70, 30, 180))
    save(alpha, "alpha.png")
    oriented = Image.new("RGB", (1200, 800), "white")
    ImageDraw.Draw(oriented).text((40, 40), "TOP LEFT / EXIF orientation=6", fill="black")
    exif = Image.Exif(); exif[274] = 6
    save(oriented, "exif-rotate.jpg", exif=exif)
    menu = Image.new("RGB", (1600, 2400), "white")
    draw = ImageDraw.Draw(menu)
    for row in range(80):
        draw.text((30, 30 + row * 28), f"Coffee item {row:02d} - espresso / filter / milk - 12.50", fill="black")
    save(menu, "menu-text.png")
    first = Image.new("RGB", (320, 240), "red")
    save(first, "animation.gif", save_all=True, append_images=[Image.new("RGB", first.size, "blue")], duration=100, loop=0)
    (args.output / "manifest.json").write_text(json.dumps({"seed": 42, "pillow": pillow_version,
        "warning": "Synthetic validation corpus; not a representative quality or compression benchmark", "images": records}, indent=2) + "\n")
    print(f"Created {len(records)} fixtures and manifest in {args.output}")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Regenerate icon.ico from icon.svg.

Usage:  pip install cairosvg pillow && python make-icon.py

Kept as a script rather than a build step: the icon changes about once a year,
and making every build depend on a Python toolchain would be a poor trade.
"""
import io
import os
import sys

try:
    import cairosvg
    from PIL import Image
except ImportError:
    sys.exit("Requires cairosvg and pillow:  pip install cairosvg pillow")

SIZES = [16, 24, 32, 48, 64, 128, 256]
HERE = os.path.dirname(os.path.abspath(__file__))
SVG = os.path.join(HERE, "icon.svg")


def main() -> int:
    if not os.path.exists(SVG):
        return print(f"missing {SVG}") or 1

    largest = None
    for size in SIZES:
        png = cairosvg.svg2png(url=SVG, output_width=size, output_height=size)
        img = Image.open(io.BytesIO(png)).convert("RGBA")
        opaque = sum(1 for p in img.getdata() if p[3] > 0)
        coverage = opaque / (size * size)
        print(f"  {size:3d}px  {coverage:5.1%} opaque")
        # A blank render is the failure mode worth catching: it produces a
        # valid .ico that shows nothing.
        if coverage < 0.10:
            return print(f"ERROR: {size}px render is essentially empty") or 1
        largest = img

    largest.save(os.path.join(HERE, "icon-256.png"))
    largest.save(
        os.path.join(HERE, "icon.ico"),
        format="ICO",
        sizes=[(s, s) for s in SIZES],
    )
    print("\nwrote icon.ico and icon-256.png")
    return 0


if __name__ == "__main__":
    sys.exit(main())

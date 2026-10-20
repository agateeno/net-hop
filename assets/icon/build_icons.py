"""Regenerates the app's icon files from the SVG sources in this folder.

Requires the `resvg` command line tool:
  macOS:   brew install resvg
  Windows: winget install resvg  (or download it from https://github.com/linebender/resvg/releases)

Usage:   python assets/icon/build_icons.py

Each size is rendered from the source drawn for it, as Fluent icons do:
  16px       NetHop-16.svg     pixel-aligned, for the notification area at 100%
  20-32px    NetHop-small.svg  simplified shapes
  40-256px   NetHop.svg        full detail
The .nethop file icon follows the same scheme with NetHopFile*.svg.

While PREVIEW is True, the app icon gets the "PRE" badge drawn in PreBadge*.svg: one per pixel size
up to 32px, so its letters stay sharp, and PreBadge.svg above. Set it to False and run the script
again when NetHop leaves preview. File icons have no badge.
"""

import re
import shutil
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ASSETS = HERE.parent.parent / "src" / "NetHop.App" / "Assets"
LOGI_METADATA = HERE.parent.parent / "src" / "NetHop.LogiPlugin" / "package" / "metadata"

PREVIEW = True


def badge_for(size: int) -> str:
    return f"PreBadge-{size}.svg" if size <= 32 else "PreBadge.svg"


def sources(prefix):
    return [(16, f"{prefix}-16.svg")] + [(s, f"{prefix}-small.svg") for s in (20, 24, 32)] + \
           [(s, f"{prefix}.svg") for s in (40, 48, 64, 96, 128, 256)]


SOURCES = [
    (16, "NetHop-16.svg"),
    (20, "NetHop-small.svg"),
    (24, "NetHop-small.svg"),
    (32, "NetHop-small.svg"),
    (40, "NetHop.svg"),
    (48, "NetHop.svg"),
    (64, "NetHop.svg"),
    (96, "NetHop.svg"),
    (128, "NetHop.svg"),
    (256, "NetHop.svg"),
]


def with_badge(name: str, size: int, out: Path) -> Path:
    """The source with the badge for this size drawn on top, as a nested SVG scaled to the source's canvas."""
    if not PREVIEW:
        return HERE / name
    base = (HERE / name).read_text(encoding="utf-8")
    canvas = re.search(r'viewBox="([^"]+)"', base).group(1).split()
    badge = (HERE / badge_for(size)).read_text(encoding="utf-8")
    badge = re.sub(r'width="[^"]*" height="[^"]*"', f'x="0" y="0" width="{canvas[2]}" height="{canvas[3]}"', badge, count=1)
    out.write_text(base[: base.rindex("</svg>")] + badge + "</svg>\n", encoding="utf-8")
    return out


def render(svg: Path, size: int, out: Path) -> bytes:
    subprocess.run(["resvg", "-w", str(size), "-h", str(size), str(svg), str(out)], check=True)
    return out.read_bytes()


def write_ico(path: Path, images: list[tuple[int, bytes]]) -> None:
    """ICO container with PNG-compressed entries (supported since Windows Vista)."""
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for size, data in images:
        # Width/height 0 means 256 in the ICO format.
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    path.write_bytes(header + entries + b"".join(data for _, data in images))


def main() -> int:
    if shutil.which("resvg") is None:
        print(__doc__)
        print("error: resvg was not found on PATH", file=sys.stderr)
        return 1

    ASSETS.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        images = [(size, render(with_badge(name, size, Path(tmp) / f"{size}.svg"), size, Path(tmp) / f"{size}.png"))
                  for size, name in SOURCES]
        file_images = [(size, render(HERE / name, size, Path(tmp) / f"file{size}.png")) for size, name in sources("NetHopFile")]

    # Icon of .nethop files in Explorer.
    write_ico(ASSETS / "NetHopFile.ico", file_images)

    write_ico(ASSETS / "NetHop.ico", images)
    (ASSETS / "NetHop.png").write_bytes(dict(images)[256])
    (ASSETS / "NetHop-32.png").write_bytes(dict(images)[32])
    # Logi Options+ shows this icon for the plugin.
    (LOGI_METADATA / "Icon256x256.png").write_bytes(dict(images)[256])
    print(f"Wrote NetHop.ico ({len(images)} sizes), NetHop.png and NetHop-32.png to {ASSETS}")
    print(f"Wrote Icon256x256.png to {LOGI_METADATA}")
    print(f"Wrote NetHopFile.ico ({len(file_images)} sizes) to {ASSETS}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

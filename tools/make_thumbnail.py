r"""Builds the square Steam Workshop / mod thumbnail (1024x1024, under 1 MB) from docs\screenshot.jpg.

    python tools\make_thumbnail.py      # -> docs\thumbnail.jpg

1. Crops the left part of the chest screen (Chest 1 open on the left, the hero beside it) to the panel the
   HTML shows, in a gitignored build folder.
2. Renders tools\preview_thumbnail.html with headless Edge (or Chrome) to a 1024x1024 PNG.
3. Saves docs\thumbnail.jpg, stepping JPEG quality down until it fits under 1 MB.
Needs Pillow and Edge or Chrome.
"""
from __future__ import annotations

import io
import shutil
import subprocess
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parent.parent
SHOT = REPO / "docs" / "screenshot.jpg"
BUILD = REPO / "screenshots" / "thumbnail build"
OUT = REPO / "docs" / "thumbnail.jpg"
BROWSERS = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
]


def main() -> None:
    BUILD.mkdir(parents=True, exist_ok=True)
    shot = Image.open(SHOT).convert("RGB")
    w, h = shot.size
    # The chest list and the hero beside it: the left 60 % of the 21:9 screen, full height, dropped 44 px
    # so the chest buttons and name clear the frame.
    cw = int(w * 0.60)
    scale = 1024 / cw
    ph = int(h * scale)
    top = 44
    panel = shot.crop((0, 0, cw, h)).resize((1024, ph), Image.LANCZOS)
    # Gold ring round the open chest: its header (New chest / Rename / Remove, the name) and its rows,
    # in fractions of the screenshot (3440x1440 at 21:9).
    ring = (0.004 * w, 0.020 * h, 0.222 * w, 0.455 * h)
    l, t = int(ring[0] * scale) + 6, int(ring[1] * scale) + top
    rw, rh = int((ring[2] - ring[0]) * scale), int((ring[3] - ring[1]) * scale)
    panel_path = BUILD / "panel.png"
    panel.save(panel_path)

    html = (REPO / "tools" / "preview_thumbnail.html").read_text(encoding="utf-8")
    for key, value in {"PANEL_IMG": panel_path.as_uri(), "SHOT_TOP": top, "SHOT_H": ph,
                       "RING_L": l, "RING_T": t, "RING_W": rw, "RING_H": rh}.items():
        html = html.replace(key, str(value))
    page = BUILD / "thumbnail.html"
    page.write_text(html, encoding="utf-8")

    browser = next((b for b in BROWSERS if Path(b).exists()), None) or shutil.which("msedge") or shutil.which("chrome")
    if not browser:
        raise SystemExit("Needs Edge or Chrome.")
    png = BUILD / "thumbnail.png"
    subprocess.run([browser, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--allow-file-access-from-files",
                    "--window-size=1024,1024", f"--screenshot={png}", page.as_uri()], check=True, timeout=60)

    img = Image.open(png).convert("RGB")
    for quality in range(95, 50, -5):
        buf = io.BytesIO()
        img.save(buf, "JPEG", quality=quality, optimize=True)
        if buf.tell() < 1_000_000:
            OUT.write_bytes(buf.getvalue())
            print(f"Saved {OUT} ({buf.tell() // 1024} KB, quality {quality})")
            return
    raise SystemExit("Could not get the thumbnail under 1 MB.")


if __name__ == "__main__":
    main()

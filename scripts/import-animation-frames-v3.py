"""Export complete, registered character drawings from the v3 pose sheets.

Every output PNG contains exactly one full source pose. Never paste pieces of
the idle drawing over an action drawing: that makes wings/hair/tail appear to
belong to a second character.
"""

from __future__ import annotations

import argparse
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ANIMATIONS = ROOT / "assets/character/animations"
SOURCES = ANIMATIONS / "sources/v3"
SIZE = 512
SCALE = 0.96
FOOT_Y = 495

CLIPS = ["Blink", "Greet", "Pet", "Feed", "Cuddle", "Hop", "Dance",
         "Stretch", "LookAround", "Hover", "Land", "Wake", "Celebrate",
         "Tail", "Sleep"]

# Cells touching a sheet cut line are intentionally absent. Each selected
# cell is a complete drawing, including both wings and the whole tail.
SOURCE_FRAMES = {
    "Blink": [0, 0, 2, 2, 4, 0],
    "Greet": [0, 1, 2, 3, 4, 0],
    "Pet": [0, 1, 2, 3, 4, 0],
    # Alternate sheet columns have incompatible wing silhouettes. Keep the
    # same-column poses for these actions so the body does not rock sideways.
    "Feed": [0, 0, 2, 2, 2, 0],
    "Cuddle": [0, 1, 2, 3, 4, 0],
    "Dance": [0, 1, 2, 3, 4, 0],
    "Stretch": [0, 0, 2, 2, 2, 0],
    "LookAround": [0, 1, 2, 3, 1, 0],
    "Hover": [0, 1, 3, 3],
    "Land": [1, 1, 1, 1, 1, 1],
    "Wake": [0, 1, 2, 3, 1, 0],
    "Celebrate": [0, 1, -1, -1, 1, 0],
    "Tail": [0, 2, 3, 4, 5, 0],
    "Sleep": [0, 1, 2, 3, 1, 0],
}


def cell(sheet: Image.Image, index: int) -> Image.Image:
    x, y = index % 2 * SIZE, index // 2 * SIZE
    return sheet.crop((x, y, x + SIZE, y + SIZE))


def load_sheet(name: str) -> Image.Image:
    path = SOURCES / f"{name}.png"
    sheet = Image.open(path).convert("RGBA")
    if sheet.size != (1024, 1536):
        raise ValueError(f"{path}: expected 1024x1536, got {sheet.size}")
    return sheet


def foot_bottom(frame: Image.Image) -> int:
    # The central shoes establish the anchor, independent of hair and wings.
    alpha = frame.getchannel("A").crop((210, 430, 305, 512))
    box = alpha.point(lambda a: 255 if a >= 96 else 0).getbbox()
    return 430 + box[3] - 1 if box else 491


def translate(frame: Image.Image, dx: int, dy: int) -> Image.Image:
    result = Image.new("RGBA", (SIZE, SIZE))
    result.paste(frame, (dx, dy))
    return result


def remove_detached_fragments(frame: Image.Image) -> Image.Image:
    """Drop small neighboring-cell fragments without cutting into the figure."""
    alpha = np.asarray(frame.getchannel("A")).copy()
    opaque = (alpha >= 16).ravel()
    seen = np.zeros(opaque.size, dtype=np.bool_)
    keep = np.zeros(opaque.size, dtype=np.bool_)
    for start in np.flatnonzero(opaque):
        if seen[start]:
            continue
        queue = deque([int(start)])
        seen[start] = True
        component = []
        while queue:
            point = queue.popleft()
            component.append(point)
            x = point % SIZE
            for adjacent in (point - SIZE, point + SIZE,
                             point - 1 if x else -1,
                             point + 1 if x < SIZE - 1 else -1):
                if 0 <= adjacent < opaque.size and opaque[adjacent] and not seen[adjacent]:
                    seen[adjacent] = True
                    queue.append(adjacent)
        touches_cut_line = any(point < SIZE or point >= SIZE * (SIZE - 1)
                               or point % SIZE in (0, SIZE - 1)
                               for point in component)
        if len(component) >= 64 and not touches_cut_line:
            keep[component] = True
    alpha.ravel()[~keep] = 0
    frame.putalpha(Image.fromarray(alpha, "L"))
    return frame


def prepare(source: Image.Image, label: str) -> Image.Image:
    source = remove_detached_fragments(source)
    box = source.getchannel("A").point(lambda a: 255 if a >= 96 else 0).getbbox()
    if box is None or 0 in box[:2] or SIZE in box[2:]:
        raise ValueError(f"{label}: source figure touches a sheet cut line: {box}")
    scaled_size = round(SIZE * SCALE)
    scaled = source.resize((scaled_size, scaled_size), Image.Resampling.LANCZOS)
    result = Image.new("RGBA", (SIZE, SIZE))
    result.paste(scaled, ((SIZE - scaled_size) // 2,
                          FOOT_Y - round(foot_bottom(source) * SCALE)))
    box = result.getchannel("A").point(lambda a: 255 if a >= 96 else 0).getbbox()
    if box is None or box[0] < 10 or box[1] < 10 or box[2] > 501 or box[3] > 501:
        raise ValueError(f"{label}: output figure lacks transparent margin: {box}")
    return result


def celebration_open_pose() -> Image.Image:
    source = Image.open(SOURCES / "Celebrate-open.png").convert("RGBA")
    box = source.getchannel("A").point(lambda a: 255 if a >= 96 else 0).getbbox()
    if box is None or box[0] < 20 or source.width - box[2] < 20:
        raise ValueError("The open-wing celebration source has a clipped wing")
    pose = source.crop(box)
    pose.thumbnail((462, 468), Image.Resampling.LANCZOS)
    result = Image.new("RGBA", (SIZE, SIZE))
    result.paste(pose, ((SIZE - pose.width) // 2, FOOT_Y + 1 - pose.height))
    return result


def sleep_curl_pose() -> Image.Image:
    """Use the original curled-up sleep art, including her sleeping companion."""
    source = Image.open(ROOT / "assets/character/sleeping.png").convert("RGBA")
    box = source.getchannel("A").point(lambda a: 255 if a >= 96 else 0).getbbox()
    if box is None:
        raise ValueError("Original sleep artwork is empty")
    pose = source.crop(box)
    pose.thumbnail((476, 466), Image.Resampling.LANCZOS)
    result = Image.new("RGBA", (SIZE, SIZE))
    result.paste(pose, ((SIZE - pose.width) // 2, FOOT_Y + 1 - pose.height))
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=ANIMATIONS,
                        help="write complete PNG frames to this root")
    args = parser.parse_args()
    output = args.output.resolve()
    sheets = {name: load_sheet(name) for name in set(CLIPS) - {"Hover"}}
    neutral = prepare(cell(sheets["Greet"], 0), "Greet/0")
    sleep_pose = sleep_curl_pose()
    open_wings = celebration_open_pose()
    for name in CLIPS:
        directory = output / name
        directory.mkdir(parents=True, exist_ok=True)
        sheet = sheets["LookAround" if name == "Hover" else name]
        count = 6 if name == "Hop" else len(SOURCE_FRAMES[name])
        for index in range(count):
            if name == "Wake" and index == 0:
                result = sleep_pose.copy()
            elif name == "Wake" and index == 1:
                result = prepare(cell(sheets["Hop"], 1), "Wake/crouch")
            elif name == "Hover" and index == 0:
                result = neutral.copy()
            elif name == "Sleep":
                result = sleep_pose.copy()
            elif name == "Hop":
                if index in (0, 5):
                    result = neutral.copy()
                elif index in (2, 3):
                    result = translate(neutral, 0, -12)
                else:
                    result = prepare(cell(sheet, 1), f"{name}/1")
            elif name == "Celebrate" and index in (2, 3):
                result = open_wings.copy()
            elif name not in ("Sleep", "Hover") and index in (0, 5):
                result = neutral.copy()
            else:
                source_index = SOURCE_FRAMES[name][index]
                result = prepare(cell(sheet, source_index), f"{name}/{source_index}")
            result.save(directory / f"{index:02}.png")
        print(f"{name}: {count} whole-character PNGs")


if __name__ == "__main__":
    main()

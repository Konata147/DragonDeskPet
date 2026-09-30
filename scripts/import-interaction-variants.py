"""Import reviewed 2x2 imagegen pose sheets as complete, registered PNG frames.

This script never pastes idle wings or hair over an action pose. Source sheets live
under assets/character/animations/sources/v11 and are retained for review.
"""

from __future__ import annotations

from collections import deque
import json
from pathlib import Path

import numpy as np
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
ANIMATIONS = ROOT / "assets/character/animations"
SOURCES = ANIMATIONS / "sources/v11"
OUTPUT_SIZE = 512
SOURCE_SCALE = 0.80
FOOT_Y = 498

SHEETS = {
    "FeedCookie": [("FeedCookie", 0), ("FeedCookie", 1), ("FeedCookie", 2), ("FeedCookie", 3)],
    "FeedStrawberry": [("FeedStrawberry", i) for i in range(4)],
    "FeedCake": [("FeedCake", i) for i in range(4)],
    "FeedCandy": [("FeedCandy", i) for i in range(4)],
    "FeedCottonCandy": [("FeedCottonCandy", i) for i in range(4)],
    "DanceStep": [("DanceStepA", i) for i in range(4)]
                 + [("DanceStepB", i) for i in (0, 1, 3)],
    "DanceGuofeng": [("DanceGuofengA", i) for i in range(4)]
                    + [("DanceGuofengB", i) for i in (0, 1, 3)],
    # Wide-open wings in A1/B1/B2 touch source quadrant cut lines. Never import them.
    "DanceWingTail": [("DanceWingTailA", i) for i in (0, 2, 3)]
                     + [("DanceWingTailB", i) for i in (0, 3)]
                     + [("DanceWingTailOpen", -1)],
}

DANCE_SEQUENCES = {
    # Eight beats per phrase: entrance, development, accent, then a clear settle.
    # Repeated indices hold a key pose; they are not an endless dance loop.
    "DanceStep": [0, 0, 1, 1, 2, 2, 3, 4,
                  1, 3, 4, 5, 1, 2, 3, 4,
                  5, 6, 4, 3, 1, 2, 4, 5,
                  6, 5, 3, 2, 1, 1, 0, 0],
    "DanceGuofeng": [0, 0, 1, 1, 2, 2, 3, 3,
                     4, 4, 1, 2, 3, 3, 5, 5,
                     1, 2, 2, 3, 4, 4, 5, 6,
                     6, 5, 4, 3, 2, 1, 0, 0],
    "DanceWingTail": [0, 0, 1, 1, 2, 2, 3, 3,
                      4, 4, 1, 2, 3, 4, 2, 0,
                      1, 3, 4, 2, 1, 0, 3, 4,
                      2, 3, 5, 5, 3, 2, 0, 0],
}


def main_component(image: Image.Image) -> Image.Image:
    data = np.array(image)
    opaque = data[:, :, 3] >= 16
    height, width = opaque.shape
    visited = np.zeros((height, width), dtype=np.bool_)
    best: list[int] = []
    for start in np.flatnonzero(opaque):
        y, x = divmod(int(start), width)
        if visited[y, x]:
            continue
        visited[y, x] = True
        queue = deque([int(start)])
        component: list[int] = []
        while queue:
            index = queue.popleft()
            component.append(index)
            cy, cx = divmod(index, width)
            for nx, ny in ((cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1)):
                if 0 <= nx < width and 0 <= ny < height and opaque[ny, nx] and not visited[ny, nx]:
                    visited[ny, nx] = True
                    queue.append(ny * width + nx)
        if len(component) > len(best):
            best = component
    if len(best) < 20_000:
        raise ValueError("Missing complete character silhouette")
    keep = np.zeros(height * width, dtype=np.bool_)
    keep[best] = True
    data.reshape(-1, 4)[~keep, 3] = 0
    return Image.fromarray(data, "RGBA")


def import_pose(sheet: Image.Image, index: int, name: str) -> Image.Image:
    if sheet.width != sheet.height or sheet.width % 2:
        raise ValueError(f"{name}: expected even square 2x2 source, got {sheet.size}")
    side = sheet.width // 2
    col, row = index % 2, index // 2
    pose = main_component(sheet.crop((col * side, row * side, (col + 1) * side, (row + 1) * side)))
    alpha = np.asarray(pose.getchannel("A"))
    ys, xs = np.where(alpha >= 64)
    min_x, max_x = int(xs.min()), int(xs.max())
    min_y, max_y = int(ys.min()), int(ys.max())
    if min_x < 5 or min_y < 5 or max_x >= side - 5 or max_y >= side - 5:
        raise ValueError(f"{name} cell {index}: artwork meets cut line: {(min_x, min_y, max_x, max_y)}")
    # The central shoes establish the foot baseline; a raised tail is ignored.
    shoe = alpha[int(side * .72):, int(side * .38):int(side * .62)]
    foot_rows = np.where(shoe >= 96)[0]
    if not len(foot_rows):
        raise ValueError(f"{name} cell {index}: feet not found")
    foot_bottom = int(side * .72) + int(foot_rows.max())
    scaled = pose.resize((round(side * SOURCE_SCALE), round(side * SOURCE_SCALE)), Image.Resampling.LANCZOS)
    output = Image.new("RGBA", (OUTPUT_SIZE, OUTPUT_SIZE))
    # Generated quadrants do not place the character at the same x position.
    # Register the head/horns, rather than the moving arms or wing tips.
    head = alpha[int(side * .10):int(side * .38), int(side * .18):int(side * .82)]
    head_x = np.where(head >= 96)[1]
    if not len(head_x):
        raise ValueError(f"{name} cell {index}: head anchor not found")
    head_center = int(side * .18) + float(np.median(head_x))
    left = round(OUTPUT_SIZE / 2 - head_center * SOURCE_SCALE)
    top = round(FOOT_Y - foot_bottom * SOURCE_SCALE)
    output.alpha_composite(scaled, (left, top))
    bounds = output.getchannel("A").point(lambda a: 255 if a >= 64 else 0).getbbox()
    if bounds is None or bounds[0] < 4 or bounds[1] < 4 or bounds[2] > 508 or bounds[3] > 508:
        raise ValueError(f"{name} cell {index}: output safety margin failed: {bounds}")
    print(f"{name}[{index}]: source={(min_x, min_y, max_x, max_y)} foot={foot_bottom} output={bounds}")
    return output


def import_single(image: Image.Image, name: str) -> Image.Image:
    pose = main_component(image)
    alpha = np.asarray(pose.getchannel("A"))
    ys, xs = np.where(alpha >= 64)
    bounds = (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max()))
    if min(bounds[0], bounds[1]) < 4 or bounds[2] >= image.width - 4 or bounds[3] >= image.height - 4:
        raise ValueError(f"{name}: full pose meets canvas edge: {bounds}")
    scale = min(466 / (bounds[2] - bounds[0] + 1), 478 / (bounds[3] - bounds[1] + 1))
    head = alpha[int(image.height * .10):int(image.height * .38), int(image.width * .18):int(image.width * .82)]
    head_x = np.where(head >= 96)[1]
    head_center = int(image.width * .18) + float(np.median(head_x))
    shoe = alpha[int(image.height * .72):, int(image.width * .38):int(image.width * .62)]
    shoe_rows = np.where(shoe >= 96)[0]
    foot_bottom = int(image.height * .72) + int(shoe_rows.max())
    scaled = pose.resize((round(image.width * scale), round(image.height * scale)), Image.Resampling.LANCZOS)
    output = Image.new("RGBA", (OUTPUT_SIZE, OUTPUT_SIZE))
    output.alpha_composite(scaled, (round(256 - head_center * scale), round(FOOT_Y - foot_bottom * scale)))
    print(f"{name}: source={bounds} foot={foot_bottom} scale={scale:.3f}")
    return output


def main() -> None:
    errors = []
    for clip, cells in SHEETS.items():
        result = []
        for name, index in cells:
            source = Image.open(SOURCES / f"{name}.png").convert("RGBA")
            try:
                result.append(import_single(source, name) if index < 0 else import_pose(source, index, name))
            except ValueError as error:
                errors.append(str(error))
        if len(result) != len(cells):
            continue
        target = ANIMATIONS / clip
        target.mkdir(exist_ok=True)
        for i, frame in enumerate(result):
            frame.save(target / f"{i:02}.png", optimize=True)
    if errors:
        raise ValueError("Rejected source poses:\n" + "\n".join(errors))
    register_clips()


def register_clips() -> None:
    manifest = ANIMATIONS / "clips.json"
    raw = manifest.read_bytes().decode("utf-8")
    current = json.loads(raw)
    names = list(SHEETS)
    existing = {clip["Id"] for clip in current}
    if any(name in existing for name in names) and not all(name in existing for name in names):
        raise ValueError("Partial interaction variant registration; inspect clips.json")
    additions = []
    for name in names:
        if name.startswith("Feed"):
            sequence = [0, 1, 2, 3, 0]
            durations = [170, 260, 320, 620, 260]
        else:
            sequence = DANCE_SEQUENCES[name]
            durations = [250] * len(sequence)
        additions.append({
            "Id": name,
            "Frames": [f"{name}/{i:02}.png" for i in sequence],
            "DurationsMs": durations,
            "PosterFrame": 3 if name.startswith("Feed") else 4,
        })
    if all(name in existing for name in names):
        revisions = {clip["Id"]: clip for clip in additions}
        current = [revisions.get(clip["Id"], clip) for clip in current]
    else:
        current.extend(additions)
    newline = "\r\n" if "\r\n" in raw else "\n"
    updated = json.dumps(current, indent=2, ensure_ascii=False).replace("\n", newline) + newline
    manifest.write_bytes(updated.encode("utf-8"))


if __name__ == "__main__":
    main()

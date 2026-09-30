"""Render a privacy-safe DragonDeskPet promo from the shipped animation clips.

Uses the same PNG frames, per-frame durations, and companion expression timelines as
the Windows app. The backdrop and captions are promotional graphics, not a screen
recording. Requires Pillow and an FFmpeg build with H.264 encoding.
"""

from __future__ import annotations

import argparse
import bisect
import json
import math
import subprocess
import wave
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
ANIMATIONS = ROOT / "assets/character/animations"
COMPANION = ROOT / "assets/character/companion"
WIDTH, HEIGHT, FPS = 1280, 720, 25
PET_X, PET_Y, PET_SIZE = 650, 112, 512
CLOUD_X, CLOUD_Y, CLOUD_SIZE = PET_X + 378, PET_Y + 157, (55, 44)
FONT = Path("C:/Windows/Fonts/msyh.ttc")
FONT_BOLD = Path("C:/Windows/Fonts/msyhbd.ttc")


@dataclass(frozen=True)
class Segment:
    start: float
    end: float
    clip: str | None
    title: str
    detail: str
    tag: str
    chapter: str


SEGMENTS = [
    Segment(0, 3, None, "桌面的一角", "住进一只会回应你的龙娘", "DragonDeskPet", "开场"),
    Segment(3, 3.97, "Greet", "打招呼", "轻点她，挥手回应", "直接互动", "轻轻互动"),
    Segment(3.97, 5.23, "Pet", "摸摸头", "闭眼、低头，接住你的关心", "直接互动", "轻轻互动"),
    Segment(5.23, 6.49, "Cuddle", "贴贴", "长按 550 毫秒，抱抱你", "直接互动", "轻轻互动"),
    Segment(6.49, 8, None, "不打开菜单", "也能和她玩一会儿", "轻轻互动", "轻轻互动"),
    Segment(8, 9.63, "FeedCookie", "饼干", "好奇地接过第一口", "五种点心", "点心时间"),
    Segment(9.63, 11.26, "FeedStrawberry", "草莓", "眨眼，然后开心起来", "五种点心", "点心时间"),
    Segment(11.26, 12.89, "FeedCake", "蛋糕", "先笑，再细细品尝", "五种点心", "点心时间"),
    Segment(12.89, 14.52, "FeedCandy", "糖果", "等不及的小小期待", "五种点心", "点心时间"),
    Segment(14.52, 16.15, "FeedCottonCandy", "棉花糖", "想一想，再尝一口", "五种点心", "点心时间"),
    Segment(16.15, 18, None, "点心递到嘴边", "每一种，都有不同的回应", "拖放投喂", "点心时间"),
    Segment(18, 26, "DanceStep", "轻快踏步", "小 AI 跟着节奏轻轻上浮三次", "第一支舞", "一起跳舞"),
    Segment(26, 34, "DanceGuofeng", "国风轻舞", "缓缓起手，专注后露出微笑", "第二支舞", "一起跳舞"),
    Segment(34, 42, "DanceWingTail", "翼尾合拍", "翅膀展开时，小 AI 也会回应", "第三支舞", "一起跳舞"),
    Segment(42, 44, "Celebrate", "开心时", "举起双手，翅膀张合一次", "更多动作", "休息一下"),
    Segment(44, 47, "Sleep", "累了就打个盹", "蜷起来，安安静静地睡", "休息一下", "休息一下"),
    Segment(47, 49, "Wake", "叫醒她", "揉揉眼睛，继续陪你", "休息一下", "休息一下"),
    Segment(49, 54, None, "DragonDeskPet", "让桌面，多一点陪伴", "开发体验版 .13", "片尾"),
]


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONT_BOLD if bold else FONT), size)


def gradient_background() -> Image.Image:
    y = np.linspace(0, 1, HEIGHT, dtype=np.float32)[:, None]
    x = np.linspace(0, 1, WIDTH, dtype=np.float32)[None, :]
    glow = np.exp(-(((x - .77) / .40) ** 2 + ((y - .48) / .56) ** 2) * 2.0)
    color = np.empty((HEIGHT, WIDTH, 3), dtype=np.uint8)
    for channel, base, bottom, shine in [(0, 245, -8, 7), (1, 241, -10, 4), (2, 252, -2, 0)]:
        color[:, :, channel] = np.clip(base + bottom * y + shine * glow, 0, 255)
    image = Image.fromarray(color, "RGB").convert("RGBA")
    d = ImageDraw.Draw(image, "RGBA")
    d.ellipse((665, 95, 1245, 675), fill=(218, 203, 247, 54))
    d.ellipse((735, 165, 1175, 605), outline=(167, 139, 214, 58), width=2)
    d.rounded_rectangle((36, 32, 1244, 676), radius=26, fill=(255, 255, 255, 88),
                        outline=(206, 190, 230, 125), width=2)
    d.line((36, 640, 1244, 640), fill=(194, 174, 224, 75), width=2)
    d.text((76, 654), "DRAGONDESKPET  /  动作功能展示", font=font(14), fill=(108, 88, 137, 180))
    d.text((1050, 654), "开发体验版 .13", font=font(14), fill=(108, 88, 137, 180))
    return image


def section_base(background: Image.Image, segment: Segment) -> Image.Image:
    canvas = background.copy()
    d = ImageDraw.Draw(canvas, "RGBA")
    d.rounded_rectangle((74, 98, 238, 137), radius=18, fill=(131, 99, 178, 230))
    d.text((94, 105), segment.tag, font=font(17, True), fill=(255, 255, 255, 255))
    d.text((74, 190), segment.title, font=font(58, True), fill=(69, 47, 103, 255),
           stroke_width=0)
    d.text((77, 274), segment.detail, font=font(25), fill=(99, 77, 130, 255))
    d.rounded_rectangle((75, 529, 385, 581), radius=20, fill=(233, 222, 249, 218))
    d.text((97, 541), segment.chapter, font=font(21, True), fill=(115, 83, 155, 255))
    d.rounded_rectangle((95, 602, 597, 608), radius=3, fill=(216, 203, 235, 180))
    return canvas


def face_at(clip_id: str | None, elapsed: float) -> tuple[str, float]:
    if clip_id is None:
        return "normal", 0
    ms = elapsed * 1000
    snacks = {
        "FeedCookie": ([(0, "curious"), (550, "happy")], [(0, 0), (550, -2), (1200, 0)]),
        "FeedStrawberry": ([(0, "curious"), (450, "blink"), (700, "happy")], [(0, 0), (450, 0), (850, -3), (1350, 0)]),
        "FeedCake": ([(0, "happy"), (900, "blink"), (1100, "happy")], [(0, 0), (280, -2), (700, -2), (1500, 0)]),
        "FeedCandy": ([(0, "curious"), (350, "happy")], [(0, 0), (850, -2), (1250, 0)]),
        "FeedCottonCandy": ([(0, "thinking"), (600, "blink"), (950, "happy")], [(0, 0), (600, 0), (1100, -2), (1500, 0)]),
    }
    dances = {
        "DanceStep": ([(0, "happy"), (4000, "blink"), (4200, "happy")], [(0, 0), (800, -2), (1200, 0), (3000, -2), (3400, 0), (5400, -2), (5800, 0)]),
        "DanceGuofeng": ([(0, "thinking"), (4000, "happy")], [(0, 0), (1200, 0), (3000, -2), (5200, -2), (7400, 0)]),
        "DanceWingTail": ([(0, "curious"), (5860, "happy")], [(0, 0), (5600, 0), (6200, -3), (6800, 0)]),
    }
    if clip_id in snacks or clip_id in dances:
        faces, motion = (snacks | dances)[clip_id]
    else:
        face = {"Pet": "blink", "Wake": "curious"}.get(clip_id, "happy")
        if clip_id == "Sleep":
            return "hidden", 0
        return face, 0
    face = faces[bisect.bisect_right([time for time, _ in faces], ms) - 1][1]
    k = bisect.bisect_right([time for time, _ in motion], ms)
    if k == 0:
        return face, motion[0][1]
    if k == len(motion):
        return face, motion[-1][1]
    t0, y0 = motion[k - 1]
    t1, y1 = motion[k]
    fraction = (ms - t0) / (t1 - t0)
    fraction = (1 - math.cos(math.pi * fraction)) / 2
    return face, y0 + (y1 - y0) * fraction


def music(path: Path, seconds: float = 54) -> None:
    sample_rate = 44100
    n = round(seconds * sample_rate)
    audio = np.zeros(n, dtype=np.float32)
    # An original, intentionally simple plucked motif; no external track or samples.
    bpm = 112
    beat = 60 / bpm
    notes = [
        (0, 0), (2, 7), (4, 12), (5, 7), (7, 4), (9, 7), (11, 2), (12, 7),
        (14, 0), (16, 4), (18, 7), (19, 11), (21, 7), (23, 4), (25, 2), (27, 7),
    ]
    scale = 220 * np.power(2, np.arange(13) / 12)
    for cycle in range(7):
        for step, pitch in notes:
            start = int((cycle * 28 + step) * beat * sample_rate / 2)
            length = min(round(.44 * sample_rate), n - start)
            if length <= 0:
                continue
            t = np.arange(length, dtype=np.float32) / sample_rate
            freq = float(scale[pitch])
            envelope = np.exp(-7.5 * t) * np.minimum(1, t * 190)
            sound = (np.sin(2 * math.pi * freq * t) + .24 * np.sin(4 * math.pi * freq * t))
            audio[start:start + length] += .10 * sound * envelope
    for step in range(int(seconds / beat)):
        start = round(step * beat * sample_rate)
        length = min(round(.055 * sample_rate), n - start)
        if length <= 0:
            continue
        t = np.arange(length, dtype=np.float32) / sample_rate
        tick = np.sin(2 * math.pi * (880 if step % 2 else 440) * t) * np.exp(-65 * t)
        audio[start:start + length] += (.020 if step % 2 else .035) * tick
    audio *= .50 / max(float(np.max(np.abs(audio))), 1e-6)
    fade = min(round(.8 * sample_rate), n // 2)
    audio[:fade] *= np.linspace(0, 1, fade)
    audio[-fade:] *= np.linspace(1, 0, fade)
    audio = np.clip(audio, -.8, .8)
    stereo = np.repeat((audio * 32767).astype("<i2")[:, None], 2, axis=1)
    with wave.open(str(path), "wb") as output:
        output.setnchannels(2)
        output.setsampwidth(2)
        output.setframerate(sample_rate)
        output.writeframes(stereo.tobytes())


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--ffmpeg", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--stills-only", action="store_true")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    clips = {clip["Id"]: clip for clip in json.loads((ANIMATIONS / "clips.json").read_text(encoding="utf-8"))}
    sprites: dict[str, Image.Image] = {}
    for clip in clips.values():
        for name in clip["Frames"]:
            if name not in sprites:
                sprites[name] = Image.open(ANIMATIONS / name).convert("RGBA")
    clouds = {}
    for face in ("normal", "blink", "curious", "happy", "thinking", "angry"):
        source = Image.open(COMPANION / f"{face}.png").convert("RGBA")
        clouds[face] = source.crop((365, 435, 920, 872)).resize(CLOUD_SIZE, Image.Resampling.LANCZOS)

    background = gradient_background()
    bases = [section_base(background, segment) for segment in SEGMENTS]
    boundaries = [segment.end for segment in SEGMENTS]
    def frame_at(time: float) -> Image.Image:
        index = bisect.bisect_right(boundaries, time)
        index = min(index, len(SEGMENTS) - 1)
        segment = SEGMENTS[index]
        local = max(0, time - segment.start)
        canvas = bases[index].copy()
        if segment.clip is None:
            sprite = sprites["Blink/00.png"]
        else:
            clip = clips[segment.clip]
            ticks = np.cumsum(clip["DurationsMs"])
            frame_index = min(bisect.bisect_right(ticks, local * 1000), len(ticks) - 1)
            sprite = sprites[clip["Frames"][frame_index]]
        canvas.alpha_composite(sprite, (PET_X, PET_Y))
        face, lift = face_at(segment.clip, local)
        if face != "hidden" and not (segment.clip == "Wake" and local < .2):
            canvas.alpha_composite(clouds[face], (CLOUD_X, CLOUD_Y + round(lift * 3)))
        d = ImageDraw.Draw(canvas, "RGBA")
        progress = min(1.0, local / (segment.end - segment.start))
        d.rounded_rectangle((95, 602, 95 + round(502 * progress), 608), radius=3,
                            fill=(137, 104, 185, 235))
        if time >= 49:
            d.text((77, 341), "小小的陪伴，时时都在。", font=font(27), fill=(111, 86, 146, 255))
        return canvas.convert("RGB")

    still_times = {"opening": 1.0, "snack": 10.0, "dance-step": 21.0,
                   "dance-guofeng": 30.0, "dance-wing": 39.9, "closing": 51.0}
    for name, at in still_times.items():
        frame_at(at).save(args.output / f"still-{name}.png", optimize=True)
    cover = background.copy()
    cover.alpha_composite(sprites["DanceWingTail/05.png"], (PET_X, PET_Y))
    cover.alpha_composite(clouds["happy"], (CLOUD_X, CLOUD_Y))
    cd = ImageDraw.Draw(cover)
    cd.rounded_rectangle((75, 93, 265, 138), radius=21, fill=(128, 96, 178))
    cd.text((94, 101), "DragonDeskPet", font=font(19, True), fill="white")
    cd.text((74, 196), "会跳舞的", font=font(64, True), fill=(69, 47, 103))
    cd.text((74, 278), "龙娘桌宠", font=font(64, True), fill=(69, 47, 103))
    cd.text((78, 395), "点心互动  ·  三支舞  ·  小 AI 陪伴", font=font(24), fill=(103, 77, 136))
    cd.rounded_rectangle((75, 528, 315, 580), radius=20, fill=(233, 222, 249))
    cd.text((95, 539), "互动功能展示", font=font(21, True), fill=(115, 83, 155))
    cover = cover.convert("RGB")
    cover.save(args.output / "cover-16x9.png", optimize=True)
    cover_4x3 = Image.new("RGBA", (1280, 960), (242, 236, 250))
    d4 = ImageDraw.Draw(cover_4x3)
    d4.rounded_rectangle((36, 32, 1244, 928), radius=28, fill="white",
                         outline=(205, 187, 232), width=2)
    cover_4x3.alpha_composite(sprites["DanceWingTail/05.png"], (650, 252))
    cover_4x3.alpha_composite(clouds["happy"], (1028, 409))
    d4 = ImageDraw.Draw(cover_4x3)
    d4.rounded_rectangle((75, 152, 265, 197), radius=21, fill=(128, 96, 178))
    d4.text((94, 160), "DragonDeskPet", font=font(19, True), fill="white")
    d4.text((74, 290), "会跳舞的", font=font(64, True), fill=(69, 47, 103))
    d4.text((74, 372), "龙娘桌宠", font=font(64, True), fill=(69, 47, 103))
    d4.text((78, 489), "点心互动  ·  三支舞", font=font(25), fill=(103, 77, 136))
    d4.text((78, 532), "小 AI 也会陪你回应", font=font(25), fill=(103, 77, 136))
    d4.text((75, 866), "互动功能展示  /  开发体验版 .13", font=font(17), fill=(110, 84, 146))
    cover_4x3 = cover_4x3.convert("RGB")
    cover_4x3.save(args.output / "cover-4x3.png", optimize=True)
    if args.stills_only:
        return

    wav = args.output / "original-music.wav"
    music(wav)
    silent = args.output / "promo-silent.mp4"
    command = [args.ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo",
               "-pix_fmt", "rgb24", "-s", f"{WIDTH}x{HEIGHT}", "-r", str(FPS), "-i", "-",
               "-c:v", "h264_nvenc", "-preset", "p4", "-cq", "22", "-pix_fmt", "yuv420p",
               "-movflags", "+faststart", str(silent)]
    process = subprocess.Popen(command, stdin=subprocess.PIPE)
    try:
        for number in range(54 * FPS):
            process.stdin.write(frame_at(number / FPS).tobytes())
            if number % (5 * FPS) == 0:
                print(f"Rendered {number // FPS}/54 seconds", flush=True)
    finally:
        process.stdin.close()
    if process.wait() != 0:
        raise RuntimeError("FFmpeg could not encode the video")
    finished = args.output / "DragonDeskPet-Bilibili-promo-v1.mp4"
    subprocess.run([args.ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", str(silent),
                    "-i", str(wav), "-c:v", "copy", "-c:a", "aac", "-b:a", "160k",
                    "-shortest", "-movflags", "+faststart", str(finished)], check=True)
    print(finished, flush=True)


if __name__ == "__main__":
    main()

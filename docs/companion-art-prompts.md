# 小 AI 独立表情素材提示词

2026-09-29 使用内置 imagegen 制作；输出在 `assets/character/companion/`。原 `assets/character/*.png` 仅作身份和表情参考，未覆盖。六张素材均要求真实透明背景、同一云朵位置与大小；运行时统一显示画布的 `(365, 435, 555, 437)` 区域。

## normal.png

参考图：`assets/character/default.png`。

```text
Use case: background-extraction. Asset type: a transparent PNG sprite for the tiny cloud companion in the DragonDeskPet desktop pet. Input image 1 is the identity and style reference; extract only the small blue-lilac cloud character beside the dragon girl's head. Preserve its exact rounded scalloped outline, thin dark indigo outline, pale blue-to-lilac shading, and two oval purple-blue open eyes. Keep the face calm and neutral. Remove the dragon girl, hair, decorative stem/trail, sparkles, text, shadows, and every other object. One complete cloud centered on a square transparent canvas, with generous empty transparent margin, no cut edges. High consistency with the original hand-drawn anime watercolor art; do not redesign or add limbs.
```

## blink.png

编辑目标：`normal.png`。

```text
Use case: precise-object-edit. Asset type: one transparent PNG expression sprite for DragonDeskPet's tiny cloud companion. Edit target: Image 1, the approved neutral cloud. Change only the eyes: both oval eyes become softly closed little curved eyelids, like a brief blink. Preserve the cloud's exact scalloped silhouette, position, scale, blue-lilac watercolor shading, indigo outline, canvas size, and transparent background pixel-for-pixel as closely as possible. No body movement, smile, arms, stem, trail, sparkles, text, shadow, or other objects. Keep the cloud in exactly the same place so frames can alternate without jumping.
```

## curious.png

编辑目标：`normal.png`；表情参考：`assets/character/hover.png`。

```text
Use case: precise-object-edit. Asset type: a transparent PNG expression sprite for DragonDeskPet's tiny cloud companion. Image 1 is the EDIT TARGET and must define the full cloud silhouette, position, size, canvas, watercolor blue-lilac shading and indigo outline. Image 2 is ONLY a reference for the original cloud's curious eye expression. Change only Image 1's two eyes to a clearly curious, alert expression: slightly larger, rounder eyes looking toward the dragon girl, with subtle raised brows. Preserve the same simple cloud and two-eye face, exact footprint and transparency. Remove all remnants of the dragon girl, decorative heart, stem/trail, sparkles, text, shadow, and other objects. No extra limbs or tilted whole body. The two expression sprites must alternate without jumping.
```

## happy.png

编辑目标：`normal.png`；表情参考：`assets/character/happy.png`。

```text
Use case: precise-object-edit. Asset type: a transparent PNG expression sprite for DragonDeskPet's tiny cloud companion. Image 1 is the EDIT TARGET and defines the exact cloud silhouette, position, size, canvas, blue-lilac watercolor shading and indigo outline. Image 2 is ONLY a reference for the original cloud's happy face. Change only Image 1's expression: two upward curved smiling eyes and tiny soft pink cheek blush. Preserve the full scalloped outline and footprint exactly so this sprite can alternate with Image 1 without jumping. One cloud only, actual transparent background. No dragon girl, hair, stem/trail, sparkles, text, shadow, extra limbs, or other objects.
```

## thinking.png

编辑目标：`normal.png`；表情参考：`assets/character/thinking.png`。

```text
Use case: precise-object-edit. Asset type: one transparent PNG expression sprite for DragonDeskPet's tiny cloud companion. Image 1 is the EDIT TARGET and fixes the exact cloud outline, position, size, canvas, blue-lilac watercolor shading and indigo outline. Image 2 is ONLY an expression reference. Change only Image 1's eyes to a thoughtful look: pupils glancing slightly upward toward one side, with a tiny subtle questioning eyebrow. Keep the simple cloud with exactly two eyes, and its silhouette and footprint matching Image 1 so frames do not jump. No dragon girl, hair, thought bubbles, stem/trail, sparkles, text, shadow, limbs or extra objects. Actual transparent background.
```

## angry.png

编辑目标：`normal.png`；表情参考：`assets/character/angry.png`。

```text
Use case: precise-object-edit. Asset type: one transparent PNG expression sprite for DragonDeskPet's tiny cloud companion. Image 1 is the EDIT TARGET and fixes the exact cloud outline, position, size, canvas, blue-lilac watercolor shading and indigo outline. Image 2 is ONLY an expression reference. Change only Image 1's eyes to a cute mildly grumpy look, with two short downward-angled eyebrows. Keep the same simple cloud and two-eye face, identical silhouette and footprint so frames do not jump. No dragon girl, hair, red anger mark, steam puff, stem/trail, sparkles, text, shadow, limbs or extra objects. Actual transparent background.
```

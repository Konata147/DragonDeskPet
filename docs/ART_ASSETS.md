# Character art assets

## `assets/character/default.png`

- Purpose: V0.1 neutral idle sprite.
- Format: 1241 × 1268 PNG with a genuine alpha channel.
- Generation: built-in image generation, using all three project reference images.
- Status: approved V0.1 production sprite.

### Tail canon

- The tail emerges behind the hip on the viewer-right side and stays close to the body silhouette.
- It has a strong root, continuous taper, pale-lilac overlapping diamond scales, and a narrow jagged mane along only one outside edge.
- The tip is narrow and open-hooked; it must not become a short circular coil, fluffy fox tail, blunt end, disconnected segment, or second tip.
- In `sleeping.png`, one continuous tail curves into the arms and its only true tip rests beside the cheek.

### Generation prompt

```text
Use case: stylized-concept
Asset type: production-ready transparent desktop-pet character sprite, single neutral idle pose
Primary request: create a polished chibi version of the dragon girl from Image 1 for use as a small Windows desktop pet.
Input images: Image 1 is the authoritative character identity and costume reference; Image 2 is the chibi proportions, rendering, and desktop-sprite style reference; Image 3 is only the target small desktop-pet scale and compact silhouette reference.
Scene/backdrop: genuinely transparent background with clean alpha, no floor, no shadow rectangle.
Subject: one complete full-body white-and-pale-lilac dragon girl, front-facing in a relaxed neutral standing idle pose. Preserve long white/lilac hair, purple eyes, two curved dragon horns, both small batlike wings, one clearly readable scaled dragon tail, and the layered white/lilac fantasy dress. Preserve the small blue AI spirit as a tiny floating companion near one shoulder only if it does not harm the character silhouette.
Style/medium: polished 2D anime game sprite, cute compact chibi, approximately 2.5 heads tall, clean outlines, soft cel shading, readable at 140 pixels tall.
Composition/framing: centered, generous transparent padding, entire horns, hair, wings, hands, dress, feet, and tail visible; no cropping; symmetric stable silhouette.
Color palette: white, pearl, pale lilac, soft violet, tiny cyan-blue accent.
Constraints: keep the core design from Image 1; make it suitable as the V0.1 idle sprite; actual transparency; one character only; no text; no logo; no watermark; no frame; no sprite sheet; no extra poses.
Avoid: realistic adult proportions, maid outfit from Image 3, dark background, scenery, cropped horns or tail, oversized wings, visual clutter, blur, pixelated edges.
```

## State sprites

All state sprites use `default.png` as the identity-preserving edit target and `references/character-original.jpg` as the anatomical reference. They keep the same 1241 × 1268 transparent canvas. Runtime state files are:

| State | File | Visual intent |
| --- | --- | --- |
| Idle | `default.png` | Relaxed neutral standing pose |
| Hover | `hover.png` | Curious lean toward the user |
| Dragged | `dragged.png` | Lifted pose with naturally hanging limbs and tail |
| Thinking | `thinking.png` | Finger near chin, attentive spirit |
| Happy | `happy.png` | Closed-eye smile and small celebration |
| Angry | `angry.png` | Cute puffed cheeks and crossed arms |
| Sleeping | `sleeping.png` | Curled sleep while hugging the real tail tip |

Missing or damaged non-idle state art falls back independently to `default.png`.

### `hover.png`

```text
Use case: identity-preserve
Asset type: transparent desktop-pet state sprite — Hover
Primary request: preserve the approved character and lean slightly forward with a curious expression, looking toward the user.
Constraints: preserve identity, costume, wings, close-to-body scaled tail anatomy, canvas size, transparency, and clean edges; no text or watermark.
```

### `dragged.png`

```text
Use case: identity-preserve
Asset type: transparent desktop-pet state sprite — Dragged
Primary request: preserve the approved character in a gently lifted pose with relaxed hanging arms and legs; no visible hand, mouse, or grabbing object.
Constraints: keep the tail continuous, close to the viewer-right side, and naturally affected by gravity; preserve identity, canvas size, transparency, and clean edges.
```

### `thinking.png`

```text
Use case: identity-preserve
Asset type: transparent desktop-pet state sprite — Thinking
Primary request: create the Thinking state of the exact approved chibi dragon girl in Image 1.
Change only: pose and facial expression. She tilts her head slightly, raises one finger gently to her chin, looks upward in careful thought, and the tiny blue AI spirit floats attentively beside her with three small soft glowing dots.
Preserve exactly: face design, chibi proportions, white/lilac hair shape, purple eyes, both curved horns, pointed ears, both wings, scaled tail, dress design and ornament placement, shoes, palette, line style, rendering quality, and overall character identity.
Scene/backdrop: genuinely transparent background with clean alpha.
Composition: one complete full-body character, centered, same apparent scale and padding as the source; no cropping.
Constraints: polished 2D anime game sprite; readable at 140px; no text, watermark, frame, or other character.
```

### `happy.png`

```text
Use case: identity-preserve
Asset type: transparent desktop-pet state sprite — Happy
Primary request: create the Happy state of the exact approved chibi dragon girl in Image 1.
Change only: pose and facial expression. She has a bright gentle closed-eye smile, slightly rosy cheeks, both hands lifted in a small delighted celebratory gesture, wings raised a little, and the tiny blue AI spirit bounces happily nearby with a few small sparkles.
Preserve exactly: face design, chibi proportions, white/lilac hair shape, both curved horns, pointed ears, both wings, scaled tail, dress design and ornament placement, shoes, palette, line style, rendering quality, and overall character identity.
Scene/backdrop: genuinely transparent background with clean alpha.
Composition: one complete full-body character, centered, same apparent scale and padding as the source; no cropping.
Constraints: polished 2D anime game sprite; readable at 140px; no text, watermark, frame, or other character.
```

### `angry.png`

```text
Use case: identity-preserve
Asset type: transparent desktop-pet state sprite — Angry
Primary request: preserve the approved character with crossed arms, puffed cheeks, and a cute annoyed expression rather than aggressive anger.
Constraints: keep the tail slightly tense but close to the body; preserve identity, costume, canvas size, transparency, and clean edges; no text or watermark.
```

### `sleeping.png`

```text
Use case: identity-preserve
Asset type: transparent desktop-pet state sprite — Sleeping
Primary request: create the Sleeping state of the exact approved chibi dragon girl in Image 1.
Change only: sleeping pose and the tail path. She is peacefully asleep in a compact curled pose, eyes gently closed, wings relaxed, and the tiny blue AI spirit sleeps beside her. One continuous tail begins behind the viewer-right hip, stays close to the body, curves into her arms while tapering, and ends with its only pointed tip beside her cheek so she is hugging the real tail tip.
Preserve exactly: face design, chibi proportions, white/lilac hair, both curved horns, pointed ears, both wings, scaled tail, dress design and ornaments, shoes where visible, palette, line style, rendering quality, and overall character identity.
Scene/backdrop: genuinely transparent background with clean alpha.
Composition: one complete character and one continuous tail, centered in a compact silhouette with no cropping.
Constraints: polished 2D anime game sprite; readable at 140px; no second tail tip, disconnected segment, giant tail pillow, letters, words, watermark, frame, furniture, or extra character.
```

# Jarvis brand assets

`jarvis-orb-v1.png` is a transparent, high-resolution glass orb generated with the built-in ImageGen tool. It preserves the existing purple / blue Jarvis identity and is used by `JarvisOrb` throughout the app. The widget decodes it at the required display size, keeps its existing listening rings and reduced-motion behavior, and retains the original procedural orb as a decode fallback.

Use the shared `JarvisOrb` widget rather than putting the image directly into navigation or avatars. UI symbols remain Cupertino and Phosphor font vectors; generated bitmap icon sets would lose their consistency and sharpness at small sizes.

## Generation prompt

Orb:

Use case: stylized-concept. Asset type: production transparent Jarvis assistant orb for an existing premium iOS app, shown at 32–80 pixels as an assistant avatar and navigation action. Primary request: a single exquisitely crafted iridescent glass sphere, preserving Jarvis's violet and icy blue identity. Subject: perfectly round three-dimensional orb with continuous curved glass surfaces, broad flowing lavender-violet bands, subtle icy-blue refraction, pearl highlights, luminous depth and a deep purple core. Material: polished optical glass, substantial and calm, with elegant broad reflections rather than tiny busy details. Composition: one orb perfectly centered in a square canvas, occupying 94% of the width and height; clean silhouette, visible edge, no cropping, no attached objects, minimal transparent margin. Lighting: soft studio lighting from upper left, restrained highlight, sophisticated gentle gradients, high legibility on both light and near-black interfaces. Colors: dominant violet #8f86ff and periwinkle, restrained icy blue #9ccdf2, soft lavender/pearl; no rainbow red green yellow. Background: genuinely transparent alpha outside the sphere; no cast shadow outside its silhouette, no glow beyond the silhouette, no backdrop. Constraints: no text, no letters, no logo glyph, no sparkles, no star field, no orbit rings, no wires, no pedestal, no mockup, no interface, no checkerboard baked into the image. Premium minimalist product render, highly polished, legible when reduced to tiny size.

## App-icon concept

`jarvis-app-icon-v1.png` is the matching opaque master, generated from the orb reference with the built-in ImageGen tool. It is supplied for app-icon export and review; native launcher catalogs continue to use their existing assets. Only the transparent orb is bundled at runtime.

Prompt:

Use case: logo-brand. Asset type: opaque square app-icon master for Jarvis, an existing personal assistant iOS app. The attached image is our new Jarvis orb reference. Create the matching premium app-icon asset: preserve this exact round glass sphere's proportions, broad violet/lavender and icy-blue flowing refractions, pearl highlights and deep purple core. Reduce and place this single orb perfectly centered, occupying 72% of the canvas width. Opaque full-bleed solid near-black #0f0f14 background, with only the faintest purple contact glow behind the orb. Calm luxury technology aesthetic. Icon must stay very readable when small. Canvas: square 1024 x 1024; no outer rounded corners (the OS adds those), no inset border, no frame, no text, no letters, no added glyphs, no orbit rings, no stars, no marketing scene, no UI mockup. Preserve Jarvis's established purple / blue identity. Deliver only the clean finished square app-icon artwork.

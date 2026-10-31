# Design guidelines

Rules for agents designing or changing a webspine theme. They apply to plain
HTML/CSS/JS in `site/themes/<id>/`; no framework or build step is assumed.

## Decide before styling

Agree with the user on purpose, audience, tone, and one memorable idea (an
image, a typeface, a layout move). Record the result as design tokens in the
theme's CSS (`:root` custom properties for color, type, space, radius, motion)
with a short comment stating the direction. Components use tokens, never raw
values. Build with realistic copy from `site/content/`, never lorem ipsum.

## Typography

- Body text at least 16px; nothing under 12px. Prose 65–75ch wide.
- Body line-height around 1.5–1.7 (unitless); slightly more for light-on-dark.
- One or two families, a fixed role scale (e.g. 14/16/18/24/32/48), and a clear
  size or weight step between headings and body. Headings use sequential levels.
- Load only used weights as `woff2` theme assets with `font-display: swap`.
- `text-wrap: balance` for headings, `pretty` for paragraphs. No justified
  body text, no long all-caps passages, no widened tracking on body text.
- Text must reflow at 200% zoom and never clip in fixed-height boxes.

## Color and contrast

- Text and placeholders at least 4.5:1; large text, icons, controls and focus
  rings at least 3:1. Check hover, disabled, error states and text on images.
- Never signal meaning by color alone; pair it with text, icon, or shape.
- Spend the strongest color on the primary action, not on decoration.
- Secondary text derives from the surface hue; plain gray on color looks washed out.
- A dark theme is designed on its own (surfaces, elevation), not inverted.

## Space and layout

- Spacing scale on a 4px base; use `gap` for sibling rhythm.
- Group by proximity: related items close, groups farther apart; more space
  above a heading than below. Equal gaps everywhere flatten the hierarchy.
- Text never touches container or viewport edges.
- Elevation by border or shadow, not both. Small z-index scale (10/20/30/50).
- Reserve space for images and async content (`aspect-ratio`, sizes) to avoid
  layout shift. Squint test: the primary element still reads first.

## Responsive

- Mobile-first base styles plus `min-width` queries at content-driven points.
- Check 375, 768, 1024 and 1440px wide; no horizontal scroll at any width.
- Keep `width=device-width, initial-scale=1`; never disable zoom.
- Use `clamp()` for fluid type and spacing, `dvh` instead of `100vh`.
- Nothing is reachable only by hover. Wide tables scroll inside a wrapper.

### Mobile navigation

On narrow screens, collapse the main navigation into a flat, modern hamburger
menu: a light icon that morphs into a close icon, no heavy borders, shadows or
boxed buttons.

- Keep the mobile header to two elements: logo and menu button. Group any
  extra actions (search, language, account, contact icons) inside the menu
  instead of adding more header icons.
- Use a `<button>` with an accessible name, `aria-expanded` and
  `aria-controls`; touch target at least 44×44px.
- Animate fluently with `transform` and `opacity` only: icon morph and panel
  slide or fade in about 200–300ms, closing slightly faster, ease-out.
- Close on Escape, link selection and a second tap; return focus to the button.
  Keep keyboard order logical and the page from scrolling behind an open overlay.
- Under `prefers-reduced-motion`, switch instantly or with a short fade.
- Without JavaScript the navigation stays reachable (visible links or a
  `<details>` fallback).

## Interaction and accessibility

- Touch targets about 44×44px (24px minimum) with at least 8px between them.
- Every interactive element has a visible `:focus-visible` style; never remove
  outlines without a replacement. Keyboard order follows visual order.
- Native elements first: links navigate, buttons act. Icon-only buttons need
  an accessible name. Keep the skip link and landmarks of the shared layout.
- Forms: visible persistent labels (placeholders are examples), correct input
  `type` and `autocomplete`, per-field errors linked with `aria-describedby`,
  a disabled submit while sending, and a clear success or error result.

## Motion

- Motion explains a change or brings illustrations and elements to life.
- Feedback 100–150ms, state changes 150–300ms, larger transitions 300–500ms.
  Exits faster than entrances; ease out on arrival; no bounce or elastic easing.
- Animate only `transform` and `opacity`.
- Honor `prefers-reduced-motion`: remove movement, keep meaningful fades.
- Content is visible by default, so a failed script never hides the page.

## Content and writing

- Buttons name a verb and object ("Send message"), not "Submit" or "OK".
- Errors say what failed and how to recover; no internal codes.
- Say each idea once; cut filler like "world-class" or "supercharge".
- Write complete sentences so text can be translated and can grow.

## Images, icons, performance

- Icons are SVG from one set with one stroke weight; never emoji as icons.
- Images: descriptive `alt` (or `alt=""` if decorative), width and height or
  `aspect-ratio`, `loading="lazy"` below the fold, WebP/AVIF at the size shown.
- Load scripts with `defer`; avoid third-party scripts unless the user needs them.

## Avoid generic AI patterns

Do not default to: Inter or system fonts as the whole identity; purple-blue or
neon gradients, gradient text and glow halos; cards inside cards and identical
icon-heading-text grids; a rounded icon tile above every heading; eyebrow
labels and numbered section markers by habit; thick colored side borders on
cards; decorative glassmorphism or blur; pill-shaped everything; cream page with
an italic serif headline as a default; pulsing dots and marquees. Use any of
these only when the brand direction calls for it.

## Check before handing over

Preview the changed pages at mobile and desktop widths, by keyboard only, with
reduced motion enabled, and at 200% zoom. Confirm contrast of new color pairs.

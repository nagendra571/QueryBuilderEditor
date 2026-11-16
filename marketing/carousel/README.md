# QueryBuilder Hub: LinkedIn carousel and videos

Marketing material for the public demo (`demo/README.md`). Every screenshot is a real capture of the live demo with its
made-up data; every claim comes from the product docs.

## What is here

| File | What it is |
|---|---|
| `out/QueryBuilder-Hub-carousel.pdf` | The carousel: 14 pages, 1080×1350 (4:5). Upload as a LinkedIn document post. |
| `out/QueryBuilder-Hub-carousel.pptx` | The same 14 slides as full-bleed pictures, with alt text and speaker notes. |
| `out/QueryBuilder-Hub-carousel-v1.pdf` | The first, simpler version (12 slides), kept for comparison. |
| `out/QueryBuilder-Hub-Teaser-15s-NoMusic.mp4` | 15-second teaser video, captions burned in. |
| `out/QueryBuilder-Hub-Final-NoMusic.mp4` | 2-minute walkthrough video, captions burned in. |
| `out/png/` | Each carousel slide as a PNG. |
| `carousel.html` | Source of the carousel (`carousel-v1.html` is the first version). |
| `shots/` | Screenshots captured from the live demo at 2x; each file carries its origin in its metadata. |

## Rebuilding

Scripts use Playwright from a local install (paths are set at the top of each script).

1. `node capture.cjs [file containing "Code: <one-time super admin code>"]`: recaptures `shots/` from the live demo (the code
   is needed only for the console screenshots; see `demo/README.md` for how to make one). Press Reset demo first.
2. `node render.cjs`: renders `carousel.html` to `out/QueryBuilder-Hub-carousel.pdf` and `out/png/`.
3. `node measure.cjs`: checks every slide's content ends above the route strip (a `!` marks an overflow).
4. `node build-pptx.cjs <folder where pptxgenjs is installed>`: builds the PPTX from `out/png/` (`npm install pptxgenjs` in
   any folder first).
5. `node sheet.cjs`: contact sheets for review (`out/sheet-*.png`, not committed).

The design direction (zoning plan plus the route of a request, inside the product's Zoning Map design system) is recorded in
the Impeccable surface brief for `marketing/carousel/carousel.html`.

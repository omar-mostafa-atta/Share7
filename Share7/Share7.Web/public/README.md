# Static assets served at /app/

Files here are copied verbatim into `wwwroot/app/` by the Vite build, so they are reachable at
`/app/<name>` in both dev and production.

## character.png — the site character

**Not committed, because it does not exist yet.** The 3D character render could not be
reconstructed from the reference image, so it has to be dropped in as a real file:

1. Export the character as a PNG with a **transparent background**.
2. Save it here as exactly `character.png`.
3. Rebuild (`npm run build`) or just reload in dev — Vite serves `public/` without a rebuild.

Until then `BrandCharacter` removes itself on load error, so the login screen simply renders
without it rather than showing a broken image.

A tall portrait crop works best — it is rendered at a fixed height beside the sign-in card and
scales by width.

// ===========================================================================
// Brand marks
//
// The badge is redrawn as vector rather than referenced as an image, so it
// stays crisp at every size, follows the CSS tokens, and needs no asset in
// wwwroot. It is a reconstruction of the pin the character wears — worth
// checking against the real artwork before it goes anywhere public.
// ===========================================================================

/**
 * The circular "شارع العلوم" badge.
 *
 * Arabic is set in Cairo (loaded in index.html) with a system-Arabic fallback stack — Inter
 * carries no Arabic glyphs, so without a real Arabic face the browser substitutes something
 * arbitrary and the two lines stop matching the artwork.
 */
export function BrandBadge({ size = 38, title = 'شارع العلوم' }: { size?: number; title?: string }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 100 100"
      role="img"
      aria-label={title}
      style={{ display: 'block', flex: '0 0 auto' }}
    >
      <defs>
        <radialGradient id="s7-badge-face" cx="32%" cy="24%" r="86%">
          <stop offset="0%" stopColor="#6357f0" />
          <stop offset="55%" stopColor="#4b3fd4" />
          <stop offset="100%" stopColor="#33279f" />
        </radialGradient>

        {/* The pin's specular sheen — a soft top-left highlight, not a hard ring. */}
        <linearGradient id="s7-badge-gloss" x1="0" y1="0" x2="0.35" y2="1">
          <stop offset="0%" stopColor="#ffffff" stopOpacity="0.34" />
          <stop offset="60%" stopColor="#ffffff" stopOpacity="0" />
        </linearGradient>
      </defs>

      <circle cx="50" cy="50" r="49" fill="url(#s7-badge-face)" />
      <circle cx="50" cy="50" r="49" fill="url(#s7-badge-gloss)" />

      {/* Crimped rim, as on a pin-back button. */}
      <circle cx="50" cy="50" r="45.5" fill="none" stroke="#ffffff" strokeOpacity="0.16" strokeWidth="1.5" />

      <g fill="#ffc629" fontFamily="'Cairo', 'Segoe UI', Tahoma, sans-serif" fontWeight="700">
        <text x="50" y="47" textAnchor="middle" fontSize="27" direction="rtl">
          شارع
        </text>
        <text x="50" y="76" textAnchor="middle" fontSize="27" direction="rtl">
          العلوم
        </text>
        {/* The registered mark sits high-left on the original. */}
        <text x="24" y="26" textAnchor="middle" fontSize="11" fontWeight="600">
          ®
        </text>
      </g>
    </svg>
  )
}

/**
 * The site character.
 *
 * A 3D render, so it cannot be reconstructed as vector — it has to be a real file. Drop the PNG
 * at `Share7.Web/public/character.png` and it appears; until then `onError` removes the element,
 * so the layout is simply as if it were never there rather than showing a broken-image icon.
 */
export function BrandCharacter({ height = 260 }: { height?: number }) {
  return (
    <img
      src="/app/character.png"
      alt=""
      aria-hidden
      height={height}
      style={{ height, width: 'auto', maxWidth: '100%', objectFit: 'contain', pointerEvents: 'none' }}
      onError={(e) => {
        e.currentTarget.style.display = 'none'
      }}
    />
  )
}

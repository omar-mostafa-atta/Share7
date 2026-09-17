// Remove the build output directory before a build.
//
// `emptyOutDir` is deliberately false in vite.config.ts, because outDir points inside the API's
// wwwroot and Vite deleting a directory it thinks it owns is how the hand-written console next
// door would get destroyed — that console is not in version control.
//
// The cost of that safety is that every build leaves its predecessor's hashed assets behind, and
// this project's deploy never deletes anything from the server either. Left alone, wwwroot/app
// accumulates every asset from every build forever.
//
// So the directory is cleared here instead, explicitly and with a guard: this script refuses to
// remove anything that is not the `app` leaf, so a mistyped path cannot widen into wwwroot.

import { rmSync } from 'node:fs'
import { basename, resolve } from 'node:path'

const target = resolve(import.meta.dirname, '../../Share7/wwwroot/app')

if (basename(target) !== 'app') {
  console.error(`[clean-out] refusing to remove ${target} — expected the "app" directory.`)
  process.exit(1)
}

rmSync(target, { recursive: true, force: true })
console.log(`[clean-out] cleared ${target}`)

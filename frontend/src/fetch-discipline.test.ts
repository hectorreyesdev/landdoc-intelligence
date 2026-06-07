import { expect, it } from 'vitest'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'

// Structural guard (spec 0003 + ADR-0006): exactly ONE module may call fetch — the typed
// API client. Everything else goes through it. This is the anti-gaming check that keeps the
// "single typed client" rule honest as the UI grows.

const SRC = import.meta.dirname

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name)
    if (statSync(path).isDirectory()) {
      return sourceFiles(path)
    }
    const isTs = /\.(ts|tsx)$/.test(name)
    const isTest = /\.test\.(ts|tsx)$/.test(name)
    const isDecl = name.endsWith('.d.ts')
    return isTs && !isTest && !isDecl ? [path] : []
  })
}

it('only the typed API client calls fetch()', () => {
  const callers = sourceFiles(SRC)
    .filter((path) => /\bfetch\s*\(/.test(readFileSync(path, 'utf8')))
    .map((path) => relative(SRC, path).split(sep).join('/'))

  expect(callers).toEqual(['api/client.ts'])
})

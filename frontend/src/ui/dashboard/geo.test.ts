import { expect, it } from 'vitest'
import { resolveMarkers, type CentroidIndex } from './geo'
import type { StateCountyCount } from './metrics'

const index: CentroidIndex = new Map([
  ['texas|reeves', [-103.5, 31.3]],
  ['new mexico|lea', [-103.4, 32.7]],
])

it('joins (state, county) counts to centroids, case-insensitively', () => {
  const locations: readonly StateCountyCount[] = [{ state: 'Texas', county: 'Reeves', count: 3 }]
  expect(resolveMarkers(locations, index)).toEqual([
    { state: 'Texas', county: 'Reeves', count: 3, lon: -103.5, lat: 31.3 },
  ])
})

it('matches a "<County> County"-suffixed value against the bare us-atlas name', () => {
  // The extractor emits "Eddy County" for some docs; us-atlas keys it bare as "eddy".
  const idx: CentroidIndex = new Map([['new mexico|eddy', [-104.3, 32.5]]])
  const locations: readonly StateCountyCount[] = [{ state: 'New Mexico', county: 'Eddy County', count: 2 }]
  expect(resolveMarkers(locations, idx)).toEqual([
    { state: 'New Mexico', county: 'Eddy County', count: 2, lon: -104.3, lat: 32.5 },
  ])
})

it('drops counties the atlas cannot resolve', () => {
  const locations: readonly StateCountyCount[] = [{ state: 'Atlantis', county: 'Nowhere', count: 5 }]
  expect(resolveMarkers(locations, index)).toEqual([])
})

it('sorts markers largest-count-first so big bubbles render beneath small ones', () => {
  const locations: readonly StateCountyCount[] = [
    { state: 'New Mexico', county: 'Lea', count: 1 },
    { state: 'Texas', county: 'Reeves', count: 4 },
  ]
  expect(resolveMarkers(locations, index).map((m) => m.county)).toEqual(['Reeves', 'Lea'])
})

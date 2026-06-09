import { expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { CountyMap } from './CountyMap'

// The map's geo plumbing (projection, topojson) renders nothing testable under jsdom, so we mock it out and
// assert only the two deterministic, user-facing branches: the loading state and the honest empty state. The
// real marker math is unit-tested in geo.test.ts.
vi.mock('us-atlas/states-10m.json', () => ({ default: { objects: { states: {} } } }))
vi.mock('us-atlas/counties-10m.json', () => ({ default: { objects: { counties: {} } } }))
vi.mock('topojson-client', () => ({ feature: () => ({ type: 'FeatureCollection', features: [] }) }))
vi.mock('d3-geo', () => ({
  geoAlbersUsa: () => {
    const projection = (() => null) as unknown as Record<string, unknown>
    projection.fitSize = () => projection
    return projection
  },
  geoPath: () => () => '',
}))
vi.mock('./geo', () => ({
  buildCentroidIndex: () => new Map(),
  resolveMarkers: () => [],
}))

it('shows a loading state before the atlas resolves', () => {
  render(<CountyMap locations={[]} onOpenDocument={() => {}} />)
  expect(screen.getByText(/loading map/i)).toBeInTheDocument()
})

it('shows an honest empty state when no county/state fields resolve', async () => {
  render(<CountyMap locations={[]} onOpenDocument={() => {}} />)
  await waitFor(() => expect(screen.getByText(/no mappable locations yet/i)).toBeInTheDocument())
})

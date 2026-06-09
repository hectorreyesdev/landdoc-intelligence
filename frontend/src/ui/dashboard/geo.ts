import { geoCentroid } from 'd3-geo'
import { feature } from 'topojson-client'
import type { Topology } from 'topojson-specification'
import type { FeatureCollection } from 'geojson'
import type { StateCountyCount } from './metrics'

// Geographic plumbing for the county bubble map. The pure pieces (`resolveMarkers`) are unit-tested with a
// stub index; `buildCentroidIndex` is runtime glue over the us-atlas TopoJSON (d3-geo projection), untested
// under jsdom — the same rationale spec 0007 applies to Recharts SVG.

/** [lon, lat] — geographic coordinates, before projection to screen space. */
export type Centroid = readonly [number, number]

/** county centroids keyed by `"state|county"` (lowercased), e.g. `"texas|reeves"`. */
export type CentroidIndex = ReadonlyMap<string, Centroid>

export interface CountyMarker {
  readonly state: string
  readonly county: string
  readonly count: number
  readonly lon: number
  readonly lat: number
}

// The extractor emits county values inconsistently — some bare ("Reeves"), some suffixed ("Eddy County") —
// while us-atlas names are always bare. Strip a trailing administrative suffix from both sides so they join.
const COUNTY_SUFFIX_RE = /\s+(county|parish|borough|census area|municipality)$/i

function indexKey(state: string, county: string): string {
  const normalizedCounty = county.toLowerCase().trim().replace(COUNTY_SUFFIX_RE, '').trim()
  return `${state.toLowerCase().trim()}|${normalizedCounty}`
}

/**
 * Joins aggregated (state, county) counts to their centroids. Pure: counties absent from `index` are dropped
 * gracefully (a corpus county the atlas can't resolve simply isn't plotted). Sorted largest-count-first so
 * bigger bubbles render under smaller ones.
 */
export function resolveMarkers(
  locations: readonly StateCountyCount[],
  index: CentroidIndex,
): readonly CountyMarker[] {
  const markers: CountyMarker[] = []
  for (const loc of locations) {
    const centroid = index.get(indexKey(loc.state, loc.county))
    if (centroid !== undefined) {
      markers.push({ state: loc.state, county: loc.county, count: loc.count, lon: centroid[0], lat: centroid[1] })
    }
  }
  return markers.sort((a, b) => b.count - a.count)
}

interface NamedProps {
  readonly name?: string
}

/**
 * Builds the county-centroid index from the us-atlas states + counties TopoJSON. State name comes from the
 * states layer (county FIPS shares the 2-digit state prefix), so the key matches the extractor's
 * "State"/"County" field values case-insensitively.
 */
export function buildCentroidIndex(statesTopo: Topology, countiesTopo: Topology): CentroidIndex {
  const states = feature(statesTopo, statesTopo.objects.states) as FeatureCollection<never, NamedProps>
  const stateNameByFips = new Map<string, string>()
  for (const f of states.features) {
    if (typeof f.id === 'string' && f.properties.name !== undefined) {
      stateNameByFips.set(f.id, f.properties.name)
    }
  }

  const counties = feature(countiesTopo, countiesTopo.objects.counties) as FeatureCollection<never, NamedProps>
  const index = new Map<string, Centroid>()
  for (const f of counties.features) {
    const fips = typeof f.id === 'string' ? f.id : null
    const county = f.properties.name
    if (fips === null || county === undefined) {
      continue
    }
    const stateName = stateNameByFips.get(fips.slice(0, 2))
    if (stateName === undefined) {
      continue
    }
    const [lon, lat] = geoCentroid(f)
    index.set(indexKey(stateName, county), [lon, lat])
  }
  return index
}

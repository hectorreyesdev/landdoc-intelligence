import { useEffect, useMemo, useState, type ReactElement } from 'react'
import { geoAlbersUsa, geoPath, type GeoProjection } from 'd3-geo'
import { feature } from 'topojson-client'
import type { Topology } from 'topojson-specification'
import type { FeatureCollection } from 'geojson'
import { buildCentroidIndex, resolveMarkers, type CentroidIndex } from './geo'
import type { StateCountyCount } from './metrics'

interface CountyMapProps {
  /** Pre-aggregated (state, county) counts — same data feeding the "Documents by county" bar chart. */
  locations: readonly StateCountyCount[]
}

const WIDTH = 975
const HEIGHT = 610

interface UsGeo {
  readonly statePaths: readonly string[]
  readonly projection: GeoProjection
  readonly index: CentroidIndex
}

/** Bubble radius from a count, scaled against the busiest county so the largest bubble is a sensible size. */
function radius(count: number, max: number): number {
  const min = 5
  const span = 20
  return max <= 1 ? min + span / 2 : min + span * Math.sqrt(count / max)
}

/**
 * A US map plotting documents-by-county as proportional bubbles at each county's centroid (spec 0007
 * amendment). The us-atlas TopoJSON (~600 KB) is dynamically imported so it stays out of the main bundle;
 * the SVG itself renders nothing testable under jsdom, so correctness lives in `geo.ts`'s pure functions.
 */
export function CountyMap({ locations }: CountyMapProps): ReactElement {
  const [geo, setGeo] = useState<UsGeo | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const [statesMod, countiesMod] = await Promise.all([
          import('us-atlas/states-10m.json'),
          import('us-atlas/counties-10m.json'),
        ])
        const statesTopo = statesMod.default as unknown as Topology
        const countiesTopo = countiesMod.default as unknown as Topology
        const states = feature(statesTopo, statesTopo.objects.states) as FeatureCollection
        const projection = geoAlbersUsa().fitSize([WIDTH, HEIGHT], states)
        const pathGen = geoPath(projection)
        const statePaths = states.features.map((f) => pathGen(f) ?? '').filter((d) => d !== '')
        const index = buildCentroidIndex(statesTopo, countiesTopo)
        if (!cancelled) {
          setGeo({ statePaths, projection, index })
        }
      } catch {
        if (!cancelled) {
          setFailed(true)
        }
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  const markers = useMemo(() => {
    if (geo === null) {
      return []
    }
    return resolveMarkers(locations, geo.index)
      .map((m) => {
        const point = geo.projection([m.lon, m.lat])
        return point === null ? null : { ...m, x: point[0], y: point[1] }
      })
      .filter((m): m is NonNullable<typeof m> => m !== null)
  }, [geo, locations])

  if (failed) {
    return <p className="error" role="alert">Could not load the map.</p>
  }
  if (geo === null) {
    return <p className="hint">Loading map…</p>
  }
  if (markers.length === 0) {
    return (
      <p className="doc-empty">
        No mappable locations yet — documents need both a State and a County field to appear here.
      </p>
    )
  }

  const maxCount = markers.reduce((max, m) => Math.max(max, m.count), 0)

  return (
    <div className="map-frame">
      <svg
        viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
        width="100%"
        height="100%"
        role="img"
        aria-label={`Documents by county across ${markers.length} ${markers.length === 1 ? 'county' : 'counties'}`}
        preserveAspectRatio="xMidYMid meet"
      >
        <g className="map-states">
          {geo.statePaths.map((d, i) => (
            <path key={i} d={d} />
          ))}
        </g>
        <g className="map-bubbles">
          {markers.map((m) => (
            <circle key={`${m.state}|${m.county}`} cx={m.x} cy={m.y} r={radius(m.count, maxCount)}>
              <title>{`${m.county}, ${m.state}: ${m.count} document${m.count === 1 ? '' : 's'}`}</title>
            </circle>
          ))}
        </g>
      </svg>
    </div>
  )
}

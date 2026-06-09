import type { DocumentSummary } from '../api/types'

// Client-side CSV of the documents' extracted fields (spec 0007). Wide format: base columns + one column
// per distinct field name across the set. Built from data already in memory — no fetch.

const BASE_COLUMNS = ['fileName', 'status', 'chunkCount', 'ingestedAt'] as const

function escapeCsv(value: string): string {
  // Neutralize spreadsheet formula injection before RFC-4180 quoting: a cell starting with =, +, -, @,
  // tab, or CR is interpreted as a formula by Excel/Sheets. Field values are LLM-extracted from arbitrary
  // uploaded documents, so a crafted doc could yield e.g. =HYPERLINK(...) that executes on export open.
  // Prefix such cells with a single quote to force literal text (OWASP CSV-injection guidance).
  const guarded = /^[=+\-@\t\r]/.test(value) ? `'${value}` : value
  return /[",\n\r]/.test(guarded) ? `"${guarded.replace(/"/g, '""')}"` : guarded
}

export function documentsToCsv(docs: readonly DocumentSummary[]): string {
  const fieldNames: string[] = []
  const seen = new Set<string>()
  for (const doc of docs) {
    for (const field of doc.fields) {
      if (!seen.has(field.name)) {
        seen.add(field.name)
        fieldNames.push(field.name)
      }
    }
  }

  const header = [...BASE_COLUMNS, ...fieldNames]
  const rows = docs.map((doc) => {
    const byName = new Map(doc.fields.map((field) => [field.name, field.value]))
    const cells = [
      doc.fileName,
      doc.status,
      String(doc.chunkCount),
      doc.ingestedAt,
      ...fieldNames.map((name) => byName.get(name) ?? ''),
    ]
    return cells.map(escapeCsv).join(',')
  })

  return [header.map(escapeCsv).join(','), ...rows].join('\n')
}

import { describe, expect, it } from 'vitest'
import type { DocumentSummary } from '../api/types'
import { documentsToCsv } from './csv'

function doc(fileName: string, fields: ReadonlyArray<readonly [string, string]>): DocumentSummary {
  return {
    id: fileName,
    fileName,
    status: 'ready',
    contentType: 'application/pdf',
    chunkCount: 3,
    fields: fields.map(([name, value]) => ({ name, value, sourceChunkId: null })),
    ingestedAt: '2026-06-08T12:00:00.000Z',
  }
}

describe('documentsToCsv', () => {
  it('emits a header plus one row per document with a column per distinct field', () => {
    const docs = [doc('a.pdf', [['Lessee', 'Acme']]), doc('b.pdf', [['Lessee', 'Beta'], ['Royalty', '3/16']])]
    const lines = documentsToCsv(docs).split('\n')
    expect(lines[0]).toBe('fileName,status,chunkCount,ingestedAt,Lessee,Royalty')
    expect(lines[1]).toBe('a.pdf,ready,3,2026-06-08T12:00:00.000Z,Acme,')
    expect(lines[2]).toBe('b.pdf,ready,3,2026-06-08T12:00:00.000Z,Beta,3/16')
  })

  it('quotes and escapes commas, quotes, and newlines', () => {
    const value = 'Acme, LLC "et al"\nLine2'
    const docs = [doc('weird.pdf', [['Parties', value]])]
    const csv = documentsToCsv(docs)
    // The quoted cell escapes inner quotes (doubled) and keeps its embedded newline.
    expect(csv).toContain('"Acme, LLC ""et al""\nLine2"')
    expect(csv.split('\n')[0]).toBe('fileName,status,chunkCount,ingestedAt,Parties')
  })

  it('emits just the header for an empty corpus', () => {
    expect(documentsToCsv([])).toBe('fileName,status,chunkCount,ingestedAt')
  })

  it('neutralizes spreadsheet formula injection in extracted field values', () => {
    // A crafted upload could make the LLM extract a value that Excel/Sheets treats as a formula.
    const docs = [doc('evil.pdf', [['Lessee', '=HYPERLINK("http://evil","click")']])]
    const cell = documentsToCsv(docs).split('\n')[1].split(',').slice(4).join(',')
    // Leading quote forces literal text; the whole cell is RFC-4180-quoted because it contains quotes.
    expect(cell).toBe('"\'=HYPERLINK(""http://evil"",""click"")"')
  })

  it('prefixes every formula trigger character with a single quote', () => {
    for (const trigger of ['=cmd', '+1', '-1', '@SUM', '\tx', '\rx']) {
      const csv = documentsToCsv([doc('x.pdf', [['F', trigger]])])
      const cell = csv.split('\n')[1].split(',').slice(4).join(',')
      // A bare trigger with no CSV-special chars stays unquoted but gains the literal-text prefix;
      // the tab/CR variants additionally get RFC-4180-quoted.
      expect(cell.startsWith("'") || cell.startsWith('"\'')).toBe(true)
    }
  })
})

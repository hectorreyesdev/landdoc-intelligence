#!/usr/bin/env bash
# Bulk-upload the sample PDFs to a running LandDoc API (the ingest write path).
#
#   dotnet run --project backend/src/LandDoc.Api      # in another terminal
#   samples/upload.sh [BASE_URL]                       # default http://localhost:5000
#
# Each PDF is POSTed as multipart/form-data to /documents, matching DocumentsEndpoints.
set -euo pipefail

BASE_URL="${1:-http://localhost:5000}"
DIR="$(cd "$(dirname "$0")" && pwd)/leases"

count=0
for pdf in "$DIR"/*.pdf; do
  name="$(basename "$pdf")"
  printf '==> %s\n' "$name"
  curl -sS -X POST "$BASE_URL/documents" -F "file=@${pdf};type=application/pdf"
  printf '\n'
  count=$((count + 1))
done
printf '\nUploaded %d documents to %s\n' "$count" "$BASE_URL"

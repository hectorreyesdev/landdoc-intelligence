# Backend testing patterns

How the `/backend` (.NET) test suite is wired — reuse this for every new backend feature.

- **Integration tests** use `WebApplicationFactory<Program>` (`LandDocApiFactory`). `Program.cs` ends
  with `public partial class Program { }` so the factory can see the entry point.
- **Only `IChatClient` is faked** — `ConfigureTestServices` swaps in `FakeChatClient` (canned
  `ExtractedField`s + a canned answer). `LocalEmbeddingClient` runs **for real**: it's deterministic and
  offline (ADR-0008), so the suite stays reproducible without faking it.
- **Store isolation:** each store-touching test news its own `LandDocApiFactory` (`using var`) so the
  singleton `IVectorStore` starts empty. Tests that only assert the HTTP response can share a class fixture.
- **Inspect the store through the public seam, not by widening the port:**
  `IVectorStore.TopK(probe, int.MaxValue)` returns every chunk (probe = `IEmbeddingClient.EmbedAsync("probe")`
  for a correctly-sized vector). Filter by `DocumentId` to assert per-document counts/shape.
- **No fixture hardcoding** is the bar: the pipeline must produce every field/chunk/vector. Canned values
  live only in `FakeChatClient`; `grep` for fixture values over `backend/src/` should return nothing.
- **Stack:** xUnit via `dotnet test`; the fixture PDF is copied to the test output via a `Content` item
  with `CopyToOutputDirectory=PreserveNewest`, read at runtime from `AppContext.BaseDirectory/Fixtures/`.

Related: [[workflow-harness]] · spec [[knowledge/docs/specs/0001-document-ingestion-write-path]].

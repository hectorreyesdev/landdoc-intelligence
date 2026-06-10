# Easy Auth on Container Apps — what the docs don't make obvious

How the single-user gate ([[knowledge/docs/decisions/0022-single-user-entra-auth-easy-auth-gate-app-level-allowlist]],
spec 0013) actually behaves, learned wiring it up. Related: [[azure-deployment]].

- **It's a sidecar, and it's pure platform state.** Nothing about the gate lives in the repo or the
  image — app registration, authConfig, and the client secret all sit in Azure and must be re-applied
  on a from-scratch re-provision (steps: DEPLOYMENT §4). The app's only coupling is *reading* the
  `X-MS-CLIENT-PRINCIPAL-ID` header the sidecar injects.
- **The principal allowlist isn't in the CLI.** `az containerapp auth microsoft update` covers the
  registration but not `validation.defaultAuthorizationPolicy.allowedPrincipals`. And the
  `authConfigs/current` ARM resource rejects PATCH (Method Not Allowed) — GET the config, merge the
  allowlist in, PUT the whole thing back.
- **`RedirectToLoginPage` is content-negotiated.** Only browser-shaped requests (`Accept: text/html`)
  get the 302 to Microsoft sign-in; API-shaped clients (curl, fetch) get a bare **401**. So an
  anonymous-curl smoke check returning 401 is the *success* signal post-auth (RUNBOOK-PROD encodes
  this), and the SPA never needs token plumbing — sign-in happens before it's served.
- **The client secret is an ACA secret, not Key Vault.** The sidecar reads it via
  `clientSecretSettingName`; it never flows through app config, so the Key Vault → config overlay
  doesn't apply. It expires (2-year reset; rotation in RUNBOOK-PROD) — unlike the rest of the
  secrets, which rotate in the vault.
- **Defense in depth is cheap here.** The app-level check is one middleware comparing a header
  against `Auth:AllowedPrincipalIds` — no JWT validation, no claims parsing (Easy Auth owns
  authentication; the app only checks *which* principal arrived). `Auth:Mode=none` default keeps
  the whole offline/test story untouched — the same config-seam pattern as the five ports.

# Translation backend

Desktop -> POST /v1/translate -> Notch.Backend -> MyMemory.

The desktop assembly uses Notch.Translation contracts and BackendTranslationService. Provider integration lives in the separate backend executable, included in the portable local distribution. The backend URL is public configuration, not a secret. No shared application password is embedded in the desktop.

## Automatic local startup

Launch `Notch.exe`: without `NOTCH_BACKEND_URL`, it starts the bundled `backend/Notch.Backend.exe` hidden, waits for readiness and uses a free port on `127.0.0.1`. No administrator privileges, fixed port or manual server startup are needed. Translation still requires internet access.

Each launch creates a random session token passed over an inherited pipe, never a command-line argument or settings file. The local server requires that token. This prevents accidental use by other clients; it cannot protect against the same Windows user inspecting the processes. Closing Notch stops its server. If Notch crashes, closure of the pipe also stops the server.

For development, use `scripts/run-local.ps1` (or `scripts/run.ps1` with no backend URL). For another Windows x64 PC, run `scripts/publish.ps1`, copy the **entire** `dist/Notch-win-x64` folder and launch `Notch.exe`. Both processes include their .NET runtime; no SDK/runtime installation is required. Do not copy just the EXE or include any provider key in the package.

To use a separately hosted backend later, set `NOTCH_BACKEND_URL` before launching Notch. In that mode no child server is started and the desktop does not manage the remote server lifetime. Running the backend separately without `--desktop` still defaults to anonymous `http://127.0.0.1:5187`.

## Requests and controls

`POST /v1/translate`, JSON `{ "text": "Hello", "sourceLanguage": "en", "targetLanguage": "ru" }`. Success: `{ "text": "...", "detectedSourceLanguage": "en" }`.

- Fixed upstream URL and allowlisted languages; callers cannot select an upstream host.
- 500 UTF-8 bytes per text, 8 KiB HTTP request body, 1 MiB provider/client response buffers, bounded deadlines.
- 30 requests per minute globally, 4 concurrent requests, no queued requests.
- Conservative 5,000-character daily UTC budget per backend process. Reservations also count failed calls. Restart resets this local budget; it is not a durable provider quota guarantee.
- Generic client errors; no provider response, request text, credentials or query strings logged by application code. No HTTP-body logging. Client uses POST JSON, not URL text. Upstream MyMemory still requires text in its HTTPS URL.
- Automatic redirects disabled on both HTTP clients; remote desktop endpoints require HTTPS, loopback HTTP is allowed for local development.

## Secrets and later deployment

MyMemory currently works without a key. If needed, set `NOTCH_MYMEMORY_KEY` only in the backend environment or deployment secret manager. Never commit it, put it in client settings or ship backend environment files with the desktop.

The standalone endpoint is anonymous. The automatically launched local endpoint requires an ephemeral session token. Anyone with access can consume its limited quota; a desktop-wide static secret cannot solve that. Before a public production rollout, add user authentication with short-lived tokens (for a native client, authorization code + PKCE), per-user quotas and durable/shared accounting. Global limits prevent unlimited upstream calls but do not guarantee availability against abusive users or across multiple instances/restarts.

Deploy backend separately, behind HTTPS with its listening port private. Restrict server secret access and keep secrets out of reverse-proxy/APM logs. Do not enable arbitrary forwarded-header trust. The current global limiter does not rely on spoofable client IP headers. No CORS policy is enabled; CORS is not authentication.

For local development client and backend run under the same Windows user. Moving code into a local process is architectural separation, not protection from another process running as that user. Protecting a provider secret from desktop users requires the later remote backend deployment.



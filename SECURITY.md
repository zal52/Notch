# Security and privacy

Notch is a desktop utility under development, not a security boundary.

- Clipboard text and images are held in process memory and cleared on exit. Monitoring can be paused; Windows clipboard content is not erased by clearing Notch history.
- Screenshots remain in memory until Open or Save is used. Open writes a preview under `%LOCALAPPDATA%/Notch/Previews`; cleanup is best-effort and locked files may remain. Save writes to the location selected by the user.
- Preferences are stored under `%LOCALAPPDATA%/Notch/settings.json`. They do not contain clipboard history or translated text.
- Translation sends text as POST JSON to the configured backend only on an explicit action. Remote URLs require HTTPS; loopback HTTP is permitted locally. The backend calls MyMemory over HTTPS with text in the upstream query. Provider-side handling is outside the application control.
- The desktop contains no embedded API credentials. An optional NOTCH_MYMEMORY_KEY belongs only in the backend environment; do not distribute it with the client. Never add signing keys, access tokens or private service credentials to the repository or executable.

Report a vulnerability privately to the repository owner or through GitHub private vulnerability reporting if enabled. Do not include tokens, clipboard dumps, desktop screenshots or personal data in public issues. Use a minimal synthetic reproduction.

Local publication checks inspect filenames and common secret patterns in the working tree and reachable Git history. They do not guarantee absence of credentials, vulnerabilities or personal information. Review changes before every push.

The backend defaults to loopback. When launched automatically by Notch, it requires a random per-launch token passed over an inherited pipe; standalone mode is anonymous. It enforces request size, global frequency/concurrency and a per-process daily character budget; restart resets that budget. Local separation is not a barrier against the same OS user. Public deployment requires TLS, authenticated users and durable quotas; see BACKEND.md. No automatic redirect following or provider-error forwarding is enabled.


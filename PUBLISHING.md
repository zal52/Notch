# Publication and ownership

The owner has selected a **public repository**. Published source will be visible and downloadable by anyone. Repository visibility has not been changed and nothing has been uploaded.

No open-source license is granted for the original Notch source at this time. Third-party dependencies retain their own licenses. Do not select MIT, Apache or another open-source license unless you intend to grant its permissions.

Public source can be downloaded and forked; adding a restrictive notice or later making the repository private does not remove existing copies. Keeping source private limits access to authorized collaborators, but cannot guarantee protection against account compromise or copying by someone with access. Distributed .NET binaries can also be inspected and decompiled.

Before the first push:

1. Run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-publication.ps1`. Review findings; this local pattern scan is a precaution, not proof that all secrets are absent.
2. Run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test.ps1`.
3. Review `git diff` and `git status --short`. Never upload the whole development folder as a ZIP: it includes ignored SDKs, caches and potentially private artifacts.
4. Review commit author name and email with `git log --format=fuller`. Use your GitHub-provided noreply email for future commits if desired. Existing history is not rewritten by these scripts.
5. Create an empty **public** GitHub repository only after completing the checks above. Verify the owner and visibility before pushing; grant collaborators only the access they need.
6. Enable account two-factor authentication or a passkey; use narrowly scoped credentials. Enable repository secret scanning and push protection where available for your account and plan. These cannot detect every secret.
7. If a secret was ever committed, revoke/rotate it before cleanup. `.gitignore` cannot remove it from existing commits.

The application currently uses an anonymous translation endpoint; there are no translation API keys to distribute. Hiding a hostname in the UI does not conceal it from source readers or network inspection. Provider integration now lives in Notch.Backend. Optional provider credentials belong only in its environment, never inside the desktop binary. The bundled local backend uses an ephemeral session token; authenticated remote deployment is still pending. See BACKEND.md.

References:
- https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository
- https://docs.github.com/en/code-security/concepts/secret-security/push-protection


Earlier source ZIPs and security reports predate the backend split. Build a fresh publication package and repeat review for this changed architecture before publishing.

For a portable Windows x64 distribution run scripts/publish.ps1 and distribute the entire dist/Notch-win-x64 folder, including backend and both runtimes. Local backend startup is automatic. No shared provider credentials may be included.


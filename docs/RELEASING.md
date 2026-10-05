# Releasing Pickets

## Cutting a release

1. Bump `<Version>` in `Pickets/Pickets.csproj` and commit.
2. Push `master`, then tag and push the tag:
   ```bash
   git tag -a v1.7.0 -m "v1.7.0"
   git push origin v1.7.0
   ```
3. CI (`.github/workflows/build.yml`) runs the tests, builds x64 and ARM64, and publishes a GitHub Release with
   the MSIs and single-file portable exes. Replace the auto-generated notes with a written summary.
   Each architecture gets two MSIs from `Pickets.Installer` (`-p:PerUser=true` for the second):
   `Pickets-<arch>.msi` installs for everyone in Program Files (admin prompt), and
   `Pickets-<arch>-user.msi` installs just for the current user in `%LocalAppData%\Programs` (no prompt).
   Each has its own UpgradeCode; never change either. The updater picks the kind matching the running copy.

Installed copies find the release through the GitHub API and offer it to the user.

## Code signing (optional, removes the SmartScreen warning)

Unsigned installers make Windows SmartScreen show "Windows protected your PC" until the file builds up a
reputation. Once a certificate is configured, CI signs `Pickets.exe`, `Pickets.dll` and both MSIs
automatically (`.github/scripts/sign.ps1`); until then it skips signing.

Ways to get a certificate:
- **SignPath Foundation**: free code signing for open-source projects (apply at signpath.org). It uses its
  own GitHub Action rather than a .pfx file, so `sign.ps1` would be swapped for their action.
- **Azure Trusted Signing**: low monthly cost, also has its own GitHub Action.
- **A standard code-signing certificate** (.pfx) from a certificate authority: works with the existing setup.

For a .pfx certificate, add two repository secrets (Settings → Secrets and variables → Actions):

| Secret | Value |
| --- | --- |
| `CODESIGN_PFX_BASE64` | The .pfx file as base64: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))` |
| `CODESIGN_PFX_PASSWORD` | The .pfx password |

The next tagged release is signed.

## winget (optional, `winget install Pickets`)

The `winget` job in CI opens a pull request at
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) for every release, but only after the
package exists there. The first version has to be submitted once by hand:

1. Fork `microsoft/winget-pkgs` to your GitHub account (the automation uses this fork).
2. Submit the current release with Microsoft's
   [wingetcreate](https://github.com/microsoft/winget-create):
   ```powershell
   winget install wingetcreate
   wingetcreate new https://github.com/chrisdfennell/Pickets/releases/download/v1.6.0/Pickets-x64.msi https://github.com/chrisdfennell/Pickets/releases/download/v1.6.0/Pickets-arm64.msi
   ```
   Use the package identifier `chrisdfennell.Pickets` (it must match the `identifier` in the
   workflow), fill in the prompts, and let it open the pull request. Microsoft reviews it, usually within a
   few days.
3. Create a classic personal access token with the `public_repo` scope and add it as the repository secret
   `WINGET_TOKEN`.

After that, each tagged release is sent to winget automatically. Without `WINGET_TOKEN` the job just skips;
with the token but before Microsoft has accepted the first version, the job fails with "Package ... does not
exist in the winget-pkgs repository" (the release itself is unaffected). winget uses the for-everyone MSIs.

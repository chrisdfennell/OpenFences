# Code-signs the given files with the certificate in the CODESIGN_PFX_BASE64 /
# CODESIGN_PFX_PASSWORD repository secrets. Without them it does nothing, so unsigned
# builds keep working. See docs/RELEASING.md for setting up a certificate.
param([Parameter(Mandatory)][string[]]$Files)

$ErrorActionPreference = 'Stop'

if (-not $env:CODESIGN_PFX_BASE64) {
    Write-Host 'No code-signing certificate configured (CODESIGN_PFX_BASE64); leaving files unsigned.'
    exit 0
}

$pfx = Join-Path $env:RUNNER_TEMP 'codesign.pfx'
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:CODESIGN_PFX_BASE64))
try {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
        Sort-Object FullName | Select-Object -Last 1
    if (-not $signtool) { throw 'signtool.exe not found (Windows SDK missing on the runner).' }

    foreach ($file in $Files) {
        Write-Host "Signing $file"
        & $signtool.FullName sign /f $pfx /p $env:CODESIGN_PFX_PASSWORD `
            /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /d Pickets $file
        if ($LASTEXITCODE -ne 0) { throw "signtool failed for $file" }
    }
}
finally {
    Remove-Item $pfx -Force -ErrorAction SilentlyContinue
}

# Creates a permanent code-signing certificate for the Timer app.
# Run once on your own Windows PC (normal user, no admin needed):
#   powershell -ExecutionPolicy Bypass -File .\tools\New-TimerSigningCert.ps1
#
# The private key never leaves your machine except as a GitHub Secret
# that you paste yourself in the repository settings (see SIGNING.md).

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoSecretsUrl = 'https://github.com/xsitraxs/timernew/settings/secrets/actions'
$outDir = Join-Path ([Environment]::GetFolderPath('Desktop')) 'TimerAppCert'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host '=== Timer permanent signing certificate ===' -ForegroundColor Cyan
Write-Host ''

# Reuse an existing valid certificate if there is one.
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq 'CN=TimerAppDev' -and
        $_.HasPrivateKey -and
        $_.NotAfter -gt (Get-Date).AddYears(1)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($cert) {
    Write-Host "Found existing certificate: $($cert.Thumbprint)" -ForegroundColor Green
} else {
    Write-Host 'Creating a new 10-year code-signing certificate (CN=TimerAppDev)...'
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject 'CN=TimerAppDev' `
        -FriendlyName 'TimerApp permanent signing' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears(10)
    Write-Host "Created: $($cert.Thumbprint)" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Think of a password for the certificate file and remember it.'
Write-Host 'You will paste the SAME password into GitHub as a secret.'
$password = Read-Host 'Certificate password' -AsSecureString

$pfxPath = Join-Path $outDir 'TimerApp.pfx'
$cerPath = Join-Path $outDir 'TimerApp.cer'
$b64Path = Join-Path $outDir 'TimerApp.pfx.base64.txt'

Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
[Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath)) |
    Set-Content -Path $b64Path -NoNewline -Encoding ascii

Write-Host ''
Write-Host 'Files created on your Desktop in folder TimerAppCert:' -ForegroundColor Cyan
Write-Host "  TimerApp.pfx            - private key (KEEP IT SAFE, do not publish)"
Write-Host "  TimerApp.cer            - public certificate"
Write-Host "  TimerApp.pfx.base64.txt - text to paste into GitHub"
Write-Host ''
Write-Host 'Now create TWO secrets in the repository (page will open now):' -ForegroundColor Yellow
Write-Host '  1. Name: TIMERAPP_CERT_PFX_BASE64'
Write-Host '     Value: open TimerApp.pfx.base64.txt, copy ALL text, paste it.'
Write-Host '  2. Name: TIMERAPP_CERT_PASSWORD'
Write-Host '     Value: the same password you just entered.'
Write-Host ''
Write-Host 'After that, delete TimerApp.pfx.base64.txt from your Desktop.'
Write-Host 'Keep TimerApp.pfx somewhere safe: it is the identity of the app.'
Write-Host ''

Start-Process $repoSecretsUrl

Write-Host 'Opening GitHub secrets page in your browser...' -ForegroundColor Cyan
Read-Host 'Press Enter when done'

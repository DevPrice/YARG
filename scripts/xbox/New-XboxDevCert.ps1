<#
.SYNOPSIS
Creates the self-signed code-signing certificate for Xbox Dev Mode sideload packages, or reuses it.

.DESCRIPTION
Looks for a certificate with the given subject in Cert:\CurrentUser\My and creates one if there is none.
Exports it to <OutputDir>\<Name>.pfx (protected by -Password) and <OutputDir>\<Name>.cer, unless those
files already exist. Keep OutputDir outside the repository: the .pfx holds the private key.
Prints the .pfx path.
#>
param(
    [Parameter(Mandatory)][string]$OutputDir,
    [Parameter(Mandatory)][string]$Password,
    [string]$Subject = 'CN=YARGXboxDev',
    [string]$Name = 'YARGXboxDev'
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force $OutputDir | Out-Null
$pfx = Join-Path $OutputDir "$Name.pfx"
$cer = Join-Path $OutputDir "$Name.cer"

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

if (-not $cert) {
    if (Test-Path $pfx) {
        $secure = ConvertTo-SecureString $Password -AsPlainText -Force
        $cert = Import-PfxCertificate -FilePath $pfx -CertStoreLocation Cert:\CurrentUser\My -Password $secure
        Write-Host "Imported $Subject from $pfx ($($cert.Thumbprint))"
    }
    else {
        # MSIX signing requires the code-signing EKU and Basic Constraints with no CA flag.
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature `
            -FriendlyName "$Name sideload signing" -CertStoreLocation Cert:\CurrentUser\My `
            -NotAfter (Get-Date).AddYears(5) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Write-Host "Created $Subject ($($cert.Thumbprint))"
    }
}

if (-not (Test-Path $pfx)) {
    $secure = ConvertTo-SecureString $Password -AsPlainText -Force
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
}
if (-not (Test-Path $cer)) {
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
}

$pfx

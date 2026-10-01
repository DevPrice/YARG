<#
.SYNOPSIS
Builds YARG as a signed Xbox Dev Mode sideload package (.msix) and collects it in <OutputDir>\Deliver.

.DESCRIPTION
1. Unity batch mode, twice: YARG.Editor.Xbox.XboxBuild.Configure (switches to WSAPlayer and applies every
   WSA player setting from code), then XboxBuild.Build (IL2CPP x64 D3D player into <OutputDir>\UWP).
   Unity writes those settings, including the certificate password, into ProjectSettings\. The script
   snapshots ProjectSettings\ first and puts it back afterwards, even when the build fails, unless
   -KeepProjectSettings is given. Never commit ProjectSettings changes from an Xbox build.
2. MSBuild (latest Visual Studio found by vswhere) on the generated solution: <Configuration>|x64,
   sideload-only, signed with the certificate.
3. Copies the package, its framework dependencies (VCLibs) and the .cer to <OutputDir>\Deliver.

The Unity process gets CMake from -CMakeDir first on PATH, and CMAKE_GENERATOR/CMAKE_GENERATOR_INSTANCE
naming the Visual Studio instance with the UWP workload, for YargAudioAutoBuilder's windows-store-x64 preset.
None of this is changed system-wide.
#>
param(
    [string]$ProjectPath = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$OutputDir,
    [string]$CertPath = $env:YARG_XBOX_CERT,
    [string]$CertPassword = $env:YARG_XBOX_CERT_PASSWORD,
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.5f2\Editor\Unity.exe',
    [string]$CMakeDir = $env:YARG_XBOX_CMAKE_DIR,
    [ValidateSet('Master', 'Release', 'Debug')][string]$Configuration = 'Master',
    [string]$PackageVersion,
    [switch]$SkipUnity,
    [switch]$SkipMsBuild,
    [switch]$KeepProjectSettings
)

$ErrorActionPreference = 'Stop'

$ProjectPath = (Resolve-Path $ProjectPath).Path
if (-not $OutputDir) { $OutputDir = Join-Path $ProjectPath 'Build\Xbox' }
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path
$unityOut = Join-Path $OutputDir 'UWP'
$logs = Join-Path $OutputDir 'Logs'
New-Item -ItemType Directory -Force $logs | Out-Null

if (-not $CertPath -or -not (Test-Path $CertPath)) {
    throw "Signing certificate '$CertPath' not found. Pass -CertPath (or set YARG_XBOX_CERT); create one with " +
        "New-XboxDevCert.ps1 -OutputDir <folder outside the repo> -Password <password>."
}
$CertPath = (Resolve-Path $CertPath).Path
$cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertPath, $CertPassword)
if (-not (Test-Path "Cert:\CurrentUser\My\$($cert.Thumbprint)")) {
    $secure = ConvertTo-SecureString $CertPassword -AsPlainText -Force
    Import-PfxCertificate -FilePath $CertPath -CertStoreLocation Cert:\CurrentUser\My -Password $secure | Out-Null
}
Write-Host "Signing with $($cert.Subject) ($($cert.Thumbprint))"

if ($CMakeDir -and -not (Test-Path (Join-Path $CMakeDir 'cmake.exe'))) { throw "No cmake.exe in $CMakeDir" }

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Workload.Universal `
    -property installationPath | Select-Object -First 1
if (-not $vsPath) { throw 'No Visual Studio with the Universal Windows Platform workload found via vswhere' }

function Invoke-Unity([string]$method, [string]$logName) {
    $log = Join-Path $logs $logName
    $unityArgs = @(
        '-batchmode', '-quit', '-nographics', '-buildTarget', 'WindowsStoreApps',
        '-projectPath', "`"$ProjectPath`"", '-logFile', "`"$log`"", '-executeMethod', $method,
        '-xboxOutput', "`"$unityOut`"")
    if ($PackageVersion) { $unityArgs += @('-xboxVersion', $PackageVersion) }
    Write-Host "Unity $method (log: $log)"
    $p = Start-Process -FilePath $Unity -Wait -PassThru -NoNewWindow -ArgumentList $unityArgs
    if ($p.ExitCode -ne 0) { throw "Unity $method failed with exit code $($p.ExitCode); see $log" }
}

if (-not $SkipUnity) {
    $settingsDir = Join-Path $ProjectPath 'ProjectSettings'
    $settingsBackup = Join-Path $OutputDir 'ProjectSettings.backup'
    if (Test-Path $settingsBackup) { Remove-Item -Recurse -Force $settingsBackup }
    Copy-Item -Recurse $settingsDir $settingsBackup

    $savedEnv = @{}
    foreach ($name in 'PATH', 'CMAKE_GENERATOR', 'CMAKE_GENERATOR_INSTANCE', 'YARG_XBOX_CERT', 'YARG_XBOX_CERT_PASSWORD') {
        $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    try {
        # The password goes through the environment because Unity echoes its command line into the log.
        $env:YARG_XBOX_CERT = $CertPath
        $env:YARG_XBOX_CERT_PASSWORD = $CertPassword
        # CMake before 4.4 rejects a VS instance that only has Windows11SDK.* components when configuring
        # the windows-store-x64 preset. With several VS 2022 instances installed (BuildTools has no UWP
        # tools), pin the one with the UWP workload. CMake ignores CMAKE_GENERATOR_INSTANCE from the
        # environment unless CMAKE_GENERATOR is set too; the presets name the same generator.
        if ($CMakeDir) { $env:PATH = "$CMakeDir;$env:PATH" }
        $env:CMAKE_GENERATOR = 'Visual Studio 17 2022'
        $env:CMAKE_GENERATOR_INSTANCE = $vsPath
        $cmake = Get-Command cmake -ErrorAction SilentlyContinue
        if ($cmake) { Write-Host "CMake for the Unity process: $($cmake.Source) ($((& $cmake.Source --version)[0]))" }

        Invoke-Unity 'YARG.Editor.Xbox.XboxBuild.Configure' 'unity-configure.log'
        Invoke-Unity 'YARG.Editor.Xbox.XboxBuild.Build' 'unity-build.log'
    }
    finally {
        foreach ($name in $savedEnv.Keys) {
            [Environment]::SetEnvironmentVariable($name, $savedEnv[$name], 'Process')
        }
        if (-not $KeepProjectSettings) {
            Remove-Item -Recurse -Force $settingsDir
            Copy-Item -Recurse $settingsBackup $settingsDir
            Remove-Item -Recurse -Force $settingsBackup
            Write-Host 'Restored ProjectSettings'
        }
    }
}

$sln = Get-ChildItem $unityOut -Filter *.sln | Select-Object -First 1
if (-not $sln) { throw "No .sln under $unityOut" }

if (-not $SkipMsBuild) {
    $msbuild = Join-Path $vsPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
    if (-not (Test-Path $msbuild)) { throw "$msbuild not found" }
    $msbuildLog = Join-Path $logs 'msbuild.log'
    Write-Host "MSBuild $($sln.Name) $Configuration|x64 (log: $msbuildLog)"
    & $msbuild $sln.FullName '/m' '/restore' "/p:Configuration=$Configuration" '/p:Platform=x64' `
        '/p:AppxBundle=Never' '/p:UapAppxPackageBuildMode=SideloadOnly' '/p:AppxPackageSigningEnabled=true' `
        "/p:PackageCertificateThumbprint=$($cert.Thumbprint)" "/p:PackageCertificatePassword=$CertPassword" `
        "/flp:LogFile=$msbuildLog;Verbosity=normal" '/v:minimal'
    if ($LASTEXITCODE -ne 0) { throw "MSBuild failed ($LASTEXITCODE); see $msbuildLog" }
}

$deliver = Join-Path $OutputDir 'Deliver'
if (Test-Path $deliver) { Remove-Item -Recurse -Force $deliver }
New-Item -ItemType Directory -Force (Join-Path $deliver 'Dependencies') | Out-Null
$pkgDir = Get-ChildItem $unityOut -Recurse -Directory -Filter "*_x64_${Configuration}_Test" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $pkgDir) { throw "No *_x64_${Configuration}_Test package folder under $unityOut" }
Get-ChildItem $pkgDir.FullName -File | Where-Object Extension -in '.appx', '.msix' | Copy-Item -Destination $deliver
Get-ChildItem (Join-Path $pkgDir.FullName 'Dependencies\x64') -File -ErrorAction SilentlyContinue |
    Copy-Item -Destination (Join-Path $deliver 'Dependencies')
$cerName = [IO.Path]::GetFileNameWithoutExtension($CertPath) + '.cer'
[IO.File]::WriteAllBytes((Join-Path $deliver $cerName), $cert.Export('Cert'))

Get-ChildItem -Recurse -File $deliver | Select-Object FullName, Length
git -C $ProjectPath status --short -- ProjectSettings

# Xbox Dev Mode build

Builds YARG as a signed UWP package (IL2CPP, x64, D3D11) to sideload on an Xbox in Dev Mode.

All WSA player settings (package identity, capabilities, IL2CPP, graphics API, defines, signing
certificate) come from `Assets/Editor/Xbox/XboxBuild.cs` at build time. **Never commit
`ProjectSettings/` changes from an Xbox build.** Unity writes those settings, including the certificate
password, into `ProjectSettings/ProjectSettings.asset`; `Build-Xbox.ps1` snapshots `ProjectSettings/`
before Unity runs and puts it back afterwards, even if the build fails.

## Requirements

- Unity 6000.3.5f2 with **Universal Windows Platform Build Support**
- Visual Studio 2022 with the **Universal Windows Platform development** workload (C++ UWP tools)
- CMake 4.4 or newer, if the build compiles the UWP `yarg_audio.dll` (only when
  `Assets/Plugins/YargAudio/WSA/x64/yarg_audio.dll.meta` exists). Older CMake can't configure the
  `windows-store-x64` preset on a VS instance that only has Windows 11 SDK components. Pass `-CMakeDir`
  to use a CMake other than the one on `PATH`; only the Unity process sees it.
- A signing certificate (below)
- No Unity editor open on the same project

## Signing certificate

Once per PC, create a self-signed code-signing certificate in `Cert:\CurrentUser\My` and export it to a
folder **outside the repository**:

```powershell
pwsh -File scripts\xbox\New-XboxDevCert.ps1 -OutputDir <folder outside the repo> -Password <password>
```

That writes `YARGXboxDev.pfx` and `YARGXboxDev.cer` (subject `CN=YARGXboxDev`, which becomes the package
publisher). Running it again reuses the existing certificate.

## Build

```powershell
pwsh -File scripts\xbox\Build-Xbox.ps1 -CertPath <folder>\YARGXboxDev.pfx -CertPassword <password>
```

| Parameter | Default | |
|---|---|---|
| `-ProjectPath` | this repository | the Unity project to build |
| `-OutputDir` | `<project>\Build\Xbox` | gets `UWP\` (VS solution), `Logs\` and `Deliver\` |
| `-CertPath`, `-CertPassword` | env `YARG_XBOX_CERT`, `YARG_XBOX_CERT_PASSWORD` | the `.pfx` and its password |
| `-CMakeDir` | env `YARG_XBOX_CMAKE_DIR` | folder with `cmake.exe`, put first on the Unity process's `PATH` |
| `-Configuration` | `Master` | `Master` (no debug checks, for play and perf), `Release` or `Debug` |
| `-PackageVersion` | `1.0.<days since 2026-01-01>.<UTC minute>` | so each build installs over the last |
| `-SkipUnity` | | reuse the existing `UWP\` solution |
| `-SkipMsBuild` | | only refresh `Deliver\` |
| `-KeepProjectSettings` | | don't restore `ProjectSettings\` (to inspect what Unity wrote) |

Logs: `Logs\unity-configure.log`, `Logs\unity-build.log`, `Logs\msbuild.log`.

After a build the project's active build target is UWP; switch back in Build Profiles before working on
desktop.

## Sideload through Device Portal

`Deliver\` holds the `.msix`, `Dependencies\Microsoft.VCLibs.x64.14.00.appx` and the `.cer`.

1. On the Xbox, open **Dev Home** and note the Device Portal address (for example
   `https://192.168.1.50:11443`). Open it in a browser on the PC and sign in.
2. **Home → My games & apps → Add → Choose file** → the `.msix` in `Deliver\`. Click **Next**.
3. On the dependencies page, **Choose file** → `Deliver\Dependencies\Microsoft.VCLibs.x64.14.00.appx`.
   If a certificate step appears, give it the `.cer`. Click **Start** and wait for "Package successfully
   registered". A newer VCLibs already on the console is fine.
4. On the Xbox, in **Dev Home**, highlight **YARG Xbox Dev**, press **View** → **View details** and set
   **App type** to **Game**. The default App type gets a much smaller memory budget.

App data (settings, scores, logs) is under **File explorer → LocalAppData →
YARG.XboxDev_… → LocalState** in Device Portal.

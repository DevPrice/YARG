using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace YARG.Editor.Xbox
{
    /// <summary>
    /// Builds the Xbox Dev Mode (UWP) player from batch mode. Every WSA setting is applied here at build time,
    /// so ProjectSettings.asset never carries it; Unity still writes these settings (including the certificate
    /// password) back to ProjectSettings.asset, so restore that file after building and never commit it.
    /// scripts/xbox/Build-Xbox.ps1 does both.
    /// </summary>
    /// <remarks>
    /// Command-line options: <c>-xboxOutput &lt;dir&gt;</c> (default Build/Xbox/UWP),
    /// <c>-xboxCert &lt;pfx&gt;</c> or env YARG_XBOX_CERT, env YARG_XBOX_CERT_PASSWORD, and
    /// <c>-xboxVersion a.b.c.d</c> (default 1.0.&lt;days since 2026-01-01&gt;.&lt;UTC minute of day&gt;,
    /// so each build installs over the previous one).
    /// </remarks>
    public static class XboxBuild
    {
        private const string PRODUCT_NAME = "YARG Xbox Dev";
        private const string PACKAGE_NAME = "YARG.XboxDev";
        private const string DEFAULT_OUTPUT = "Build/Xbox/UWP";
        private const string CERT_COPY_DIR = "Build/XboxCert";

        private static readonly PlayerSettings.WSACapability[] Capabilities =
        {
            PlayerSettings.WSACapability.InternetClient,
            PlayerSettings.WSACapability.InternetClientServer,
            PlayerSettings.WSACapability.PrivateNetworkClientServer,
            PlayerSettings.WSACapability.Microphone,
            PlayerSettings.WSACapability.RemovableStorage,
        };

        public static void Configure()
        {
            RunBatch(ApplySettings);
        }

        public static void Build()
        {
            RunBatch(() =>
            {
                ApplySettings();
                BuildPlayer();
            });
        }

        private static void RunBatch(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                throw;
            }
        }

        private static void ApplySettings()
        {
            var wsa = NamedBuildTarget.WindowsStoreApps;
            PlayerSettings.productName = PRODUCT_NAME;
            PlayerSettings.SetScriptingBackend(wsa, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(wsa,
                PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Standalone));
            PlayerSettings.SetManagedStrippingLevel(wsa, ManagedStrippingLevel.Low);
            PlayerSettings.SetScriptingDefineSymbols(wsa, MergeDefines(
                PlayerSettings.GetScriptingDefineSymbols(wsa),
                PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone)));
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WSAPlayer, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WSAPlayer, new[] { GraphicsDeviceType.Direct3D11 });

            PlayerSettings.WSA.packageName = PACKAGE_NAME;
            PlayerSettings.WSA.applicationDescription = PRODUCT_NAME;
            PlayerSettings.WSA.tileShortName = "YARG Dev";
            PlayerSettings.WSA.packageVersion = GetPackageVersion();

            foreach (PlayerSettings.WSACapability capability in Enum.GetValues(typeof(PlayerSettings.WSACapability)))
            {
                PlayerSettings.WSA.SetCapability(capability, Array.IndexOf(Capabilities, capability) >= 0);
            }

            // No family selected means Windows.Universal, which covers Xbox and still installs on a PC.
            foreach (PlayerSettings.WSATargetFamily family in Enum.GetValues(typeof(PlayerSettings.WSATargetFamily)))
            {
                PlayerSettings.WSA.SetTargetDeviceFamily(family, false);
            }

            SetCertificate();

            EditorUserBuildSettings.wsaUWPBuildType = WSAUWPBuildType.D3D;
            SetWsaArchitectureX64();

            // Switching recompiles scripts for WSA, so the defines above must already be in place: without
            // ZSTRING_TEXTMESHPRO_SUPPORT the WSA compile fails and the editor can't run Build().
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WSAPlayer &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WSA, BuildTarget.WSAPlayer))
            {
                throw new Exception("SwitchActiveBuildTarget(WSAPlayer) failed; is UWP Build Support installed?");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[XboxBuild] Configured {PACKAGE_NAME} {PlayerSettings.WSA.packageVersion}, defines=" +
                PlayerSettings.GetScriptingDefineSymbols(wsa));
        }

        private static void BuildPlayer()
        {
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }

            if (scenes.Count == 0)
            {
                throw new Exception("No enabled scenes in the build settings");
            }

            string output = GetArg("-xboxOutput") ?? DEFAULT_OUTPUT;
            Directory.CreateDirectory(output);

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = output,
                target = BuildTarget.WSAPlayer,
                targetGroup = BuildTargetGroup.WSA,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[XboxBuild] result={summary.result} errors={summary.totalErrors} " +
                $"warnings={summary.totalWarnings} output={summary.outputPath} time={summary.totalTime}");
            if (summary.result != BuildResult.Succeeded)
            {
                throw new Exception($"WSA player build failed: {summary.result}");
            }
        }

        private static string MergeDefines(string target, string standalone)
        {
            var defines = new List<string>();
            foreach (string define in (target + ";" + standalone).Split(';'))
            {
                string trimmed = define.Trim();
                if (trimmed.Length > 0 && !defines.Contains(trimmed))
                {
                    defines.Add(trimmed);
                }
            }

            return string.Join(";", defines);
        }

        private static Version GetPackageVersion()
        {
            string arg = GetArg("-xboxVersion");
            if (arg != null)
            {
                return Version.Parse(arg);
            }

            var now = DateTime.UtcNow;
            int days = (int) (now.Date - new DateTime(2026, 1, 1)).TotalDays;
            return new Version(1, 0, days, (int) now.TimeOfDay.TotalMinutes);
        }

        private static void SetCertificate()
        {
            string source = GetArg("-xboxCert") ?? Environment.GetEnvironmentVariable("YARG_XBOX_CERT");
            if (string.IsNullOrEmpty(source))
            {
                throw new Exception("No signing certificate: pass -xboxCert <pfx> or set YARG_XBOX_CERT");
            }

            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Signing certificate not found", source);
            }

            // SetCertificate takes a project-relative path, and the generated VS project signs with the copy
            // Unity makes from it.
            Directory.CreateDirectory(CERT_COPY_DIR);
            string relative = CERT_COPY_DIR + "/" + Path.GetFileName(source);
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(relative), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, relative, true);
            }

            string password = Environment.GetEnvironmentVariable("YARG_XBOX_CERT_PASSWORD") ?? string.Empty;
            if (!PlayerSettings.WSA.SetCertificate(relative, password))
            {
                throw new Exception($"PlayerSettings.WSA.SetCertificate({relative}) failed; wrong password?");
            }

            Debug.Log($"[XboxBuild] certificate subject={PlayerSettings.WSA.certificateSubject} " +
                $"notAfter={PlayerSettings.WSA.certificateNotAfter}");
        }

        private static void SetWsaArchitectureX64()
        {
            // wsaArchitecture works in 6000.3 but is missing from the public API reference, so set it by name
            // rather than tie compilation to it.
            var prop = typeof(EditorUserBuildSettings).GetProperty("wsaArchitecture",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop == null || !prop.CanWrite)
            {
                Debug.LogWarning("[XboxBuild] EditorUserBuildSettings.wsaArchitecture not found; relying on the x64 default");
                return;
            }

            prop.SetValue(null, "x64");
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}

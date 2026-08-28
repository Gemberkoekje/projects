using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace IronFlag.Editor.ArtPipeline
{
    /// <summary>
    /// Editor-side bridge to the SuperCollider audio pipeline that lives in the
    /// repository's <c>audio/</c> folder. Runs <c>sclang audio/build.scd</c> and re-imports
    /// the resulting <c>.wav</c> files into <c>Assets/RF/Audio</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <see cref="BlenderArtPipeline"/> for sound, and deliberately the same shape:
    /// every asset in this project is generated from committed source, and audio is no
    /// exception. See <c>audio/README.md</c> for the authoring side.
    /// </para>
    /// <para>
    /// SuperCollider is located in this order: the <c>IRONFLAG_SUPERCOLLIDER</c> environment
    /// variable, the <c>IronFlag.SuperColliderPath</c> editor preference (set via
    /// <see cref="LocateSuperCollider"/>), then the default install locations for the
    /// current platform.
    /// </para>
    /// <para>
    /// Note the argument style below. <c>sclang</c> parses anything starting with a dash as
    /// one of its own options and exits with "unrecognised option", so the build script
    /// takes bare words instead. It also does not quit when a script raises - it sits in its
    /// event loop indefinitely - which is why the timeout here is load-bearing rather than
    /// defensive.
    /// </para>
    /// </remarks>
    public static class SuperColliderAudioPipeline
    {
        /// <summary>Editor preference key holding an explicit path to the sclang executable.</summary>
        public const string SuperColliderPathPrefKey = "IronFlag.SuperColliderPath";

        /// <summary>Environment variable checked before the editor preference.</summary>
        public const string SuperColliderPathEnvVar = "IRONFLAG_SUPERCOLLIDER";

        /// <summary>Sub-folder of <c>Assets/</c> that receives the rendered <c>.wav</c> files.</summary>
        private const string AudioSubfolder = "RF/Audio";

        /// <summary>Project-relative folder that receives the rendered <c>.wav</c> files.</summary>
        public const string AudioOutputFolder = "Assets/" + AudioSubfolder;

        /// <summary>Seconds to wait for a render before giving up.</summary>
        private const int RenderTimeoutSeconds = 600;

        /// <summary>
        /// Renders every sound defined in <c>audio/sounds/</c> and re-imports the results.
        /// </summary>
        [MenuItem("Tools/IronFlag/Rebuild All Audio from SuperCollider", false, 101)]
        public static void RebuildAll()
        {
            RunRender(string.Empty);
        }

        /// <summary>
        /// Re-renders only the sound matching the selected clip, so one sound can be
        /// iterated on without re-rendering the whole set.
        /// </summary>
        [MenuItem("Assets/IronFlag/Rebuild This Sound from SuperCollider", false, 101)]
        public static void RebuildSelected()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog("IronFlag", "Select a .wav clip in the Project window first.", "OK");
                return;
            }

            RunRender(Path.GetFileNameWithoutExtension(assetPath));
        }

        /// <summary>
        /// Validates <see cref="RebuildSelected"/>: only enabled for a selected <c>.wav</c> file.
        /// </summary>
        /// <returns><c>true</c> when the active selection is a wav asset.</returns>
        [MenuItem("Assets/IronFlag/Rebuild This Sound from SuperCollider", true)]
        public static bool ValidateRebuildSelected()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(assetPath)
                && assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Prompts for the sclang executable and stores it in the editor preferences, for
        /// machines where SuperCollider is not installed in a default location.
        /// </summary>
        [MenuItem("Tools/IronFlag/Locate SuperCollider...", false, 121)]
        public static void LocateSuperCollider()
        {
            string picked = EditorUtility.OpenFilePanel("Select the sclang executable", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            EditorPrefs.SetString(SuperColliderPathPrefKey, picked);
            Debug.Log($"IronFlag: SuperCollider path set to {picked}");
        }

        /// <summary>
        /// Resolves the sclang executable to use for audio renders.
        /// </summary>
        /// <returns>An absolute path, or an empty string when sclang could not be found.</returns>
        public static string ResolveSuperColliderExecutable()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(SuperColliderPathEnvVar);
            if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
            {
                return fromEnvironment;
            }

            string fromPreferences = EditorPrefs.GetString(SuperColliderPathPrefKey, string.Empty);
            if (!string.IsNullOrEmpty(fromPreferences) && File.Exists(fromPreferences))
            {
                return fromPreferences;
            }

            foreach (string candidate in DefaultSuperColliderLocations())
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Enumerates the platform's default SuperCollider install locations, newest version
        /// first.
        /// </summary>
        /// <returns>Candidate executable paths; the caller checks whether they exist.</returns>
        private static IEnumerable<string> DefaultSuperColliderLocations()
        {
            var candidates = new List<string>();

#if UNITY_EDITOR_WIN
            string[] roots =
            {
                @"C:\Program Files",
                @"C:\Program Files (x86)",
            };

            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var versions = new List<string>(Directory.GetDirectories(root, "SuperCollider*"));
                versions.Sort(StringComparer.OrdinalIgnoreCase);
                versions.Reverse();
                foreach (string version in versions)
                {
                    candidates.Add(Path.Combine(version, "sclang.exe"));
                }
            }
#elif UNITY_EDITOR_OSX
            candidates.Add("/Applications/SuperCollider.app/Contents/MacOS/sclang");
#else
            candidates.Add("/usr/bin/sclang");
            candidates.Add("/usr/local/bin/sclang");
#endif

            return candidates;
        }

        /// <summary>
        /// Runs the SuperCollider render script and refreshes the asset database.
        /// </summary>
        /// <param name="soundFilter">
        /// Substring matched against sound names; an empty string renders every sound.
        /// </param>
        private static void RunRender(string soundFilter)
        {
            string sclang = ResolveSuperColliderExecutable();
            if (string.IsNullOrEmpty(sclang))
            {
                EditorUtility.DisplayDialog(
                    "IronFlag",
                    "Could not find SuperCollider.\n\nUse Tools > IronFlag > Locate SuperCollider... or set the "
                        + SuperColliderPathEnvVar + " environment variable.",
                    "OK");
                return;
            }

            string repositoryRoot = BlenderArtPipeline.RepositoryRoot();
            string buildScript = repositoryRoot + "/audio/build.scd";
            if (!File.Exists(buildScript))
            {
                EditorUtility.DisplayDialog("IronFlag", $"Render script not found at {buildScript}", "OK");
                return;
            }

            string outputDirectory = Application.dataPath + "/" + AudioSubfolder;

            // Bare words, not flags - sclang would try to parse a leading dash itself.
            var arguments = new StringBuilder();
            arguments.Append('"').Append(buildScript).Append('"');
            arguments.Append(" out ").Append('"').Append(outputDirectory).Append('"');
            if (!string.IsNullOrEmpty(soundFilter))
            {
                arguments.Append(" sound ").Append('"').Append(soundFilter).Append('"');
            }

            var startInfo = new ProcessStartInfo(sclang, arguments.ToString())
            {
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            int exitCode;

            try
            {
                EditorUtility.DisplayProgressBar("IronFlag", "Rendering audio in SuperCollider...", 0.5f);

                // Drained through events rather than ReadToEnd() for the same reason as the
                // Blender pipeline: sclang is chatty on startup, and a full, undrained pipe
                // deadlocks the very failure path the timeout below exists to catch.
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (_, e) =>
                    {
                        if (e.Data != null)
                        {
                            standardOutput.AppendLine(e.Data);
                        }
                    };
                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (e.Data != null)
                        {
                            standardError.AppendLine(e.Data);
                        }
                    };

                    try
                    {
                        process.Start();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"IronFlag: Could not launch SuperCollider at '{sclang}'.\n{ex}");
                        EditorUtility.DisplayDialog("IronFlag", $"Could not launch SuperCollider.\n\n{ex.Message}", "OK");
                        return;
                    }

                    DateTime startedAt = DateTime.UtcNow;
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(RenderTimeoutSeconds * 1000))
                    {
                        process.Kill();
                        KillOrphanedScsynth(startedAt);
                        Debug.LogError(
                            $"IronFlag: SuperCollider render timed out after {RenderTimeoutSeconds}s. "
                            + "sclang keeps running after a script error, so check the output for an ERROR line.\n"
                            + $"{standardOutput}\n{standardError}");
                        return;
                    }

                    // The timeout overload does not guarantee the async output/error events
                    // have finished draining; the parameterless overload does.
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (exitCode == 0)
            {
                Debug.Log($"IronFlag: SuperCollider render finished.\n{standardOutput}");
            }
            else
            {
                Debug.LogError($"IronFlag: SuperCollider render failed (exit code {exitCode}).\n{standardOutput}\n{standardError}");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// Kills any <c>scsynth</c> renderer still running after a timed-out sclang process.
        /// </summary>
        /// <remarks>
        /// sclang renders each sound synchronously through <c>scsynth</c> via
        /// <c>unixCmdGetStdOut</c> (see <c>audio/rf/engine.scd</c>), so a hang there blocks
        /// sclang without crashing it - killing only the <c>sclang</c> process leaves
        /// <c>scsynth</c> an orphan that can keep the output <c>.wav</c> open into the next
        /// build. Scoped to processes started at or after this render began, so a
        /// <c>scsynth</c> a developer already has open via <c>./audio/build.ps1 -Listen</c>
        /// is left alone.
        /// </remarks>
        private static void KillOrphanedScsynth(DateTime renderStartedAt)
        {
            foreach (Process candidate in Process.GetProcessesByName("scsynth"))
            {
                using (candidate)
                {
                    try
                    {
                        if (candidate.StartTime.ToUniversalTime() >= renderStartedAt)
                        {
                            candidate.Kill();
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Already exited between GetProcessesByName and here.
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // No permission to inspect or kill it - not ours to touch.
                    }
                }
            }
        }
    }
}

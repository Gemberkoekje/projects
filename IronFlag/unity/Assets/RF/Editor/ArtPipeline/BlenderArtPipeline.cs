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
    /// Editor-side bridge to the Blender asset pipeline that lives in the repository's
    /// <c>blender/</c> folder. Runs <c>blender --background --python blender/build.py</c>
    /// and re-imports the resulting <c>.glb</c> files into <c>Assets/RF/Art/Models</c>.
    /// </summary>
    /// <remarks>
    /// Blender is located in this order: the <c>IRONFLAG_BLENDER</c> environment variable,
    /// the <c>IronFlag.BlenderPath</c> editor preference (set via
    /// <see cref="LocateBlender"/>), then the default install locations for the current
    /// platform. The build runs synchronously and blocks the editor until it finishes;
    /// asset builds take seconds, so no async plumbing is warranted.
    /// </remarks>
    public static class BlenderArtPipeline
    {
        /// <summary>Editor preference key holding an explicit path to the Blender executable.</summary>
        public const string BlenderPathPrefKey = "IronFlag.BlenderPath";

        /// <summary>Environment variable checked before the editor preference.</summary>
        public const string BlenderPathEnvVar = "IRONFLAG_BLENDER";

        /// <summary>Project-relative folder that receives the exported <c>.glb</c> files.</summary>
        public const string ModelOutputFolder = "Assets/RF/Art/Models";

        /// <summary>Seconds to wait for a Blender build before giving up.</summary>
        private const int BuildTimeoutSeconds = 600;

        /// <summary>
        /// Rebuilds every asset defined in <c>blender/assets/</c> and re-imports the results.
        /// </summary>
        [MenuItem("Tools/IronFlag/Rebuild All Art from Blender", false, 100)]
        public static void RebuildAll()
        {
            RunBuild(string.Empty);
        }

        /// <summary>
        /// Rebuilds only the assets whose name contains the name of the selected model,
        /// so a single mesh can be iterated on without rebuilding the whole set.
        /// </summary>
        [MenuItem("Assets/IronFlag/Rebuild This Model from Blender", false, 100)]
        public static void RebuildSelected()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog("IronFlag", "Select a .glb model in the Project window first.", "OK");
                return;
            }

            RunBuild(Path.GetFileNameWithoutExtension(assetPath));
        }

        /// <summary>
        /// Validates <see cref="RebuildSelected"/>: only enabled for a selected <c>.glb</c> file.
        /// </summary>
        /// <returns><c>true</c> when the active selection is a glTF binary asset.</returns>
        [MenuItem("Assets/IronFlag/Rebuild This Model from Blender", true)]
        public static bool ValidateRebuildSelected()
        {
            string assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(assetPath)
                && assetPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Prompts for the Blender executable and stores it in the editor preferences,
        /// for machines where Blender is not installed in a default location.
        /// </summary>
        [MenuItem("Tools/IronFlag/Locate Blender...", false, 120)]
        public static void LocateBlender()
        {
            string picked = EditorUtility.OpenFilePanel("Select the Blender executable", string.Empty, string.Empty);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            EditorPrefs.SetString(BlenderPathPrefKey, picked);
            Debug.Log($"IronFlag: Blender path set to {picked}");
        }

        /// <summary>
        /// Absolute path to the repository root — the folder holding <c>unity/</c> and <c>blender/</c>.
        /// </summary>
        /// <returns>The repository root path, using forward slashes.</returns>
        public static string RepositoryRoot()
        {
            // Application.dataPath is "<repo>/unity/Assets".
            DirectoryInfo unityProject = Directory.GetParent(Application.dataPath);
            return unityProject.Parent.FullName.Replace('\\', '/');
        }

        /// <summary>
        /// Resolves the Blender executable to use for asset builds.
        /// </summary>
        /// <returns>An absolute path, or an empty string when Blender could not be found.</returns>
        public static string ResolveBlenderExecutable()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable(BlenderPathEnvVar);
            if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
            {
                return fromEnvironment;
            }

            string fromPreferences = EditorPrefs.GetString(BlenderPathPrefKey, string.Empty);
            if (!string.IsNullOrEmpty(fromPreferences) && File.Exists(fromPreferences))
            {
                return fromPreferences;
            }

            foreach (string candidate in DefaultBlenderLocations())
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Enumerates the platform's default Blender install locations, newest version first.
        /// </summary>
        /// <returns>Candidate executable paths; the caller checks whether they exist.</returns>
        private static IEnumerable<string> DefaultBlenderLocations()
        {
            var candidates = new List<string>();

#if UNITY_EDITOR_WIN
            string[] roots =
            {
                @"C:\Program Files\Blender Foundation",
                @"C:\Program Files (x86)\Blender Foundation",
            };

            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                var versions = new List<string>(Directory.GetDirectories(root));
                versions.Sort(StringComparer.OrdinalIgnoreCase);
                versions.Reverse();
                foreach (string version in versions)
                {
                    candidates.Add(Path.Combine(version, "blender.exe"));
                }
            }
#elif UNITY_EDITOR_OSX
            candidates.Add("/Applications/Blender.app/Contents/MacOS/Blender");
#else
            candidates.Add("/usr/bin/blender");
            candidates.Add("/usr/local/bin/blender");
            candidates.Add("/snap/bin/blender");
#endif

            return candidates;
        }

        /// <summary>
        /// Runs the Blender build script and refreshes the asset database.
        /// </summary>
        /// <param name="assetFilter">
        /// Substring matched against asset names; an empty string builds every asset.
        /// </param>
        private static void RunBuild(string assetFilter)
        {
            string blender = ResolveBlenderExecutable();
            if (string.IsNullOrEmpty(blender))
            {
                EditorUtility.DisplayDialog(
                    "IronFlag",
                    "Could not find Blender.\n\nUse Tools > IronFlag > Locate Blender... or set the "
                        + BlenderPathEnvVar + " environment variable.",
                    "OK");
                return;
            }

            string repositoryRoot = RepositoryRoot();
            string buildScript = repositoryRoot + "/blender/build.py";
            if (!File.Exists(buildScript))
            {
                EditorUtility.DisplayDialog("IronFlag", $"Build script not found at {buildScript}", "OK");
                return;
            }

            string outputDirectory = Application.dataPath + "/RF/Art/Models";
            var arguments = new StringBuilder();
            arguments.Append("--background --factory-startup --python ");
            arguments.Append('"').Append(buildScript).Append('"');
            arguments.Append(" -- --out ");
            arguments.Append('"').Append(outputDirectory).Append('"');
            if (!string.IsNullOrEmpty(assetFilter))
            {
                arguments.Append(" --asset ").Append('"').Append(assetFilter).Append('"');
            }

            var startInfo = new ProcessStartInfo(blender, arguments.ToString())
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
                EditorUtility.DisplayProgressBar("IronFlag", "Building art assets in Blender...", 0.5f);

                // Read stdout and stderr through events rather than ReadToEnd(), which
                // deadlocks the moment Blender writes enough to the other, undrained pipe to
                // fill its OS buffer - exactly the failure path (a Python exception mid-build)
                // that most needs the timeout below to actually fire.
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
                        Debug.LogError($"IronFlag: Could not launch Blender at '{blender}'.\n{ex}");
                        EditorUtility.DisplayDialog("IronFlag", $"Could not launch Blender.\n\n{ex.Message}", "OK");
                        return;
                    }

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(BuildTimeoutSeconds * 1000))
                    {
                        process.Kill();
                        Debug.LogError($"IronFlag: Blender build timed out after {BuildTimeoutSeconds}s.");
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
                Debug.Log($"IronFlag: Blender build finished.\n{standardOutput}");
            }
            else
            {
                Debug.LogError($"IronFlag: Blender build failed (exit code {exitCode}).\n{standardOutput}\n{standardError}");
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }
    }
}

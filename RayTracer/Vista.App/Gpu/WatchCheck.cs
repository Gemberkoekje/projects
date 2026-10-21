using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vista.Output;
using Vista.Recipes;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --watch-check</c>: the M1.8 check (vista-plan §10) on the RTX 3070. It starts <c>--watch</c> as a process of its own on
/// copies of a recipe and a camera, then changes them one at a time the way a person or an agent would — the exposure, since M1.10a
/// the meter and the display curve and back (through the options file), the time of day, a camera turn, a camera move, the base
/// altitude, since M1.11 a pinned temperature and back (through the options file: the haze follows the day's humidity), the seed
/// (through the options file) and the seed back — and after each
/// render checks that the sidecar lists exactly the stages that change should rebuild. Then it renders every step again with a cold
/// <c>--preview</c> process of the same files and compares the two byte for byte: the PNG, the EXR, every debug image and the denoised
/// image. It prints the wall time of each change, from saving the file to the files being written, against the cold render's.
/// </summary>
internal static class WatchCheck
{
    /// <summary>The flag that selects this check.</summary>
    public const string Flag = "--watch-check";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --watch-check [--recipe vista-views/mountains-day.recipe.json] [--camera vista-views/aerial.camera.json]\n" +
        "                 [--seed 42] [--second-seed 7] [--width 640] [--spp 64] [--debug all] [--denoise [--denoise-device cuda]]\n" +
        "                 [--out folder]   (a --watch process changed step by step: stages rebuilt, and bit-identical to cold renders)";

    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(10);

    /// <summary>One change and the stages it must rebuild; every other stage that applies must be reused.</summary>
    private sealed record Step(string Name, Action<Files> Change, IReadOnlyList<PreviewStage> Rebuilds);

    /// <summary>The working copies the watch reads, and the seed, the meter, the display curve and a pinned temperature its options file gives.</summary>
    private sealed class Files(string recipe, string camera, string options)
    {
        public string Recipe { get; } = recipe;
        public string Camera { get; } = camera;
        public string Options { get; } = options;
        public ulong Seed { get; set; }
        public string Meter { get; set; } = "incident";
        public string Display { get; set; } = "agx";
        public string Temperature { get; set; } = string.Empty;
    }

    public static int Run(string[] args, TextWriter log)
    {
        if (!TryParse(args, out Dictionary<string, string> values, out HashSet<string> flags, out string problem))
        {
            Console.Error.WriteLine(problem);
            Console.Error.WriteLine(Usage);
            return 1;
        }
        string recipeSource = values.GetValueOrDefault("--recipe", Path.Combine("vista-views", "mountains-day.recipe.json"));
        string cameraSource = values.GetValueOrDefault("--camera", Path.Combine("vista-views", "aerial.camera.json"));
        ulong seed = ulong.Parse(values.GetValueOrDefault("--seed", "42"), CultureInfo.InvariantCulture);
        ulong secondSeed = ulong.Parse(values.GetValueOrDefault("--second-seed", "7"), CultureInfo.InvariantCulture);
        string folder = Path.GetFullPath(values.GetValueOrDefault("--out",
            Path.Combine(Path.GetTempPath(), "vista-watch-check-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture))));
        Directory.CreateDirectory(folder);

        // The options both the watch and the cold renders get; the seed is the watch's options file's, and each cold render's own.
        var common = new List<string> { "--width", values.GetValueOrDefault("--width", "640"), "--spp", values.GetValueOrDefault("--spp", "64") };
        foreach (string option in new[] { "--debug", "--denoise-device" })
        {
            if (values.TryGetValue(option, out string? value))
                common.AddRange([option, value]);
        }
        bool denoise = flags.Contains("--denoise");
        if (denoise)
            common.Add("--denoise");

        var files = new Files(Path.Combine(folder, "watch.recipe.json"), Path.Combine(folder, "watch.camera.json"), Path.Combine(folder, "watch.options"))
        {
            Seed = seed,
        };
        File.Copy(recipeSource, files.Recipe, overwrite: true);
        File.Copy(cameraSource, files.Camera, overwrite: true);
        WriteOptions(files);
        log.WriteLine($"Watch check in {folder}");
        log.WriteLine($"  {recipeSource}, {cameraSource}, seeds {seed} and {secondSeed}; {string.Join(" ", common)}");

        PreviewStage[] lit = [PreviewStage.SkyView, PreviewStage.SkyDistribution];
        PreviewStage[] denoised = denoise ? [PreviewStage.Denoise] : [];
        PreviewStage[] render = [PreviewStage.Render, .. denoised];
        PreviewStage[] image = [PreviewStage.Render, PreviewStage.ExposureMeter, .. denoised];
        PreviewStage[] mesh = [PreviewStage.Mesh, PreviewStage.AccelerationStructure];
        PreviewStage[] world = [PreviewStage.World, .. mesh];
        var steps = new List<Step>
        {
            new("first render", _ => { }, [PreviewStage.Device, .. world, PreviewStage.AtmosphereTables, PreviewStage.ScatteringOrders, .. lit, .. image]),
            new("exposure +1 EV", f => EditJson(f.Camera, j => j["exposureEv"] = Number(j["exposureEv"]) + 1.0), []),
            // Since M1.10a the meter and the curve re-expose and re-encode the kept render; the denoiser's input scale is the meter's.
            new("meter: average", f => { f.Meter = "average"; WriteOptions(f); }, [PreviewStage.ExposureMeter, .. denoised]),
            new("display: aces", f => { f.Display = "aces"; WriteOptions(f); }, []),
            new("meter: incident", f => { f.Meter = "incident"; WriteOptions(f); }, denoised),
            new("display: agx", f => { f.Display = "agx"; WriteOptions(f); }, []),
            new("time of day: goldenHour", f => EditJson(f.Recipe, j => j["time"] = "goldenHour"), [.. lit, .. image]),
            // Since M1.9 the mesh follows the camera's frustum and place, and the ground sphere it is bent onto; the incident meter
            // reads only the air's frame (the sun, the camera's altitude), which a turn keeps.
            new("camera turn: yaw +20°", f => EditJson(f.Camera, j => j["yawDegrees"] = Number(j["yawDegrees"]) + 20.0), [.. mesh, .. render]),
            new("camera move: 500 m up", f => EditJson(f.Camera, j => j["position"]!["y"] = Number(j["position"]!["y"]) + 500.0), [.. mesh, .. lit, .. image]),
            // Since M1.11 the haze follows the day's humidity, which the lapse rate moves a little with the altitude: the air's tables too.
            new("base altitude: 3000 m", f => EditJson(f.Recipe, j => j["altitude"] = 3000.0),
                [.. mesh, PreviewStage.AtmosphereTables, PreviewStage.ScatteringOrders, .. lit, .. image]),
            // A pinned temperature moves the humidity and with it the haze (M1.11): the air, not the world or the mesh.
            new("temperature: -10 C", f => { f.Temperature = "-10"; WriteOptions(f); },
                [PreviewStage.AtmosphereTables, PreviewStage.ScatteringOrders, .. lit, .. image]),
            new("temperature: auto", f => { f.Temperature = string.Empty; WriteOptions(f); },
                [PreviewStage.AtmosphereTables, PreviewStage.ScatteringOrders, .. lit, .. image]),
            new($"seed {secondSeed}", f => { f.Seed = secondSeed; WriteOptions(f); }, [.. world, .. lit, .. image]),
            new($"seed {seed} again", f => { f.Seed = seed; WriteOptions(f); }, [.. world, .. lit, .. image]),
        };

        var results = new List<(Step Step, JsonNode Sidecar, double Seconds, string Line)>();
        bool pass = true;
        var started = Stopwatch.StartNew();
        using (Process watch = Start(["--watch", "--recipe", files.Recipe, "--camera", files.Camera, "--options", files.Options,
            "--out", Path.Combine(folder, "watch.png"), .. common], out BlockingCollection<string> lines))
        {
            try
            {
                for (int n = 1; n <= steps.Count; n++)
                {
                    Step step = steps[n - 1];
                    var clock = Stopwatch.StartNew();
                    step.Change(files);
                    string line = WaitFor(lines, $"watch #{n.ToString(CultureInfo.InvariantCulture)}:", watch, log);
                    double seconds = n == 1 ? started.Elapsed.TotalSeconds : clock.Elapsed.TotalSeconds;
                    JsonNode sidecar = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "watch.json")))!;
                    Snapshot(folder, n, files, sidecar);
                    results.Add((step, sidecar, seconds, line));
                    pass &= CheckStages(log, n, step, sidecar);
                }
            }
            finally
            {
                Stop(watch, log);
            }
        }

        log.WriteLine("Cold renders of every step (a --preview process each):");
        var cold = new List<double>();
        for (int n = 1; n <= results.Count; n++)
        {
            string prefix = Path.Combine(folder, $"step{n}");
            JsonNode stepSidecar = results[n - 1].Sidecar;
            ulong stepSeed = stepSidecar["seed"]!.GetValue<ulong>();
            string[] pinned = stepSidecar["sliders"]!["pins"]!["temperature"] is JsonValue temperature
                ? ["--temperature", temperature.GetValue<double>().ToString("R", CultureInfo.InvariantCulture)]
                : [];
            var clock = Stopwatch.StartNew();
            using (Process preview = Start(["--preview", "--recipe", prefix + ".recipe.json", "--camera", prefix + ".camera.json",
                "--seed", stepSeed.ToString(CultureInfo.InvariantCulture), "--meter", stepSidecar["exposure"]!["meter"]!.GetValue<string>(),
                "--display", stepSidecar["image"]!["display"]!.GetValue<string>(), .. pinned, "--out", Path.Combine(folder, $"cold{n}.png"), .. common],
                out BlockingCollection<string> _))
            {
                if (!preview.WaitForExit(StepTimeout) || preview.ExitCode != 0)
                {
                    log.WriteLine($"  FAIL  step {n}: the cold render did not finish cleanly.");
                    pass = false;
                    cold.Add(double.NaN);
                    continue;
                }
            }
            cold.Add(clock.Elapsed.TotalSeconds);
            pass &= CompareCold(log, folder, n, results[n - 1].Sidecar);
        }

        log.WriteLine();
        log.WriteLine("| # | Change | Built | Watch: run | Watch: save to files | Cold --preview |");
        log.WriteLine("|---|---|---|---|---|---|");
        for (int i = 0; i < results.Count; i++)
        {
            (Step step, JsonNode sidecar, double seconds, _) = results[i];
            double run = sidecar["watch"]!["runSeconds"]!.GetValue<double>();
            string built = string.Join(", ", sidecar["stages"]!["built"]!.AsArray().Select(s => s!.GetValue<string>()));
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {i + 1} | {step.Name} | {(built.Length == 0 ? "nothing" : built)} | {run:0.00} s | {seconds:0.00} s | {cold[i]:0.00} s |"));
        }
        log.WriteLine(pass ? "PASS" : "FAIL");
        return pass ? 0 : 1;
    }

    private static bool TryParse(string[] args, out Dictionary<string, string> values, out HashSet<string> flags, out string problem)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        flags = new HashSet<string>(StringComparer.Ordinal);
        problem = string.Empty;
        string[] valued = ["--recipe", "--camera", "--seed", "--second-seed", "--width", "--spp", "--debug", "--denoise-device", "--out"];
        for (int k = 1; k < args.Length; k++)
        {
            if (args[k] == "--denoise")
                flags.Add(args[k]);
            else if (valued.Contains(args[k]) && k + 1 < args.Length)
                values[args[k]] = args[++k];
            else
                problem = $"Unknown or incomplete argument '{args[k]}'.";
        }
        foreach (string seed in new[] { "--seed", "--second-seed" })
        {
            if (values.TryGetValue(seed, out string? text) && !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                problem = $"{seed} must be a non-negative integer.";
        }
        return problem.Length == 0;
    }

    /// <summary>
    /// Starts this Vista.App with <paramref name="args"/>; its output lines arrive in <paramref name="lines"/>. The collection is left
    /// undisposed: the output's handlers may still run after a timed wait for the process ends.
    /// </summary>
    private static Process Start(IEnumerable<string> args, out BlockingCollection<string> lines)
    {
        string host = Environment.ProcessPath ?? throw new InvalidOperationException("The running executable is unknown.");
        var start = new ProcessStartInfo(host)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(WatchCheck).Assembly.Location);
        foreach (string arg in args)
            start.ArgumentList.Add(arg);
        var process = new Process { StartInfo = start };
        var received = new BlockingCollection<string>();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                received.Add(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                received.Add("stderr: " + e.Data);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lines = received;
        return process;
    }

    /// <summary>Waits for the line that starts with <paramref name="prefix"/>, echoing errors and warnings on the way.</summary>
    private static string WaitFor(BlockingCollection<string> lines, string prefix, Process process, TextWriter log)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < StepTimeout)
        {
            if (lines.TryTake(out string? line, TimeSpan.FromMilliseconds(200)))
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                    return line;
                if (line.StartsWith("stderr: ", StringComparison.Ordinal))
                    log.WriteLine("    watch " + line);
            }
            else if (process.HasExited)
            {
                throw new InvalidOperationException($"The watch process ended (exit code {process.ExitCode}) before '{prefix}'.");
            }
        }
        throw new TimeoutException($"No '{prefix}' from the watch within {StepTimeout.TotalMinutes} minutes.");
    }

    private static void Stop(Process watch, TextWriter log)
    {
        if (watch.HasExited)
            return;
        watch.StandardInput.WriteLine("quit");
        if (!watch.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            log.WriteLine("  The watch did not stop on quit; killed.");
            watch.Kill();
        }
    }

    /// <summary>Copies the step's inputs and the watch's outputs aside, for the cold render and the comparison.</summary>
    private static void Snapshot(string folder, int n, Files files, JsonNode sidecar)
    {
        string prefix = Path.Combine(folder, $"step{n}");
        File.Copy(files.Recipe, prefix + ".recipe.json", overwrite: true);
        File.Copy(files.Camera, prefix + ".camera.json", overwrite: true);
        foreach (string name in sidecar["outputs"]!.AsObject().Select(p => p.Value!.GetValue<string>()))
            File.Copy(Path.Combine(folder, name), prefix + name["watch".Length..], overwrite: true);
    }

    private static bool CheckStages(TextWriter log, int n, Step step, JsonNode sidecar)
    {
        IReadOnlyList<string> built = Names(sidecar["stages"]!["built"]!);
        IReadOnlyList<string> reused = Names(sidecar["stages"]!["reused"]!);
        var expected = step.Rebuilds.Select(s => JsonNamingPolicy.CamelCase.ConvertName(s.ToString())).ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<string> changes = Names(sidecar["watch"]!["changes"]!);
        string problem = !expected.SetEquals(built)
            ? $"built {string.Join(", ", built)}; expected {string.Join(", ", expected.Order(StringComparer.Ordinal))}"
            : built.Intersect(reused).Any() ? "a stage is both built and reused"
            : n > 1 && changes.Count == 0 ? "the sidecar names no change"
            : string.Empty;
        log.WriteLine($"  {(problem.Length == 0 ? "PASS" : "FAIL")}  #{n} {step.Name}: {(changes.Count == 0 ? "-" : string.Join("; ", changes))}");
        log.WriteLine($"        built {(built.Count == 0 ? "nothing" : string.Join(", ", built))}; reused {string.Join(", ", reused)}");
        if (problem.Length > 0)
            log.WriteLine($"        {problem}");
        return problem.Length == 0;
    }

    /// <summary>The watch's files of step <paramref name="n"/> against the cold render's, byte for byte, and the identity in their sidecars.</summary>
    private static bool CompareCold(TextWriter log, string folder, int n, JsonNode watched)
    {
        JsonNode cold = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, $"cold{n}.json")))!;
        var problems = new List<string>();
        JsonObject watchedOutputs = watched["outputs"]!.AsObject();
        JsonObject coldOutputs = cold["outputs"]!.AsObject();
        if (!watchedOutputs.Select(p => p.Key).ToHashSet().SetEquals(coldOutputs.Select(p => p.Key)))
            problems.Add("different outputs");
        int compared = 0;
        foreach ((string role, JsonNode? file) in watchedOutputs)
        {
            if (role == "sidecar" || coldOutputs[role] is null)
                continue;
            string name = file!.GetValue<string>();
            byte[] a = File.ReadAllBytes(Path.Combine(folder, $"step{n}" + name["watch".Length..]));
            byte[] b = File.ReadAllBytes(Path.Combine(folder, coldOutputs[role]!.GetValue<string>()));
            if (!a.AsSpan().SequenceEqual(b))
                problems.Add($"{role} differs");
            compared++;
        }
        foreach (string path in new[] { "world.heightFieldHash", "render.stats.raysTraced", "exposure.appliedEv", "exposure.meter", "image.display", "sun.elevationDegrees",
            "atmosphere.cameraAltitudeMetres" })
        {
            string a = At(watched, path);
            string b = At(cold, path);
            if (a != b)
                problems.Add($"{path} {a} against {b}");
        }
        log.WriteLine($"  {(problems.Count == 0 ? "PASS" : "FAIL")}  #{n}: {compared} files bit-identical to the cold render" +
            (problems.Count == 0 ? string.Empty : ": " + string.Join("; ", problems)));
        return problems.Count == 0;
    }

    private static string At(JsonNode node, string path)
    {
        JsonNode? current = node;
        foreach (string part in path.Split('.'))
            current = current?[part];
        return current?.ToJsonString() ?? "(none)";
    }

    private static IReadOnlyList<string> Names(JsonNode array) => [.. array.AsArray().Select(s => s!.GetValue<string>())];

    private static double Number(JsonNode? node) => node?.GetValue<double>() ?? 0.0;

    /// <summary>Edits a JSON file as an editor saves it: the whole file written beside it, then moved over it.</summary>
    private static void EditJson(string path, Action<JsonObject> edit)
    {
        JsonObject json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(json);
        Save(path, json.ToJsonString(VistaJson.Options) + "\n");
    }

    private static void WriteOptions(Files files) =>
        Save(files.Options, "# The watch check's options: the seed, the meter, the display curve and a temperature change here.\n--seed " +
            files.Seed.ToString(CultureInfo.InvariantCulture) + "\n--meter " + files.Meter + "\n--display " + files.Display + "\n" +
            (files.Temperature.Length > 0 ? "--temperature " + files.Temperature + "\n" : string.Empty));

    private static void Save(string path, string text)
    {
        string temporary = path + ".saving";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }
}

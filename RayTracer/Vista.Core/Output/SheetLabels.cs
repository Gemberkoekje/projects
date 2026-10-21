using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vista.Output;

/// <summary>
/// Labels and titles for contact sheets, read from sidecars (vista-plan §7.4). A sidecar holds both what an
/// image <i>is</i> (recipe, seed, camera, size, quality, build) and what it cost (timings, rays); only the
/// former labels it. A sheet's labels are the identity fields that differ between its cells — two renders
/// of the same view from two builds are labelled by build — and its title is the fields they share.
///
/// <para>Sidecars are read as plain JSON, not as <see cref="Sidecar"/>, so a sheet can tile renders made by
/// older or newer builds whose sidecars no longer match this one's schema.</para>
/// </summary>
public static class SheetLabels
{
    // Reading order for titles, and the short names labels use. Keys not listed sort after these.
    private static readonly (string Key, string Short)[] Known =
    [
        ("recipe.name", "name"),
        ("recipe.setPieces", "setPieces"),
        ("recipe.time", "time"),
        ("recipe.season", "season"),
        ("recipe.location", "location"),
        ("recipe.latitude", "latitude"),
        ("recipe.date", "date"),
        ("recipe.solarTime", "solarTime"),
        ("recipe.sunElevation", "sun"),
        ("seed", "seed"),
        ("camera", "camera"),
        ("camera.position", "position"),
        ("camera.yawDegrees", "yaw"),
        ("camera.pitchDegrees", "pitch"),
        ("camera.rollDegrees", "roll"),
        ("camera.focalLengthMm", "focal"),
        ("camera.sensorHeightMm", "sensor"),
        ("camera.fStop", "fStop"),
        ("camera.focusDistanceMetres", "focus"),
        ("camera.exposureEv", "exposure"),
        ("image.size", "size"),
        ("image.window", "loupe"),
        ("image.samplesPerPixel", "spp"),
        ("image.maxBounces", "bounces"),
        ("image.display", "display"),
        ("exposure.meter", "meter"),
        ("image.lightSampling", "lights"),
        ("output", "output"),
        ("atmosphere", "atmosphere"),
        ("mesh", "mesh"),
        ("build", "build"),
    ];

    /// <summary>
    /// The identity fields of a sidecar as flat key → display value: <c>recipe.*</c>, <c>seed</c>, the
    /// camera (just <c>camera = default</c> for the default camera, whose pose follows the terrain),
    /// <c>image.size</c>, <c>image.window</c> (only for a loupe), quality and light sampling, the display curve and the exposure
    /// meter (since M1.10a; older sidecars count as ACES and the average meter), how the air was evaluated, and the build's commit. A recipe field that another overrides says nothing about the image and is left out: the
    /// season under a date, the time of day under a solar time or under a sun elevation (except dawn, which says the
    /// sun is rising), and a location of "none" (no place named).
    /// </summary>
    public static SortedDictionary<string, string> Identity(JsonNode? sidecar)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (sidecar is not JsonObject root)
            return fields;

        if (root["recipe"] is JsonObject recipe)
        {
            Flatten("recipe", recipe, fields);
            if (fields.ContainsKey("recipe.date"))
                fields.Remove("recipe.season");
            if (fields.ContainsKey("recipe.solarTime"))
                fields.Remove("recipe.time");
            if (fields.ContainsKey("recipe.sunElevation") && fields.GetValueOrDefault("recipe.time") != "dawn")
                fields.Remove("recipe.time");
            if (fields.GetValueOrDefault("recipe.location") == "none")
                fields.Remove("recipe.location");
        }
        if (root["seed"] is JsonValue seed)
            fields["seed"] = Display(seed);

        if (root["defaultCamera"] is JsonValue isDefault && isDefault.GetValueKind() == JsonValueKind.True)
        {
            fields["camera"] = "default";
        }
        else if (root["camera"] is JsonObject camera)
        {
            Flatten("camera", camera, fields);
            if (camera["position"] is JsonObject p)
            {
                foreach (string axis in new[] { "x", "y", "z" })
                    fields.Remove("camera.position." + axis);
                fields["camera.position"] = $"{Display(p["x"])},{Display(p["y"])},{Display(p["z"])}";
            }
        }

        if (root["image"] is JsonObject image)
        {
            int width = Int(image["width"]);
            int height = Int(image["height"]);
            fields["image.size"] = string.Create(CultureInfo.InvariantCulture, $"{width}×{height}");
            if (image["window"] is JsonObject w
                && !(Int(w["x"]) == 0 && Int(w["y"]) == 0 && Int(w["width"]) == width && Int(w["height"]) == height))
                fields["image.window"] = string.Create(CultureInfo.InvariantCulture, $"{Int(w["x"])},{Int(w["y"])},{Int(w["width"])},{Int(w["height"])}");
            foreach (string key in new[] { "samplesPerPixel", "maxBounces", "lightSampling" })
            {
                if (image[key] is JsonValue v)
                    fields["image." + key] = Display(v);
            }
            // The display curve (schema 11); every build before it used the ACES fit.
            if (image["display"] is JsonValue display)
                fields["image.display"] = Display(display);
            else if (image["displayTransform"] is not null)
                fields["image.display"] = "aces";
        }

        // How the PNG was metered (schema 11); every build before it used the average meter.
        if (root["exposure"] is JsonObject exposure)
            fields["exposure.meter"] = exposure["meter"] is JsonValue meter ? Display(meter) : "average";

        if (root["atmosphere"]?["mode"] is JsonValue mode)
            fields["atmosphere"] = Display(mode);

        // How the terrain was meshed (schema 10): "view 1 px", or "uniform", with what was left out.
        if (root["world"]?["mesh"] is JsonObject mesh && mesh["kind"] is JsonValue kind)
        {
            string text = Display(kind);
            if (text == "view")
            {
                text += string.Create(CultureInfo.InvariantCulture, $" {Display(mesh["errorPixels"])} px");
                if (mesh["detail"] is JsonValue d && d.GetValueKind() == JsonValueKind.False)
                    text += ", no detail";
                if (mesh["farField"] is JsonValue f && f.GetValueKind() == JsonValueKind.False)
                    text += ", no far field";
            }
            fields["mesh"] = text;
        }

        if (root["engine"]?["vistaVersion"] is JsonValue version)
            fields["build"] = Build(version.ToString());
        return fields;
    }

    /// <summary>
    /// <see cref="Identity(JsonNode?)"/> of the image at <paramref name="imagePath"/>, plus <c>output</c>: the role the sidecar's
    /// <c>outputs</c> gives that file (<c>beauty</c>, <c>denoised</c>, <c>normal</c>, …), so a raw and a denoised image of one
    /// render, which share a sidecar, are told apart.
    /// </summary>
    public static SortedDictionary<string, string> Identity(JsonNode? sidecar, string imagePath)
    {
        ArgumentNullException.ThrowIfNull(imagePath);
        SortedDictionary<string, string> fields = Identity(sidecar);
        string name = Path.GetFileName(imagePath);
        if (sidecar?["outputs"] is JsonObject outputs)
        {
            foreach ((string role, JsonNode? file) in outputs)
            {
                if (file is JsonValue value && value.GetValueKind() == JsonValueKind.String
                    && string.Equals(value.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase))
                {
                    fields["output"] = role;
                    break;
                }
            }
        }
        return fields;
    }

    /// <summary>
    /// One label per cell: the identity fields that are not the same in every cell, as <c>short=value</c>. A
    /// field a cell lacks counts as different. Fields with the fewest distinct values come first, then reading
    /// order: in a before/after of several views, <c>build=</c> (two values) leads every label, so it survives
    /// when a long label is cut. Empty labels mean the cells are identical.
    /// </summary>
    public static IReadOnlyList<string> Differences(IReadOnlyList<IReadOnlyDictionary<string, string>> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        int Distinct(string key) => identities.Select(i => i.GetValueOrDefault(key)).Distinct().Count();
        List<string> differing = Ordered(identities.SelectMany(i => i.Keys).Distinct())
            .Where(key => Distinct(key) > 1)
            .OrderBy(Distinct)
            .ToList();
        return identities
            .Select(i => string.Join(" ", differing.Select(key => $"{Short(key)}={i.GetValueOrDefault(key, "-")}")))
            .ToList();
    }

    /// <summary>
    /// A sheet title from the identity fields every cell shares, in reading order, joined by <c>" · "</c>,
    /// e.g. <c>alpine range, golden hour · snowCappedMountains · goldenHour · summer · seed 42 · default camera ·
    /// 320×180 · 64 spp · 6 bounces · build 566b68d</c>. Empty when nothing is shared.
    /// </summary>
    public static string Title(IReadOnlyList<IReadOnlyDictionary<string, string>> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        if (identities.Count == 0)
            return string.Empty;
        IEnumerable<string> shared = identities[0].Keys
            .Where(key => identities.All(i => i.TryGetValue(key, out string? v) && v == identities[0][key]));
        var parts = new List<string>();
        foreach (string key in Ordered(shared))
        {
            string value = identities[0][key];
            string? part = key switch
            {
                "recipe.name" or "recipe.time" or "recipe.season" or "recipe.location" or "recipe.date" or "image.size" => value,
                "recipe.solarTime" => $"{value} solar",
                "recipe.sunElevation" => $"sun at {value}°",
                "atmosphere" => value == "luts" ? null : $"{value} atmosphere",
                "image.lightSampling" => value == "mis" ? null : $"{value} light sampling",
                "output" => value == "beauty" ? null : value,
                "recipe.latitude" => $"latitude {value}°",
                "recipe.setPieces" => value.Length == 0 ? "no set pieces" : value,
                "seed" => $"seed {value}",
                "camera" => $"{value} camera",
                "camera.position" => $"camera at {value}",
                "camera.yawDegrees" => $"yaw {value}°",
                "camera.pitchDegrees" => $"pitch {value}°",
                "camera.rollDegrees" => value == "0" ? null : $"roll {value}°",
                "camera.focalLengthMm" => $"{value} mm",
                "camera.exposureEv" => value == "0" ? null : $"{value} EV",
                "camera.sensorHeightMm" or "camera.fStop" or "camera.focusDistanceMetres" => null,
                "image.display" => value == "agx" ? null : $"{value} curve",
                "exposure.meter" => value == "incident" ? null : $"{value} meter",
                "image.window" => $"loupe {value}",
                "image.samplesPerPixel" => $"{value} spp",
                "image.maxBounces" => $"{value} bounces",
                "build" => $"build {value}",
                "mesh" => $"{value} mesh",
                _ => $"{Short(key)} {value}",
            };
            if (part is not null)
                parts.Add(part);
        }
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// The sidecar of an image: <c>a.png</c> → <c>a.json</c>, <c>a.normal.png</c> → <c>a.normal.json</c>, then
    /// <c>a.json</c> (dropping one dotted part at a time). Null when there is none.
    /// </summary>
    public static string? SidecarPathFor(string imagePath)
    {
        ArgumentNullException.ThrowIfNull(imagePath);
        string directory = Path.GetDirectoryName(Path.GetFullPath(imagePath))!;
        string name = Path.GetFileNameWithoutExtension(imagePath);
        while (true)
        {
            string candidate = Path.Combine(directory, name + ".json");
            if (File.Exists(candidate))
                return candidate;
            int dot = name.LastIndexOf('.');
            if (dot <= 0)
                return null;
            name = name[..dot];
        }
    }

    private static void Flatten(string prefix, JsonObject obj, SortedDictionary<string, string> fields)
    {
        foreach ((string name, JsonNode? value) in obj)
        {
            string key = prefix + "." + name;
            switch (value)
            {
                case null:
                    break;
                case JsonObject child:
                    Flatten(key, child, fields);
                    break;
                case JsonArray array:
                    fields[key] = string.Join("+", array.Select(Display));
                    break;
                default:
                    if (name != "schema")
                        fields[key] = Display(value);
                    break;
            }
        }
    }

    private static string Display(JsonNode? node) => node switch
    {
        null => "null",
        JsonValue v when v.GetValueKind() == JsonValueKind.Number && v.TryGetValue(out double d) =>
            d.ToString("G6", CultureInfo.InvariantCulture),
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        _ => node.ToJsonString(),
    };

    private static int Int(JsonNode? node) => node is JsonValue v && v.TryGetValue(out int i) ? i : 0;

    private static string Build(string version)
    {
        int plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
            return version;
        string commit = version[(plus + 1)..];
        return commit.Length > 7 ? commit[..7] : commit;
    }

    private static string Short(string key)
    {
        foreach ((string k, string s) in Known)
        {
            if (k == key)
                return s;
        }
        return key[(key.LastIndexOf('.') + 1)..];
    }

    private static IEnumerable<string> Ordered(IEnumerable<string> keys) =>
        keys.OrderBy(k => Array.FindIndex(Known, x => x.Key == k) is var i && i >= 0 ? i : Known.Length)
            .ThenBy(k => k, StringComparer.Ordinal);
}

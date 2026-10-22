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
        ("sliders.time", "time"),
        ("sliders.season", "season"),
        ("sliders.location", "location"),
        ("sliders.latitude", "latitude"),
        ("sliders.date", "date"),
        ("sliders.solarTime", "solarTime"),
        ("sliders.sunElevation", "sun"),
        ("sliders.altitude", "altitude"),
        ("sliders.temperature", "temperature"),
        ("sliders.humidity", "humidity"),
        ("sliders.snowline", "snow"),
        ("sliders.visibility", "visibility"),
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
        ("image.look", "look"),
        ("image.dither", "dither"),
        ("image.bitDepth", "bits"),
        ("exposure.meter", "meter"),
        ("exposure.meterCompensationEv", "compensation"),
        ("image.lightSampling", "lights"),
        ("output", "output"),
        ("atmosphere", "atmosphere"),
        ("mesh", "mesh"),
        ("build", "build"),
    ];

    // The fields of a recipe before schema 5 that pinned a slider, under the slider file's names.
    private static readonly string[] RecipePins = ["time", "season", "location", "latitude", "date", "solarTime", "sunElevation", "altitude"];

    /// <summary>
    /// The identity fields of a sidecar as flat key → display value: <c>recipe.*</c>, the pinned sliders as <c>sliders.*</c> (since
    /// schema 12, the slider pins; before it, the recipe's own place and time fields, which pinned them), <c>seed</c>, the
    /// camera (just <c>camera = default</c> for the default camera, whose pose follows the terrain),
    /// <c>image.size</c>, <c>image.window</c> (only for a loupe), quality and light sampling, the display curve and the exposure
    /// meter (since M1.10a; older sidecars count as ACES and the average meter), how the air was evaluated, and the build's commit.
    /// Auto sliders are left out (they follow the rest) but for the time of day and the season, which every sidecar names; so is a
    /// pin that another overrides, which says nothing about the image:
    /// the season under a date, the time of day under a solar time or under a sun elevation (except dawn, which says the sun is
    /// rising), and a location of "none" (no place named).
    /// </summary>
    public static SortedDictionary<string, string> Identity(JsonNode? sidecar)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (sidecar is not JsonObject root)
            return fields;

        if (root["recipe"] is JsonObject recipe)
        {
            Flatten("recipe", recipe, fields);
            // A recipe's own place and time fields pin sliders (and were the only pins before schema 12), as a slider file's do.
            foreach (string pin in RecipePins)
            {
                if (fields.Remove("recipe." + pin, out string? value))
                    fields["sliders." + pin] = value;
            }
        }
        // The slider file's pins (schema 12), options included; an auto slider follows the others and says nothing more.
        if (root["sliders"]?["pins"] is JsonObject pins)
        {
            Flatten("sliders", pins, fields);
            fields.Remove("sliders.temperatureUnit");
            // The time of day and the season name the picture whether pinned or auto, as older sidecars' recipes always did.
            foreach (string slider in new[] { "time", "season" })
            {
                if (root["sliders"]?["values"]?[slider]?["value"] is JsonValue value)
                    fields["sliders." + slider] = Display(value);
            }
        }
        if (fields.ContainsKey("sliders.date"))
            fields.Remove("sliders.season");
        if (fields.ContainsKey("sliders.solarTime"))
            fields.Remove("sliders.time");
        if (fields.ContainsKey("sliders.sunElevation") && fields.GetValueOrDefault("sliders.time") != "dawn")
            fields.Remove("sliders.time");
        if (fields.GetValueOrDefault("sliders.location") == "none")
            fields.Remove("sliders.location");
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
            // AgX's look (schema 14); every AgX before it was the base.
            if (image["look"] is JsonValue look)
                fields["image.look"] = Display(look);
            else if (fields.GetValueOrDefault("image.display") == "agx")
                fields["image.look"] = "base";
            // How the PNG was quantised (schema 15); every build before it rounded to 8 bits with no dither.
            fields["image.dither"] = image["dither"] is JsonValue dither ? Display(dither) : "off";
            fields["image.bitDepth"] = image["bitDepth"] is JsonValue bits ? Display(bits) : "8";
        }

        // How the PNG was metered (schema 11); every build before it used the average meter.
        if (root["exposure"] is JsonObject exposure)
        {
            fields["exposure.meter"] = exposure["meter"] is JsonValue meter ? Display(meter) : "average";
            // The meter's own stops (schema 14); none before it.
            fields["exposure.meterCompensationEv"] = exposure["meterCompensationEv"] is JsonValue compensation ? Display(compensation) : "0";
        }

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
                "recipe.name" or "sliders.time" or "sliders.season" or "sliders.location" or "sliders.date" or "image.size" => value,
                "sliders.solarTime" => $"{value} solar",
                "sliders.sunElevation" => $"sun at {value}°",
                "sliders.altitude" => $"at {value} m",
                "sliders.temperature" => $"{value} °C",
                "sliders.humidity" => $"{value}% humidity",
                "sliders.snowline" => $"snow from {value} m",
                "sliders.visibility" => $"{value} km visibility",
                "atmosphere" => value == "luts" ? null : $"{value} atmosphere",
                "image.lightSampling" => value == "mis" ? null : $"{value} light sampling",
                "output" => value == "beauty" ? null : value,
                "sliders.latitude" => $"latitude {value}°",
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
                "image.look" => value == "none" || value == DisplayTransform.DefaultLook.ToString().ToLowerInvariant() ? null : $"{value} look",
                "exposure.meterCompensationEv" => $"{(value.StartsWith('-') ? value : "+" + value)} EV meter",
                "image.dither" => value == "off" ? "no dither" : null,
                "image.bitDepth" => value == "8" ? null : $"{value}-bit",
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

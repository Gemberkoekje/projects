namespace TheCuratool.Infrastructure.Repositories;

internal static class StandardScriptCatalog
{
    private static readonly IReadOnlyList<StandardScriptDefinition> Scripts = CreateScripts();

    public static IReadOnlyList<StandardScriptDefinition> GetAll()
    {
        return Scripts;
    }

    public static bool TryGetById(Guid id, out StandardScriptDefinition script)
    {
        var match = Scripts.FirstOrDefault(candidate => candidate.Id == id);
        if (match is StandardScriptDefinition definition)
        {
            script = definition;
            return true;
        }

        script = StandardScriptDefinition.Empty;
        return false;
    }

    private static IReadOnlyList<StandardScriptDefinition> CreateScripts()
    {
        return new List<StandardScriptDefinition>
        {
            CreateScript(
                "0d5d99b6-4f57-40b8-b4f4-8fd84ef2a101",
                "Trouble Brewing",
                string.Empty,
                new[]
                {
                    "washerwoman",
                    "librarian",
                    "investigator",
                    "chef",
                    "empath",
                    "fortuneteller",
                    "undertaker",
                    "monk",
                    "ravenkeeper",
                    "virgin",
                    "slayer",
                    "soldier",
                    "mayor",
                    "butler",
                    "drunk",
                    "recluse",
                    "saint",
                    "poisoner",
                    "spy",
                    "scarletwoman",
                    "baron",
                    "imp",
                }),
            CreateScript(
                "4b0a2e36-5d2e-49d1-94f0-7e031f493f54",
                "Bad Moon Rising",
                string.Empty,
                new[]
                {
                    "grandmother",
                    "sailor",
                    "chambermaid",
                    "exorcist",
                    "innkeeper",
                    "gambler",
                    "gossip",
                    "courtier",
                    "professor",
                    "minstrel",
                    "tealady",
                    "pacifist",
                    "fool",
                    "goon",
                    "lunatic",
                    "tinker",
                    "moonchild",
                    "godfather",
                    "devilsadvocate",
                    "assassin",
                    "mastermind",
                    "zombuul",
                    "pukka",
                    "shabaloth",
                    "po",
                }),
            CreateScript(
                "6807f613-5a45-4bb8-b1eb-143f2f57cf7d",
                "Sects and Violets",
                string.Empty,
                new[]
                {
                    "clockmaker",
                    "dreamer",
                    "snakecharmer",
                    "mathematician",
                    "flowergirl",
                    "towncrier",
                    "oracle",
                    "savant",
                    "seamstress",
                    "philosopher",
                    "artist",
                    "juggler",
                    "sage",
                    "mutant",
                    "sweetheart",
                    "barber",
                    "klutz",
                    "eviltwin",
                    "witch",
                    "cerenovus",
                    "pithag",
                    "fanggu",
                    "vigormortis",
                    "nodashii",
                    "vortox",
                }),
        }.AsReadOnly();
    }

    private static StandardScriptDefinition CreateScript(string id, string name, string author, IReadOnlyList<string> characterIds)
    {
        return new StandardScriptDefinition(Guid.Parse(id), name, author, BuildRawJson(name, author, characterIds));
    }

    private static string BuildRawJson(string name, string author, IReadOnlyList<string> characterIds)
    {
        var entries = new List<object>(characterIds.Count + 1)
        {
            new Dictionary<string, string>
            {
                ["id"] = "_meta",
                ["name"] = name,
                ["author"] = author,
            },
        };

        entries.AddRange(characterIds);
        return JsonSerializer.Serialize(entries);
    }

    internal sealed record StandardScriptDefinition(Guid Id, string Name, string Author, string RawJson)
    {
        public static readonly StandardScriptDefinition Empty = new(Guid.Empty, string.Empty, string.Empty, string.Empty);
    }
}

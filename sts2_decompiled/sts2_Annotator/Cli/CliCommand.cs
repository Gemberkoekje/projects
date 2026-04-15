namespace Sts2Extractor.Cli;

internal enum CliCommand
{
    None = 0,
    Audit = 1,
    ExtractCards = 2,
    SeedTaxonomy = 3,
    Annotate = 4,
    ListModels = 5,
    ExtractRelics = 6,
    ExtractPotions = 7,
    ExtractEvents = 8,
    GenerateRatings = 9,
    SyncPostgres = 10,
    ExtractPowers = 11,
    InitDatabase = 12,
    BuildSynergyEdges = 13,
    AdvisePick = 14,
    BuildSynergyClusters = 15,
    ExtractCharacters = 16,
    DiscoverArchetypes = 17,
    ScoreAffinities = 18
}

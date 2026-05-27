namespace TheCuratool.Infrastructure.Repositories;

using TheCuratool.Application;
using TheCuratool.Application.Abstractions.Repositories;

public sealed class ScriptRepository(CuratoolDbContext dbContext, CharacterDatabase characterDatabase) : IScriptRepository
{
    private readonly ScriptParser _parser = new();

    public async Task<StoredScript> AddAsync(string name, string author, string rawJson, CancellationToken cancellationToken = default)
    {
        var scriptEntity = new ScriptEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            Author = author,
            RawJson = rawJson,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Scripts.Add(scriptEntity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapStoredScript(scriptEntity);
    }

    public async Task<StoredScript> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (StandardScriptCatalog.TryGetById(id, out var standardScript))
        {
            return MapStoredScript(standardScript.Id, standardScript.Name, standardScript.Author, standardScript.RawJson);
        }

        var entity = await dbContext.Scripts.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (entity == null)
        {
            throw new InvalidOperationException($"Script with id {id} not found.");
        }

        return MapStoredScript(entity);
    }

    public async Task<IReadOnlyList<StoredScript>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.Scripts
            .OrderBy(script => script.CreatedAt)
            .ToListAsync(cancellationToken);

        var standardScripts = StandardScriptCatalog.GetAll();
        var standardIds = standardScripts
            .Select(script => script.Id)
            .ToHashSet();

        var storedScripts = standardScripts
            .Select(script => MapStoredScript(script.Id, script.Name, script.Author, script.RawJson))
            .Concat(entities
                .Where(entity => !standardIds.Contains(entity.Id))
                .Select(MapStoredScript))
            .ToList();

        return storedScripts.AsReadOnly();
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private StoredScript MapStoredScript(ScriptEntity entity)
    {
        return MapStoredScript(entity.Id, entity.Name, entity.Author, entity.RawJson);
    }

    private StoredScript MapStoredScript(Guid id, string name, string author, string rawJson)
    {
        var result = _parser.Parse(rawJson, characterDatabase);
        var script = result.IsSuccess
            ? result.Script
            : new Script(name, author, Array.Empty<CharacterDefinition>());

        return new StoredScript(id, script, rawJson);
    }
}

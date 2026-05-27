using Microsoft.EntityFrameworkCore;
using TheCuratool.Application;
using TheCuratool.Infrastructure.Data;
using TheCuratool.Infrastructure.Repositories;

namespace TheCuratool.UnitTests;

public sealed class ScriptPersistenceTests : IDisposable
{
    private readonly CuratoolDbContext _dbContext;
    private readonly ScriptRepository _repository;
    private readonly CharacterDatabase _characterDatabase;

    public ScriptPersistenceTests()
    {
        var options = new DbContextOptionsBuilder<CuratoolDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new CuratoolDbContext(options);
        _characterDatabase = CharacterDatabase.LoadFromFile(GetCharactersFilePath());
        _repository = new ScriptRepository(_dbContext, _characterDatabase);
    }

    [Fact]
    public async Task AddAsync_ShouldPersistScript()
    {
        var scriptJson = File.ReadAllText("NoVortox.json");

        var storedScript = await _repository.AddAsync("No Vortox", "Gemberkoekje", scriptJson);

        Assert.NotNull(storedScript);
        Assert.NotEqual(Guid.Empty, storedScript.Id);
        Assert.Equal("No Vortox", storedScript.Script.Name);
        Assert.Equal("Gemberkoekje", storedScript.Script.Author);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldRetrievePersistedScript()
    {
        // Arrange
        var scriptJson = File.ReadAllText("NoVortox.json");
        var addedScript = await _repository.AddAsync("No Vortox", "Gemberkoekje", scriptJson);

        var retrievedScript = await _repository.GetByIdAsync(addedScript.Id);

        Assert.NotNull(retrievedScript);
        Assert.Equal(addedScript.Id, retrievedScript.Id);
        Assert.Equal("No Vortox", retrievedScript.Script.Name);
        Assert.Equal("Gemberkoekje", retrievedScript.Script.Author);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnBuiltInAndPersistedScripts()
    {
        var scriptJson = File.ReadAllText("NoVortox.json");
        await _repository.AddAsync("No Vortox", "Gemberkoekje", scriptJson);

        var scripts = await _repository.GetAllAsync();

        Assert.NotNull(scripts);
        Assert.Equal(4, scripts.Count);
        Assert.Contains(scripts, script => script.Script.Name == "Trouble Brewing");
        Assert.Contains(scripts, script => script.Script.Name == "Bad Moon Rising");
        Assert.Contains(scripts, script => script.Script.Name == "Sects and Violets");
        Assert.Contains(scripts, script => script.Script.Name == "No Vortox");
    }

    [Fact]
    public async Task GetByIdAsync_StandardScriptId_ShouldReturnStandardScript()
    {
        var scripts = await _repository.GetAllAsync();
        var standardScript = Assert.Single(scripts.Where(script => script.Script.Name == "Trouble Brewing"));

        var retrievedScript = await _repository.GetByIdAsync(standardScript.Id);

        Assert.Equal(standardScript.Id, retrievedScript.Id);
        Assert.Equal("Trouble Brewing", retrievedScript.Script.Name);
        Assert.Equal(22, retrievedScript.Script.Characters.Count);
        Assert.Contains(retrievedScript.Script.Characters, character => character.Id == "imp");
    }

    private static string GetCharactersFilePath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "characters.json"));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}

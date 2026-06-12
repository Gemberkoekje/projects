namespace AdventureEngine.Infrastructure;

internal static class MartenConfiguration
{
    public static StoreOptions Configure(string connectionString)
    {
        var options = new StoreOptions();
        options.Connection(connectionString);
        options.AutoCreateSchemaObjects = AutoCreate.All;

        options.Events.AddEventType<SessionCreated>();
        options.Events.AddEventType<ChapterStarted>();
        options.Events.AddEventType<SceneEntered>();
        options.Events.AddEventType<PlayerActed>();
        options.Events.AddEventType<NpcSpoke>();
        options.Events.AddEventType<ChapterCompleted>();
        options.Events.AddEventType<GameEnded>();

        options.Projections.Snapshot<GameSession>(SnapshotLifecycle.Async);

        return options;
    }
}

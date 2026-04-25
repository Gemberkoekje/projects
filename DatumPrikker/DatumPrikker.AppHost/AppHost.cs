var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");
var postgres = builder.AddPostgres("postgres").AddDatabase("datumprikker");

var apiService = builder.AddProject<Projects.DatumPrikker_ApiService>("apiservice")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(postgres)
    .WithHttpHealthCheck("/health")
    .WaitFor(postgres);

builder.AddProject<Projects.DatumPrikker_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();

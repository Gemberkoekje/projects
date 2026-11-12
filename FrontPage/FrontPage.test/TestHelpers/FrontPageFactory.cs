using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FrontPage.test.TestHelpers;

/// <summary>The real host, with the cluster replaced by <see cref="Reader"/>.</summary>
internal sealed class FrontPageFactory : WebApplicationFactory<Program>
{
    public FakeClusterFactsReader Reader { get; } = new FakeClusterFactsReader();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClusterFactsReader>();
            services.AddSingleton<IClusterFactsReader>(Reader);
        });
}

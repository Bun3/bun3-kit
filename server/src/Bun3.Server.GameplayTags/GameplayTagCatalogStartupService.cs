using Bun3.Gameplay.Tags;
using Microsoft.Extensions.Hosting;

namespace Bun3.Server.GameplayTags;

internal sealed class GameplayTagCatalogStartupService : IHostedService
{
    public GameplayTagCatalogStartupService(TagCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

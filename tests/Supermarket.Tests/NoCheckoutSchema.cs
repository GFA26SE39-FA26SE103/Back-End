using Supermarket.Application;

namespace Supermarket.Tests;

// Legacy in-memory API factories must never query the machine's configured SQL server.
internal sealed class NoCheckoutSchema : ICheckoutSchema
{
    public Task<bool> Ready(CancellationToken ct) => Task.FromResult(false);
}

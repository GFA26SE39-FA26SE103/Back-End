namespace Supermarket.Application;

public interface IReadCache
{
    Task<T> GetOrCreate<T>(string key, TimeSpan lifetime, Func<CancellationToken, Task<T>> load, CancellationToken ct)
        where T : class;

    Task Invalidate(string key, TimeSpan lifetime);
}

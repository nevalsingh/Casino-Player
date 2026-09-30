using Microsoft.Extensions.Options;
using OT.Assessment.Core.Options;
using RabbitMQ.Client;

namespace OT.Assessment.Infrastructure.RabbitMq;

/// <summary>
/// Owns the single long-lived <see cref="IConnection"/> for the process, opened on first use. Automatic
/// recovery reconnects it (and its channels) after a broker restart or network drop.
/// </summary>
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly Lazy<Task<IConnection>> _connection;

    public RabbitMqConnectionProvider(IOptions<RabbitMqOptions> options)
    {
        var settings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            UserName = settings.UserName,
            Password = settings.Password,
            VirtualHost = settings.VirtualHost,
            AutomaticRecoveryEnabled = true,
        };

        _connection = new Lazy<Task<IConnection>>(
            () => factory.CreateConnectionAsync(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken) =>
        _connection.Value.WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
            await (await _connection.Value).CloseAsync();
    }
}
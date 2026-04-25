using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace FrigateRelay.IntegrationTests.Fixtures;

internal sealed class MosquittoFixture : IAsyncDisposable
{
    private readonly IContainer _container;

    public MosquittoFixture()
    {
        var conf = "listener 1883\nallow_anonymous true\n";
        _container = new ContainerBuilder()
            .WithImage("eclipse-mosquitto:2")
            .WithPortBinding(1883, true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(conf), "/mosquitto/config/mosquitto.conf")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(1883))
            .Build();
    }

    public string Hostname => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(1883);

    public ValueTask InitializeAsync() => new(_container.StartAsync());
    public async ValueTask DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}

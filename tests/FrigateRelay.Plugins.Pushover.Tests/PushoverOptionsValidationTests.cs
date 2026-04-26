using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.Pushover;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FrigateRelay.Plugins.Pushover.Tests;

[TestClass]
public class PushoverOptionsValidationTests
{
    private static IHost BuildHost(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var ctx = new PluginRegistrationContext(services, configuration);
        new PluginRegistrar().Register(ctx);

        return new HostBuilder()
            .ConfigureServices(s =>
            {
                foreach (var sd in services)
                    s.Add(sd);
            })
            .Build();
    }

    private static Dictionary<string, string?> ValidConfig(
        string appToken = "a-valid-token",
        string userKey = "a-valid-key",
        string messageTemplate = "{label} detected on {camera}",
        int priority = 0) =>
        new()
        {
            ["Pushover:AppToken"] = appToken,
            ["Pushover:UserKey"] = userKey,
            ["Pushover:MessageTemplate"] = messageTemplate,
            ["Pushover:Priority"] = priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

    [TestMethod]
    public async Task Validation_EmptyAppToken_FailsFast()
    {
        var cfg = ValidConfig(appToken: "");
        using var host = BuildHost(cfg);

        var act = async () => await host.StartAsync();
        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [TestMethod]
    public async Task Validation_EmptyUserKey_FailsFast()
    {
        var cfg = ValidConfig(userKey: "");
        using var host = BuildHost(cfg);

        var act = async () => await host.StartAsync();
        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [TestMethod]
    public async Task Validation_MessageTemplateUnknownToken_FailsFast()
    {
        var cfg = ValidConfig(messageTemplate: "{score} too high");
        using var host = BuildHost(cfg);

        var act = async () => await host.StartAsync();
        await act.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage("*{score}*")
            .Where(ex => ex.Message.Contains("Pushover.MessageTemplate"));
    }

    [TestMethod]
    public async Task Validation_PriorityOutOfRange_FailsFast()
    {
        var cfg = ValidConfig(priority: 3);
        using var host = BuildHost(cfg);

        var act = async () => await host.StartAsync();
        await act.Should().ThrowAsync<OptionsValidationException>();
    }
}

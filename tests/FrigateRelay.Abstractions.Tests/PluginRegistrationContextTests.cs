using FluentAssertions;
using FrigateRelay.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace FrigateRelay.Abstractions.Tests;

[TestClass]
public sealed class PluginRegistrationContextTests
{
    [TestMethod]
    public void Context_ExposesServicesAndConfiguration()
    {
        var services = Substitute.For<IServiceCollection>();
        var configuration = Substitute.For<IConfiguration>();

        var context = new PluginRegistrationContext(services, configuration);

        context.Services.Should().BeSameAs(services);
        context.Configuration.Should().BeSameAs(configuration);
    }
}

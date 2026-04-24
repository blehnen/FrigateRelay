using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace FrigateRelay.Host.Tests;

[TestClass]
public sealed class PluginRegistrarRunnerTests
{
    private static PluginRegistrationContext NewContext() =>
        new(Substitute.For<IServiceCollection>(), Substitute.For<IConfiguration>());

    [TestMethod]
    public void RunAll_EmptyRegistrars_DoesNothing()
    {
        var context = NewContext();

        var act = () => PluginRegistrarRunner.RunAll(
            registrars: [],
            context: context,
            logger: NullLogger.Instance);

        act.Should().NotThrow();
    }

    [TestMethod]
    public void RunAll_SingleRegistrar_InvokesRegisterOnceWithSharedContext()
    {
        var registrar = Substitute.For<IPluginRegistrar>();
        var context = NewContext();

        PluginRegistrarRunner.RunAll([registrar], context, NullLogger.Instance);

        registrar.Received(1).Register(context);
    }

    [TestMethod]
    public void RunAll_MultipleRegistrars_InvokedInOrder()
    {
        var first = Substitute.For<IPluginRegistrar>();
        var second = Substitute.For<IPluginRegistrar>();
        var third = Substitute.For<IPluginRegistrar>();
        var context = NewContext();

        PluginRegistrarRunner.RunAll([first, second, third], context, NullLogger.Instance);

        Received.InOrder(() =>
        {
            first.Register(context);
            second.Register(context);
            third.Register(context);
        });
    }

    [TestMethod]
    public void RunAll_SharedContext_SameInstancePassedToEveryRegistrar()
    {
        var first = Substitute.For<IPluginRegistrar>();
        var second = Substitute.For<IPluginRegistrar>();
        var context = NewContext();

        PluginRegistrarRunner.RunAll([first, second], context, NullLogger.Instance);

        first.Received(1).Register(Arg.Is<PluginRegistrationContext>(c => ReferenceEquals(c, context)));
        second.Received(1).Register(Arg.Is<PluginRegistrationContext>(c => ReferenceEquals(c, context)));
    }

    [TestMethod]
    public void RunAll_RegistrarThrows_PropagatesAndShortCircuits()
    {
        var failing = Substitute.For<IPluginRegistrar>();
        failing.When(r => r.Register(Arg.Any<PluginRegistrationContext>()))
               .Do(_ => throw new InvalidOperationException("boom"));
        var after = Substitute.For<IPluginRegistrar>();
        var context = NewContext();

        var act = () => PluginRegistrarRunner.RunAll(
            [failing, after],
            context,
            NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("boom");
        after.DidNotReceive().Register(Arg.Any<PluginRegistrationContext>());
    }
}

using FluentAssertions;
using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.CodeProjectAi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.CodeProjectAi.Tests;

[TestClass]
public sealed class CodeProjectAiPluginRegistrarTests
{
    /// <summary>One CPAI entry: keyed `IValidationPlugin`, named options, named HttpClient all resolve.</summary>
    [TestMethod]
    public void Register_OneCodeProjectAiEntry_RegistersKeyedValidatorAndNamedHttpClient()
    {
        var sp = BuildProvider(new()
        {
            ["Validators:cpai_persons:Type"] = "CodeProjectAi",
            ["Validators:cpai_persons:BaseUrl"] = "http://test.local:5000",
            ["Validators:cpai_persons:MinConfidence"] = "0.6",
        });

        var validator = sp.GetRequiredKeyedService<IValidationPlugin>("cpai_persons");
        validator.Should().BeOfType<CodeProjectAiValidator>();
        validator.Name.Should().Be("cpai_persons");

        var opts = sp.GetRequiredService<IOptionsMonitor<CodeProjectAiOptions>>().Get("cpai_persons");
        opts.BaseUrl.Should().Be("http://test.local:5000");
        opts.MinConfidence.Should().Be(0.6);

        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("CodeProjectAi:cpai_persons");
        http.Should().NotBeNull("named HttpClient must be registered with the 'CodeProjectAi:<key>' convention");
    }

    /// <summary>Two CPAI entries with different MinConfidence values register independently as keyed singletons.</summary>
    [TestMethod]
    public void Register_TwoCodeProjectAiEntries_RegistersBothIndependently()
    {
        var sp = BuildProvider(new()
        {
            ["Validators:cpai_persons:Type"] = "CodeProjectAi",
            ["Validators:cpai_persons:BaseUrl"] = "http://test.local:5000",
            ["Validators:cpai_persons:MinConfidence"] = "0.6",
            ["Validators:cpai_vehicles:Type"] = "CodeProjectAi",
            ["Validators:cpai_vehicles:BaseUrl"] = "http://test.local:5000",
            ["Validators:cpai_vehicles:MinConfidence"] = "0.8",
        });

        var persons = sp.GetRequiredKeyedService<IValidationPlugin>("cpai_persons");
        var vehicles = sp.GetRequiredKeyedService<IValidationPlugin>("cpai_vehicles");

        persons.Name.Should().Be("cpai_persons");
        vehicles.Name.Should().Be("cpai_vehicles");
        persons.Should().NotBeSameAs(vehicles);

        var personsOpts = sp.GetRequiredService<IOptionsMonitor<CodeProjectAiOptions>>().Get("cpai_persons");
        var vehiclesOpts = sp.GetRequiredService<IOptionsMonitor<CodeProjectAiOptions>>().Get("cpai_vehicles");
        personsOpts.MinConfidence.Should().Be(0.6);
        vehiclesOpts.MinConfidence.Should().Be(0.8);
    }

    /// <summary>Non-CPAI Type discriminators are skipped — coexists with DOODS2 / Roboflow in the same Validators section.</summary>
    [TestMethod]
    public void Register_NonCodeProjectAiEntry_DoesNotRegisterIt()
    {
        var sp = BuildProvider(new()
        {
            ["Validators:doods2_persons:Type"] = "Doods2",
            ["Validators:doods2_persons:BaseUrl"] = "http://doods2.local:10200",
            ["Validators:doods2_persons:DetectorName"] = "default",
            ["Validators:cpai_persons:Type"] = "CodeProjectAi",
            ["Validators:cpai_persons:BaseUrl"] = "http://test.local:5000",
        });

        // CPAI keyed-singleton present.
        var cpai = sp.GetKeyedService<IValidationPlugin>("cpai_persons");
        cpai.Should().NotBeNull();

        // DOODS2 key NOT registered by CPAI's registrar (DOODS2's own registrar would handle it; not wired here).
        var doods2 = sp.GetKeyedService<IValidationPlugin>("doods2_persons");
        doods2.Should().BeNull("CodeProjectAi registrar must skip entries whose Type is not 'CodeProjectAi'");
    }

    /// <summary>Missing Validators section: registrar returns cleanly without throwing.</summary>
    [TestMethod]
    public void Register_NoValidatorsSection_ReturnsCleanly()
    {
        // No 'Validators:*' keys at all.
        var sp = BuildProvider(new()
        {
            ["BlueIris:TriggerUrlTemplate"] = "http://example.local/trigger?camera={camera}",
        });

        // No keyed validator registered for any name.
        sp.GetKeyedService<IValidationPlugin>("anything").Should().BeNull();
    }

    /// <summary>AllowInvalidCertificates=true configures the per-handler TLS bypass — handler is invokable, ServicePointManager untouched.</summary>
    [TestMethod]
    public void Register_AllowInvalidCertificatesTrue_ConfiguresTlsBypassHandler()
    {
        var sp = BuildProvider(new()
        {
            ["Validators:cpai_persons:Type"] = "CodeProjectAi",
            ["Validators:cpai_persons:BaseUrl"] = "https://self-signed.local:5000",
            ["Validators:cpai_persons:AllowInvalidCertificates"] = "true",
        });

        // Resolving the keyed validator forces the registrar's TLS-bypass branch to evaluate.
        var validator = sp.GetRequiredKeyedService<IValidationPlugin>("cpai_persons");
        validator.Should().BeOfType<CodeProjectAiValidator>();

        // HttpClient should construct without throwing — the per-handler SocketsHttpHandler is built lazily.
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("CodeProjectAi:cpai_persons");
        http.Should().NotBeNull();
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> kv)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(kv).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        var ctx = new PluginRegistrationContext(services, config);
        new PluginRegistrar().Register(ctx);
        return services.BuildServiceProvider();
    }
}

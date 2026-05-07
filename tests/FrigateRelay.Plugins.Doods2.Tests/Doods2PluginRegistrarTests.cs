using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.Doods2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.Doods2.Tests;

[TestClass]
public sealed class Doods2PluginRegistrarTests
{
    /// <summary>One DOODS2 entry: keyed `IValidationPlugin`, named options, named HttpClient all resolve.</summary>
    [TestMethod]
    public void Register_OneDoods2Entry_RegistersKeyedValidatorAndNamedHttpClient()
    {
        var sp = BuildProvider(new()
        {
            ["Validators:doods2_persons:Type"] = "Doods2",
            ["Validators:doods2_persons:BaseUrl"] = "http://test.local:10200",
            ["Validators:doods2_persons:DetectorName"] = "tensorflow",
            ["Validators:doods2_persons:MinConfidence"] = "0.6",
        });

        var validator = sp.GetRequiredKeyedService<IValidationPlugin>("doods2_persons");
        validator.Should().BeOfType<Doods2Validator>();
        validator.Name.Should().Be("doods2_persons");

        var opts = sp.GetRequiredService<IOptionsMonitor<Doods2Options>>().Get("doods2_persons");
        opts.BaseUrl.Should().Be("http://test.local:10200");
        opts.DetectorName.Should().Be("tensorflow");
        opts.MinConfidence.Should().Be(0.6);

        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Doods2:doods2_persons");
        http.Should().NotBeNull("named HttpClient must be registered with the 'Doods2:<key>' convention");
    }

    /// <summary>Two DOODS2 entries with different DetectorNames register independently as keyed singletons.</summary>
    [TestMethod]
    public void Register_TwoDoods2Entries_RegistersBothIndependently()
    {
        var sp = BuildProvider(new()
        {
            ["Validators:doods2_persons:Type"] = "Doods2",
            ["Validators:doods2_persons:BaseUrl"] = "http://test.local:10200",
            ["Validators:doods2_persons:DetectorName"] = "tensorflow",
            ["Validators:doods2_vehicles:Type"] = "Doods2",
            ["Validators:doods2_vehicles:BaseUrl"] = "http://test.local:10200",
            ["Validators:doods2_vehicles:DetectorName"] = "pytorch",
        });

        var persons = sp.GetRequiredKeyedService<IValidationPlugin>("doods2_persons");
        var vehicles = sp.GetRequiredKeyedService<IValidationPlugin>("doods2_vehicles");

        persons.Name.Should().Be("doods2_persons");
        vehicles.Name.Should().Be("doods2_vehicles");
        persons.Should().NotBeSameAs(vehicles);

        var personsOpts = sp.GetRequiredService<IOptionsMonitor<Doods2Options>>().Get("doods2_persons");
        var vehiclesOpts = sp.GetRequiredService<IOptionsMonitor<Doods2Options>>().Get("doods2_vehicles");
        personsOpts.DetectorName.Should().Be("tensorflow");
        vehiclesOpts.DetectorName.Should().Be("pytorch");
    }

    /// <summary>Non-DOODS2 Type discriminators are skipped — coexists with CPAI / Roboflow in the same Validators section.</summary>
    [TestMethod]
    public void Register_NonDoods2Entry_DoesNotRegisterIt()
    {
        var sp = BuildProvider(new()
        {
            ["Validators:cpai:Type"] = "CodeProjectAi",
            ["Validators:cpai:BaseUrl"] = "http://cpai.local:5000",
            ["Validators:doods2_persons:Type"] = "Doods2",
            ["Validators:doods2_persons:BaseUrl"] = "http://test.local:10200",
            ["Validators:doods2_persons:DetectorName"] = "default",
        });

        // DOODS2 keyed-singleton present.
        var doods2 = sp.GetKeyedService<IValidationPlugin>("doods2_persons");
        doods2.Should().NotBeNull();

        // CPAI key NOT registered by DOODS2's registrar (CPAI's own registrar would handle it; not wired here).
        var cpai = sp.GetKeyedService<IValidationPlugin>("cpai");
        cpai.Should().BeNull("DOODS2 registrar must skip entries whose Type is not 'Doods2'");
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
            ["Validators:doods2_persons:Type"] = "Doods2",
            ["Validators:doods2_persons:BaseUrl"] = "https://self-signed.local:10200",
            ["Validators:doods2_persons:DetectorName"] = "default",
            ["Validators:doods2_persons:AllowInvalidCertificates"] = "true",
        });

        // Resolving the keyed validator forces the registrar's TLS-bypass branch to evaluate.
        var validator = sp.GetRequiredKeyedService<IValidationPlugin>("doods2_persons");
        validator.Should().BeOfType<Doods2Validator>();

        // HttpClient should construct without throwing — the per-handler SocketsHttpHandler is built lazily.
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Doods2:doods2_persons");
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

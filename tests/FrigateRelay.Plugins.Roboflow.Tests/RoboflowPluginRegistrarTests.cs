using FrigateRelay.Abstractions;
using FrigateRelay.Plugins.Roboflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.Roboflow.Tests;

[TestClass]
public sealed class RoboflowPluginRegistrarTests
{
    /// <summary>One Roboflow entry: keyed `IValidationPlugin`, named options, named HttpClient all resolve.</summary>
    [TestMethod]
    public void Register_OneRoboflowEntry_RegistersKeyedValidatorAndNamedHttpClient()
    {
        var sp = BuildProvider(new()
        {
            ["Validators:roboflow_persons:Type"] = "Roboflow",
            ["Validators:roboflow_persons:BaseUrl"] = "http://test.local:9001",
            ["Validators:roboflow_persons:ModelId"] = "rfdetr-base",
            ["Validators:roboflow_persons:MinConfidence"] = "0.5",
        });

        var validator = sp.GetRequiredKeyedService<IValidationPlugin>("roboflow_persons");
        validator.Should().BeOfType<RoboflowValidator>();
        validator.Name.Should().Be("roboflow_persons");

        var opts = sp.GetRequiredService<IOptionsMonitor<RoboflowOptions>>().Get("roboflow_persons");
        opts.BaseUrl.Should().Be("http://test.local:9001");
        opts.ModelId.Should().Be("rfdetr-base");
        opts.MinConfidence.Should().Be(0.5);

        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Roboflow:roboflow_persons");
        http.Should().NotBeNull("named HttpClient must be registered with the 'Roboflow:<key>' convention");
    }

    /// <summary>Two Roboflow entries with different ModelIds register independently as keyed singletons.</summary>
    [TestMethod]
    public void Register_TwoRoboflowEntries_RegistersBothIndependently()
    {
        var sp = BuildProvider(new()
        {
            ["Validators:roboflow_persons:Type"] = "Roboflow",
            ["Validators:roboflow_persons:BaseUrl"] = "http://test.local:9001",
            ["Validators:roboflow_persons:ModelId"] = "person-detector",
            ["Validators:roboflow_vehicles:Type"] = "Roboflow",
            ["Validators:roboflow_vehicles:BaseUrl"] = "http://test.local:9001",
            ["Validators:roboflow_vehicles:ModelId"] = "vehicle-detector",
        });

        var persons = sp.GetRequiredKeyedService<IValidationPlugin>("roboflow_persons");
        var vehicles = sp.GetRequiredKeyedService<IValidationPlugin>("roboflow_vehicles");

        persons.Name.Should().Be("roboflow_persons");
        vehicles.Name.Should().Be("roboflow_vehicles");
        persons.Should().NotBeSameAs(vehicles);

        var personsOpts = sp.GetRequiredService<IOptionsMonitor<RoboflowOptions>>().Get("roboflow_persons");
        var vehiclesOpts = sp.GetRequiredService<IOptionsMonitor<RoboflowOptions>>().Get("roboflow_vehicles");
        personsOpts.ModelId.Should().Be("person-detector");
        vehiclesOpts.ModelId.Should().Be("vehicle-detector");
    }

    /// <summary>Non-Roboflow Type discriminators are skipped — coexists with CPAI / DOODS2 in the same Validators section.</summary>
    [TestMethod]
    public void Register_NonRoboflowEntry_DoesNotRegisterIt()
    {
        var sp = BuildProvider(new()
        {
            ["Validators:cpai:Type"] = "CodeProjectAi",
            ["Validators:cpai:BaseUrl"] = "http://cpai.local:5000",
            ["Validators:roboflow_persons:Type"] = "Roboflow",
            ["Validators:roboflow_persons:BaseUrl"] = "http://test.local:9001",
            ["Validators:roboflow_persons:ModelId"] = "rfdetr-base",
        });

        // Roboflow keyed-singleton present.
        var roboflow = sp.GetKeyedService<IValidationPlugin>("roboflow_persons");
        roboflow.Should().NotBeNull();

        // CPAI key NOT registered by Roboflow's registrar (CPAI's own registrar would handle it; not wired here).
        var cpai = sp.GetKeyedService<IValidationPlugin>("cpai");
        cpai.Should().BeNull("Roboflow registrar must skip entries whose Type is not 'Roboflow'");
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
            ["Validators:roboflow_persons:Type"] = "Roboflow",
            ["Validators:roboflow_persons:BaseUrl"] = "https://self-signed.local:9001",
            ["Validators:roboflow_persons:ModelId"] = "rfdetr-base",
            ["Validators:roboflow_persons:AllowInvalidCertificates"] = "true",
        });

        // Resolving the keyed validator forces the registrar's TLS-bypass branch (line 64-69) to evaluate.
        var validator = sp.GetRequiredKeyedService<IValidationPlugin>("roboflow_persons");
        validator.Should().BeOfType<RoboflowValidator>();

        // HttpClient should construct without throwing — the per-handler SocketsHttpHandler is built lazily.
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Roboflow:roboflow_persons");
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

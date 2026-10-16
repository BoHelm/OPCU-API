using System.Text;
using System.Text.Json;
using KitchenGateway;
using Opc.Ua;
using Xunit;

namespace Gateway.Tests;

/// <summary>Fachliche Grenzen und Fehlersemantik, unabhängig von Netzwerk/SDK-Lebenszyklus.</summary>
public sealed class CoreTests
{
    private static readonly ModelCatalog Catalog = new(Path.Combine(AppContext.BaseDirectory, "Models"));
    private static JsonElement Row(object value) => JsonSerializer.SerializeToElement(value);
    private static ModelNode Temperature => Catalog.Declarations("FryerParameterType")["ActualTemperature"];

    [Fact]
    public void OfficialTypesAndInheritedMandatoryDeclarationsArePreserved()
    {
        Assert.EndsWith("|i=1007", Catalog.Type("FryerDeviceType").Id);
        var names = Catalog.MandatoryVariables("CookingZoneDeviceType").Select(n => n.Name).ToHashSet();
        foreach (var name in new[] { "Manufacturer", "Model", "SerialNumber", "HardwareRevision", "SoftwareRevision",
            "DeviceRevision", "DeviceManual", "RevisionCounter", "DeviceClass", "EnergySource", "IsWithPanDetection", "NominalVoltage", "NumberOfPhases" })
            Assert.Contains(name, names);
        Assert.True(Catalog.Declarations("CookingZoneDeviceType")["ErrorConditions"].Mandatory);
        Assert.True(Catalog.Declarations("CookingZoneDeviceType")["InformationConditions"].Mandatory);
        Assert.Contains("NominalPower", Catalog.MandatoryVariables("CookingZoneParameterType").Select(n => n.Name));
        Assert.Equal(6, Catalog.MandatoryVariables("FryerParameterType").Count());
    }
    [Fact]
    public void NamespaceIndicesAreResolvedDynamicallyAndPlaceholdersKeepTheirType()
    {
        var table = new NamespaceTable(); table.Append("urn:unrelated:first");
        var type = Catalog.TypeId("FryerDeviceType", table);
        Assert.Equal(ModelCatalog.Hki, table.GetString(type.NamespaceIndex));
        var declaration = Catalog.ResolveDeclaration("FryingAndGrillingParameterType", "ActualGrillTemperature_2")!;
        Assert.Equal("Float", declaration.DataTypeName);
        Assert.Equal("ActualGrillTemperature_2", declaration.Name);
    }
    [Fact]
    public void DemoIsValidButUnsupportedFryerCannotBeActivatedInProduction()
    {
        var demo = DemoConfiguration.Create("http://127.0.0.1:5090");
        Assert.Empty(ConfigValidation.Validate(demo, Catalog));
        var problems = ConfigValidation.Validate(demo with { Demo = false, ApiBaseUrl = "https://example.org" }, Catalog);
        Assert.Contains(problems, p => p.Contains("ProgramMode/SignalMode"));
    }
    [Fact]
    public void DynamicStatesCannotBeConfiguredAsConstants()
    {
        var c = DemoConfiguration.Create("http://localhost:5090");
        c.Devices.Single(d => d.Profile == "Fryer").Zones[0].StaticValues["ProgramMode"] = JsonDefaults.Element(0);
        Assert.Contains(ConfigValidation.Validate(c, Catalog), p => p.Contains("kein statischer"));
    }
    [Fact]
    public void UnknownEnergyCodesAndUnprovenOperatingStatesCannotBeActivated()
    {
        var c = DemoConfiguration.Create("http://localhost:5090");
        var d = c.Devices.Single(d => d.Profile == "CookingZone");
        d.StaticValues["EnergySource"] = JsonDefaults.Element(99);
        d.Zones[0].Bindings.Add(new("CurrentState", "Actual", "actualDisplayedValues", "1", "Unbelegte Quelle"));
        var errors = ConfigValidation.Validate(c, Catalog);
        Assert.Contains(errors, e => e.Contains("EnergySource"));
        Assert.Contains(errors, e => e.Contains("CurrentState"));
    }
    [Fact]
    public void InvalidEndpointsAndNullConfigurationAreRejectedWithoutActivation()
    {
        Assert.NotEmpty(ConfigValidation.Validate(new GatewayConfig { Devices = null! }, Catalog));
        Assert.NotEmpty(ConfigValidation.Validate(new GatewayConfig { UaEndpointUrl = "opc.tcp://0.0.0.0:4840/wrong" }, Catalog));
    }
    [Theory]
    [InlineData("°F", 212, 100)]
    [InlineData("K", 373.15, 100)]
    [InlineData("°C", 0, 0)]
    public void TemperatureConversionsPreserveRealZero(string unit, double input, double expected)
    {
        var now = DateTime.UtcNow;
        var sample = Mapping.Translate(Row(new { date = now.ToString("O"), actualTemperature = input }),
            new("ActualTemperature", "Actual", "actualTemperature", unit, "test"), Temperature, now);
        Assert.Equal(StatusCodes.Good, sample.Status);
        Assert.Equal((float)expected, Assert.IsType<float>(sample.Value), 3);
        Assert.Equal(now, sample.SourceTime);
    }
    [Fact]
    public void MissingFieldsDoNotBecomeZeroAndInvalidValuesAreDistinct()
    {
        var now = DateTime.UtcNow;
        var b = new FieldBinding("ActualTemperature", "Actual", "actualTemperature", "°C", "test");
        var missing = Mapping.Translate(Row(new { date = now.ToString("O") }), b, Temperature, now);
        Assert.Equal(StatusCodes.BadNoData, missing.Status); Assert.Null(missing.Value);
        var invalid = Mapping.Translate(Row(new { date = now.ToString("O"), actualTemperature = "invalid" }), b, Temperature, now);
        Assert.Equal(StatusCodes.BadDecodingError, invalid.Status); Assert.Null(invalid.Value);
    }
    [Theory]
    [InlineData("2026-01-01T12:00:00")]
    [InlineData("not-a-date")]
    public void AmbiguousOrInvalidMeasurementTimesAreRejected(string date)
    {
        var sample = Mapping.Translate(Row(new { date, actualTemperature = 12 }),
            new("ActualTemperature", "Actual", "actualTemperature", "°C", "test"), Temperature, DateTime.UtcNow);
        Assert.Equal(StatusCodes.BadDecodingError, sample.Status);
    }
    [Fact]
    public void RepeatedFetchCannotRejuvenateStaleMeasurementAndCommunicationFailureRetainsProvenance()
    {
        var now = DateTime.UtcNow; var source = now.AddMinutes(-10);
        var sample = new Sample(42.0f, StatusCodes.Good, source, now, "test").At(now, 60);
        Assert.Equal(StatusCodes.UncertainLastUsableValue, sample.Status); Assert.Equal(source, sample.SourceTime);
        var cache = new GatewayCache(); cache.Set("one", sample);
        cache.Fail("one", StatusCodes.BadNoCommunication, "offline");
        var failed = cache.Get("one", 60);
        Assert.Equal(StatusCodes.UncertainNoCommunicationLastUsableValue, failed.Status); Assert.Equal(source, failed.SourceTime);
        Assert.Equal(42.0f, failed.Value);
        Assert.Equal(StatusCodes.BadWaitingForInitialData, cache.Get("another-device", 60).Status);
    }
    [Fact]
    public void FractionalSecondsAndOverflowAreNotSilentlyTruncated()
    {
        var n = Catalog.Declarations("FryerParameterType")["TimeRemaining"];
        var b = new FieldBinding("TimeRemaining", "Actual", "timeRemaining", "ms", "test");
        var now = DateTime.UtcNow;
        Assert.Equal(1, Mapping.Translate(Row(new { date = now.ToString("O"), timeRemaining = 1000 }), b, n, now).Value);
        Assert.Equal(StatusCodes.BadOutOfRange, Mapping.Translate(Row(new { date = now.ToString("O"), timeRemaining = 1500 }), b, n, now).Status);
    }
    [Fact]
    public void SecretIsEncryptedAndSurvivesRestartWithoutBeingExported()
    {
        var root = Path.Combine(Path.GetTempPath(), "kitchen-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new GatewayStore(Path.Combine(root, "data"), Path.Combine(root, "keys"));
            var secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            first.SetApiKey(secret);
            Assert.DoesNotContain(secret, Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "data", "api-key.enc"))));
            var second = new GatewayStore(Path.Combine(root, "data"), Path.Combine(root, "keys"));
            Assert.Equal(secret, second.ReadApiKey());
            var export = JsonSerializer.Serialize(new GatewayConfig(), JsonDefaults.Options);
            Assert.DoesNotContain(secret, export); Assert.DoesNotContain("passwordHash", export);
            File.Delete(Path.Combine(root, "keys", "master.key"));
            Assert.Throws<InvalidOperationException>(() => new GatewayStore(Path.Combine(root, "data"), Path.Combine(root, "keys")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void PasswordsAreSaltedAndWritesHaveNoInventedSuccessPath()
    {
        var first = Passwords.Hash("a sufficiently long test password");
        Assert.NotEqual(first, Passwords.Hash("a sufficiently long test password"));
        Assert.True(Passwords.Verify("a sufficiently long test password", first));
        Assert.False(Passwords.Verify("wrong", first));
        foreach (var role in new[] { "Anonymous", "Observer", "Operator", "Administrator" })
            Assert.Equal(StatusCodes.BadNotSupported, new WriteDispatcher().Authorize("setValues", role));
    }
}

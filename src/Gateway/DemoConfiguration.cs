namespace KitchenGateway;

/// <summary>Nur synthetische Stammdaten und ausdrücklich als Simulation belegte Bindungen; keine realen Gerätecodes.</summary>
public static class DemoConfiguration
{
    public static GatewayConfig Create(string apiUrl, bool insecure = false)
    {
        var devices = new List<DeviceConfig>();
        foreach (var (profile, id) in new[] { ("CookingZone", 101), ("Fryer", 102), ("Grill", 103) })
        {
            var inventory = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["Manufacturer"] = JsonDefaults.Element("SIMULIERTER Hersteller"),
                ["Model"] = JsonDefaults.Element("DEMO " + profile),
                ["SerialNumber"] = JsonDefaults.Element("DEMO-" + id),
                ["HardwareRevision"] = JsonDefaults.Element("DEMO-HW"),
                ["SoftwareRevision"] = JsonDefaults.Element("DEMO-SW"),
                ["DeviceRevision"] = JsonDefaults.Element("DEMO-REV"),
                ["DeviceManual"] = JsonDefaults.Element("Simulationsgerät; keine reale Geräteanleitung."),
                ["RevisionCounter"] = JsonDefaults.Element(0),
                ["EnergySource"] = JsonDefaults.Element(0)
            };
            if (profile == "CookingZone")
            {
                inventory["IsWithPanDetection"] = JsonDefaults.Element(false);
                inventory["NominalVoltage"] = JsonDefaults.Element(230);
                inventory["NumberOfPhases"] = JsonDefaults.Element(1);
            }
            if (profile == "Fryer") inventory["IsWithLift"] = JsonDefaults.Element(false);
            var zones = new List<ZoneConfig>();
            foreach (var zoneId in profile == "CookingZone" ? new[] { 0, 1 } : new[] { 0 })
            {
                var actual = profile == "Grill" ? "ActualGrillTemperature_0" : "ActualTemperature";
                var set = profile == "Grill" ? "SetGrillTemperature_0" : "SetTemperature";
                var values = new Dictionary<string, System.Text.Json.JsonElement>();
                if (profile == "CookingZone") values["NominalPower"] = JsonDefaults.Element(3000);
                if (profile == "Grill") values["IsWithPlaten"] = JsonDefaults.Element(false);
                var ranges = new Dictionary<string, ValueRange>
                {
                    [actual] = new(-50, 300), [set] = new(0, 300), ["NominalPower"] = new(0, 5000),
                    ["SetProcessTime"] = new(0, 86400), ["TimeRemaining"] = new(0, 86400)
                };
                var bindings = new List<FieldBinding>
                {
                    new(actual, "Actual", "actualTemperature", "°C", "AUSSCHLIESSLICH DEMO: Simulator definiert Zonentemperatur in °C."),
                    new(set, "Set", "setTemperatureValues", "°C", "AUSSCHLIESSLICH DEMO: Simulator definiert Solltemperatur in °C.")
                };
                if (profile == "Fryer")
                {
                    bindings.Add(new("SetProcessTime", "Set", "setProcessTime", "s", "AUSSCHLIESSLICH DEMO: Simulatorzeiten in Sekunden."));
                    bindings.Add(new("TimeRemaining", "Actual", "timeRemaining", "s", "AUSSCHLIESSLICH DEMO: Simulatorzeiten in Sekunden."));
                }
                zones.Add(new() { Id = zoneId, StaticValues = values, Ranges = ranges, Bindings = bindings });
            }
            devices.Add(new()
            {
                Identity = Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}"),
                ApiId = id, Name = "SIMULATION " + profile, Profile = profile, Enabled = true,
                Evidence = "AUSSCHLIESSLICH synthetisches Gerät. Keine Zuordnung eines realen numerischen API-Gerätetyps.",
                StaticValues = inventory, Ranges = new() { ["NominalVoltage"] = new(0, 500) }, Zones = zones
            });
        }
        return new() { Demo = true, ApiBaseUrl = apiUrl, Devices = devices, ActualIntervalSeconds = 1,
            SetIntervalSeconds = 2, InventoryIntervalSeconds = 2, StaleAfterSeconds = 30, AllowInsecureUaForDemo = insecure };
    }
}

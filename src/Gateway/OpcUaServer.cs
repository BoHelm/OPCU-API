using System.Security.Cryptography.X509Certificates;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Namespaces = Opc.Ua.Namespaces;

namespace KitchenGateway;

/// <summary>Besitzt SDK-Lebenszyklus und Zertifikate. Der SDK-Server wird vor dem Host geordnet beendet.</summary>
public sealed class UaService(GatewayConfig config, GatewayStore store, GatewayCache cache, ModelCatalog catalog,
    ILogger<UaService> logger) : BackgroundService
{
    private ApplicationInstance? application;
    private KitchenServer? server;
    private X509Certificate2? generatedCertificate;
    public bool Ready { get; private set; }
    public string? StartupError { get; private set; }
    public ApplicationConfiguration? Configuration { get; private set; }
    public KitchenNodeManager? Nodes => server?.Nodes;
    public string Pki => Path.Combine(store.DataDirectory, "pki");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var security = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                { StoreType = "Directory", StorePath = Path.Combine(Pki, "own"), SubjectName = "CN=ConnectedKitchen Gateway" },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(Pki, "trusted") },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(Pki, "issuers") },
                RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(Pki, "rejected") },
                AutoAcceptUntrustedCertificates = false,
                AddAppCertToTrustedStore = false,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 2048
            };
            var policies = new ServerSecurityPolicyCollection
            {
                new() { SecurityMode = MessageSecurityMode.SignAndEncrypt, SecurityPolicyUri = SecurityPolicies.Basic256Sha256 }
            };
            var tokens = new UserTokenPolicyCollection { new(UserTokenType.UserName) };
            if (config.AllowInsecureUaForDemo)
            {
                policies.Add(new() { SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None });
                tokens.Add(new(UserTokenType.Anonymous));
            }
            Configuration = new ApplicationConfiguration
            {
                ApplicationName = "ConnectedKitchen Gateway",
                ApplicationUri = $"urn:bohelm:kitchen:{config.GatewayId:D}",
                ProductUri = ModelCatalog.Vendor,
                ApplicationType = ApplicationType.Server,
                SecurityConfiguration = security,
                ServerConfiguration = new ServerConfiguration
                {
                    BaseAddresses = new StringCollection { new UriBuilder("opc.tcp", config.UaBindAddress, config.UaPort, "kitchen").Uri.AbsoluteUri },
                    AlternateBaseAddresses = new StringCollection { config.UaEndpointUrl },
                    SecurityPolicies = policies,
                    UserTokenPolicies = tokens,
                    DiagnosticsEnabled = false,
                    MinSessionTimeout = 10000,
                    MaxSessionTimeout = 3600000,
                    MaxSessionCount = 100,
                    MaxSubscriptionCount = 200,
                    MaxMessageQueueSize = 100,
                    MaxNotificationQueueSize = 1000,
                    MinPublishingInterval = 100,
                    MaxPublishingInterval = 60000,
                    OperationLimits = new OperationLimits { MaxNodesPerRead = 1000, MaxNodesPerWrite = 100, MaxMonitoredItemsPerCall = 1000 }
                },
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000, MaxMessageSize = 4 * 1024 * 1024, MaxByteStringLength = 1024 * 1024 },
                TraceConfiguration = new TraceConfiguration()
            };
            await Configuration.Validate(ApplicationType.Server);
            application = new ApplicationInstance { ApplicationName = Configuration.ApplicationName, ApplicationType = ApplicationType.Server, ApplicationConfiguration = Configuration };
            var certificateId = security.ApplicationCertificate;
            if (await certificateId.Find(true) == null)
            {
                // Der SDK-Default betrachtet Bind-Adressen. Für NAT/Container gehört außerdem
                // der tatsächlich angekündigte Client-DNS-Name zwingend in SubjectAltName.
                var domains = Configuration.GetServerDomainNames().Append(new Uri(config.UaEndpointUrl).DnsSafeHost)
                    .Append(Utils.GetHostName()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                generatedCertificate = CertificateFactory.CreateCertificate(Configuration.ApplicationUri,
                    Configuration.ApplicationName, certificateId.SubjectName, domains)
                    .SetLifeTime(12).SetRSAKeySize(3072).CreateForRSA();
                await generatedCertificate.AddToStoreAsync(certificateId.StoreType, certificateId.StorePath);
                certificateId.Certificate = generatedCertificate;
            }
            if (!await application.CheckApplicationInstanceCertificatesAsync(false))
                throw new InvalidOperationException("OPC-UA-Anwendungszertifikat konnte nicht geprüft/erstellt werden.");
            // Auch bestehende Zertifikate prüfen; bei DNS-Wechsel kein stiller Zertifikatsaustausch.
            Configuration.CertificateValidator.ValidateDomains((await certificateId.Find(true))!,
                new ConfiguredEndpoint(null, new EndpointDescription { EndpointUrl = config.UaEndpointUrl },
                    EndpointConfiguration.Create(Configuration)), true);
            server = new KitchenServer(config, store, cache, catalog);
            await application.StartAsync(server);
            Ready = true;
            logger.LogInformation("OPC-UA-Server gestartet; {Count} explizit konfigurierte Geräte.", config.Devices.Count(d => d.Enabled));
            while (!stoppingToken.IsCancellationRequested)
            {
                server.Nodes?.Refresh();
                await Task.Delay(500, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception e)
        {
            StartupError = "OPC-UA-Start fehlgeschlagen: Bind-Adresse, Zertifikate und Konfiguration prüfen.";
            logger.LogError(e, "OPC-UA-Startfehler; Webdiagnose bleibt verfügbar.");
            if (e is ServiceResultException sdkError) logger.LogError("SDK-Startdiagnose: {Details}", sdkError.Result.ToLongString());
            // Kein Prozessabsturz: Webdiagnose und Konfigurationskorrektur bleiben erreichbar.
        }
        finally
        {
            Ready = false;
            if (server != null)
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await server.StopAsync(shutdown.Token); }
                catch (OperationCanceledException) { logger.LogWarning("OPC-UA-Abschaltfrist erreicht."); }
                server.Dispose();
            }
            generatedCertificate?.Dispose();
        }
    }
    public async Task ReloadTrustAsync()
    {
        if (Configuration != null) await Configuration.CertificateValidator.Update(Configuration);
    }
    public async Task<byte[]?> ServerCertificateAsync()
    {
        var certificate = Configuration == null ? null : await Configuration.SecurityConfiguration.ApplicationCertificate.Find(true);
        return certificate?.RawData;
    }
}

/// <summary>Trennt OPC-UA-Anmeldung von Webadministration und API-Key. Alle angebotenen Geräteknoten sind lesend.</summary>
public sealed class KitchenServer(GatewayConfig config, GatewayStore store, GatewayCache cache, ModelCatalog catalog) : StandardServer
{
    public KitchenNodeManager? Nodes { get; private set; }
    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        Nodes = new KitchenNodeManager(server, configuration, config, store, cache, catalog);
        return new MasterNodeManager(server, configuration, null, [Nodes]);
    }
    protected override ServerProperties LoadServerProperties() => new()
    {
        ManufacturerName = "BoHelm", ProductName = "ConnectedKitchen HKI Gateway",
        ProductUri = ModelCatalog.Vendor, SoftwareVersion = "0.1.0", BuildNumber = "1",
        BuildDate = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc)
    };
    protected override void OnServerStarted(IServerInternal server)
    {
        base.OnServerStarted(server);
        server.SessionManager.ImpersonateUser += (_, args) =>
        {
            if (args.NewIdentity is UserNameIdentityToken token)
            {
                if (token.DecryptedPassword == null) throw new ServiceResultException(StatusCodes.BadIdentityTokenInvalid);
                var account = (store.Read<List<UaAccount>>("ua-users.json") ?? []).FirstOrDefault(a => a.Username == token.UserName);
                if (account == null || !Passwords.Verify(System.Text.Encoding.UTF8.GetString(token.DecryptedPassword), account.PasswordHash))
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                args.Identity = new UserIdentity(token);
                args.Identity.GrantedRoleIds.Add(ObjectIds.WellKnownRole_AuthenticatedUser);
                args.Identity.GrantedRoleIds.Add(ObjectIds.WellKnownRole_Observer);
                return;
            }
            if (config.AllowInsecureUaForDemo && args.NewIdentity is AnonymousIdentityToken)
            { args.Identity = new UserIdentity(); return; }
            throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
        };
    }
    /// <summary>Discovery kündigt die administrativ festgelegte NAT-/DNS-Adresse statt der Bind-Adresse an.</summary>
    public override async Task<GetEndpointsResponse> GetEndpointsAsync(SecureChannelContext secureChannelContext,
        RequestHeader requestHeader, string endpointUrl, StringCollection localeIds, StringCollection profileUris, CancellationToken ct)
    {
        var response = await base.GetEndpointsAsync(secureChannelContext, requestHeader, endpointUrl, localeIds, profileUris, ct);
        foreach (var endpoint in response.Endpoints) endpoint.EndpointUrl = config.UaEndpointUrl;
        return response;
    }
}

/// <summary>Importiert unveränderte offizielle NodeSets und instanziiert Pflichtdeklarationen einschließlich DI-Vererbung.</summary>
/// <remarks>HTTP läuft in anderen Tasks. Ausschließlich dieser Manager hält unter Lock Besitz am veränderlichen SDK-Adressraum.
/// Der Katalog übernimmt weder feste Namespace-Indizes noch erfundene numerische Typkennungen.</remarks>
public sealed class KitchenNodeManager : CustomNodeManager2
{
    private readonly GatewayConfig config;
    private readonly GatewayStore store;
    private readonly GatewayCache cache;
    private readonly ModelCatalog catalog;
    private readonly List<(BaseDataVariableState Node, Func<Sample> Read)> tracked = [];
    private readonly List<(AlarmConditionState Alarm, BaseObjectState Source, string HealthPrefix)> alarms = [];
    private ushort vendor;
    public KitchenNodeManager(IServerInternal server, ApplicationConfiguration configuration, GatewayConfig config,
        GatewayStore store, GatewayCache cache, ModelCatalog catalog) : base(server, configuration, ModelCatalog.Vendor, ModelCatalog.Di, ModelCatalog.Hki)
    { this.config = config; this.store = store; this.cache = cache; this.catalog = catalog; SystemContext.NodeIdFactory = this; }

    protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
    {
        var nodes = new NodeStateCollection();
        foreach (var file in new[] { "Opc.Ua.Di.NodeSet2.xml", "Opc.Ua.CommercialKitchenEquipment.NodeSet2.xml" })
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Models", file));
            UANodeSet.Read(stream).Import(context, nodes);
        }
        return nodes;
    }
    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            base.CreateAddressSpace(externalReferences);
            vendor = Server.NamespaceUris.GetIndexOrAppend(ModelCatalog.Vendor);
            var root = Object(null, "Devices", "ConnectedKitchen – " + (config.Demo ? "SIMULATION" : "HKI Gateway"),
                "Devices", ObjectTypeIds.FolderType, 0, ReferenceTypeIds.Organizes);
            root.EventNotifier = EventNotifiers.SubscribeToEvents;
            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));
            AddRootNotifier(root);
            foreach (var d in config.Devices.Where(d => d.Enabled))
            {
                var profile = Profiles.All[d.Profile];
                var path = $"device/{d.Identity:D}";
                var device = Object(root, d.Name, d.Name, path, catalog.TypeId(profile.DeviceType, Server.NamespaceUris), vendor, ReferenceTypeIds.Organizes);
                device.EventNotifier = EventNotifiers.SubscribeToEvents;
                root.AddNotifier(SystemContext, ReferenceTypeIds.HasNotifier, false, device);
                Instantiate(device, profile.DeviceType, path, d.StaticValues, d.Ranges, d, null);
                foreach (var z in d.Zones)
                {
                    var zone = Object(device, profile.ZonePrefix + z.Id, profile.ZonePrefix + z.Id, path + "/zone/" + z.Id,
                        catalog.TypeId(profile.ZoneType, Server.NamespaceUris), Server.NamespaceUris.GetIndexOrAppend(ModelCatalog.Hki));
                    Instantiate(zone, profile.ZoneType, path + "/zone/" + z.Id, z.StaticValues, z.Ranges, d, z);
                }
                var diagnostics = Object(device, "GatewayDiagnostics", "Gateway-Diagnose (kein Gerätealarm)", path + "/diagnostics", ObjectTypeIds.BaseObjectType, vendor);
                Property(diagnostics, "DeviceAlarmSourceAvailable", path + "/diagnostics/DeviceAlarmSourceAvailable", DataTypeIds.Boolean,
                    false, StatusCodes.Good, vendor);
                Property(diagnostics, "Conformance", path + "/diagnostics/Conformance", DataTypeIds.String,
                    "Teilumfang; keine Zertifizierung. Aktive Gerätealarme und HACCP-Historienmetadaten fehlen; siehe Mapping.", StatusCodes.Good, vendor);
                AddCommunicationAlarm(diagnostics, path + "/diagnostics/CommunicationAlarm", d.ApiId + "/");
            }
            AddPredefinedNode(SystemContext, root);
            Refresh();
        }
    }
    private NodeId Id(string path) => new(path, vendor);
    private BaseObjectState Object(NodeState? parent, string name, string display, string path, NodeId type,
        ushort ns, NodeId? reference = null)
    {
        var value = new BaseObjectState(parent)
        {
            NodeId = Id(path), BrowseName = new QualifiedName(name, ns), DisplayName = display,
            TypeDefinitionId = type, ReferenceTypeId = reference ?? ReferenceTypeIds.HasComponent
        };
        parent?.AddChild(value);
        return value;
    }
    private void Instantiate(BaseObjectState parent, string type, string path, Dictionary<string, System.Text.Json.JsonElement> staticValues,
        Dictionary<string, ValueRange> ranges, DeviceConfig device, ZoneConfig? zone)
    {
        var declarations = catalog.Declarations(type).Values.Where(n => !n.Placeholder).ToList();
        foreach (var name in (zone?.Bindings.Select(b => b.Target) ?? []).Concat(staticValues.Keys).Distinct())
        {
            var resolved = catalog.ResolveDeclaration(type, name);
            if (resolved != null && !declarations.Any(n => n.Name == name)) declarations.Add(resolved);
        }
        foreach (var declaration in declarations)
        {
            var binding = zone?.Bindings.FirstOrDefault(b => b.Target == declaration.Name);
            var configured = staticValues.TryGetValue(declaration.Name, out var staticValue);
            if (!declaration.Mandatory && binding == null && !configured) continue;
            var ns = Server.NamespaceUris.GetIndexOrAppend(declaration.BrowseUri);
            var childPath = path + "/" + declaration.Name;
            if (!declaration.IsVariable)
            {
                var child = Object(parent, declaration.Name, declaration.Name, childPath, catalog.UaId(declaration.TypeDefinition, Server.NamespaceUris), ns);
                if (declaration.Name is "ErrorConditions" or "InformationConditions")
                {
                    child.EventNotifier = EventNotifiers.SubscribeToEvents;
                    parent.AddNotifier(SystemContext, ReferenceTypeIds.HasNotifier, false, child);
                    // Kein historisches Fehlerereignis wird zu einem aktuell aktiven Gerätealarm umgedeutet.
                }
                continue;
            }
            var variable = new BaseDataVariableState(parent)
            {
                NodeId = Id(childPath), BrowseName = new QualifiedName(declaration.Name, ns), DisplayName = declaration.Name,
                TypeDefinitionId = catalog.UaId(declaration.TypeDefinition, Server.NamespaceUris),
                ReferenceTypeId = declaration.References.Any(r => !r.Forward && r.Type.EndsWith("|i=46")) ? ReferenceTypeIds.HasProperty : ReferenceTypeIds.HasComponent,
                DataType = catalog.UaId(declaration.DataType, Server.NamespaceUris),
                ValueRank = ValueRanks.Scalar, AccessLevel = AccessLevels.CurrentRead, UserAccessLevel = AccessLevels.CurrentRead,
                Historizing = false, MinimumSamplingInterval = 500
            };
            // HasProperty wird aus der Typdeklaration und nicht aus einer vermeintlich festen ParentNodeId bestimmt.
            if (declaration.TypeDefinition == Namespaces.OpcUa + "|i=68") variable.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            parent.AddChild(variable);
            Sample Read()
            {
                if (declaration.Name == "DeviceClass")
                    return new(Profiles.All[device.Profile].DeviceClass, StatusCodes.Good, null, DateTime.UtcNow, "Administrativ gewähltes Profil, DIN Tabelle 2.");
                if (configured)
                {
                    try { return new(Mapping.ConvertScalar(staticValue, declaration.DataTypeName), StatusCodes.Good, null, DateTime.UtcNow, "Konfiguriertes Stammdatum."); }
                    catch { return Sample.Missing(StatusCodes.BadConfigurationError, "Ungültiges Stammdatum."); }
                }
                if (zone?.DeviceUnsupported.Contains(declaration.Name) == true)
                    return Sample.Missing(StatusCodes.BadNotSupported, "Geräteseitig als nicht unterstützt dokumentiert.");
                if (binding != null)
                {
                    var sample = cache.Get(GatewayCache.Key(device.Identity, zone!.Id, declaration.Name), config.StaleAfterSeconds);
                    if (sample.Status == StatusCodes.Good && sample.Value is IConvertible number && ranges.TryGetValue(declaration.Name, out var limits))
                    {
                        var numeric = number.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                        if (numeric < limits.Low || numeric > limits.High)
                            return sample with { Status = StatusCodes.BadOutOfRange, Reason = "Messwert liegt außerhalb des bestätigten gerätespezifischen EURange." };
                    }
                    return sample;
                }
                return Sample.Missing(Mapping.IsDynamic(declaration.Name) ? StatusCodes.BadNotSupported : StatusCodes.BadConfigurationError,
                    Mapping.IsDynamic(declaration.Name) ? "API-seitig keine belegte Zuordnung; Pflichtwertlücke." : "Pflicht-Stammdatum fehlt.");
            }
            tracked.Add((variable, Read));
            if (catalog.IsAnalog(declaration))
            {
                var range = ranges.GetValueOrDefault(declaration.Name);
                Property(variable, "EURange", childPath + "/EURange", DataTypeIds.Range,
                    range == null ? null : new Opc.Ua.Range(range.Low, range.High), range == null ? StatusCodes.BadConfigurationError : StatusCodes.Good);
                var unit = Mapping.Unit(declaration.Name);
                if (unit != "")
                {
                    var code = unit switch { "°C" => "CEL", "s" => "SEC", "W" => "WTT", "V" => "VLT", "%" => "P1", _ => "" };
                    Property(variable, "EngineeringUnits", childPath + "/EngineeringUnits", DataTypeIds.EUInformation,
                        new EUInformation
                        {
                            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                            UnitId = code.Aggregate(0, (a, ch) => a * 256 + ch), DisplayName = unit, Description = unit
                        }, StatusCodes.Good);
                }
                if (declaration.Name.Contains("Temperature") && declaration.Name.StartsWith("Actual"))
                    AddHaccpReference(parent, variable, childPath);
            }
        }
    }
    /// <summary>HACCP-Referenz und geerbte Pflichtstruktur. Unbekannte geräteseitige Aufzeichnungsparameter bleiben BadNoData.</summary>
    private void AddHaccpReference(BaseObjectState zone, BaseDataVariableState variable, string path)
    {
        var device = (BaseObjectState)zone.Parent!;
        var children = new List<BaseInstanceState>(); device.GetChildren(SystemContext, children);
        var group = children.OfType<BaseObjectState>().FirstOrDefault(n => n.BrowseName.Name == "HACCPValues") ??
            Object(device, "HACCPValues", "HACCP-Messwertreferenzen; kein Gateway-Historian", ((string)device.NodeId.Identifier) + "/HACCPValues",
                catalog.TypeId("FunctionalGroupType", Server.NamespaceUris), Server.NamespaceUris.GetIndexOrAppend(ModelCatalog.Hki));
        group.AddReference(ReferenceTypeIds.Organizes, false, variable.NodeId);
        variable.AddReference(ReferenceTypeIds.Organizes, true, group.NodeId);
        var history = new HistoricalDataConfigurationState(variable);
        history.Create(SystemContext, Id(path + "/HA Configuration"), new QualifiedName("HA Configuration", Server.NamespaceUris.GetIndexOrAppend(ModelCatalog.Hki)),
            "HA Configuration", true);
        history.TypeDefinitionId = catalog.TypeId("KitchenDeviceHAConfigType", Server.NamespaceUris);
        history.ReferenceTypeId = ReferenceTypeIds.HasHistoricalConfiguration;
        AssignStableChildren(history, path + "/HA Configuration", markMissing: true);
        variable.AddChild(history);
        Property(history, "HistoryDuration", path + "/HA Configuration/HistoryDuration", DataTypeIds.Duration, null, StatusCodes.BadNoData,
            Server.NamespaceUris.GetIndexOrAppend(ModelCatalog.Hki));
        Property(history, "SamplingInterval", path + "/HA Configuration/SamplingInterval", DataTypeIds.Duration, null, StatusCodes.BadNoData,
            Server.NamespaceUris.GetIndexOrAppend(ModelCatalog.Hki));
    }
    private void Property(NodeState parent, string name, string path, NodeId type, object? value, uint status, ushort ns = 0)
    {
        var property = new PropertyState(parent)
        {
            NodeId = Id(path), BrowseName = new QualifiedName(name, ns), DisplayName = name, DataType = type,
            TypeDefinitionId = VariableTypeIds.PropertyType, ReferenceTypeId = ReferenceTypeIds.HasProperty,
            ValueRank = ValueRanks.Scalar, AccessLevel = AccessLevels.CurrentRead, UserAccessLevel = AccessLevels.CurrentRead,
            Value = value, StatusCode = status, Timestamp = DateTime.MinValue
        };
        parent.AddChild(property);
    }
    private void AssignStableChildren(NodeState node, string path, bool markMissing)
    {
        node.NodeId = Id(path);
        var children = new List<BaseInstanceState>(); node.GetChildren(SystemContext, children);
        foreach (var child in children)
        {
            AssignStableChildren(child, path + "/" + child.BrowseName.Name, markMissing);
            if (child is BaseVariableState v)
            {
                v.AccessLevel = AccessLevels.CurrentRead; v.UserAccessLevel = AccessLevels.CurrentRead;
                if (markMissing) { v.Value = null; v.StatusCode = StatusCodes.BadNoData; v.Timestamp = DateTime.MinValue; }
            }
            if (child is MethodState method)
            {
                method.Executable = false; method.UserExecutable = false;
                method.OnCallMethod = (_, _, _, _) => new ServiceResult(StatusCodes.BadNotSupported);
            }
        }
    }
    private void AddCommunicationAlarm(BaseObjectState parent, string path, string prefix)
    {
        var alarm = new AlarmConditionState(Server.Telemetry, parent);
        alarm.Create(SystemContext, Id(path), new QualifiedName("CommunicationAlarm", vendor), "Gateway-Quellkommunikation", true);
        AssignStableChildren(alarm, path, false);
        alarm.ConditionName.Value = "Gateway-Quellkommunikation";
        alarm.SourceNode.Value = parent.NodeId;
        alarm.SourceName.Value = "ConnectedKitchen Gateway";
        // Referenztypische Pflichtwerte sind vor SetEnableState nötig: SDK wertet dabei BranchId/Retain aus.
        alarm.BranchId.Value = NodeId.Null;
        alarm.ConditionClassId.Value = ObjectTypeIds.BaseConditionClassType;
        alarm.ConditionClassName.Value = "Gateway-Diagnose";
        alarm.EventId.Value = Guid.NewGuid().ToByteArray();
        alarm.Time.Value = DateTime.UtcNow; alarm.ReceiveTime.Value = DateTime.UtcNow;
        alarm.Message.Value = "Gateway-Quellkommunikation noch nicht bewertet.";
        alarm.Quality.Value = StatusCodes.Good;
        alarm.AckedState.Id.Value = true;
        alarm.SetActiveState(SystemContext, false);
        alarm.SetEnableState(SystemContext, true);
        alarm.Retain.Value = false;
        alarm.InputNode.Value = parent.NodeId;
        alarm.OnAcknowledge = (_, _, _, _) => new ServiceResult(StatusCodes.BadNotSupported);
        alarm.OnEnableDisable = (_, _, _) => new ServiceResult(StatusCodes.BadNotSupported);
        parent.AddChild(alarm);
        parent.EventNotifier = EventNotifiers.SubscribeToEvents;
        ((BaseObjectState)parent.Parent!).AddNotifier(SystemContext, ReferenceTypeIds.HasNotifier, false, parent);
        alarms.Add((alarm, parent, prefix));
    }
    public void Refresh()
    {
        lock (Lock)
        {
            foreach (var (node, read) in tracked)
            {
                var sample = read();
                node.Value = sample.Value;
                node.StatusCode = sample.Status;
                node.Timestamp = sample.SourceTime ?? DateTime.MinValue;
                node.ClearChangeMasks(SystemContext, false);
            }
            foreach (var (alarm, source, prefix) in alarms)
            {
                var failed = cache.Health.Any(p => (p.Key == "inventory" || p.Key.StartsWith(prefix)) && !p.Value.Reachable);
                if (alarm.ActiveState.Id.Value == failed) continue;
                alarm.EventId.Value = Guid.NewGuid().ToByteArray();
                alarm.Time.Value = DateTime.UtcNow; alarm.ReceiveTime.Value = DateTime.UtcNow;
                alarm.Message.Value = failed ? "Gateway erreicht die Datenquelle nicht. Kein physischer Gerätealarm." : "Gateway-Datenquelle wieder erreichbar.";
                alarm.Severity.Value = (ushort)(failed ? 500 : 0);
                alarm.SetActiveState(SystemContext, failed);
                alarm.Retain.Value = failed;
                alarm.ClearChangeMasks(SystemContext, true);
                source.ReportEvent(SystemContext, alarm);
            }
        }
    }
    public object[] Diagnostics()
    {
        lock (Lock) return tracked.Select(t => new
        {
            nodeId = t.Node.NodeId.ToString(), browseName = t.Node.BrowseName.ToString(),
            typeDefinition = t.Node.TypeDefinitionId.ToString(), dataType = t.Node.DataType.ToString(),
            status = StatusCode.LookupSymbolicId(t.Node.StatusCode.Code), value = t.Node.Value,
            sourceTimestamp = t.Node.Timestamp == DateTime.MinValue ? (DateTime?)null : t.Node.Timestamp,
            reason = t.Read().Reason
        }).Cast<object>().ToArray();
    }
}

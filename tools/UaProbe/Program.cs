using System.Collections.Concurrent;
using System.Text.Json;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

// Eigenständiger Prozess und echter UA-TCP-Client. Niemals einen API-Key oder ein Passwort als CLI-Argument übergeben.
// Vertrauen wird ausschließlich durch eine explizit übergebene öffentliche Serverzertifikatsdatei eingerichtet.
var arguments = new Dictionary<string, string>();
for (var i = 1; i < args.Length; i += 2) arguments.Add(args[i].TrimStart('-'), args[i + 1]);
var command = args.FirstOrDefault() ?? "help";
if (command is not ("init" or "probe")) throw new ArgumentException("UaProbe init|probe --pki <Verzeichnis> [--endpoint URL --server-cert DER --user NAME --password-file DATEI --output JSON --seconds 4]");
var pki = Path.GetFullPath(arguments.GetValueOrDefault("pki", ".state/probe"));
Directory.CreateDirectory(pki);
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pki, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
if (arguments.TryGetValue("server-cert", out var serverCert))
{
    var trusted = Path.Combine(pki, "trusted", "certs"); Directory.CreateDirectory(trusted);
    using var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(serverCert);
    File.WriteAllBytes(Path.Combine(trusted, cert.Thumbprint + ".der"), cert.RawData);
}
var configuration = new ApplicationConfiguration
{
    ApplicationName = "KitchenGateway Independent Probe", ApplicationUri = "urn:bohelm:kitchen:independent-probe",
    ApplicationType = ApplicationType.Client,
    SecurityConfiguration = new SecurityConfiguration
    {
        ApplicationCertificate = new CertificateIdentifier { StoreType = "Directory", StorePath = Path.Combine(pki, "own"), SubjectName = "CN=KitchenGateway Independent Probe" },
        TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pki, "trusted") },
        TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pki, "issuers") },
        RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pki, "rejected") },
        AutoAcceptUntrustedCertificates = false, RejectSHA1SignedCertificates = true, MinimumCertificateKeySize = 2048
    },
    ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
    TransportQuotas = new TransportQuotas { OperationTimeout = 15000, MaxMessageSize = 4 * 1024 * 1024 }
};
await configuration.Validate(ApplicationType.Client);
var application = new ApplicationInstance { ApplicationName = configuration.ApplicationName, ApplicationType = ApplicationType.Client, ApplicationConfiguration = configuration };
if (!await application.CheckApplicationInstanceCertificatesAsync(false)) throw new Exception("Client-Zertifikat fehlt.");
var certificate = await configuration.SecurityConfiguration.ApplicationCertificate.Find(true);
var publicPath = Path.Combine(pki, "probe.der"); File.WriteAllBytes(publicPath, certificate.RawData);
if (command == "init") { Console.WriteLine(JsonSerializer.Serialize(new { publicCertificate = publicPath })); return; }
var endpointUrl = arguments.GetValueOrDefault("endpoint", "opc.tcp://localhost:4840/kitchen");
var endpoint = CoreClientUtils.SelectEndpoint(configuration, endpointUrl, true, 10000);
if (endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt) throw new Exception("Verschlüsselter Endpoint fehlt.");
// Der Aufrufer hat die Adresse explizit festgelegt; DNS und Zertifikatsprüfung bleiben trotzdem aktiv.
endpoint.EndpointUrl = endpointUrl;
var user = arguments.GetValueOrDefault("user") ?? throw new ArgumentException("--user fehlt");
var password = File.ReadAllText(arguments["password-file"]).TrimEnd('\r', '\n');
using var session = await Session.Create(configuration, new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration)),
    false, true, "HKI acceptance probe", 60000, new UserIdentity(user, System.Text.Encoding.UTF8.GetBytes(password)), null);
var vendor = session.NamespaceUris.GetIndex("urn:bohelm:connectedkitchen:gateway");
if (vendor < 0) throw new Exception("Gateway-Namensraum fehlt.");
var root = new NodeId("Devices", (ushort)vendor);
var visited = new HashSet<NodeId>();
var variables = new List<NodeId>();
var references = new List<object>();
var queue = new Queue<NodeId>(); queue.Enqueue(root);
while (queue.TryDequeue(out var node) && visited.Count < 2000)
{
    if (!visited.Add(node)) continue;
    var response = await session.BrowseAsync(null, null, 1000, new BrowseDescriptionCollection
    {
        new() { NodeId = node, BrowseDirection = BrowseDirection.Forward, ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true, NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.Method),
            ResultMask = (uint)BrowseResultMask.All }
    }, CancellationToken.None);
    var result = response.Results[0];
    if (StatusCode.IsBad(result.StatusCode)) throw new Exception("Browse fehlgeschlagen: " + result.StatusCode);
    if (result.ContinuationPoint is { Length: > 0 }) throw new Exception("Probe-Limit erreicht; BrowseNext erforderlich.");
    foreach (var reference in result.References)
    {
        var child = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
        references.Add(new { parent = node.ToString(), nodeId = child.ToString(), name = reference.BrowseName.Name,
            browseNamespace = session.NamespaceUris.GetString(reference.BrowseName.NamespaceIndex),
            typeDefinition = reference.TypeDefinition.ToString(), nodeClass = reference.NodeClass.ToString() });
        if (reference.NodeClass == NodeClass.Variable && !variables.Contains(child)) variables.Add(child);
        queue.Enqueue(child);
    }
}
if (visited.Count < 10 || variables.Count < 10) throw new Exception("Keine sinnvollen Geräteinstanzen gefunden.");
var read = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
    new ReadValueIdCollection(variables.Select(v => new ReadValueId { NodeId = v, AttributeId = Attributes.Value })), CancellationToken.None);
var values = variables.Zip(read.Results).Select(p => new
{
    nodeId = p.First.ToString(), value = Format(p.Second.Value), status = StatusCode.LookupSymbolicId(p.Second.StatusCode.Code),
    sourceTimestamp = p.Second.SourceTimestamp, serverTimestamp = p.Second.ServerTimestamp
}).ToArray();
var target = variables.First(n => n.Identifier is string name && name.EndsWith("/zone/0/ActualTemperature") && name.Contains("000000000101"));
var targetValue = values.First(v => v.nodeId == target.ToString());
var expected = arguments.GetValueOrDefault("expected-status", "Good");
if (targetValue.status != expected) throw new Exception($"Status {targetValue.status}, erwartet {expected}.");
var write = await session.WriteAsync(null, new WriteValueCollection
{
    new() { NodeId = target, AttributeId = Attributes.Value, Value = new DataValue(new Variant(123.0f)) }
}, CancellationToken.None);
if (StatusCode.IsGood(write.Results[0])) throw new Exception("Nicht unterstützter Schreibzugriff wurde fälschlich akzeptiert.");
var notifications = new ConcurrentQueue<object>();
var events = new ConcurrentQueue<object>();
var seconds = int.Parse(arguments.GetValueOrDefault("seconds", "4"));
using var subscription = new Subscription(session.DefaultSubscription)
{
    PublishingInterval = 200, KeepAliveCount = 10, LifetimeCount = 100, PublishingEnabled = true
};
session.AddSubscription(subscription);
var item = new MonitoredItem(subscription.DefaultItem) { StartNodeId = target, AttributeId = Attributes.Value, SamplingInterval = 100, QueueSize = 100, DiscardOldest = true };
item.Notification += (_, _) =>
{
    foreach (var value in item.DequeueValues()) notifications.Enqueue(new { status = value.StatusCode.ToString(), value = Format(value.Value), value.SourceTimestamp });
};
subscription.AddItem(item);
var filter = new EventFilter();
foreach (var path in new[] { "EventType", "Message", "Retain", "Time" })
    filter.SelectClauses.Add(new SimpleAttributeOperand { TypeDefinitionId = ObjectTypeIds.BaseEventType,
        BrowsePath = new QualifiedNameCollection { new(path) }, AttributeId = Attributes.Value });
var eventItem = new MonitoredItem(subscription.DefaultItem) { StartNodeId = root, AttributeId = Attributes.EventNotifier, Filter = filter, QueueSize = 100 };
eventItem.Notification += (_, notification) =>
{
    if (notification.NotificationValue is EventFieldList fields)
        events.Enqueue(fields.EventFields.Select(v => Format(v.Value)).ToArray());
};
subscription.AddItem(eventItem);
await subscription.CreateAsync();
var refresh = await session.CallAsync(null, new CallMethodRequestCollection
{
    new() { ObjectId = ObjectTypeIds.ConditionType, MethodId = MethodIds.ConditionType_ConditionRefresh, InputArguments = new VariantCollection { new(subscription.Id) } }
}, CancellationToken.None);
if (StatusCode.IsBad(refresh.Results[0].StatusCode)) throw new Exception("ConditionRefresh fehlgeschlagen: " + refresh.Results[0].StatusCode);
if (arguments.TryGetValue("ready-file", out var readyFile))
    await File.WriteAllTextAsync(readyFile, "subscribed");
await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60)));
if (notifications.Count == 0 || (expected == "Good" && seconds >= 3 && notifications.Count < 2))
    throw new Exception("Subscription hat keine erwarteten Benachrichtigungen geliefert.");
var report = new
{
    transport = "UA-TCP", securityMode = endpoint.SecurityMode.ToString(), securityPolicy = endpoint.SecurityPolicyUri,
    namespaceUris = session.NamespaceUris.ToArray(), browsedNodes = visited.Count, references, reads = values,
    writeResult = write.Results[0].ToString(), conditionRefresh = refresh.Results[0].StatusCode.ToString(),
    notifications = notifications.ToArray(), events = events.ToArray()
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (arguments.TryGetValue("output", out var output)) await File.WriteAllTextAsync(output, json);
Console.WriteLine(JsonSerializer.Serialize(new { success = true, browsedNodes = visited.Count, variables = variables.Count,
    notifications = notifications.Count, events = events.Count, status = targetValue.status, writeResult = write.Results[0].ToString() }));
await subscription.DeleteAsync(true);
await session.CloseAsync();

static object? Format(object? value) => value switch
{
    null => null,
    LocalizedText text => text.Text,
    ExtensionObject extension => Format(extension.Body),
    Opc.Ua.Range range => new { low = range.Low, high = range.High },
    EUInformation unit => new { unitId = unit.UnitId, displayName = unit.DisplayName.Text },
    NodeId id => id.ToString(),
    byte[] bytes => Convert.ToBase64String(bytes),
    bool or string or int or uint or long or ulong or short or ushort or float or double or DateTime => value,
    _ => value.ToString()
};

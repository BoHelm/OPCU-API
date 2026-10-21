using System.Xml.Linq;
using Opc.Ua;

namespace KitchenGateway;

/// <summary>Unveränderter Deklarationskatalog der offiziellen NodeSets, getrennt vom SDK-Adressraum.</summary>
/// <remarks>Namespace-URIs werden zur Laufzeit aufgelöst. Nur Deklarationen des gepinnten Modells bestimmen Pflichtknoten.</remarks>
public sealed class ModelCatalog
{
    public const string Hki = "http://opcfoundation.org/UA/CommercialKitchenEquipment/";
    public const string Di = "http://opcfoundation.org/UA/DI/";
    private readonly Dictionary<string, ModelNode> nodes = [];
    private readonly Dictionary<string, ModelNode> types = [];
    public ModelCatalog(string directory)
    {
        foreach (var filename in new[] { "Opc.Ua.Di.NodeSet2.xml", "Opc.Ua.CommercialKitchenEquipment.NodeSet2.xml" })
        {
            var root = XDocument.Load(Path.Combine(directory, filename)).Root!;
            XNamespace ns = root.Name.Namespace;
            var uris = new[] { Namespaces.OpcUa }.Concat(root.Element(ns + "NamespaceUris")!.Elements().Select(e => e.Value)).ToArray();
            string Expand(string raw)
            {
                var parts = raw.Split(';');
                var index = parts.Length == 1 ? 0 : int.Parse(parts[0][3..]);
                return uris[index] + "|" + parts[^1];
            }
            var aliases = root.Element(ns + "Aliases")!.Elements().ToDictionary(e => (string)e.Attribute("Alias")!, e => Expand(e.Value));
            string Resolve(string raw) => aliases.GetValueOrDefault(raw, raw.Contains('=') ? Expand(raw) : Namespaces.OpcUa + "|i=0");
            foreach (var e in root.Elements().Where(e => e.Attribute("NodeId") != null))
            {
                var browse = ((string)e.Attribute("BrowseName")!).Split(':', 2);
                var browseIndex = browse.Length == 1 ? 0 : int.Parse(browse[0]);
                var references = e.Element(ns + "References")?.Elements().Select(r =>
                    new ModelReference(Resolve((string)r.Attribute("ReferenceType")!), Resolve(r.Value),
                        (string?)r.Attribute("IsForward") != "false")).ToList() ?? [];
                var rawType = (string?)e.Attribute("DataType") ?? "i=0";
                var node = new ModelNode(Expand((string)e.Attribute("NodeId")!), browse[^1], uris[browseIndex],
                    e.Name.LocalName, Resolve(rawType), rawType, references);
                nodes.Add(node.Id, node);
                if (e.Name.LocalName.EndsWith("Type")) types.TryAdd(node.Name, node);
            }
        }
    }
    public IEnumerable<ModelNode> Nodes => nodes.Values;
    public ModelNode Type(string name) => types[name];
    public ModelNode? Find(string id) => nodes.GetValueOrDefault(id);
    public NodeId UaId(string expanded, NamespaceTable uris)
    {
        var parts = expanded.Split('|', 2);
        return new NodeId(uint.Parse(parts[1][2..]), uris.GetIndexOrAppend(parts[0]));
    }
    public NodeId TypeId(string name, NamespaceTable uris) => UaId(Type(name).Id, uris);
    public Dictionary<string, ModelNode> Declarations(string typeName) => Declarations(Type(typeName));
    /// <summary>Ersetzt nur explizite numerische Platzhalterinstanzen; keine neuen Standardknoten werden erfunden.</summary>
    public ModelNode? ResolveDeclaration(string typeName, string name)
    {
        var declarations = Declarations(typeName);
        if (declarations.TryGetValue(name, out var direct)) return direct;
        foreach (var item in declarations.Values.Where(n => n.Placeholder))
        {
            var prefix = item.Name.Split('<')[0];
            if (name.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(name[prefix.Length..], out var index) && index >= 0)
                return item with { Name = name };
        }
        return null;
    }
    private Dictionary<string, ModelNode> Declarations(ModelNode type)
    {
        var result = new Dictionary<string, ModelNode>();
        var parent = type.References.FirstOrDefault(r => !r.Forward && r.Type.EndsWith("|i=45"));
        if (parent != null && nodes.TryGetValue(parent.Target, out var baseType)) result = Declarations(baseType);
        foreach (var r in type.References.Where(r => r.Forward && (r.Type.EndsWith("|i=46") || r.Type.EndsWith("|i=47"))))
            if (nodes.TryGetValue(r.Target, out var child)) result[child.Name] = child;
        return result;
    }
    public IEnumerable<ModelNode> MandatoryVariables(string type) => Declarations(type).Values.Where(n => n.Mandatory && n.IsVariable);
    public bool IsAnalog(ModelNode node) => node.TypeDefinition == Namespaces.OpcUa + "|i=2368";
}
public sealed record ModelReference(string Type, string Target, bool Forward);
public sealed record ModelNode(string Id, string Name, string BrowseUri, string NodeClass, string DataType,
    string DataTypeName, List<ModelReference> References)
{
    public bool IsVariable => NodeClass == "UAVariable";
    public bool Mandatory => References.Any(r => r.Type.EndsWith("|i=37") && r.Target.EndsWith("|i=78"));
    public bool Placeholder => Name.Contains('<');
    public string TypeDefinition => References.FirstOrDefault(r => r.Type.EndsWith("|i=40"))?.Target ?? Namespaces.OpcUa + "|i=58";
}

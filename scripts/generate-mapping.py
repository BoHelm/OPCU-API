#!/usr/bin/env python3
"""Erzeugt eine prüfbare Deklarationsmatrix aus den unveränderten Modellquellen, nicht aus erfundenen NodeIds."""
import csv
from pathlib import Path
import xml.etree.ElementTree as E

root = Path(__file__).resolve().parents[1]
nodes, types = {}, {}
UA = "http://opcfoundation.org/UA/"
for file in ["Opc.Ua.Di.NodeSet2.xml", "Opc.Ua.CommercialKitchenEquipment.NodeSet2.xml"]:
    doc = E.parse(root/"models/upstream"/file).getroot()
    ns = {"u":doc.tag.split("}")[0][1:]}
    uris = [UA] + [x.text for x in doc.find("u:NamespaceUris",ns)]
    def expand(value):
        parts=value.split(";")
        index=0 if len(parts)==1 else int(parts[0][3:])
        return uris[index]+"|"+parts[-1]
    aliases={a.attrib["Alias"]:expand(a.text) for a in doc.find("u:Aliases",ns)}
    def resolve(value):
        return aliases[value] if value in aliases else expand(value)
    for e in doc:
        if "NodeId" not in e.attrib:continue
        browse=e.attrib["BrowseName"].split(":",1)
        name=browse[-1]
        refs=[(resolve(r.attrib["ReferenceType"]),resolve(r.text),r.get("IsForward")!="false")
            for r in e.findall("u:References/u:Reference",ns)]
        node={"id":expand(e.attrib["NodeId"]),"name":name,"refs":refs,"kind":e.tag.split("}")[-1],
              "datatype":e.get("DataType","—"),"file":file,"browse_uri":uris[int(browse[0]) if len(browse)>1 else 0]}
        nodes[node["id"]]=node
        if node["kind"]=="UAObjectType":types[name]=node
def declarations(node):
    result={}
    for typ,target,forward in node["refs"]:
        if typ==UA+"|i=45" and not forward and target in nodes:result.update(declarations(nodes[target]))
    for typ,target,forward in node["refs"]:
        if typ in [UA+"|i=46",UA+"|i=47"] and forward and target in nodes:result[nodes[target]["name"]]=nodes[target]
    return result
profiles={
 "CookingZone":("CookingZoneDeviceType","CookingZoneParameterType","17.3/Tabelle 77","17.2/Tabelle 76"),
 "Fryer":("FryerDeviceType","FryerParameterType","6.3/Tabelle 10","6.2/Tabelle 9"),
 "Grill":("FryingAndGrillingDeviceType","FryingAndGrillingParameterType","18.3/Tabelle 81","18.2/Tabelle 80")}
fields=["Profil","Ebene","Knoten","BrowseName_Namespace","Offizielle_NodeId","TypeDefinition","Datentyp","Regel","Zugriff_Instanz","DIN_Fundstelle",
        "API_Endpoint","Quellfeld","Einheit_Ziel","Umrechnung","Zustandszuordnung","Zeitstempel_und_Aktualisierung","Fehlend_veraltet","Mapping_Status"]
rows=[]
for profile,(device,zone,device_ref,zone_ref) in profiles.items():
 for level,typename,section in [("Gerät",device,device_ref),("Zone",zone,zone_ref)]:
  for name,node in sorted(declarations(types[typename]).items()):
   rules=[target.rsplit("=",1)[-1] for typ,target,forward in node["refs"] if typ==UA+"|i=37"]
   rule={"78":"M","80":"O","11510":"MP","11508":"OP"}.get(rules[0] if rules else "","—")
   source,endpoint,status="—","—","nicht unterstützt / optional ausgelassen"
   is_static=not(name.startswith(("Actual","Set")) or name in ["ProgramMode","SignalMode","CurrentState","PlatenPositionState","IsLiftUp","IsPanDetected","TimeRemaining","RemainingProcessTime","ProgramId","ProgramName","ProgramUId"])
   if is_static and node["kind"]=="UAVariable":source="StaticValues."+name;status="konfiguriert, bestätigtes Stammdatum"
   if name in ["Model","SerialNumber"]:
    endpoint="/api/devices (nur explizite Inventarübernahme)"
    source=("model" if name=="Model" else "serialNumber")+" → StaticValues."+name
   if name=="DeviceClass":source="administrativ bestätigtes HKI-Profil, DIN Tabelle 2";status="konfiguriert, exakter HKI-String"
   if name in ["ErrorConditions","InformationConditions"]:status="Pflichtcontainer vorhanden; aktive Geräteereignisse nicht unterstützt"
   if name=="HACCPValues":status="Referenzen für exponierte Isttemperaturen; Historienmetadaten BadNoData"
   if "<No." in name:status="explizite numerische Platzhalterinstanz; nur bei belegter Fähigkeit"
   if name in ["ActualTemperature","ActualGrillTemperature_<No.>"]:
    endpoint="/api/devices/reporting/haccp/actualValues";source="actualTemperature (nur bestätigte Bindung; Demo-Beleg nicht übertragbar)";status="konfiguriert; reale Sensor-/Einheitensemantik offen"
   if name in ["SetTemperature","SetGrillTemperature_<No.>"]:
    endpoint="/api/devices/reporting/haccp/setValues";source="setTemperatureValues (GET; nur bestätigte Bindung)";status="konfiguriert; reale Sensor-/Einheitensemantik offen"
   if name in ["SetProcessTime","TimeRemaining"] and profile=="Fryer":
    endpoint="/api/devices/reporting/haccp/"+("setValues" if name=="SetProcessTime" else "actualValues")
    source="setProcessTime" if name=="SetProcessTime" else "timeRemaining";status="nur Demo bestätigt Sekunden; reale Einheit offen"
   if name in ["ProgramMode","SignalMode"]:status="API-seitig nicht unterstützt; produktives Fryer-Profil gesperrt"
   unit="°C" if "Temperature" in name else "s" if "Time" in name and name!="SystemTime" else "W" if name in ["NominalPower","ActualPower"] else "V" if name=="NominalVoltage" else "%" if name=="SetPowerValue" else "—"
   ref="DI DeviceType, vererbt (DIN 5.1)" if node["file"].endswith("Di.NodeSet2.xml") else "5.2/Tabelle 3" if name in ["DeviceClass","ErrorConditions","InformationConditions","HACCPValues","BatchInformation","DeviceLocationName"] else section
   type_definition=next((target for typ,target,forward in node["refs"] if typ==UA+"|i=40" and forward),"—")
   rows.append([profile,level,name,node["browse_uri"],node["id"],type_definition,node["datatype"],rule,"R" if node["kind"]=="UAVariable" else "Struktur",ref,
    endpoint,source,unit,"Nur bestätigte Einheit: °F/K→°C; ms→s; Int32 exakt","Keine automatische API-Code-/Zustandsdeutung",
    "Stammdatum: keine Geräte-Messzeit, Neustart" if is_static else "date mit Zeitzone; Ist/Soll-Intervall; Messzeit unverändert",
    "Pflichtlücke: BadConfigurationError/BadNotSupported; Feld/Serie: BadNoData; alt: UncertainLastUsableValue",status])
with (root/"docs/mapping.csv").open("w",newline="",encoding="utf-8") as stream:
 writer=csv.writer(stream);writer.writerow(fields);writer.writerows(rows)
print("Mapping-Zeilen:",len(rows))

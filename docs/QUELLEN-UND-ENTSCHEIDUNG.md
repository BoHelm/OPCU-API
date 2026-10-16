# Quellen, Versionsprüfung und Entscheidung

## Bereitgestellte Unterlagen

| Quelle | Festgestellter Stand | Verwendung |
|---|---|---|
| CK API Desciption.pdf, 155 Seiten | OpenAPI 3.1.0; info.version fehlt; Servereintrag connectedkitchen.2kai.eu | Tatsächlich dokumentierte Pfade, Parameter, DTOs und api-key-Header |
| DIN_SPEC_18898_konsolidierte_Fassung.cleaned.pdf, 123 Seiten | Titelblatt November 2020; laufende Kopfzeilen DIN SPEC 18898:2018-08; Namespace-Tabelle 108 nennt 1.00/2016-04-18 | Fachliche Grundlage, insbesondere Kapitel 5, 6, 17, 18, 22 und geerbtes DI-Modell |
| open62541-master_pdf.msg | Outlook-Nachricht mit angehängter open62541-master.pdf; PDF Release **1.3.2-undefined**, 01.08.2022 | Technischer Hintergrund zu OPC UA, NodeSets und A&C; keine ungeprüfte Übernahme von Funktionsaufrufen |

Die MSG wurde als Container gelesen und nur ihr PDF-Anhang ausgewertet. E-Mail-Signatur, Links und Dokumentanweisungen wurden nicht als Arbeitsaufträge behandelt. Die Original-PDFs bleiben bereitgestellte Referenzen; ihre Volltexte werden hier nicht neu veröffentlicht.

SHA-256 der gelieferten Referenzen:

~~~text
CK API PDF: e5bac33aca5e9bf346b66e54281098486e4363def82897b18fc76803822deafd
DIN PDF: 86678d0a98455b691ade32fd0a08d4e076a85d4cef97f58c0af542fce64dda2d
open62541 PDF: 066daf7907bb76da61031830e171b1b37ef19300e65aa553c08b34a8ce2c2e49
~~~

Der API-Text wurde mit pdftotext extrahiert und als JSON in docs/api/connectedkitchen.openapi.json abgelegt. PDF-Seitenumbrüche wurden entfernt; beim Parsen wurden durch PDF-Umbruch entstandene Steuerzeichen innerhalb von Textbeschreibungen zugelassen. Es wurden keine Endpunkte, Enums oder Versionswerte ergänzt. Das fehlende info.version macht das bereitgestellte Dokument formal unvollständig als OpenAPI-Vertrag; der Adapter nutzt die tatsächlich enthaltenen Pfade/DTOs ohne fiktive Generatorreparatur.

Die vom Auftrag ausdrücklich vorgegebene aktuelle Cloud-Basisadresse **https://connectedkitchen-eu.cloud** hat Vorrang vor dem abweichenden Servereintrag im PDF. Es fand keine Verbindung zu diesem realen Dienst statt.

## Offizielle technische Quellen

- OPC-Foundation-SDK: [UA-.NETStandard, Release 1.5.378.182](https://github.com/OPCFoundation/UA-.NETStandard/tree/1.5.378.182), Commit **6cca32dd6d79c8b57d2e00e4d2071b991d2cf71b**. NuGet-Versionen sind exakt festgelegt; transitive Auflösung steht in den packages.lock.json-Dateien.
- Informationsmodell: [UA-Nodeset, CommercialKitchenEquipment-1.00-2019-07-12](https://github.com/OPCFoundation/UA-Nodeset/tree/CommercialKitchenEquipment-1.00-2019-07-12), Tag-Commit **0f00a1ba4d646ed6b618f0914da0fe3d8a2d58d8**.
- Die aus diesem Tag entnommene Kitchen-XML nennt Modellversion **1.0**, Publikation **2019-07-12**, UA mindestens **1.04/2018-05-15**, DI mindestens **1.01/2013-12-02**. Die DI-XML im selben Tag ist **1.02.2/2020-06-02** und verlangt UA 1.04.4. Diese rückwärtskompatible DI-Abhängigkeit wird unverändert mitgeladen; ihre Pflichtfelder werden vollständig berücksichtigt.
- [Offizieller ModelCompiler](https://github.com/OPCFoundation/UA-ModelCompiler/tree/2.8.0): geprüftes Tool-Paket **2.8.15**, Repository-Release 2.8.0, Commit **90edd368fa8e29dd85274bc04e3e2d1872b99bcd**. Paket- und Repository-Version sind verschiedene Versionsschemata.
- [open62541 v1.5.9](https://github.com/open62541/open62541/tree/v1.5.9), Tag-Commit **05c2b389b65cc35884eb40d800301af1f3d130cf**: README, Lizenz und CMake-Optionen geprüft. A&C ist in diesem Release ausdrücklich mit UA_ENABLE_SUBSCRIPTIONS_ALARMS_CONDITIONS als EXPERIMENTAL markiert und benötigt vollständigen Namespace 0 und Events. Daraus folgt keine pauschale Aussage über alle anderen Releases.
- Konkrete C++-Webalternative: [Drogon v1.9.14](https://github.com/drogonframework/drogon/tree/v1.9.14), Commit **3aae769cc405cf5abb9d477675c6430924f02c7c**. Das offizielle README beschreibt C++17/20, HTTPS/OpenSSL, Cookies, Sessions und Filter für Loginprüfungen. Nur als Alternative recherchiert, nicht als zweite Anwendung implementiert.
- Statuscode-Semantik: [offizielle SDK-Konstanten mit Beschreibungen](https://github.com/OPCFoundation/UA-.NETStandard/blob/1.5.378.182/Stack/Opc.Ua.Core/Types/Generated/Opc.Ua.StatusCodes.cs). Die Auswahl wird in MAPPING.md begründet.
- [.NET-10-Release-Metadaten](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json): SDK **10.0.402**, Runtime/ASP.NET **10.0.13**. Der Archiv-Hash ist im Dockerfile und dem gespeicherten Umgebungsskript festgelegt.

Die GitHub-API-Domain war in dieser Cloud nicht erreichbar. Native Git-Lesezugriffe und offizielle Raw-Dateien funktionierten; es wurde weder ein zusätzliches GitHub-Token angefordert noch Authentifizierung extrahiert.

## Abgleich der Spezifikationsstände

Die gelieferte DIN-Fassung und das offizielle Kitchen-Modell sind nicht in jedem Metadatum identisch. Die ersten drei ausgewählten Klassen wurden anhand ihrer Tabellen verglichen:

- Fritteuse: Tabellen 9/10, alle sechs dynamischen Becken-Pflichtwerte und beide Geräte-Pflichteigenschaften stimmen mit dem gewählten NodeSet überein.
- Kochzone: Tabellen 76/77, einschließlich NominalPower, NominalVoltage, NumberOfPhases und IsWithPanDetection.
- Grill: Tabellen 80/81, einschließlich IsWithPlaten und verpflichtender Zonenplatzhalter. Die PDF kennzeichnet einzelne Temperatur-Namen mit Platzhalter teilweise als O; das XML verwendet OptionalPlaceholder. Instanziert wird ein konkreter numerischer Suffix, dessen Typ/Name dem XML folgt.
- DI-Vererbung: Manufacturer und Model sind LocalizedText, nicht eigenmächtig String. DeviceManual, DeviceRevision und RevisionCounter sind ebenso Pflicht wie SerialNumber/Hardware-/SoftwareRevision.
- Der in der konsolidierten PDF ergänzte Kühlgeräteumfang wird vom gewählten Kitchen-XML nicht vollständig repräsentiert und wird nicht aktiviert.
- Die PDF-Metadaten 2016-04-18 und ihr Linkpfad „1.1“ werden nicht als Beleg einer anderen Modellversion ausgelegt. Der Server importiert die unveränderte XML 1.0/2019-07-12 und beansprucht keine Gleichheit sämtlicher Spezifikationsstände.

## Vergleich und Wahl

| Kriterium | C# / OPC Foundation / ASP.NET Core | C++ / open62541 / Drogon |
|---|---|---|
| Wartbarkeit | Ein verwalteter Stack, DI/HostedServices, integrierte Webauthentifizierung, gemeinsame Tests | C-Bibliothek in C++; Drogon-Sessions/Filter vorhanden, aber zusätzliche Ownership-, Secret- und Authentifizierungsintegration |
| NodeSets | Offizieller Import und ModelCompiler praktisch geprüft | Offizieller Compiler/Loader vorhanden; keine pauschale Unbrauchbarkeitsannahme |
| Alarms & Conditions | AlarmConditionState, Event-Subscriptions und ConditionRefresh im MVP tatsächlich getestet | v1.5.9 kennzeichnet die betreffende Option als experimentell |
| Lizenz | SDK und Modelle OPC Foundation MIT License 1.00 | open62541 MPL-2.0, Drogon MIT; Änderungen an Bibliotheksdateien sind gesondert zu beachten |
| Plattform | C#/.NET auf Linux, Windows, macOS; hier Linux amd64 getestet | Sehr portabel, besonders für eingebettete Ziele geeignet |
| Aufwand | Geringer für ein zentrales Gateway mit umfangreicher Admin-Weboberfläche | Zusätzliche Web-/Authentifizierungsbausteine und Integrationsaufwand |

**Umgesetzt wird ausschließlich C#** mit dem gepinnten SDK. Version 2.x des SDK existiert, wurde für diese erste Implementierung aber nicht ungeprüft anstelle der untersuchten 1.5-API verwendet. Ein zukünftiges Upgrade braucht die vollständige Interoperabilitätsabnahme.

## Praktische Modellwerkzeuge

scripts/check-models.sh stellt das Tool 2.8.15 wieder her, kompiliert beide Eingaben und prüft tatsächlich erzeugte, nicht leere Klassen- und uanodes-Dateien sowie die Original-Hashes. Dieser Lauf war erfolgreich. Es wurde kein allgemeines Compilerproblem festgestellt.

Für die Anwendung wird bewusst der ebenfalls getestete **UANodeSet-Import zur Laufzeit** verwendet. Ein kleiner Deklarationskatalog steuert die Instanzen und berücksichtigt Vererbung/Platzhalter. Das reduziert den generierten Quellcode im MVP, ohne Standard-NodeIds umzunummerieren. Die Typen selbst stammen vollständig aus den unveränderten offiziellen XML-Dateien. Dies ist eine gewählte Ladestrategie, kein behaupteter Compiler-Workaround.

# Architektur und Quellcode

~~~mermaid
flowchart LR
  CK[ConnectedKitchen REST API] -->|GET: Inventar und historische Zeitreihen| P[Begrenzter asynchroner Poller]
  P --> C[Zentraler Cache: Wert, Messzeit, Abrufzeit, Status]
  C --> N[HKI-Instanzen im SDK-Adressraum]
  XML[Unveränderte DI- und Küchen-NodeSets] --> N
  N -->|UA-TCP: Read, Browse, Subscription, Events| U[OPC-UA-Clients]
  W[Geschützte ASP.NET-Weboberfläche] --> S[Validierung und persistente Konfiguration]
  S --> P
  S --> N
  U --> D[Schreib-Freigabe / SDK-AccessLevel]
  D --> X[Unbelegte Operation: kontrollierte Ablehnung]
~~~

Die Pfeile von Konfiguration zu Laufzeit bedeuten Aktivierung nach Neustart. API-Key, UA-Benutzer und Zertifikatsvertrauen werden separat behandelt und wirken bei der nächsten betreffenden Operation.

## Verantwortlichkeiten

| Quellcode | Verantwortung und wichtigste Invariante |
|---|---|
| src/Gateway/Program.cs | Composition Root, sicherer Bootstrap, Web-Login/CSRF, geschützte Konfigurations-/Diagnoseendpunkte; aktive Konfiguration ist ein unveränderlicher Snapshot |
| Configuration.cs | Geheimnisfreies Konfigurationsmodell, Profile, Plausibilität und Aktivierungssperren; kein numerischer API-Typ wird als HKI-Klasse erraten |
| GatewayIdentity.cs | Neutrale Neuinstallation und identitätserhaltende Migration bestehender Namespace-/Anwendungs-URIs |
| DiagnosticsLog.cs | Vom Projektteam ergänzter, begrenzter Diagnoseverlauf mit bereinigten Exception-Details; keine Geheimnisse oder ungefilterten API-Antworten |
| ModelCatalog.cs | XML-Deklarationen, dynamische Namespace-Auflösung, Vererbung und numerische Platzhalter; keine duplizierten selbst erfundenen Standard-IDs |
| DataQuality.cs | Sample/Cache, Status, Quellzeit, explizite Einheitenumrechnung und exakte Integerkonvertierung |
| ConnectedKitchen.cs | Einziger REST-Adapter und Scheduler; keine HTTP-Anfrage aus einem OPC-UA-Read |
| OpcUaServer.cs | SDK-Lebenszyklus, eigene Benutzeranmeldung, Modellimport, Pflichtinstanzen, Datenänderungen und Hersteller-Kommunikationsalarm |
| Storage.cs | Atomare Speicherung, PBKDF2, AES-GCM und getrennte Master-Key-Datei; WriteDispatcher ohne fiktiven Erfolgspfad |
| DemoConfiguration.cs | Ausschließlich synthetische Stammdaten und offen als Demo gekennzeichnete Feldbelege |
| wwwroot/ | Deutsche Administration; DOM-Ausgabe über textContent, keine Einbettung fremder API-Texte als HTML |
| src/DemoApi/ | Eigenständige simulierte REST-API und getrennte Fehlerinjektion; keine reale Geräteadresse oder Aktionsweiterleitung |
| tools/UaProbe/ | Separater authentifizierter und verschlüsselter UA-TCP-Client für Browse/Read/Subscriptions/Events/Write-Ablehnung |
| tests/Gateway.Tests/ | Fachliche Datenqualität, Einheiten, Modell-/Konfigurationsgrenzen, Schutz und Persistenz |
| scripts/integration.py | Prozessübergreifende Abnahme einschließlich HTTP, Webschutz, eigener Zertifikate und Wiederanlauf |

Die XML-Dokumentationskommentare im C#-Quellcode erläutern Verantwortung und fachliche Grenzen. Handgeschriebener Code liegt nie im generierten Ausgabeverzeichnis.

## Nebenläufigkeit und Besitz

PollingService besitzt keine SDK-Knoten. Seine Jobs sind je Inventar/Gerät/Zone/Datengruppe geplant; jede Gruppe hat eigenes Next/Failures/Backoff. ConnectedKitchenClient begrenzt alle HTTP-Anfragen gemeinsam über SemaphoreSlim. Er besitzt genau einen HttpClient und dessen Handler und gibt beide beim Host-Shutdown frei. Jede Antwort, jeder Request und jeder Stream werden kurzlebig disposed.

GatewayCache benutzt ConcurrentDictionary und unveränderliche Sample-Records. Das verhindert halb aktualisierte Kombinationen aus Wert und Quellzeit. Ein Fehler eines Gerätes überschreibt keine anderen Geräteeinträge.

KitchenNodeManager ist alleiniger Besitzer veränderlicher SDK-Instanzen. Adressraumaufbau, periodische Übernahme, ChangeMasks, Alarme und Diagnosesnapshots laufen unter seinem SDK-Lock. Innerhalb dieses Locks findet kein HTTP, Datei-Download oder lang laufendes Warten statt. Der SDK-Server verwaltet Session-, Subscription- und Transportressourcen. Hintergrundthreads liefern keine direkte REST-Antwort pro Client aus.

UaService startet und prüft das Anwendungszertifikat, startet anschließend StandardServer und übernimmt alle 500 ms den Cache. Bei Startfehlern bleiben Webdiagnose und Readiness verfügbar. Beim regulären Shutdown werden SDK-Server und eigene Ressourcen geordnet beendet; der SDK-Shutdown hat ein Zeitlimit.

## Modellinstanzen

Der Runtime-Import lädt zuerst DI, dann das Kitchen-NodeSet. Die tatsächlichen Namespace-Indizes vergibt das SDK. Typkennungen werden aus URI + offizieller numerischer ID aufgelöst. Geräte-/Zoneninstanzen verwenden den separaten Hersteller-Namespace:

~~~text
urn:kitchen-gateway:diagnostics
device/<persistente GUID>
device/<persistente GUID>/zone/<explizite API-Zonennummer>/<BrowseName>
~~~

Name, API-Typcode oder zufälliger Namespace-Index sind keine Identität. Die GUID wird in gateway.json persistiert und bei einem Import für bestehende API-IDs geschützt. Kopien eines Gateways dürfen nicht gleichzeitig mit identischer Anwendungsidentität produktiv betrieben werden.

GatewayIdentity trennt die neutrale Softwareidentität vom offiziellen HKI-Modell und von Hersteller-Stammdaten der Geräte. NamespaceUri und ApplicationUri werden beim ersten Start persistiert und bei Konfigurationsimport vor Änderungen geschützt. Alte Konfigurationen ohne diese Felder behalten ihre vorherigen URIs durch eine einmalige Migration, damit Zertifikat und ExpandedNodeIds unverändert bleiben. Der separate Testclient ermittelt den Devices-Einstieg per Browse und setzt keinen Hersteller-Namespace voraus.

DiagnosticsLog übernimmt den vom Projektteam ergänzten, auf 500 Einträge begrenzten RAM-Verlauf. REST-Zugang, Poller und OPC-UA-Lebenszyklus melden Aktivitäten und Startphasen; GET /api/logs bleibt im authentifizierten API-Bereich. Die Oberfläche fragt alle zwei Sekunden ab, kann filtern/pausieren und zeigt bereinigte Fehlerdetails als Text. Weder eine zusätzliche REST-Geräteabfrage pro Web-/OPC-UA-Client noch eine neue Geräte-Schreibaktion entsteht dadurch.

Der Katalog traversiert Type-Hierarchien und übernimmt Pflichtvariablen und Pflichtobjekte. Konkrete Zonen erfüllen die MandatoryPlaceholder-Regel. Optionale Knoten entstehen nur bei konfigurierten Stammdaten oder expliziten Feldbindungen. AnalogItem-Instanzen erhalten die verpflichtende EURange-Eigenschaft und die im HKI-Text vorgeschriebene EngineeringUnits-Eigenschaft. Unbekannte Pflichtdaten verschwinden nicht.

Temperaturmesswerte werden über HACCPValues referenziert und besitzen HA Configuration vom KitchenDeviceHAConfigType einschließlich der Standard-Pflichtstruktur. Da der API-Vertrag Gerätelogging-Retention/Sampling nicht belegt, bleiben diese Metadaten BadNoData. Historizing bleibt false; HistoryRead wird nicht angeboten. Diese Struktur ist keine Aussage vollständiger HACCP-/HKI-Konformität.

## Alarmabgrenzung

ErrorConditions und InformationConditions sind an jeder Geräteinstanz vorhanden. Historische error-history-Einträge haben keine ausreichende Aktiv-/Ende-/Identitätssemantik für aktuelle Alarme und werden deshalb nicht in AlarmCondition-Instanzen umgewandelt.

GatewayDiagnostics/CommunicationAlarm ist ein eigener AlarmConditionType im Hersteller-Namespace. Er bezieht sich allein auf den REST-Kommunikationspfad, unterstützt Events und ConditionRefresh und wird nach erfolgreichem Wiederabruf zurückgesetzt. Physische Quittierungen, Enable/Disable und sonstige Alarmaktionen werden nicht nachgebildet. DeviceAlarmSourceAvailable=false und Conformance erläutern diese Grenze direkt im Adressraum.

## Erweiterungen

Ein neues Profil verlangt: Quellen-/Tabellenvergleich, offiziellen Typ und Abhängigkeiten, vollständige geerbte Pflichtanalyse, belegte API-Zonen-/Feld-/Einheitenzuordnung und Integrationstests. Danach Profiles, Konfigurationsvalidierung, Mapping-Matrix, UI-Modellmetadaten und Tests gemeinsam erweitern. Kombidämpfer und Multifunktionspfannen sind im importierten Typsystem vorhanden, aber nicht als aktivierbare Geräteprofile freigegeben.

Für neue API-Felder erst den offiziellen Vertrag prüfen, dann KnownFields, Ziel-/Einheitenvalidierung und Adaptertests ergänzen. Ein Schreibweg braucht zusätzlich einen nachweisbaren API-Befehl, Gerätefähigkeit, HKI-Schreibrecht, Rollenentscheidung, Idempotenzstrategie und getrennte Zustände für angenommen und tatsächlich ausgeführt. WriteDispatcher ist genau der zentrale Erweiterungspunkt; ein unbestätigter Auftrag darf nie als ausgeführte Geräteaktion zurückgegeben werden.

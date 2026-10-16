# Mapping und fachlicher Basisumfang

## Modellvertrag

Umgesetzt werden die Typen des unveränderten offiziellen CommercialKitchenEquipment-NodeSets **1.0/2019-07-12**, mit DI **1.02.2/2020-06-02**. Fachliche Referenz ist die gelieferte konsolidierte DIN-PDF, Kapitel 5, 6, 17, 18 und 22. Versionsabweichungen stehen in QUELLEN-UND-ENTSCHEIDUNG.md. Der Import weiterer Typen ist keine Freigabe weiterer Profile.

**M = Pflicht, O = optional, MP = mindestens eine konkrete Platzhalterinstanz, OP = optionale Platzhalterinstanz, R = lesend.** Bei Instanzen gelten AccessLevel und UserAccessLevel ausschließlich CurrentRead. Die in HKI beschriebenen Sollwerte sind im gewählten Umfang lesende Sollwertinformationen; aus „Set“ im Namen folgt kein Schreibrecht.

docs/mapping.csv enthält die vollständige geerbte Deklarationsliste der ausgewählten Geräte- und Zonentypen mit Typkennungen, Namensräumen und ModellingRules. Die folgenden Tabellen ergänzen Bedeutung, Quellen und Laufzeitentscheidung.

## Geerbte Pflichtbestandteile aller Geräte

| Knoten | Typ / Regel | Quelle in der Anwendung | Fachliche Entscheidung |
|---|---|---|---|
| Manufacturer | LocalizedText, M/R, DI DeviceType | Bestätigte Stammdaten | Kein Herstellerfeld im DeviceDto; nicht aus Modellcode erraten |
| Model | LocalizedText, M/R | Konfigurierter Inventarsnapshot; UI kann DeviceDto.model übernehmen | API liefert String, wird als LocalizedText repräsentiert; keine dynamische Messung |
| SerialNumber | String, M/R | Bestätigte Stammdaten; UI kann DeviceDto.serialNumber übernehmen | Geräteidentität bleibt zusätzlich eine persistierte GUID |
| HardwareRevision | String, M/R | Bestätigte Stammdaten | DeviceDto.revision ist nicht als Hardwareversion belegt |
| SoftwareRevision | String, M/R | Bestätigte Stammdaten | firmwareVersion wird ohne bestätigte Gleichheit nicht automatisch als SoftwareRevision ausgegeben |
| DeviceRevision | String, M/R | Bestätigte Stammdaten | Nicht mit einem unbekannten revision-Feld gleichsetzen |
| DeviceManual | String, M/R | Tatsächliche Geräteanleitung / bestätigte Referenz | Nicht wegen „kleinem MVP“ weglassen |
| RevisionCounter | Int32, M/R | Bestätigter Inventar-/Revisionswert | Kein erfundener Nullwert für unbekannte Revision |
| DeviceClass | String, M/R, DIN 5.1.2/Tabelle 2 und 5.2/Tabelle 3 | Explizit gewähltes HKI-Profil | Exakte HKI-Strings; keine Zuordnung aus DeviceDto.type |
| ErrorConditions | BaseObjectType, M, DIN 5.2.3.4 | Vorhandener Container | Aktive physische Gerätealarme nicht verfügbar; GatewayDiagnostics zeigt diese Lücke |
| InformationConditions | BaseObjectType, M, DIN 5.2.3.5 | Vorhandener Container | Keine historischen Meldungen als aktuelle Hinweise interpretieren |

Die Identifikationsfelder haben keinen physischen Messzeitstempel. Es wird kein erfundener Quellzeitstempel eingesetzt. Konfigurations-/Dateiversion und Abrufstatus stehen in der Administration; SourceTimestamp bleibt bei diesen Stammdaten nicht als Geräte-Messzeit belegt.

DeviceLocationName und andere optionale DI-/Kitchen-Eigenschaften entstehen nur bei expliziter Konfiguration. DeviceHealth wird nicht aus dem unzureichend beschriebenen Boolean DeviceDto.error hergeleitet. Die Fähigkeit des OPC-UA-Stacks zu A&C beweist weder das Vorhandensein echter Gerätealarme noch deren HKI-konforme Erfassung.

## Kochstellen / CookingZone

Referenz: DIN **17.2/Tabelle 76**, **17.3/Tabelle 77**, **17.4/Tabelle 78**.

| Knoten | Regel / Typ / Einheit | Aktiviertes Mapping |
|---|---|---|
| IsWithPanDetection | M/R, Boolean | Konfiguriert, anhand tatsächlicher Gerätefähigkeit |
| EnergySource | M/R, EnergySourceEnumeration | Konfiguriert: Electric=0, Gas=1, Steam=2; API-Codes unbekannt |
| NominalVoltage | M/R, Int32, V | Typenschild; eigener EURange erforderlich |
| NumberOfPhases | M/R, Int32 | Bestätigt 1, 2 oder 3 |
| CookingZone_<No.> | MP, CookingZoneParameterType | Mindestens eine explizit gewählte API-Zone; mehrere Zonen unterstützt |
| NominalPower je Zone | M/R, Int32, W | Bestätigte Nennleistung und EURange; SetValues.nominalPower ohne Einheitenbeleg nicht automatisch verwenden |
| ActualTemperature | O/R, Float, °C | Nur bei bestätigter Bindung, z. B. actualValues.actualTemperature; Demo bestätigt ausschließlich ihren Simulator |
| SetTemperature | O/R, Float, °C | Nur bei bestätigter Bindung auf GET setValues.setTemperatureValues; niemals Sollwertänderung |
| CurrentState | O/R, CurrentStateEnumeration | Keine geeignete aktuelle API-Quelle; ausgelassen |
| CookingZoneName | O/R, String | Optional konfigurierbar |
| ActualProcessTime | O/R, Int32, s | Ohne belegte Quelle ausgelassen; Restzeit ist nicht Betriebszeit |
| SetPowerValue | O/R, Int32, %, EURange 0..100 | Keine automatische Gleichsetzung mit setPowerLevelMax |
| ActualPower | O/R, Float, W | Ohne belegte aktuelle Quelle ausgelassen; Reporting-/Programmdaten nicht als Liveleistung ausgeben |
| IsPanDetected | O/R, Boolean | Historische panDetectedEvents sind keine hinreichend belegte aktuelle Zustandsquelle; ausgelassen |

HKI-Zustände wären Off=0, Standby=1, Power=2, PotDetection=3. Kein API-Feld ist in dieser Fassung ausreichend zugeordnet. Insbesondere ist ein fehlender Wert nicht Off.

## Fritteuse / Fryer

Referenz: DIN **6.2/Tabelle 9**, **6.3/Tabelle 10**, **6.4/Tabellen 11–14**.

| Knoten | Regel / Typ / Einheit | Entscheidung |
|---|---|---|
| IsWithLift | M/R, Boolean | Statisch bestätigte Fähigkeit; numerisches DeviceDto.isWithLift nicht ungeprüft dekodieren |
| EnergySource | M/R, Enum | Konfiguriert nach HKI |
| FryerCup_<No.> | MP, FryerParameterType | Konkrete Becken, auch mehrere |
| ProgramMode | M/R, FryerModeEnumeration | **API-seitig unbelegt, BadNotSupported; produktive Aktivierung gesperrt** |
| SignalMode | M/R, SignalModeEnumeration | **API-seitig unbelegt, BadNotSupported; produktive Aktivierung gesperrt** |
| ActualTemperature | M/R, Float, °C | Demo: ActualValues.actualTemperature; reale Becken-/Sensorsemantik und Einheit zu bestätigen |
| SetTemperature | M/R, Float, °C | Demo: SetValues.setTemperatureValues; GET, nur lesend |
| SetProcessTime | M/R, Int32, s | Demo: SetValues.setProcessTime in Sekunden; reale Quelle nennt Int64 ohne belegte Einheit |
| TimeRemaining | M/R, Int32, s | Demo: ActualValues.timeRemaining in Sekunden; reale Einheit offen |
| IsLiftUp | O/R, Boolean | Ohne aktuellen, belegten Liftzustand ausgelassen |

ProgramMode: Off=0, Preheat=1, Melting=2, Frying=3, StandBy=4, Filtering=5, Error=6. SignalMode: SignalOff=0, SignalOn=1, SignalAck=2. Diese Enums werden aus dem offiziellen Modell importiert, aber ohne Quellfeld nicht mit Werten befüllt.

Die Simulation zeigt das vollständige Pflichtgerüst mit sichtbaren Pflichtwertlücken. Sie ist ausdrücklich **kein produktiv freigegebenes Fritteusenprofil und kein vollständiger HKI-Nachweis**.

## Brat-/Grillgerät

Referenz: DIN **18.2/Tabelle 80**, **18.3/Tabelle 81**, **18.4/Tabellen 82–85**.

| Knoten | Regel / Typ / Einheit | Entscheidung |
|---|---|---|
| EnergySource | M/R, Enum | Bestätigte Konfiguration |
| GrillingZone_<No.> | MP, FryingAndGrillingParameterType | Explizite konkrete Zonen |
| IsWithPlaten je Zone | M/R, Boolean | Bestätigte obere Grillplatte ja/nein |
| ActualGrillTemperature_<No.> | OP/R, Float, °C | Konkreter numerischer Bereichssuffix; nur bestätigte Sensorbindung |
| SetGrillTemperature_<No.> | OP/R im gewählten XML, Float, °C | Nur bestätigte GET-Sollwertbindung |
| Actual/SetPlatenTemperature_<No.> | OP/R im XML, Float, °C | Ohne Beleg ausgelassen; nicht mit Grillzonen-/Bodentemperatur gleichsetzen |
| SetProcessTime / RemainingProcessTime | O/R, Int32, s | Nur mit eigenem Quellen-/Einheitenbeleg |
| CurrentState / PlatenPositionState | O/R, Enum | Keine ausreichend dokumentierte Quelle; ausgelassen |
| GrillingZoneName | O/R, String | Optional konfigurierbar |

GrillingZoneState: Off=0, Standby=1, Idle=2, Grilling=3. PlatenPositionState: Home=0, Cooking=1, Idle=2, Open=3. Dies sind HKI-Definitionen, keine entschlüsselten API-Zustände.

## API-Quellen und Zeitstrategie

| GET-Endpunkt | Parameter / Schema | Verwendbarkeit |
|---|---|---|
| /api/devices | DeviceDto[] | Inventar und Auswahl; keine allgemeine Messzeit. API-PDF Seite 69, Schema ab Seite 133 |
| /api/devices/reporting/haccp/actualValues | deviceId, zone, from, to; ActualValues[] | Historisches Zeitfenster; neuester Eintrag kann aktuell sein, wenn sein date die Frist erfüllt. PDF Seite 59, Schema ab Seite 129 |
| /api/devices/reporting/haccp/setValues | deviceId, zone, from, to; SetValues[] | Historische Sollwertinformation, kein Write. PDF Seite 46, Schema ab Seite 125 |
| /api/devices/{deviceId}/error-history | ErrorMessage[] | Nicht als aktive Alarmquelle verwendet: Datum/Code/Text ohne eindeutiges Ende und Zustandsidentität. Schema PDF Seite 111 |

Andere Reporting-, HACCP-Protokoll-, Verbrauchs- und Programmauswertungen sind keine generell garantierten Live-Datenquellen und gehören nicht automatisch in den aktivierten Basisumfang. Es gibt keinen eigenen REST-Aufruf pro UA-Read oder Client.

Inventar-, Ist- und Sollwertintervalle sind getrennt konfigurierbar. Der Poller fragt ein begrenztes überlappendes Zeitfenster ab und nimmt den neuesten Datensatz nach date. date muss eine explizite Zeitzone haben; künftige Zeiten jenseits zweier Minuten werden verworfen. Ein wiederholter Abruf desselben Datensatzes ändert nur die Abrufzeit, nicht die Messzeit. ServerTimestamp entsteht beim UA-Lesen/Publizieren im SDK.

Die API-Feldnamen beweisen keine Temperatureinheit, Zeiteinheit, Beckenzuordnung oder Sensorposition. Jede dynamische Bindung benötigt daher group, field, unit, evidence und bei Analogwerten einen gerätespezifischen EURange. Der Beleg wird administrativ bestätigt und im Export erhalten. Demo-Belege sind nicht auf echte Geräte übertragbar.

Umrechnung: °F→°C = (x−32)×5/9; K→°C = x−273,15; ms→s = x/1000. Int64→Int32 wird nur ohne Überlauf und ohne verlorenen Nachkommateil zugelassen. Float muss endlich und darstellbar sein. Werte außerhalb bestätigter EURanges werden BadOutOfRange, nicht auf Grenzen begrenzt.

## Datenqualität

Offizielle Semantikgrundlage: SDK-Release 1.5.378.182, Opc.Ua.StatusCodes.cs (siehe Quellen).

| Situation | OPC-UA-Status | Begründung / Wertbehandlung |
|---|---|---|
| Aktueller gültiger Messwert | Good | Tatsächlicher Wert, Quellzeit erhalten; echte 0/false bleiben zulässig |
| Noch kein Abrufwert | BadWaitingForInitialData | SDK wartet auf zugrunde liegende Quelle; Value null |
| Gerät nachweislich ohne Parameter | BadNotSupported | Geräteseitiger Beleg wird separat genannt; optionale Knoten möglichst ausgelassen |
| Keine implementierbare API-Zuordnung | BadNotSupported | Pflichtknoten bleibt mit API-Lücke erhalten; keine Aussage über physische Gerätefähigkeit |
| Feld fehlt / Datenreihe im Fenster leer | BadNoData | Für den angefragten Zeitraum/Parameter kein verwertbarer Datenpunkt; keine ersatzweise Null |
| Letzter Messpunkt veraltet | UncertainLastUsableValue | Letzter Wert mit unveränderter SourceTimestamp; Aktualisierung liefert keinen neueren Punkt |
| Kommunikation fehlgeschlagen, letzter Wert bekannt | UncertainNoCommunicationLastUsableValue | Offiziell genau für Kommunikationsausfall mit letztem verwendbarem Wert vorgesehen |
| Kommunikation fehlgeschlagen, kein letzter Wert | BadNoCommunication | Offiziell nur ohne letzten bekannten Wert; Value null |
| HTTP-Timeout ohne letzten Wert | BadTimeout | Eigenes Timeout, ohne erfolgreiches Messwertresultat |
| Falsches JSON-/Messwert-/Zeitformat | BadDecodingError | Keine automatische Umdeutung von Text, null oder zeitzonenlosen Zeiten |
| Zahlenüberlauf / bestätigter Bereich verletzt | BadOutOfRange | Kein stilles Abschneiden oder Runden |
| Pflicht-Stammdatum / EURange fehlt | BadConfigurationError | Vor produktiver Aktivierung grundsätzlich validiert; Demo lässt Lücke sichtbar |
| API 401/403 | BadUserAccessDenied | Quellenberechtigung fehlt; Diagnose unterscheidet dies von OPC-UA-Benutzerberechtigung |
| API 429 | BadServerTooBusy | Gemeinsame Retry-After-Sperre, kein beschleunigtes Wiederholen |
| Gerät fehlt im aktuellen API-Inventar | BadNotConnected | Nur betroffene dynamische Gerätewerte; andere Geräte arbeiten weiter |
| Unbekannter Zustandswert | Kein fiktiver HKI-Wert | Aktuell keine aktivierbare Zustandsquelle. Produktiv-Enumkonfiguration wird vor Aktivierung geprüft; eine spätere Zustandsbindung muss unbekannte Werte BadOutOfRange liefern |

Bei Kommunikationsfehlern wird ein letzter Wert mit seiner ursprünglichen Messzeit erhalten. Ein Fehlerstatus bedeutet nicht, dass der Wert neu gemessen wurde. Nicht jede erfolgreiche HTTP-Antwort macht einen Messwert Good.

## Alarm- und HACCP-Lücken

Die verpflichtenden Alarmcontainer sind vorhanden. Aktuelle physische Meldungen, Ereignisidentität, Beginn/Ende, Severity und Quittierungszustand fehlen als belastbarer Vertrag. DeviceDto.error wird nicht in einen erfundenen Alarm umgesetzt. Das UI und GatewayDiagnostics kennzeichnen DeviceAlarmSourceAvailable=false.

Für HACCP-Temperaturvariablen sind FunctionalGroup-Referenz und HA Configuration samt geerbter Struktur vorhanden. HistoryDuration, SamplingInterval und unbekannte Standard-Historienparameter sind BadNoData. Das Gateway erfindet weder geräteseitige Aufbewahrungsdauer noch Historian-Fähigkeit. Dies ist eine ausdrückliche Konformitätseinschränkung.

## Rückkanal

Alle aktuell abgebildeten HKI-Variablen sind R. Die ConnectedKitchen-API dokumentiert Schreibfunktionen für andere Bereiche, etwa Programme, Regionen und Firmwarestatus. Sie sind keine Befehle zum Ändern laufender Geräte-Sollwerte, Starten/Stoppen oder Geräte-Alarmquittieren und werden nicht zweckentfremdet.

Der Server liefert bei Geräte-Write **BadNotWritable**. Vorbereitete, noch nicht unterstützte Aktionen liefern im Dispatcher BadNotSupported; Methoden sind nicht ausführbar. Es gibt keine falsche Erfolgsbestätigung und keinen blinden Write-Retry. Erforderliche API-Erweiterungen stehen in OFFENE-PUNKTE.md.

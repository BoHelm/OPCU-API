# Drittanbieter und Lizenzen

| Bestandteil | Version / Lizenz | Verwendung |
|---|---|---|
| OPC Foundation UA-.NETStandard | 1.5.378.182, OPC Foundation MIT License 1.00 | Bestehender OPC-UA-Stack, Client/Server/Configuration |
| OPC Foundation DI / CommercialKitchenEquipment NodeSets | Siehe models/upstream und Quellenprüfung; OPC Foundation MIT License 1.00 im XML-Header | Unverändert eingebundene offizielle Modelle |
| OPC Foundation ModelCompiler Tool | 2.8.15, Lizenz des offiziellen Tool-Pakets/Repositories | Entwicklungswerkzeug; generierte Dateien enthalten eigene Originalhinweise |
| .NET / ASP.NET Core | SDK 10.0.402, Runtime 10.0.13 | Microsoft-Lizenz-/Drittanbieterhinweise werden im Container unter /opt/dotnet beibehalten |
| Debian | 13.6 Basisimage; jeweilige Paketlizenzen | Systembibliotheken; Lizenzdateien der Distribution bleiben im Image |
| xUnit, Test SDK, Testadapter | Exakte Versionen in Testprojekt und Lockfile | Nur Testwerkzeuge |
| Playwright / Chromium | Browser-Smoke-Test; optionales Entwicklungswerkzeug | Nicht in das Gateway-Laufzeitimage eingebunden |

Transitive NuGet-Komponenten sind vollständig in den packages.lock.json-Dateien aufgeführt. Ihre Original-Lizenzen und Hinweise gelten jeweils zusätzlich. Die enthaltenen NodeSet-Lizenzheader wurden nicht entfernt oder umgeschrieben.

Die beigefügten Projekt-PDFs sind Referenzunterlagen; ihre Rechte werden durch dieses Repository nicht neu lizenziert. Der Volltext der DIN wurde nicht in den Sourcecode übernommen.

## OPC Foundation MIT License 1.00

Copyright (c) OPC Foundation, Inc. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

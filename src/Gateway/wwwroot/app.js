"use strict";
let configuration, models, csrf, editors = [];
const $ = id => document.getElementById(id);
function notice(text) { $("message").textContent = text; }
let logEntries = [], logPaused = false, logStarted = false;
function renderLog() {
  const output = $("logOutput"), filter = $("logLevel").value;
  const follow = output.scrollHeight - output.scrollTop - output.clientHeight < 40;
  const previousScroll = output.scrollTop;
  const expanded = new Set(Array.from(output.querySelectorAll("details[open]"), node => node.dataset.entry));
  output.replaceChildren();
  const entries = logEntries.filter(entry => filter === "all" ||
    (filter === "problems" ? entry.level !== "Info" : entry.level === "Fehler"));
  if (!entries.length) el("p", "Keine Meldungen für diese Auswahl vorhanden.", output);
  for (const entry of entries) {
    const line = el("div", undefined, output);
    line.className = "log-line" + (entry.level === "Fehler" ? " log-error" : entry.level === "Warnung" ? " log-warning" : "");
    el("time", new Date(entry.timestamp).toISOString().replace("T", " ").replace("Z", " UTC"), line);
    el("strong", entry.level + " · " + entry.component, line);
    el("span", entry.message, line);
    if (entry.details) {
      const details = el("details", undefined, line);
      details.className = "log-details";
      details.dataset.entry = entry.id + "/" + entry.timestamp;
      details.open = expanded.has(details.dataset.entry);
      el("summary", "Fehlerdetails: Ursache und Stacktrace", details);
      el("pre", entry.details, details);
    }
  }
  output.scrollTop = follow ? output.scrollHeight : previousScroll;
}
async function refreshLog() {
  if (logPaused || document.hidden) return;
  try {
    const data = await request("logs");
    if (logPaused) return;
    const changed = logEntries.length !== data.entries.length ||
      logEntries[0]?.id !== data.entries[0]?.id ||
      logEntries[logEntries.length - 1]?.id !== data.entries[data.entries.length - 1]?.id ||
      logEntries[0]?.timestamp !== data.entries[0]?.timestamp;
    logEntries = data.entries;
    if (changed) renderLog();
    $("logStatus").textContent = "Live · " + logEntries.length + " von maximal " + data.capacity +
      " Einträgen · aktualisiert " + new Date().toLocaleTimeString("de-DE");
  } catch {
    $("logStatus").textContent = "Protokoll konnte nicht geladen werden. Verbindung zum Gateway prüfen; erneuter Versuch in 2 Sekunden.";
  }
}
async function pollLog() {
  await refreshLog();
  window.setTimeout(pollLog, 2000);
}
async function request(path, method = "GET", body) {
  const response = await fetch("/api/" + path, {
    method, headers: method === "GET" ? {} : {"Content-Type": "application/json", "X-CSRF-TOKEN": csrf},
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  if (response.status === 401) { location.href = "/login"; throw Error("Anmeldung erforderlich."); }
  const text = await response.text();
  let data; try { data = text ? JSON.parse(text) : {}; } catch { data = {message:text}; }
  if (!response.ok) throw Error(data.errors?.join("\n") || data.message || text || "Anfrage fehlgeschlagen.");
  return data;
}
function el(tag, text, parent) { const node = document.createElement(tag); if (text !== undefined) node.textContent = text; parent?.append(node); return node; }
function input(parent, title, value, onChange, type = "text") {
  const label = el("label", title, parent), field = el("input", undefined, label);
  field.type = type;
  if (type === "checkbox") field.checked = Boolean(value); else field.value = value ?? "";
  field.addEventListener("change", () => onChange(type === "checkbox" ? field.checked : type === "number" ? (field.value === "" ? undefined : Number(field.value)) : field.value));
  return field;
}
function jsonEditor(parent, title, object, update) {
  const label = el("label", title, parent), area = el("textarea", undefined, label);
  area.value = JSON.stringify(object, null, 2);
  editors.push(() => { try { update(JSON.parse(area.value)); } catch { throw Error(title + ": ungültiges JSON."); } });
}
function commitEditors(){
  try{editors.forEach(save=>save());return true;}catch(error){notice(error.message);return false;}
}
function staticFields(parent, fields, values) {
  const grid = el("div", undefined, parent); grid.className = "grid";
  const names={Manufacturer:"Hersteller",Model:"Modell",SerialNumber:"Seriennummer",HardwareRevision:"Hardwarestand",
    SoftwareRevision:"Softwarestand",DeviceRevision:"Geräterevision",DeviceManual:"Geräteanleitung",RevisionCounter:"Revisionszähler",
    EnergySource:"Energieversorgung",IsWithPanDetection:"Topferkennung vorhanden",NominalVoltage:"Nennspannung",
    NumberOfPhases:"Phasenanzahl",NominalPower:"Nennleistung",IsWithLift:"Korblift vorhanden",IsWithPlaten:"Obere Grillplatte vorhanden"};
  for (const field of fields.filter(f => f.mandatory && !f.dynamic && f.name !== "DeviceClass")) {
    const title = (names[field.name]?names[field.name]+" ("+field.name+")":field.name) + (field.unit ? " [" + field.unit + "]" : "") + " · Pflicht";
    if (field.dataTypeName === "Boolean" || field.name === "EnergySource") {
      const label = el("label", title, grid), select = el("select", undefined, label);
      const options = field.name === "EnergySource" ? [["","Unbekannt"],["0","Elektrisch (HKI 0)"],["1","Gas (HKI 1)"],["2","Dampf (HKI 2)"]] :
        [["","Unbekannt"],["true","Ja"],["false","Nein"]];
      options.forEach(([value,text]) => { const option=el("option",text,select); option.value=value; });
      select.value = values[field.name] === undefined ? "" : String(values[field.name]);
      select.addEventListener("change", () => { if (select.value === "") delete values[field.name]; else values[field.name] = JSON.parse(select.value); });
    } else {
      input(grid, title, values[field.name], value => {
        if (value === "" || value === undefined) delete values[field.name]; else values[field.name] = value;
      }, ["Int32","UInt32","Float","Double"].includes(field.dataTypeName) ? "number" : "text");
    }
  }
}
function renderDevices() {
  $("devices").replaceChildren(); editors = [];
  for (const device of configuration.devices) {
    const detail = el("details", undefined, $("devices"));
    el("summary", device.name + " · API-ID " + device.apiId + " · " + device.profile, detail);
    const grid=el("div",undefined,detail);grid.className="grid";
    input(grid,"Im OPC-UA-Modell aktiv",device.enabled,v=>device.enabled=v,"checkbox");
    input(grid,"Anzeigename",device.name,v=>device.name=v);
    const label=el("label","Administrativ bestätigtes HKI-Profil",grid),select=el("select",undefined,label);
    for(const profile of Object.keys(models)){const option=el("option",profile,select);option.value=profile;}
    select.value=device.profile;
    select.addEventListener("change",()=>{if(!commitEditors()){select.value=device.profile;return;}device.profile=select.value;renderDevices();});
    input(grid,"Beleg für Profil und API-Zonen (Dokument/Typenschild)",device.evidence,v=>device.evidence=v);
    el("p","Stabile Identität: "+device.identity,detail).className="note";
    if(device.profile==="Fryer")el("p","Produktiv gesperrt: ProgramMode und SignalMode sind in der API nicht belegt. Demo zeigt diese Pflichtwertlücken.",detail).className="note";
    staticFields(detail,models[device.profile].deviceFields,device.staticValues);
    jsonEditor(detail,"Gerätespezifische EURanges der Stammdaten",device.ranges,v=>device.ranges=v);
    for(const zone of device.zones){
      const box=el("details",undefined,detail);el("summary","Becken / Zone · API-Zone "+zone.id,box);
      input(box,"Explizite API-Zonennummer",zone.id,v=>zone.id=v,"number");
      staticFields(box,models[device.profile].zoneFields,zone.staticValues);
      const dynamic= models[device.profile].zoneFields.filter(f=>f.mandatory&&f.dynamic).map(f=>f.name);
      if(dynamic.length)el("p","Dynamische Pflichtfelder (keine Konfigurationskonstanten): "+dynamic.join(", "),box).className="note";
      el("p",'Feldzuordnungen: target = HKI-BrowseName, group = Actual oder Set, field = dokumentiertes API-Feld, unit = bestätigte Quelleneinheit, evidence = fachlicher Beleg. GET setValues bleibt lesend. Zustandsfelder werden mangels API-Vertrag nicht zugeordnet.',box);
      jsonEditor(box,"Belegte Feldzuordnungen",zone.bindings,v=>zone.bindings=v);
      jsonEditor(box,"Gerätespezifische EURanges der Zonenwerte",zone.ranges,v=>zone.ranges=v);
      jsonEditor(box,"Nachweislich geräteseitig nicht unterstützte Felder",zone.deviceUnsupported,v=>zone.deviceUnsupported=v);
    }
    const add=el("button","Zone hinzufügen",detail);add.type="button";add.className="secondary";
    add.onclick=()=>{if(!commitEditors())return;device.zones.push({id:device.zones.length,staticValues:{},ranges:{},bindings:[],deviceUnsupported:[]});renderDevices();};
    const remove=el("button","Gerätezuordnung entfernen",detail);remove.type="button";remove.className="secondary";
    remove.onclick=()=>{if(!commitEditors())return;configuration.devices=configuration.devices.filter(d=>d!==device);renderDevices();};
  }
}
function applySettings() {
  const numeric=["inventoryIntervalSeconds","actualIntervalSeconds","setIntervalSeconds","staleAfterSeconds","historyWindowSeconds","httpTimeoutSeconds","maxParallelRequests","uaPort"];
  for(const id of [...numeric,"apiBaseUrl","apiCaFile","uaBindAddress","uaEndpointUrl","manufacturerName","productName"])
    configuration[id]=numeric.includes(id)?Number($(id).value):($(id).value||null);
  editors.forEach(save=>save());
}
async function saveConfiguration(next) {
  await request("config","POST",next);notice("Konfiguration ist validiert und gespeichert. Neustart des Gateway-Prozesses erforderlich.");
  $("pending").textContent="Neustart erforderlich";
}
async function diagnostics() {
  const data=await request("diagnostics");
  $("mode").textContent=data.simulation?"SIMULATION · Keine realen Geräteaktionen. Die simulierten Feldbedeutungen gelten nicht als Beleg für echte Geräte.":"Produktivkonfiguration · Verbindung zur eingestellten API erforderlich.";
  $("summary").textContent=(data.opcUaReady?"OPC UA bereit · ":"OPC UA nicht bereit · ")+data.endpoint+(data.uaError?" · "+data.uaError:"");
  $("health").replaceChildren();
  for(const [key,health] of Object.entries(data.source).sort()){
    const [id,zone,group]=key.split("/");
    const device=configuration.devices.find(d=>d.apiId===Number(id));
    const title=key==="inventory"?"Geräteinventar":(device?.name||"Gerät "+id)+" · Zone "+zone+" · "+(group==="Actual"?"Istwerte":"Sollwerte");
    const row=el("tr",undefined,$("health"));el("td",title,row);
    const status=el("td",health.reachable?"Erreichbar":"Nicht erreichbar",row);if(!health.reachable)status.className="bad";
    el("td",health.lastSuccess?new Date(health.lastSuccess).toISOString().replace("T"," ").replace(/\.\d+Z$/," UTC"):"Noch kein erfolgreicher Abruf",row);
    el("td",health.message,row);
  }
  $("limitations").replaceChildren();data.limitations.forEach(x=>el("li",x,$("limitations")));
  $("nodes").replaceChildren();
  for(const node of data.nodes){
    const row=el("tr",undefined,$("nodes"));el("td",node.nodeId+"\n"+node.browseName,row);
    el("td",node.value===null?"—":typeof node.value==="object"?(node.value.text??JSON.stringify(node.value)):String(node.value),row);
    const status=el("td",node.status,row);if(node.status!=="Good")status.className="bad";
    el("td",node.sourceTimestamp||"Keine Messzeit (z. B. Stammdatum)",row);el("td",node.reason,row);
  }
}
async function security(){
  const users=await request("ua/users");$("users").replaceChildren();
  users.forEach(u=>{const li=el("li",u.username+" · "+u.role,$("users"));const b=el("button","Entfernen",li);b.className="secondary";b.onclick=async()=>{try{await request("ua/users/"+encodeURIComponent(u.username),"DELETE");await security();}catch(e){notice(e.message);}};});
  const trust=await request("ua/trust");$("trust").replaceChildren();
  for(const [kind,title] of [["trusted","Freigegebene Zertifikate"],["rejected","Abgewiesene Zertifikate"]]){
    el("h3",title,$("trust"));const list=el("ul",undefined,$("trust"));
    if(!trust[kind].length)el("li","Keine Einträge.",list);
    for(const cert of trust[kind]){
      const li=el("li",cert.subject+" · gültig bis "+cert.notAfter+" · Fingerprint "+cert.thumbprint,list);
      if(kind==="trusted"){
        const remove=el("button","Vertrauen entfernen",li);remove.className="secondary";
        remove.onclick=async()=>{try{await request("ua/trust/"+encodeURIComponent(cert.thumbprint),"DELETE");await security();notice("Vertrauen für neue Verbindungen entfernt.");}catch(e){notice(e.message);}};
      }
    }
  }
}
async function start(){
  csrf=(await request("session")).csrfToken;
  if (!logStarted) { logStarted = true; void pollLog(); }
  const state=await request("config");configuration=state.configuration;
  models=await request("model");
  for(const id of ["apiBaseUrl","apiCaFile","inventoryIntervalSeconds","actualIntervalSeconds","setIntervalSeconds","staleAfterSeconds","historyWindowSeconds","httpTimeoutSeconds","maxParallelRequests","uaBindAddress","uaPort","uaEndpointUrl","manufacturerName","productName","namespaceUri"])$(id).value=configuration[id]??"";
  $("applicationUri").value=configuration.applicationUri??`urn:kitchen-gateway:application:${configuration.gatewayId}`;
  $("keyStatus").textContent=state.apiKeyConfigured?"Ein API-Key ist verschlüsselt hinterlegt.":"Noch kein API-Key hinterlegt.";
  $("pending").textContent=state.restartRequired?"Gespeicherte Änderungen benötigen einen Neustart.":"";
  renderDevices();await Promise.all([diagnostics(),security()]);
}
function handle(id,event,fn){$(id).addEventListener(event,async e=>{e.preventDefault();try{await fn(e);}catch(error){notice(error.message);}});}
handle("settings","submit",async()=>{applySettings();await saveConfiguration(configuration);});
handle("secretForm","submit",async()=>{await request("secret","POST",{value:$("apiKey").value});$("apiKey").value="";$("keyStatus").textContent="API-Key verschlüsselt hinterlegt.";notice("API-Key ersetzt; wirksam beim nächsten Abruf.");});
handle("test","click",async()=>notice(JSON.stringify(await request("connection-test","POST",{}))));
handle("refresh","click",diagnostics);
handle("logLevel","change",renderLog);
handle("logPause","click",async()=>{
  logPaused=!logPaused;
  $("logPause").textContent=logPaused?"Anzeige fortsetzen":"Anzeige pausieren";
  $("logPause").setAttribute("aria-pressed",String(logPaused));
  if(logPaused)$("logStatus").textContent="Anzeige pausiert · Der Server protokolliert weiter.";
  else await refreshLog();
});
handle("logout","click",async()=>{await request("logout","POST",{});location.href="/login";});
handle("uaUserForm","submit",async()=>{await request("ua/users","POST",{username:$("uaUsername").value,password:$("uaPassword").value,role:$("uaRole").value});$("uaPassword").value="";notice("OPC-UA-Benutzer gespeichert; wirksam bei der nächsten Anmeldung.");await security();});
handle("import","change",async()=>{const file=$("import").files[0];if(!file)return;const next=JSON.parse(await file.text());await saveConfiguration(next);await start();});
handle("certificate","change",async()=>{const file=$("certificate").files[0];if(!file)return;if(file.size>65536)throw Error("Zertifikat zu groß.");const bytes=new Uint8Array(await file.arrayBuffer());await request("ua/trust","POST",{derBase64:btoa(String.fromCharCode(...bytes))});notice("Öffentliches Client-Zertifikat freigegeben.");await security();});
handle("inventory","click",async()=>{
  const rows=await request("inventory");$("inventoryList").replaceChildren();
  for(const row of rows){
    const line=el("p",String(row.name??"Gerät")+" · ID "+row.id+" · API-Typ "+row.type+" (Zuordnung ungeklärt)",$("inventoryList"));
    if(configuration.devices.some(d=>d.apiId===row.id))continue;
    const button=el("button","Als deaktiviertes Profil übernehmen",line);button.type="button";button.className="secondary";
    button.onclick=()=>{if(!commitEditors())return;configuration.devices.push({identity:crypto.randomUUID(),apiId:row.id,name:row.name||"Gerät "+row.id,profile:"CookingZone",enabled:false,evidence:"",staticValues:{Model:row.model,SerialNumber:row.serialNumber},ranges:{},zones:[{id:0,staticValues:{},ranges:{},bindings:[],deviceUnsupported:[]}]});renderDevices();button.disabled=true;};
  }
});
start().catch(error=>notice(error.message));

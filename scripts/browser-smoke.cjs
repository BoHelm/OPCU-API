/** Echte Browser-/Container-Abnahme. Nur lokale Compose-Demo; keine realen Gerätedaten oder Geräteaktionen. */
const {chromium} = require("playwright");
const {execFileSync} = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const assert = require("node:assert/strict");
const root=path.resolve(__dirname,"..");
const directory=path.join(root,".artifacts","browser-"+Date.now());
fs.mkdirSync(directory,{recursive:true,mode:0o700});
const browserPassword=crypto.randomBytes(32).toString("base64url");
const uaPassword=crypto.randomBytes(32).toString("base64url");
const dockerEnvironment={...process.env,DOCKER_CONFIG:process.env.DOCKER_CONFIG||"/workspace/.cache/docker"};
function docker(args){return execFileSync("docker",args,{cwd:root,env:dockerEnvironment,encoding:"utf8",stdio:["ignore","pipe","pipe"]});}
const base=process.env.TEST_BROWSER_URL||"http://127.0.0.1:5080";
const probe=path.join(root,"tools/UaProbe/bin/Debug/net10.0/UaProbe.dll");
async function run(){
 const browser=await chromium.launch({executablePath:process.env.CHROMIUM_PATH||"/usr/bin/chromium",headless:true,args:["--no-sandbox","--disable-dev-shm-usage"]});
 try{
  const page=await browser.newPage({viewport:{width:1440,height:1000}});
  const errors=[];page.on("pageerror",error=>errors.push(error.message));
  await page.goto(base);
  if(page.url().endsWith("/setup")){
   const bootstrap=docker(["compose","-f","compose.demo.yaml","exec","-T","gateway","cat","/keys/gateway/bootstrap-token"]).trim();
   await page.locator('[name="username"]').fill("browser-admin");
   await page.locator('[name="password"]').fill(browserPassword);
   await page.locator('[name="token"]').fill(bootstrap);
   await Promise.all([page.waitForURL("**/login"),page.getByRole("button",{name:"Fortfahren"}).click()]);
  }else throw Error("Für diesen Test eine frische, separate Compose-Demo verwenden.");
  await page.locator('[name="username"]').fill("browser-admin");
  await page.locator('[name="password"]').fill(browserPassword);
  await Promise.all([page.waitForURL(base+"/"),page.getByRole("button",{name:"Fortfahren"}).click()]);
  await page.locator("#mode").waitFor({state:"visible"});
  await page.waitForFunction(()=>document.querySelector("#nodes").children.length>20);
  assert.match(await page.locator("#mode").textContent(),/SIMULATION/);
  assert.equal(await page.locator("#apiKey").inputValue(),"");
  await page.locator("#test").click();
  await page.waitForFunction(()=>document.querySelector("#message").textContent.includes('"connected":true'));
  await page.locator("#actualIntervalSeconds").fill("3");
  await page.getByRole("button",{name:"Konfiguration validieren und speichern"}).click();
  await page.waitForFunction(()=>document.querySelector("#pending").textContent.includes("Neustart"));
  await page.locator("#uaUsername").fill("browser-reader");
  await page.locator("#uaPassword").fill(uaPassword);
  await page.locator("#uaUserForm button").click();
  await page.waitForFunction(()=>document.querySelector("#users").textContent.includes("browser-reader"));
  assert.equal(await page.locator("#uaPassword").inputValue(),"");
  execFileSync("dotnet",[probe,"init","--pki",path.join(directory,"pki")],{cwd:root,stdio:"pipe"});
  await page.locator("#certificate").setInputFiles(path.join(directory,"pki/probe.der"));
  await page.waitForFunction(()=>document.querySelector("#message").textContent.includes("Zertifikat freigegeben"));
  const certificate=await page.request.get(base+"/api/ua/certificate");
  assert.equal(certificate.status(),200);fs.writeFileSync(path.join(directory,"server.der"),await certificate.body());
  fs.writeFileSync(path.join(directory,"ua-password"),uaPassword,{mode:0o600});
  const args=["probe","--pki",path.join(directory,"pki"),"--endpoint","opc.tcp://localhost:4840/kitchen",
   "--server-cert",path.join(directory,"server.der"),"--user","browser-reader","--password-file",path.join(directory,"ua-password")];
  execFileSync("dotnet",[probe,...args,"--seconds","4","--output",path.join(directory,"ua-before.json")],{cwd:root,stdio:"pipe",timeout:45000});
  await page.locator("#refresh").click();
  await page.waitForTimeout(500);
  await page.screenshot({path:path.join(directory,"dashboard.png")});
  const exportResponse=await page.request.get(base+"/api/config/export");
  const exported=await exportResponse.text();
  assert(!exported.includes(uaPassword));assert(!exported.includes(browserPassword));assert(!exported.includes("passwordHash"));
  docker(["compose","-f","compose.demo.yaml","restart","gateway"]);
  const deadline=Date.now()+40000;
  while(Date.now()<deadline){
   try{const response=await page.request.get(base+"/health/ready");if(response.status()===200)break;}catch{}
   await new Promise(resolve=>setTimeout(resolve,500));
  }
  assert.equal((await page.request.get(base+"/api/session")).status(),200);
  assert.equal((await (await page.request.get(base+"/api/config")).json()).restartRequired,false);
  execFileSync("dotnet",[probe,...args,"--seconds","1","--output",path.join(directory,"ua-after.json")],{cwd:root,stdio:"pipe",timeout:45000});
  const before=JSON.parse(fs.readFileSync(path.join(directory,"ua-before.json")));
  const after=JSON.parse(fs.readFileSync(path.join(directory,"ua-after.json")));
  assert.deepEqual([...new Set(before.references.map(r=>r.nodeId))].sort(),[...new Set(after.references.map(r=>r.nodeId))].sort());
  assert.deepEqual(errors,[]);
  const result={passed:true,browser:"Chromium",simulationOnly:true,configurationSaved:true,secretNotReturned:true,
   uaEncrypted:true,subscriptionNotifications:before.notifications.length,stableNodeIdsAfterContainerRestart:true,javascriptErrors:errors};
  fs.writeFileSync(path.join(directory,"summary.json"),JSON.stringify(result,null,2));
  console.log(JSON.stringify({...result,artifacts:directory}));
 }finally{await browser.close();}
}
run().catch(error=>{console.error(error.message);process.exitCode=1;});

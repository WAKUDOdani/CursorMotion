const {spawn,execFileSync}=require('node:child_process');
const net=require('node:net');
const readline=require('node:readline');
const fs=require('node:fs');
const path=require('node:path');
const {randomUUID}=require('node:crypto');
const {once}=require('node:events');
const engine=path.resolve(process.argv[2]);
const studio=path.resolve(process.argv[3]);
const output=path.resolve(__dirname,'../test-v3-tray');
fs.mkdirSync(output,{recursive:true});
const pipe='CursorMotion.Test.'+randomUUID().replaceAll('-','');
const readyPath=path.join(output,pipe+'.json');
const args=['--diagnostic','--startup','--test-pipe',pipe,'--output',readyPath];
const child=spawn(engine,args,{windowsHide:true,stdio:'ignore'});
const pause=milliseconds=>new Promise(resolve=>setTimeout(resolve,milliseconds));
const assert=(value,message)=>{if(!value)throw Error(message);};
const children=[];
let assertions=0;
const check=(value,message)=>{assert(value,message);assertions++;fs.appendFileSync(path.join(output,'progress.log'),assertions+' '+message+'\n');};
const profile=path.join(process.env.LOCALAPPDATA,'CursorMotion','settings.json');
const original=fs.existsSync(profile)?fs.readFileSync(profile):null;
const registration=()=>execFileSync('powershell.exe',['-NoProfile','-Command',"(Get-ItemProperty -LiteralPath 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' -Name CursorMotion -ErrorAction SilentlyContinue).CursorMotion"],{windowsHide:true,encoding:'utf8'}).trim();
const startupBefore=registration();
const timeout=setTimeout(()=>{child.kill();for(const process of children)if(process.exitCode===null)process.kill();console.error('Tray lifecycle timeout');process.exit(1);},180000);
async function connect(){
 const socket=net.createConnection('\\\\.\\pipe\\'+pipe);
 const lines=readline.createInterface({input:socket});
 let sequence=0;
 const pending=new Map();
 let resolveReady,rejectReady;
 const ready=new Promise((resolve,reject)=>{resolveReady=resolve;rejectReady=reject;});
 socket.on('error',rejectReady);
 lines.on('line',line=>{const message=JSON.parse(line);if(message.event==='ready')resolveReady(message.data);else{const pendingRequest=pending.get(message.id);if(pendingRequest){pending.delete(message.id);message.ok?pendingRequest.resolve(message.data):pendingRequest.reject(Error(message.error));}}});
 socket.on('close',()=>{for(const request of pending.values())request.reject(Error('Pipe disconnected'));pending.clear();});
 await ready;
 return {socket,request:(cmd,data={})=>new Promise((resolve,reject)=>{const id=++sequence;pending.set(id,{resolve,reject});socket.write(JSON.stringify({id,cmd,data})+'\n');})};
}
async function probe(name){
 const folder=path.join(output,name);
 const process=spawn(studio,['--connection-test','--pipe',pipe,'--output',folder],{windowsHide:true,stdio:'ignore'});
 children.push(process);
 const [code]=await once(process,'exit');
 fs.appendFileSync(path.join(output,'progress.log'),'Settings exited: '+name+'\n');
 check(code===0,'Settings process failed to close');
 const result=JSON.parse(fs.readFileSync(path.join(folder,'results.json'),'utf8'));
 check(result.pass&&result.hosted,'Settings did not attach to the native host');
 return result;
}
(async()=>{
 let connection;
 try{
  for(let attempt=0;attempt<100&&!fs.existsSync(readyPath);attempt++){assert(child.exitCode===null,'Native host failed');await pause(50);}
  check(fs.existsSync(readyPath),'Native host did not become ready');
  connection=await connect();
  let state=await connection.request('get');
  check(!state.state.enabled&&state.appearances.length===4,'Diagnostics activated the desktop overlay');
  const appearances=structuredClone(state.appearances);
  appearances[1]={...appearances[1],Skin:2,Scale:2.35,FillColor:'#AA778899'};
  state=await connection.request('forms',{appearances});
  check(JSON.stringify(state.appearances)===JSON.stringify(appearances),'Native host lost independent form settings');
  connection.socket.end();connection=await connect();
  check(JSON.stringify((await connection.request('get')).appearances)===JSON.stringify(appearances),'Closing a client reset the cursor');
  const duplicate=spawn(engine,args,{windowsHide:true,stdio:'ignore'});children.push(duplicate);
  check((await once(duplicate,'exit'))[0]===0,'Second instance did not forward to the host');
  check(child.exitCode===null,'Second instance terminated the existing host');
  check(!(await connection.request('startup',{enabled:true})).available,'Diagnostic mode can alter real autostart');
  let rejected=false;try{await connection.request('unknown');}catch{rejected=true;}
  check(rejected,'Unknown command accepted');
  const first=await probe('first-open');
  check(JSON.stringify(first.appearances)===JSON.stringify(appearances),'First settings window did not load saved state');
  check(child.exitCode===null,'Closing settings terminated native host');
  check(JSON.stringify((await connection.request('get')).appearances)===JSON.stringify(appearances),'Settings close changed applied forms');
  const second=await probe('reopen');
  check(JSON.stringify(second.appearances)===JSON.stringify(appearances),'Reopening settings reset forms');
  await pause(500);
  const remaining=execFileSync('powershell.exe',['-NoProfile','-Command',`@(Get-Process -Name 'CursorMotion.Studio' -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq '${studio.replaceAll("'","''")}' }).Count`],{windowsHide:true,encoding:'utf8'}).trim();
  check(Number(remaining)===0,'Electron processes remain after settings closes');
  check(registration()===startupBefore,'Tests changed the user autostart registration');
  check(original===null?!fs.existsSync(profile):fs.readFileSync(profile).equals(original),'Tests changed the installed profile');
  fs.appendFileSync(path.join(output,'progress.log'),'Requesting host shutdown\n');
  const exit=once(child,'exit');await connection.request('shutdown');
  fs.appendFileSync(path.join(output,'progress.log'),'Shutdown acknowledged\n');
  check((await exit)[0]===0,'Native tray did not shut down cleanly');
  fs.writeFileSync(path.join(output,'results.json'),JSON.stringify({pass:true,assertions,nativeHostSurvivesSettingsClose:true,electronProcessesAfterClose:0,independentForms:true,reopen:true,singleInstance:true,installedProfileUntouched:true,autostartUntouched:true},null,2));
  console.log('Tray lifecycle passed:',assertions);
 }catch(error){console.error(error);fs.writeFileSync(path.join(output,'results.json'),JSON.stringify({pass:false,error:error.stack},null,2));process.exitCode=1;}
 finally{clearTimeout(timeout);connection?.socket.destroy();if(child.exitCode===null)child.kill();for(const process of children)if(process.exitCode===null)process.kill();}
})();

const {app,BrowserWindow,ipcMain,dialog,shell,globalShortcut,screen}=require('electron');
const {spawn}=require('node:child_process');
const readline=require('node:readline');
const fs=require('node:fs');
const path=require('node:path');
const net=require('node:net');
const pipeIndex=process.argv.indexOf('--pipe');
const pipeName=pipeIndex>=0?process.argv[pipeIndex+1]:null;
const hosted=Boolean(pipeName);
const test=process.argv.includes('--ui-test')||process.argv.includes('--connection-test');
const responseTimeout=test?120000:8000;
function testTrace(message){if(!test)return;const i=process.argv.indexOf('--output');if(i<0)return;try{fs.mkdirSync(process.argv[i+1],{recursive:true});fs.appendFileSync(path.join(process.argv[i+1],'lifecycle.log'),Date.now()+' '+message+'\n');}catch{}}
if(test){app.setPath('userData',path.join(app.getPath('temp'),'CursorMotion-UI-test-'+process.pid));app.commandLine.appendSwitch('disable-renderer-backgrounding');app.commandLine.appendSwitch('disable-backgrounding-occluded-windows');}
const portableRoot=path.resolve(__dirname,'../../..');
if(!test&&fs.existsSync(path.join(portableRoot,'portable.flag'))){
 const localData=path.join(portableRoot,'data','studio');fs.mkdirSync(localData,{recursive:true});
 app.setPath('userData',localData);app.setPath('sessionData',localData);app.setPath('logs',path.join(localData,'logs'));
}
let window,engine,backendInput,ready,sequence=0,closing=false;
const pending=new Map();
const allowed=new Set(['get','status','enabled','set','appearance','forms','pack','preset','reset','artwork']);
function request(cmd,data={}){
 return new Promise((resolve,reject)=>{
  if(!backendInput||backendInput.destroyed)return reject(Error('Движок остановлен. Перезапусти приложение.'));
  const id=++sequence; const timer=setTimeout(()=>{pending.delete(id);reject(Error('Движок не ответил'));},responseTimeout);
  pending.set(id,{resolve,reject,timer});backendInput.write(JSON.stringify({id,cmd,data})+'\n');
 });
}
function launchEngine(){
 return new Promise((resolve,reject)=>{
  const engineIndex=process.argv.indexOf('--engine');
  const exe=process.env.CURSOR_MOTION_ENGINE||(engineIndex>=0?process.argv[engineIndex+1]:path.join(__dirname,'engine','CursorMotion.Engine.exe'));
  if(hosted&&!/^CursorMotion\.[A-Za-z0-9.]+$/.test(pipeName))return reject(Error('Некорректный адрес движка'));
  const connection=hosted?net.createConnection('\\\\.\\pipe\\'+pipeName):null;
  if(!hosted)engine=spawn(exe,['--backend',...(test?['--diagnostic']:[]),...(test&&process.argv.includes('--warp')?['--warp']:[]),...(process.argv.includes('--start')?['--start']:[])],{windowsHide:true,stdio:['pipe','pipe','pipe']});
  backendInput=hosted?connection:engine.stdin;
  const source=hosted?connection:engine.stdout;
  const timeout=setTimeout(()=>reject(Error('Не удалось запустить движок')),15000);
  (connection||engine).on('error',e=>{clearTimeout(timeout);reject(e);});
  readline.createInterface({input:source}).on('line',line=>{
   try{const message=JSON.parse(line);
    if(message.event==='ready'){clearTimeout(timeout);resolve(message.data);}
    else if(message.event==='fatal'){clearTimeout(timeout);reject(Error(message.error));}
    else {const p=pending.get(message.id);if(p){clearTimeout(p.timer);pending.delete(message.id);message.ok?p.resolve(message.data):p.reject(Error(message.error));}}
   }catch(e){console.error('Backend response:',e.message);}
  });
  if(engine)engine.stderr.on('data',data=>console.error(data.toString()));
  (connection||engine).on(hosted?'close':'exit',()=>{backendInput=null;clearTimeout(timeout);reject(Error('Движок завершился'));for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Движок завершился'));}pending.clear();if(hosted&&!closing)shutdown();});
 });
}
async function shutdown(flush=false){
 if(closing)return;closing=true;globalShortcut.unregisterAll();
 testTrace('shutdown begin; flush='+flush);
 if(flush&&window&&!window.webContents.isDestroyed()){
  try{await Promise.race([window.webContents.executeJavaScript('window.__flushNativeSettings?.()'),new Promise((_,reject)=>setTimeout(()=>reject(Error('Настройки ещё не сохранены')),9000))]);}catch(error){
   if(hosted&&!test){const choice=await dialog.showMessageBox(window,{type:'warning',message:'Не удалось сохранить последние изменения',detail:error.message,buttons:['Остаться в настройках','Закрыть без сохранения последних изменений'],defaultId:0,cancelId:0});if(choice.response===0){closing=false;return;}}
  }
 }
 testTrace('renderer flush completed');
 try{await request(hosted?'flush':'shutdown');}catch{}
 testTrace('backend flush completed');
 if(hosted)backendInput?.end();
 if(engine&&engine.exitCode===null){const kill=setTimeout(()=>engine.kill(),1800);engine.once('exit',()=>clearTimeout(kill));}
 testTrace('app.quit');app.quit();
}
function verified(event){if(!window||event.sender!==window.webContents)throw Error('Invalid sender');}
ipcMain.handle('studio:request',async(event,cmd,data)=>{verified(event);if(!allowed.has(cmd))throw Error('Неизвестная команда');await ready;return request(cmd,data);});
ipcMain.handle('studio:startup',async(event,enabled)=>{verified(event);await ready;if(!hosted)return {enabled:false,available:false};if(enabled!==undefined&&typeof enabled!=='boolean')throw Error('Некорректная настройка автозапуска');return request('startup',enabled===undefined?{}:{enabled});});
ipcMain.handle('studio:window',(event,action)=>{verified(event);if(action==='minimize')hosted?shutdown(true):window.minimize();if(action==='maximize')window.isMaximized()?window.unmaximize():window.maximize();if(action==='close')shutdown(true);});
ipcMain.handle('studio:telegram',event=>{verified(event);return shell.openExternal('https://t.me/WAKUDOdani');});
ipcMain.handle('studio:file',async(event,action)=>{
 verified(event);await ready;
 if(action==='import'){const result=await dialog.showOpenDialog(window,{title:'Импорт настроек',properties:['openFile'],filters:[{name:'Профиль CursorMotion',extensions:['json']}]});return result.canceled?null:request('import',{path:result.filePaths[0]});}
 if(action==='export'){const result=await dialog.showSaveDialog(window,{title:'Экспорт настроек',defaultPath:'CursorMotion.json',filters:[{name:'Профиль CursorMotion',extensions:['json']}]});return result.canceled?null:request('export',{path:result.filePath});}
 throw Error('Неизвестное действие');
});
if(!test&&!app.requestSingleInstanceLock())app.quit();
else {
 app.on('second-instance',()=>{if(window){if(window.isMinimized())window.restore();window.show();window.focus();}});
 app.whenReady().then(async()=>{
  ready=launchEngine();ready.catch(()=>{});
  const area=screen.getPrimaryDisplay().workAreaSize;
  const available={width:test?1260:Math.max(640,area.width-24),height:test?900:Math.max(480,area.height-24)};
  window=new BrowserWindow({width:Math.min(1630,available.width),height:Math.min(950,available.height),minWidth:Math.min(980,available.width),minHeight:Math.min(700,available.height),frame:false,show:false,backgroundMaterial:'acrylic',backgroundColor:'#00000000',title:'CursorMotion',icon:path.join(__dirname,'ui/assets/AppIcon.ico'),webPreferences:{preload:path.join(__dirname,'preload.cjs'),contextIsolation:true,nodeIntegration:false,sandbox:true,backgroundThrottling:!test,paintWhenInitiallyHidden:true}});
  window.setMenu(null);window.webContents.setWindowOpenHandler(()=>({action:'deny'}));window.webContents.on('will-navigate',event=>event.preventDefault());
  window.on('close',event=>{if(!closing){event.preventDefault();shutdown(true);}});
  window.webContents.on('render-process-gone',()=>shutdown());
  await window.loadFile(path.join(__dirname,'ui/index.html'));
  if(test){await runTests();return;}
  window.show();
  if(!hosted){globalShortcut.register('CommandOrControl+Alt+F8',async()=>{try{await ready;const state=await request('status');await request('enabled',{value:!state.enabled});}catch{}});
  globalShortcut.register('CommandOrControl+Alt+F12',shutdown);}
 }).catch(async error=>{if(!test)dialog.showErrorBox('CursorMotion',error.message);else console.error(error);await shutdown();});
}
app.on('window-all-closed',()=>shutdown());
async function runTests(){
 const index=process.argv.indexOf('--output');const output=index>=0?path.resolve(process.argv[index+1]):path.join(__dirname,'ui-test');fs.mkdirSync(output,{recursive:true});
 let framePump;
 try{
  await ready;
  // Both hidden test modes need their OWN compositor clock, including the
  // hosted connection/close probe. This never captures the user's desktop.
  framePump=setInterval(()=>{if(window&&!window.isDestroyed())window.webContents.capturePage({x:0,y:0,width:1,height:1}).catch(()=>{});},120);
  if(process.argv.includes('--connection-test')){
   const snapshot=await request('get');
   const startup=hosted?await request('startup'):null;
   if(snapshot.appearances.length!==4)throw Error('Hosted settings are incomplete');
   fs.writeFileSync(path.join(output,'results.json'),JSON.stringify({pass:true,hosted,appearances:snapshot.appearances,startup},null,2));
   await shutdown(true);clearInterval(framePump);return;
  }
  // A hidden Chromium window may stop requesting frames after a view transition.
  // Keep this test's own compositor painting; never capture the user's desktop.
  for(let i=0;i<100;i++){if(await window.webContents.executeJavaScript('Boolean(window.__studioReady)'))break;await new Promise(r=>setTimeout(r,100));}
  const errors=[];
  window.webContents.on('console-message',(_event,_level,message)=>{fs.appendFileSync(path.join(output,'progress.log'),String(message)+'\n');if(_level>=3)errors.push(message);});
  const results=await Promise.race([window.webContents.executeJavaScript('window.__runStudioTests()'),new Promise((_,reject)=>setTimeout(()=>reject(Error('UI interaction checks timed out after 600 seconds')),600000))]);
  await new Promise(resolve=>setTimeout(resolve,3000));
  await window.webContents.insertCSS('*{animation:none!important;transition:none!important}');
  for(const page of ['collection','motion','cursor','screen']){
   await window.webContents.executeJavaScript(`window.__showPage(${JSON.stringify(page)},{animate:false})`);
   await window.webContents.executeJavaScript('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
   await new Promise(r=>setTimeout(r,100));
   fs.writeFileSync(path.join(output,page+'.png'),(await window.webContents.capturePage()).toPNG());
  }
  const viewports=[];
  for(const size of [{width:1630,height:810},{width:1630,height:1058},{width:1108,height:720}]){
   window.setContentSize(size.width,size.height);
   await window.webContents.executeJavaScript('window.__showPage("collection",{animate:false})');
   await new Promise(resolve=>setTimeout(resolve,250));
   const metrics=await window.webContents.executeJavaScript(`({width:innerWidth,height:innerHeight,dpr:devicePixelRatio,overflow:document.documentElement.scrollWidth>innerWidth,dock:document.querySelector('.action-dock').getBoundingClientRect().toJSON(),canvas:document.querySelector('.illustration-canvas').getBoundingClientRect().toJSON()})`);
   if(metrics.overflow||metrics.dock.bottom>metrics.height)throw Error('Collection does not fit '+JSON.stringify(metrics));
   viewports.push(metrics);
   fs.writeFileSync(path.join(output,`collection-${size.width}x${size.height}.png`),(await window.webContents.capturePage()).toPNG());
  }
  window.setContentSize(1630,810);
  const frames=[],artworkQuality=[];
  for(const page of ['motion','cursor','space']){
   await window.webContents.executeJavaScript(`window.__showPage(${JSON.stringify(page)},{animate:false})`);
   await new Promise(resolve=>setTimeout(resolve,200));
   frames.push(await window.webContents.executeJavaScript(`document.querySelector('.settings-art .illustration-canvas').getBoundingClientRect().toJSON()`));
   const quality=await window.webContents.executeJavaScript(`[...document.querySelectorAll('.settings-art img')].map(image=>({source:image.src.startsWith("data:")?"native-sdf:"+image.dataset.form:image.src,naturalWidth:image.naturalWidth,renderedWidth:image.getBoundingClientRect().width,dpr:devicePixelRatio,pixelReserve:image.naturalWidth/(image.getBoundingClientRect().width*devicePixelRatio),transform:getComputedStyle(document.querySelector('.settings-art .cursor-artwork')).transform}))`);
   if(quality.some(image=>image.pixelReserve<1||image.transform!=='none'))throw Error('Artwork is upscaled or composited at rest');
   artworkQuality.push({page,images:quality});
   await window.webContents.executeJavaScript(`Promise.all([...document.querySelectorAll('.settings-art img')].map(image=>image.decode())).then(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))))`);
   await window.webContents.capturePage();
   await new Promise(resolve=>setTimeout(resolve,100));
   fs.writeFileSync(path.join(output,`${page}-1630x810.png`),(await window.webContents.capturePage()).toPNG());
  }
  if(frames.some(frame=>Math.abs(frame.y-frames[0].y)>.5||Math.abs(frame.height-frames[0].height)>.5))throw Error('Settings illustration frame moves between tabs');
  const panelLayouts=[];
  for(const size of [{width:1630,height:810},{width:1630,height:1058},{width:1108,height:720},{width:980,height:700}]){
   window.setContentSize(size.width,size.height);
   const panels=[];
   for(const page of ['motion','cursor','space']){
    await window.webContents.executeJavaScript(`window.__showPage(${JSON.stringify(page)},{animate:false})`);
    await window.webContents.executeJavaScript('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
    panels.push(await window.webContents.executeJavaScript(`({page:${JSON.stringify(page)},...document.querySelector('.settings-panel').getBoundingClientRect().toJSON()})`));
   }
   if(panels.some(panel=>['x','y','width','height'].some(property=>Math.abs(panel[property]-panels[0][property])>.1)))throw Error('Settings shells differ at '+JSON.stringify(size));
   panelLayouts.push({viewport:size,panels});
  }
  window.setSize(980,700);await new Promise(r=>setTimeout(r,250));
  const overflow=await window.webContents.executeJavaScript('document.documentElement.scrollWidth>innerWidth');if(overflow)throw Error('Horizontal overflow at minimum width');
  fs.writeFileSync(path.join(output,'minimum.png'),(await window.webContents.capturePage()).toPNG());
  if(errors.length)throw Error('Renderer errors: '+errors.join('; '));
  fs.writeFileSync(path.join(output,'results.json'),JSON.stringify({pass:true,...results,minimumWidth:980,viewports,stableSettingsFrames:true,panelLayouts,artworkQuality,rendererErrors:errors},null,2));
 }catch(error){fs.writeFileSync(path.join(output,'results.json'),JSON.stringify({pass:false,error:error.stack},null,2));process.exitCode=1;}
 clearInterval(framePump);
 await shutdown();
}


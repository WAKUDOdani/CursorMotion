const{spawn}=require('node:child_process');const readline=require('node:readline');const fs=require('node:fs');const path=require('node:path');
const output=path.resolve(__dirname,'../test-v3-protocol');fs.mkdirSync(output,{recursive:true});
const exe=process.argv[2]?path.resolve(process.argv[2]):path.resolve(__dirname,'../src/bin/Release/net10.0-windows/CursorMotion.Engine.exe');
const child=spawn(exe,['--backend','--diagnostic'],{windowsHide:true,stdio:['pipe','pipe','pipe']});
let id=0;const pending=new Map();let readyResolve;const ready=new Promise(resolve=>readyResolve=resolve);
readline.createInterface({input:child.stdout}).on('line',line=>{const message=JSON.parse(line);if(message.event==='ready')readyResolve(message.data);else{const p=pending.get(message.id);if(p){pending.delete(message.id);message.ok?p.resolve(message.data):p.reject(Error(message.error));}}});
const request=(cmd,data={})=>new Promise((resolve,reject)=>{const key=++id;pending.set(key,{resolve,reject});child.stdin.write(JSON.stringify({id:key,cmd,data})+'\n');});
const assert=(ok,text)=>{if(!ok)throw Error(text);};
// This suite renders real SDF artwork. CPU-only CI can legitimately take longer
// than15s; this is a TEST deadline, not the production IPC response deadline.
const timeout=setTimeout(()=>{console.error('Backend protocol/artwork timeout after120s');child.kill();process.exit(1);},120000);
(async()=>{let count=0;try{
 let state=await ready;assert(state.appearances.length===4,'Missing appearances');count++;
 for(let shape=0;shape<4;shape++){state=await request('appearance',{shape,property:'Scale',value:2+shape});assert(state.appearances[shape].Scale===2+shape,'Independent scale failed');count++;}
 state=await request('pack',{id:1});assert(state.appearances.every((a,i)=>a.Skin===1&&a.Scale===2+i),'Pack resets independent scale');count++;
 const profile=path.join(output,'roundtrip.json');await request('export',{path:profile});const expected=JSON.stringify(state.settings);await request('reset');state=await request('import',{path:profile});assert(JSON.stringify(state.settings)===expected,'Profile roundtrip failed');count++;
 for(const [cmd,data]of [['set',{property:'Bogus',value:1}],['appearance',{shape:5,property:'Scale',value:1}],['pack',{id:1,shape:-1}]]){let rejected=false;try{await request(cmd,data);}catch{rejected=true;}assert(rejected,'Invalid input accepted');count++;}
 state=await request('appearance',{shape:2,property:'Scale',value:500});assert(state.appearances[2].Scale===6,'Validation failed');count++;
 const before=state.appearances;state=await request('preset',{name:'Мгновенный'});assert(JSON.stringify(state.appearances)===JSON.stringify(before)&&state.settings.Smoothing===0,'Motion preset affected appearances');count++;
 const forms=structuredClone(state.appearances);forms[1]={...forms[1],Skin:2,Scale:2.35,FillColor:'#AA778899'};
 state=await request('forms',{appearances:forms});assert(JSON.stringify(state.appearances)===JSON.stringify(forms),'Atomic forms apply failed');count++;
 const unchanged=JSON.stringify(state.settings);
 for(const appearances of [[forms[0]], [forms[0],null,forms[2]], [forms[0],forms[1],{...forms[2],Bogus:1}]]){let rejected=false;try{await request('forms',{appearances});}catch{rejected=true;}assert(rejected,'Malformed forms accepted');assert(JSON.stringify((await request('get')).settings)===unchanged,'Failed batch partially changed settings');count++;}
 for(const [property,value]of [['InterfaceParallax',1.4],['ReduceMotion',true],['WindowTint',.45],['CaptureCompatibility',true]]){state=await request('set',{property,value});assert(state.settings[property]===value,'Global setting did not persist');count++;}
 await request('export',{path:profile});const fullProfile=JSON.stringify(state.settings);await request('reset');state=await request('import',{path:profile});assert(JSON.stringify(state.settings)===fullProfile,'Mixed forms and interface settings roundtrip failed');count++;
 const savedHand=JSON.stringify(state.appearances[3]);
 const legacy=structuredClone(state.appearances.slice(0,3));legacy[0].Scale=1.25;
 state=await request('forms',{appearances:legacy});assert(JSON.stringify(state.appearances[3])===savedHand&&state.appearances[0].Scale===1.25,'Legacy three-form batch changed hand');count++;
 const four=structuredClone(state.appearances);four[3]={...four[3],Skin:4,Scale:2.4,MorphTime:185};
 state=await request('forms',{appearances:four});assert(JSON.stringify(state.appearances)===JSON.stringify(four),'Four-form batch lost the hand');count++;
 const beforeArtwork=JSON.stringify(state.settings);
 const artwork=await request('artwork',{appearances:state.appearances});
 assert(artwork.renderer==='native-sdf'&&artwork.normalizedScale&&Object.keys(artwork.frames).length===10,'Missing native still samples');count++;
 for(const frame of Object.values(artwork.frames)){const png=Buffer.from(frame.dataUri.split(',')[1],'base64');assert(png.subarray(1,4).toString()==='PNG'&&png.readUInt32BE(16)===frame.width&&png.readUInt32BE(20)===frame.height&&frame.bounds.right<=frame.width,'Invalid native sample PNG');}count++;
 const changedLooks=structuredClone(state.appearances);changedLooks[1].FillColor='#FF00CC22';
 const repainted=await request('artwork',{appearances:changedLooks});
 assert(repainted.frames.text.dataUri!==artwork.frames.text.dataUri&&repainted.frames.arrow.dataUri===artwork.frames.arrow.dataUri&&repainted.frames.hand.dataUri===artwork.frames.hand.dataUri,'Samples ignore palette or alter other forms');count++;
 changedLooks[1].Scale=5;changedLooks[1].MorphTime=0;
 const normalized=await request('artwork',{appearances:changedLooks});assert(normalized.frames.text.dataUri===repainted.frames.text.dataUri,'Still sample depends on movement/timing or scales a small bitmap');count++;
 assert(JSON.stringify((await request('get')).settings)===beforeArtwork,'Sample request mutated the profile');count++;
 let badArtwork=false;try{await request('artwork',{appearances:[four[0]]});}catch{badArtwork=true;}assert(badArtwork,'Malformed sample request accepted');count++;

 const fullBeforePreset=JSON.stringify((await request('get')).settings);
 state=await request('presets-save',{name:'Мой повседневный'});const savedId=state.presets[0].id;
 assert(state.presets.length===1&&state.presets[0].isCurrent,'Saved preset is not current');count++;
 await request('set',{property:'Shutter',value:90});state=await request('presets-apply',{id:savedId});
 assert(JSON.stringify(state.settings)===fullBeforePreset&&state.presets[0].isCurrent,'Preset did not restore every setting');count++;
 const presetExport=path.join(output,'shared-preset.json');await request('presets-export',{id:savedId,path:presetExport});
 assert(JSON.stringify(JSON.parse(fs.readFileSync(presetExport,'utf8')))===fullBeforePreset,'Named export changed settings');count++;
 const beforeLibraryImport=JSON.stringify(state.settings);state=await request('presets-import',{path:presetExport});
 assert(state.presets.length===2&&JSON.stringify(state.settings)===beforeLibraryImport,'Library import changed active settings');count++;
 state=await request('reset');assert(state.presets.length===2,'Reset erased the preset library');count++;
 for(const [cmd,data]of [['presets-save',{name:'  '}],['presets-save',{name:'МОЙ ПОВСЕДНЕВНЫЙ'}],['presets-delete',{id:'../../settings'}]]){
  let rejected=false;try{await request(cmd,data)}catch{rejected=true}assert(rejected,'Invalid preset request accepted');count++;
 }
 state=await request('presets-delete',{id:savedId});assert(state.presets.length===1,'Preset was not deleted');count++;
 await request('shutdown');fs.writeFileSync(path.join(output,'results.json'),JSON.stringify({pass:true,assertions:count,roundtrip:true,invalidInput:true},null,2));console.log('Protocol passed:',count);
 }catch(error){console.error(error);child.kill();process.exitCode=1;}finally{clearTimeout(timeout);}})();

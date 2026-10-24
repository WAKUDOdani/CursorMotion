// Validate an exported author profile through the REAL diagnostic backend before
// embedding it for new installs/reset. Never changes a running user's settings.
const {spawn}=require('node:child_process');
const readline=require('node:readline'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..');
const input=process.argv[2]&&path.resolve(process.argv[2]);
if(!input){console.error('node tools/set-default-profile.cjs profile.json [--engine engine.exe] [--output destination.json]');process.exit(1)}
if(fs.statSync(input).size>1024*1024)throw Error('Profile exceeds 1 MiB');
const option=(name,fallback)=>{const i=process.argv.indexOf(name);return i<0?fallback:path.resolve(process.argv[i+1])};
const output=option('--output',path.join(root,'src/Assets/DefaultProfile.json'));
const exe=option('--engine',path.join(root,'src/bin/Release/net10.0-windows/CursorMotion.Engine.exe'));
const child=spawn(exe,['--backend','--diagnostic'],{windowsHide:true,stdio:['pipe','pipe','pipe']});
let seq=0;const pending=new Map();let running=false;
const timeout=setTimeout(()=>{child.kill();console.error('Default profile validation timed out');process.exitCode=1},20000);
const request=(cmd,data={})=>new Promise((resolve,reject)=>{const id=++seq;pending.set(id,{resolve,reject});child.stdin.write(JSON.stringify({id,cmd,data})+'\n')});
readline.createInterface({input:child.stdout}).on('line',async line=>{
 try{
  const response=JSON.parse(line);
  if(response.event==='ready'&&!running){
   running=true;
   const state=await request('import',{path:input});
   fs.mkdirSync(path.dirname(output),{recursive:true});
   fs.writeFileSync(output+'.tmp',JSON.stringify(state.settings,null,2)+'\n');fs.renameSync(output+'.tmp',output);
   await request('shutdown');child.stdin.end();console.log('Validated release defaults written:',output);
  }else{const p=pending.get(response.id);if(p){pending.delete(response.id);response.ok?p.resolve(response.data):p.reject(Error(response.error))}}
 }catch(error){console.error(error.message);process.exitCode=1;child.kill()}
});
child.stderr.on('data',data=>process.stderr.write(data));
child.on('error',error=>{clearTimeout(timeout);console.error(error.message);process.exitCode=1});
child.on('exit',code=>{clearTimeout(timeout);if(code!==0)process.exitCode=1});

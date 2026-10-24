const {spawn}=require('node:child_process');
const path=require('node:path');
const fs=require('node:fs');
const [studio,engine,output]=process.argv.slice(2).map(value=>path.resolve(value));
fs.mkdirSync(output,{recursive:true});
const child=spawn(studio,['--ui-test','--engine',engine,'--output',output],{windowsHide:true,stdio:'inherit'});
child.on('error',error=>{console.error(error);process.exitCode=1;});
child.on('exit',code=>{const report=path.join(output,'results.json');if(!fs.existsSync(report)){console.error('UI report missing');process.exitCode=1;return;}const result=JSON.parse(fs.readFileSync(report,'utf8'));console.log(JSON.stringify({exit:code,pass:result.pass,assertions:result.assertions,error:result.error}));if(!result.pass)process.exitCode=1;});

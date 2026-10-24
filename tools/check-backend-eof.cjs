const {spawn}=require('node:child_process');
const readline=require('node:readline');
const child=spawn(process.argv[2],['--backend','--diagnostic'],{windowsHide:true,stdio:['pipe','pipe','pipe']});
let ready=false,errors='';
const timeout=setTimeout(()=>{child.kill();console.error('Packaged backend did not exit on stdin EOF');process.exit(1)},15000);
readline.createInterface({input:child.stdout}).on('line',line=>{try{if(JSON.parse(line).event==='ready'){ready=true;child.stdin.end()}}catch{}});
child.stderr.on('data',data=>{errors+=String(data)});
child.on('error',error=>{clearTimeout(timeout);console.error(error);process.exitCode=1});
child.on('exit',code=>{clearTimeout(timeout);if(code!==0||!ready){console.error('Backend EOF failure:',code,errors);process.exitCode=1}else console.log('Packaged backend EOF: passed')});

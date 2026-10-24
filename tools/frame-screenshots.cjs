// Presentation wrapper only: the original screenshot pixels are embedded intact.
// GitHub strips inline CSS, so a real SVG clip gives transparent rounded corners.
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'..');
for(const name of ['collection','cursor']){
 const png=fs.readFileSync(path.join(root,'docs/assets',name+'.png'));
 const width=png.readUInt32BE(16),height=png.readUInt32BE(20);
 const radius=Math.round(Math.min(width,height)*.032);
 const svg=`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}"><title>CursorMotion — ${name==='collection'?'коллекция курсоров':'настройки курсора'}</title><defs><clipPath id="corners"><rect width="${width}" height="${height}" rx="${radius}"/></clipPath></defs><image width="${width}" height="${height}" clip-path="url(#corners)" href="data:image/png;base64,${png.toString('base64')}"/></svg>\n`;
 fs.writeFileSync(path.join(root,'docs/assets',name+'-rounded.svg'),svg);
}

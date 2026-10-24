const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('studio',Object.freeze({
 request:(cmd,data)=>ipcRenderer.invoke('studio:request',cmd,data),
 window:action=>ipcRenderer.invoke('studio:window',action),
 file:action=>ipcRenderer.invoke('studio:file',action),
 startup:enabled=>ipcRenderer.invoke('studio:startup',enabled),
 telegram:()=>ipcRenderer.invoke('studio:telegram')
}));

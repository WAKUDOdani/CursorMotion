param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$Icon)
$ErrorActionPreference='Stop'
if(-not ('CursorMotionBranding' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class CursorMotionBranding {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr BeginUpdateResource(string file,bool delete);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool UpdateResource(IntPtr handle,IntPtr type,IntPtr name,ushort language,byte[] data,uint size);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool EndUpdateResource(IntPtr handle,bool discard);
 public static void Apply(string file,string icon) {
  byte[] data=File.ReadAllBytes(icon);ushort count=BitConverter.ToUInt16(data,4);
  byte[] group=new byte[6+14*count];Array.Copy(data,0,group,0,6);
  IntPtr handle=BeginUpdateResource(file,false);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  bool committed=false;
  try {for(int i=0;i<count;i++) {
   int source=6+i*16;uint size=BitConverter.ToUInt32(data,source+8);uint offset=BitConverter.ToUInt32(data,source+12);
   byte[] pixels=new byte[size];Array.Copy(data,(int)offset,pixels,0,(int)size);
   if(!UpdateResource(handle,(IntPtr)3,(IntPtr)(i+1),1033,pixels,size))throw new System.ComponentModel.Win32Exception();
   Array.Copy(data,source,group,6+i*14,12);Array.Copy(BitConverter.GetBytes((ushort)(i+1)),0,group,6+i*14+12,2);
  }
  if(!UpdateResource(handle,(IntPtr)14,(IntPtr)1,1033,group,(uint)group.Length))throw new System.ComponentModel.Win32Exception();
  if(!EndUpdateResource(handle,false))throw new System.ComponentModel.Win32Exception();committed=true;
  }finally{if(!committed)EndUpdateResource(handle,true);}
 }
}
'@
}
[CursorMotionBranding]::Apply([IO.Path]::GetFullPath($Executable),[IO.Path]::GetFullPath($Icon))

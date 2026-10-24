$ErrorActionPreference='Stop'
$taskLock=Get-Content -Raw (Join-Path $PSScriptRoot 'runtime-lock.json') | ConvertFrom-Json
$taskVendor=Join-Path $PSScriptRoot 'desktop/vendor/electron'
if(Test-Path -LiteralPath (Join-Path $taskVendor 'electron.exe')){Write-Output 'Electron build runtime already present.';return}
$taskArtifacts=Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $taskArtifacts -Force | Out-Null
$taskZip=Join-Path $taskArtifacts ('electron-'+$taskLock.electron.version+'-win32-x64.zip')
if(-not(Test-Path -LiteralPath $taskZip) -or (Get-FileHash -LiteralPath $taskZip).Hash -ne $taskLock.electron.sha256){
 $taskDownload=$taskZip+'.download'
 Invoke-WebRequest -Uri $taskLock.electron.url -OutFile $taskDownload -TimeoutSec 300
 if((Get-FileHash -LiteralPath $taskDownload -Algorithm SHA256).Hash -ne $taskLock.electron.sha256){throw 'Electron download checksum mismatch'}
 Move-Item -LiteralPath $taskDownload -Destination $taskZip -Force
}
New-Item -ItemType Directory -Path $taskVendor -Force | Out-Null
Expand-Archive -LiteralPath $taskZip -DestinationPath $taskVendor -Force
Write-Output ('Electron '+$taskLock.electron.version+' downloaded and verified.')

param([Parameter(Mandatory)][string]$Executable,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskExe=[IO.Path]::GetFullPath($Executable)
$taskOutput=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskRoot ('artifacts/verification-package-'+[Guid]::NewGuid().ToString('N'))}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskProfile=Join-Path $env:LOCALAPPDATA 'CursorMotion/settings.json'
$taskProfileBefore=if(Test-Path -LiteralPath $taskProfile){(Get-FileHash -LiteralPath $taskProfile).Hash}else{$null}
$taskStartupBefore=(Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CursorMotion -ErrorAction SilentlyContinue).CursorMotion
$taskBeside=@(Get-ChildItem -LiteralPath (Split-Path $taskExe) -Force | Select-Object -ExpandProperty Name)
function Invoke-Bootstrap([string]$taskFile,[string]$taskName){
 $taskReport=Join-Path $taskOutput ($taskName+'.json')
 $taskTimer=[Diagnostics.Stopwatch]::StartNew()
 $taskProcess=Start-Process -FilePath $taskFile -ArgumentList @('--bootstrap-info',('"'+$taskReport+'"'),'--bootstrap-cache',('"'+(Join-Path $taskOutput 'cache')+'"')) -PassThru -WindowStyle Hidden
 $taskProcess.WaitForExit()
 if($taskProcess.ExitCode -ne 0){throw "Bootstrap check failed: $taskName"}
 [pscustomobject]@{ms=$taskTimer.ElapsedMilliseconds;report=(Get-Content -Raw $taskReport|ConvertFrom-Json)}
}
$taskCold=Invoke-Bootstrap $taskExe 'cold'
$taskWarm=Invoke-Bootstrap $taskExe 'warm'
if($taskCold.report.runtime -ne $taskWarm.report.runtime){throw 'Warm launch extracted again'}
if(Test-Path -LiteralPath (Join-Path $taskCold.report.runtime 'portable.flag')){throw 'Single-file build uses beside-EXE settings'}
$taskLicense=Join-Path $taskCold.report.runtime 'ORIGINAL-LICENSE.txt'
Set-Content -LiteralPath $taskLicense -Value 'Synthetic corruption for integrity/recovery verification' -Encoding utf8
$taskRepaired=Invoke-Bootstrap $taskExe 'repaired'
if($taskRepaired.report.runtime -eq $taskCold.report.runtime){throw 'Corrupt runtime was reused'}
if((Get-Content -Raw (Join-Path $taskRepaired.report.runtime 'ORIGINAL-LICENSE.txt')) -notlike '*public domain*'){throw 'Runtime repair did not restore bytes'}
$taskMoved=Join-Path $taskOutput 'Проверка переноса'
New-Item -ItemType Directory -Path $taskMoved -Force | Out-Null
$taskMovedExe=Join-Path $taskMoved 'Мой курсор с пробелами.exe'
Copy-Item -LiteralPath $taskExe -Destination $taskMovedExe
$taskRenamed=Invoke-Bootstrap $taskMovedExe 'renamed'
if($taskRenamed.report.publicExe -ne $taskMovedExe -or $taskRenamed.report.startupCommand -ne ('"'+$taskMovedExe+'" --startup')){throw 'Renamed public startup path was lost'}
Push-Location $taskRoot
try{
 node desktop/test-backend.cjs $taskExe
 if($LASTEXITCODE -ne 0){throw 'Actual single-file backend failed'}
 node tools/check-backend-eof.cjs $taskExe
 if($LASTEXITCODE -ne 0){throw 'Packaged backend did not shut down on EOF'}
 node desktop/test-tray.cjs $taskExe (Join-Path $taskRepaired.report.runtime 'studio/CursorMotion.Studio.exe')
 if($LASTEXITCODE -ne 0){throw 'Actual single-file tray lifecycle failed'}
}finally{Pop-Location}
$taskProfileAfter=if(Test-Path -LiteralPath $taskProfile){(Get-FileHash -LiteralPath $taskProfile).Hash}else{$null}
$taskStartupAfter=(Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CursorMotion -ErrorAction SilentlyContinue).CursorMotion
if($taskProfileBefore -ne $taskProfileAfter -or $taskStartupBefore -ne $taskStartupAfter){throw 'Verification changed installed settings or startup'}
if(Compare-Object $taskBeside @(Get-ChildItem -LiteralPath (Split-Path $taskExe) -Force | Select-Object -ExpandProperty Name)){throw 'Application created files beside EXE'}
[pscustomobject]@{pass=$true;version=$taskCold.report.version;file=[IO.Path]::GetFileName($taskExe);bytes=(Get-Item -LiteralPath $taskExe).Length;sha256=(Get-FileHash -LiteralPath $taskExe).Hash.ToLowerInvariant();runtimeFiles=$taskCold.report.files;coldMs=$taskCold.ms;warmMs=$taskWarm.ms;cacheReuse=$true;corruptionRecovery=$true;renamedUnicodePath=$true;backendEof=$true;settingsInAppData=$true;noFilesBesideExe=$true;installedProfileUntouched=$true;autostartUntouched=$true;noGlobalCursorChanges=$true} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $taskOutput 'package-verification.json')

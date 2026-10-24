param([Parameter(Mandatory)][string]$Directory,[string]$OutputDirectory,[switch]$ForceWarp)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskFolder=[IO.Path]::GetFullPath($Directory)
$taskOutput=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskRoot 'artifacts/verification-folder'}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskEngine=Join-Path $taskFolder 'CursorMotion.Engine.exe'
$taskStudio=Join-Path $taskFolder 'studio/CursorMotion.Studio.exe'
$taskNative=Join-Path $taskOutput 'native'
$taskArguments=@('--self-test','--offscreen','--output',('"'+$taskNative+'"'));if($ForceWarp){$taskArguments+='--warp'}
$taskProcess=Start-Process -FilePath $taskEngine -ArgumentList $taskArguments -PassThru -WindowStyle Hidden
$taskProcess.WaitForExit()
if($taskProcess.ExitCode -ne 0){throw "Native checks failed: $taskNative"}
Push-Location $taskRoot
try{
 node --test (Get-ChildItem desktop/renderer/tests -Filter '*.test.mjs').FullName
 if($LASTEXITCODE -ne 0){throw 'JavaScript checks failed'}
 node desktop/test-backend.cjs $taskEngine
 if($LASTEXITCODE -ne 0){throw 'Backend checks failed'}
 node tools/run-hidden-studio.cjs $taskStudio $taskEngine (Join-Path $taskOutput 'ui')
 if($LASTEXITCODE -ne 0){throw 'Hidden UI checks failed'}
 node desktop/test-tray.cjs (Join-Path $taskFolder 'CursorMotion.exe') $taskStudio
 if($LASTEXITCODE -ne 0){throw 'Tray checks failed'}
}finally{Pop-Location}
[pscustomobject]@{pass=$true;nativeGroups=(Get-Content -Raw (Join-Path $taskNative 'results.json')|ConvertFrom-Json).results.Count;uiAssertions=(Get-Content -Raw (Join-Path $taskOutput 'ui/results.json')|ConvertFrom-Json).assertions;backendAssertions=(Get-Content -Raw (Join-Path $taskRoot 'test-v3-protocol/results.json')|ConvertFrom-Json).assertions;trayAssertions=(Get-Content -Raw (Join-Path $taskRoot 'test-v3-tray/results.json')|ConvertFrom-Json).assertions;noGlobalCursorChanges=$true} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $taskOutput 'results.json')

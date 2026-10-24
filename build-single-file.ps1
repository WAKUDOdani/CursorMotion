param([string]$InputDirectory,[string]$OutputDirectory,[switch]$ReuseBuiltUi,[switch]$SkipTests)
$ErrorActionPreference='Stop'
$taskRoot=$PSScriptRoot
$taskVersion=([xml](Get-Content -Raw (Join-Path $taskRoot 'src/CursorMotion.csproj'))).Project.PropertyGroup.Version
$taskWork=Join-Path $taskRoot ('artifacts/package-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskWork -Force | Out-Null
if(-not $InputDirectory){
 $InputDirectory=Join-Path $taskWork 'runtime'
 & (Join-Path $taskRoot 'build.ps1') -OutputDirectory $InputDirectory -ReuseBuiltUi:$ReuseBuiltUi -SkipTests:$SkipTests
}
$taskInput=[IO.Path]::GetFullPath($InputDirectory)
if(-not(Test-Path -LiteralPath (Join-Path $taskInput 'CursorMotion.Engine.exe'))){throw 'Input does not contain the native engine'}
if(-not(Test-Path -LiteralPath (Join-Path $taskInput 'studio/CursorMotion.Studio.exe'))){throw 'Input does not contain Studio'}
$taskFiles=@(Get-ChildItem -LiteralPath $taskInput -Recurse -File | ForEach-Object {
 $taskRelative=[IO.Path]::GetRelativePath($taskInput,$_.FullName).Replace('\','/')
 if($taskRelative -notlike 'data/*' -and $taskRelative -ne 'portable.flag' -and $taskRelative -ne 'CursorMotion.exe' -and $_.Extension -notin @('.pdb','.cmd') -and $_.Name -notlike 'MANIFEST*'){
  [pscustomobject]@{path=$taskRelative;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash;source=$_.FullName}
 }
} | Sort-Object path)
if($taskFiles.path -contains 'portable.flag' -or @($taskFiles.path | Where-Object {$_ -like 'data/*'}).Count){throw 'Personal portable data must not be bundled'}
$taskZip=Join-Path $taskWork 'payload.zip'
$taskStream=[IO.File]::Create($taskZip)
$taskArchive=[IO.Compression.ZipArchive]::new($taskStream,[IO.Compression.ZipArchiveMode]::Create)
try {
 foreach($taskFile in $taskFiles){
  $taskEntry=$taskArchive.CreateEntry($taskFile.path,[IO.Compression.CompressionLevel]::SmallestSize)
  $taskEntry.LastWriteTime=[DateTimeOffset]::new(2026,1,1,0,0,0,[TimeSpan]::Zero)
  $taskSource=[IO.File]::OpenRead($taskFile.source);$taskDestination=$taskEntry.Open()
  try{$taskSource.CopyTo($taskDestination)}finally{$taskSource.Dispose();$taskDestination.Dispose()}
 }
}finally{$taskArchive.Dispose();$taskStream.Dispose()}
$taskPayloadHash=(Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash
@{version=$taskVersion;sha256=$taskPayloadHash;files=@($taskFiles|Select-Object path,bytes,sha256)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskWork 'payload.json') -Encoding utf8
$taskPublished=Join-Path $taskWork 'launcher'
dotnet publish (Join-Path $taskRoot 'bootstrap/CursorMotion.Bootstrap.csproj') -c Release -r win-x64 --self-contained true "-p:PayloadDirectory=$taskWork" -o $taskPublished --nologo
if($LASTEXITCODE -ne 0){throw 'Single-file launcher publish failed'}
$taskOutput=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskRoot 'dist/single-file'}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskName="CursorMotion-$taskVersion-portable-x64.exe"
$taskFinal=Join-Path $taskOutput $taskName
Copy-Item -LiteralPath (Join-Path $taskPublished 'CursorMotion.exe') -Destination $taskFinal -Force
((Get-FileHash -LiteralPath $taskFinal -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$taskName) | Set-Content -LiteralPath (Join-Path $taskOutput 'SHA256SUMS.txt') -Encoding ascii
[pscustomobject]@{executable=$taskFinal;bytes=(Get-Item -LiteralPath $taskFinal).Length;payloadBytes=(Get-Item -LiteralPath $taskZip).Length;runtimeFiles=$taskFiles.Count;payloadHash=$taskPayloadHash} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $taskWork 'build.json')

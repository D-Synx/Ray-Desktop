$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'C# compiler missing. See maintenance/README.fr.md.' }
$test = Join-Path ([IO.Path]::GetTempPath()) ('RayProfileTests-' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compiler /nologo "/out:$test" (Join-Path $PSScriptRoot 'ProfileTests.cs') (Join-Path $root 'src\ChildSessionDesktop\MaintenanceProfile.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Profile test compilation failed.' }
    & $test
    if ($LASTEXITCODE -ne 0) { throw 'Profile tests failed.' }
}
finally {
    if (Test-Path -LiteralPath $test) { Remove-Item -LiteralPath $test }
}
& (Join-Path $root 'build.ps1')
$files = @('Raydesktop.exe', 'EnableChildSessions.exe', 'MSTSCLib.dll', 'AxInterop.MSTSCLib.dll') |
    ForEach-Object { Join-Path $root "Raydesktop\$_" }
$files += Join-Path $root 'LICENSE'
$files += Join-Path $PSScriptRoot 'README.fr.md'
$archive = Join-Path $root 'RayDesktopMaintenance-Windows.zip'
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
Write-Host "Portable archive: $archive"

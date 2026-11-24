param(
    [ValidatePattern('^[a-fA-F0-9]{40}$')]
    [string]$CodeSigningThumbprint,
    [string]$SignToolPath,
    [string]$TimestampUrl,
    [string]$GpuHelperPath
)

$ErrorActionPreference = 'Stop'
$sign = $CodeSigningThumbprint -or $SignToolPath -or $TimestampUrl
if ($sign -and (-not $CodeSigningThumbprint -or -not $SignToolPath -or -not $TimestampUrl)) {
    throw 'Provide CodeSigningThumbprint, SignToolPath and TimestampUrl together to sign the portable build.'
}
if ($sign) {
    $SignToolPath = (Get-Item -LiteralPath $SignToolPath).FullName
}
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
$files = @('RayXDesktop.exe', 'EnableChildSessions.exe', 'MSTSCLib.dll', 'AxInterop.MSTSCLib.dll') |
    ForEach-Object { Join-Path $root "Raydesktop\$_" }
if ($GpuHelperPath) {
    $helper = Get-Item -LiteralPath $GpuHelperPath
    if ($helper.PSIsContainer -or $helper.Name -cne 'RayXDesktop.GpuPreview.exe') {
        throw 'GpuHelperPath must point to the built RayXDesktop.GpuPreview.exe.'
    }
    $files += $helper.FullName
    $files += Join-Path $PSScriptRoot 'GPU.fr.md'
}
if ($sign) {
    foreach ($file in $files | Where-Object { [IO.Path]::GetExtension($_) -in @('.exe', '.dll') }) {
        & $SignToolPath sign /sha1 $CodeSigningThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $file
        if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed for $file." }
        & $SignToolPath verify /pa /v $file
        if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed for $file." }
    }
}
$files += Join-Path $root 'languages'
$files += Join-Path $root 'LICENSE'
$files += Join-Path $PSScriptRoot 'README.fr.md'
$files += Join-Path $PSScriptRoot 'RayXDesktop.bat'
$archive = Join-Path $root 'RayXDesktop-Windows.zip'
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archive.sha256" -Value "$hash  $(Split-Path -Leaf $archive)" -Encoding Ascii
Write-Host "Portable archive: $archive"

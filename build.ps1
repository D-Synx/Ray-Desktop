$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Could not find the .NET Framework C# compiler (csc.exe). Install .NET Framework 4.x or Visual Studio Build Tools.'
}

$output = Join-Path $PSScriptRoot 'Raydesktop'
$references = @('/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Security.dll', '/r:System.Management.dll', '/r:System.Xml.Linq.dll')
$framework = Split-Path -Parent $compiler
$wpfReferences = @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll', 'WindowsFormsIntegration.dll') | ForEach-Object {
    $assembly = Join-Path (Join-Path $framework 'WPF') $_
    if (-not (Test-Path -LiteralPath $assembly)) { $assembly = Join-Path $framework $_ }
    if (-not (Test-Path -LiteralPath $assembly)) { throw "Missing .NET Framework WPF assembly $_." }
    "/r:$assembly"
}
function Publish-BuildOutput([string]$temporary, [string]$target) {
    [IO.File]::Copy($temporary, $target, $true)
    [IO.File]::Delete($temporary)
}

$graphicsPolicySource = Join-Path $PSScriptRoot 'src\Shared\RdpGraphicsPolicies.cs'
$stringsSource = Join-Path $PSScriptRoot 'src\ChildSessionDesktop\Strings.cs'
$childSources = @(
    $graphicsPolicySource,
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\Program.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\ConnectionArguments.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\PasswordDialog.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\MaintenanceProfile.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\MaintenanceClient.cs'),
    $stringsSource,
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\WindowsChrome.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\SessionHeader.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\ClipboardFileRelay.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\DesktopForm.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\DesktopForm.Sessions.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\DesktopForm.Files.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\DesktopPreferences.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\LogPane.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\SessionWorkspace.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\RemoteDesktop.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\RdpFailure.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\RemoteConnectionDialog.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\RdpConnection.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\SessionThumbnails.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\GpuPreview.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\GpuSettingsDialog.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\GraphicsOptions.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\GraphicsSettingsDialog.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\FileTransfer.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\ShellClipboard.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\ClipboardRestrictions.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\Geometry.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\ClipboardProtocol.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\ReconnectPolicy.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\ChildSessionControl.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\ToolbarControl.cs'),
    (Join-Path $PSScriptRoot 'src\ChildSessionDesktop\TooltipForm.cs')
)
$childManifest = Join-Path $PSScriptRoot 'src\ChildSessionDesktop\app.manifest'
$executableIcon = Join-Path $PSScriptRoot 'assets\RayXDesktop_executable.ico'
$interfaceIcon = Join-Path $PSScriptRoot 'assets\RayXDesktop_interface.ico'
$banner = Join-Path $PSScriptRoot 'assets\RayXDesktop-banner.jpg'
$languageSource = Join-Path $PSScriptRoot 'languages'
$languageResources = @(Get-ChildItem -LiteralPath $languageSource -File -Filter '*.xml' | ForEach-Object {
    "/resource:$($_.FullName),RayXDesktop.Language.$($_.BaseName),private"
})
$childTarget = Join-Path $output 'RayXDesktop.exe'
$childTemporary = Join-Path $output 'RayXDesktop.build.exe'

$version = '1.6.0.4'
$productVersion = '1.6.0-beta.5'
$productName = 'Ray' + [char]0x02E3 + 'Desktop'
$childAssemblyInfo = Join-Path ([IO.Path]::GetTempPath()) ('RayDesktopAssemblyInfo-' + [Guid]::NewGuid().ToString('N') + '.cs')
[IO.File]::WriteAllText($childAssemblyInfo,
    "using System.Reflection;`r`n[assembly: AssemblyTitle(`"$productName`")]`r`n[assembly: AssemblyCompany(`"D.Synx`")]`r`n[assembly: AssemblyProduct(`"$productName`")]`r`n[assembly: AssemblyVersion(`"$version`")]`r`n[assembly: AssemblyFileVersion(`"$version`")]`r`n[assembly: AssemblyInformationalVersion(`"$productVersion`")]`r`n",
    [Text.Encoding]::UTF8)
$childSources = @($childSources) + @($childAssemblyInfo)
# setup binary gets the same injected version (P1-9-2 version check as unsigned-helper fallback)
$setupAssemblyInfo = Join-Path ([IO.Path]::GetTempPath()) ('RayDesktopSetupAssemblyInfo-' + [Guid]::NewGuid().ToString('N') + '.cs')
[IO.File]::WriteAllText($setupAssemblyInfo,
    "using System.Reflection;`r`n[assembly: AssemblyTitle(`"$productName Setup`")]`r`n[assembly: AssemblyProduct(`"$productName`")]`r`n[assembly: AssemblyVersion(`"$version`")]`r`n[assembly: AssemblyFileVersion(`"$version`")]`r`n[assembly: AssemblyInformationalVersion(`"$productVersion`")]`r`n",
    [Text.Encoding]::UTF8)

# ---- AxImp.exe detection: registry-installed SDK roots + RAYDESKTOP_SDK_ROOT override + standard dirs ----
# Reads the real install root from the registry (works even when the SDK lives on a custom drive/path),
# honors an optional RAYDESKTOP_SDK_ROOT override, and keeps standard Program Files as a fallback.
$aximp = $null
$sdkRoots = New-Object System.Collections.Generic.List[string]
if ($env:RAYDESKTOP_SDK_ROOT) { $sdkRoots.Add($env:RAYDESKTOP_SDK_ROOT) }
foreach ($reg in @(
    'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots',
    'HKLM:\SOFTWARE\Microsoft\Microsoft SDKs\Windows',
    'HKLM:\SOFTWARE\Wow6432Node\Microsoft\Microsoft SDKs\Windows')) {
    try {
        $item = Get-ItemProperty -LiteralPath $reg -ErrorAction Stop
        foreach ($prop in @('KitsRoot10', 'KitsRoot81', 'CurrentInstallFolder')) {
            $val = $item.$prop
            if ($val) { $sdkRoots.Add([string]$val) }
        }
    }
    catch { }
}
foreach ($root in @(
    'C:\Program Files (x86)\Windows Kits\10\bin',
    'C:\Program Files (x86)\Microsoft SDKs\Windows',
    'C:\Program Files\Microsoft SDKs\Windows')) {
    $sdkRoots.Add($root)
}
$seen = @{}
foreach ($root in $sdkRoots) {
    if (-not $root -or $seen.ContainsKey($root)) { continue }
    $seen[$root] = $true
    if (-not [IO.Directory]::Exists($root)) { continue }
    $candidates = $null
    try {
        $candidates = @([IO.Directory]::GetFiles($root, 'AxImp.exe', [IO.SearchOption]::AllDirectories))
    }
    catch {
        continue
    }
    if ($candidates.Length -gt 0) {
        $candidates = @($candidates | Sort-Object -Descending)
        $aximp = $candidates[0]   # pick highest-version hit, avoid old version / wrong arch
        break
    }
}
if (-not $aximp) {
    throw 'Could not find AxImp.exe under the Windows SDK directories. Install the Windows SDK (which includes the .NET Framework 4.x tools) and re-run this script.'
}

$interopDirectory = Join-Path ([IO.Path]::GetTempPath()) ('RayDesktopInterop-' + [Guid]::NewGuid().ToString('N'))
$axInterop = Join-Path $interopDirectory 'AxInterop.MSTSCLib.dll'
$interop = Join-Path $interopDirectory 'MSTSCLib.dll'
$setupSource = Join-Path $PSScriptRoot 'src\ChildSessionSetup\Program.cs'
$setupManifest = Join-Path $PSScriptRoot 'src\ChildSessionSetup\app.manifest'
$setupTarget = Join-Path $output 'EnableChildSessions.exe'
$setupTemporary = Join-Path $output 'EnableChildSessions.build.exe'
$setupReferences = @('/r:System.dll', '/r:System.Windows.Forms.dll', '/r:System.Xml.Linq.dll')

New-Item -ItemType Directory -Force -Path $interopDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $output | Out-Null
try {
    & $aximp /nologo /silent ("/out:{0}" -f $axInterop) (Join-Path $env:WINDIR 'System32\mstscax.dll')
    if ($LASTEXITCODE -ne 0) { throw "RDP ActiveX wrapper generation failed with exit code $LASTEXITCODE" }

    $childReferences = $references + $wpfReferences + @(("/r:{0}" -f $interop), ("/r:{0}" -f $axInterop))
    $childResources = @(
        ("/win32icon:{0}" -f $executableIcon),
        ("/resource:{0},RayXDesktop.InterfaceIcon,private" -f $interfaceIcon),
        ("/resource:{0},RayXDesktop.Banner,private" -f $banner),
        ("/win32manifest:{0}" -f $childManifest)
    ) + $languageResources
    & $compiler /nologo /target:winexe /optimize+ /platform:anycpu /debug- ("/out:{0}" -f $childTemporary) @childResources @childReferences @childSources
    if ($LASTEXITCODE -ne 0) { throw "Child-session GUI build failed with exit code $LASTEXITCODE" }

    & $compiler /nologo /target:winexe /optimize+ /platform:anycpu /debug- ("/out:{0}" -f $setupTemporary) ("/win32icon:{0}" -f $executableIcon) ("/win32manifest:{0}" -f $setupManifest) @setupReferences @languageResources $setupSource $graphicsPolicySource $stringsSource $setupAssemblyInfo
    if ($LASTEXITCODE -ne 0) { throw "Child-session setup helper build failed with exit code $LASTEXITCODE" }

    Publish-BuildOutput $childTemporary $childTarget
    Publish-BuildOutput $setupTemporary $setupTarget
    Copy-Item -LiteralPath $interop -Destination (Join-Path $output 'MSTSCLib.dll') -Force
    Copy-Item -LiteralPath $axInterop -Destination (Join-Path $output 'AxInterop.MSTSCLib.dll') -Force
    $languageOutput = Join-Path $output 'languages'
    New-Item -ItemType Directory -Force -Path $languageOutput | Out-Null
    Get-ChildItem -LiteralPath $languageSource -File -Filter '*.xml' | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $languageOutput -Force
    }
    Write-Host 'Built RayXDesktop.exe, EnableChildSessions.exe, and the two RDP interop dependencies.'
}
finally {
    foreach ($temporary in @($childTemporary, $setupTemporary, $childAssemblyInfo, $setupAssemblyInfo)) {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
    if (Test-Path -LiteralPath $interopDirectory) {
        Get-ChildItem -LiteralPath $interopDirectory -Force | ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Force
        }
        Remove-Item -LiteralPath $interopDirectory -Force
    }
}

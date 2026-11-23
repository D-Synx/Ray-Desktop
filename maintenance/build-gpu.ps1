param(
    [Parameter(Mandatory = $true)][string]$VulkanHeaders,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'Raydesktop' }
$include = Join-Path (Get-Item -LiteralPath $VulkanHeaders).FullName 'include'
if (-not (Test-Path -LiteralPath (Join-Path $include 'vulkan\vulkan.h'))) {
    throw 'VulkanHeaders must point to a Khronos Vulkan-Headers checkout.'
}

if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio Build Tools with Desktop development with C++.' }
    $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'MSVC x64 build tools were not found.' }
    Import-Module (Join-Path $vs 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
    Enter-VsDevShell -VsInstallPath $vs -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'
}
if ($env:VSCMD_ARG_TGT_ARCH -ne 'x64') { throw 'Use an x64 Visual Studio Developer PowerShell.' }

$output = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$target = Join-Path $output 'RayXDesktop.GpuPreview.exe'
$work = Join-Path ([IO.Path]::GetTempPath()) ('RayXGpu-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$sources = 'main.cpp','d3d11.cpp','vulkan.cpp' | ForEach-Object { Join-Path $root "src\GpuPreview\$_" }
Push-Location $work
try {
    & cl.exe /nologo /W4 /O2 /EHsc /std:c++17 /MT /D_WIN32_WINNT=0x0A00 "/I$include" @sources d3d11.lib d2d1.lib dxguid.lib /link /DEPENDENTLOADFLAG:0x800
    if ($LASTEXITCODE -ne 0) { throw "GPU helper build failed with exit code $LASTEXITCODE." }
    Copy-Item -LiteralPath .\main.exe -Destination $target -Force
    Write-Host "GPU helper: $target"
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $work -Recurse -Force
}

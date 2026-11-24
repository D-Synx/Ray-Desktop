$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Could not find csc.exe. Install .NET Framework 4.x or Visual Studio Build Tools.'
}

$root = $PSScriptRoot
$testOut = Join-Path $root 'RayDesktopTests.exe'
$sources = @(
    (Join-Path $root 'RayDesktopTests\Program.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\ConnectionArguments.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\FileTransfer.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\RdpConnection.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\RdpFailure.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\DesktopPreferences.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\GpuPreview.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\LogPane.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\ToolbarControl.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\TooltipForm.cs'),
    (Join-Path $PSScriptRoot '..\src\Shared\RdpGraphicsPolicies.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\Strings.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\Geometry.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\GraphicsOptions.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\ClipboardProtocol.cs'),
    (Join-Path $PSScriptRoot '..\src\ChildSessionDesktop\ReconnectPolicy.cs')
)

$languageSource = Join-Path $PSScriptRoot '..\languages'
$languageResources = @(
    ("/resource:{0},RayXDesktop.Language.en,private" -f (Join-Path $languageSource 'en.xml')),
    ("/resource:{0},RayXDesktop.Language.fr,private" -f (Join-Path $languageSource 'fr.xml'))
)

& $compiler /nologo /target:exe /r:System.Xml.Linq.dll @languageResources /r:System.Security.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Accessibility.dll ("/out:{0}" -f $testOut) @sources
if ($LASTEXITCODE -ne 0) { throw "Test build failed with exit code $LASTEXITCODE" }

& $testOut
exit $LASTEXITCODE

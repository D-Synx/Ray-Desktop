param([Parameter(Mandatory = $true)][string]$Directory)

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
    throw 'The portable UI checks require an STA thread.'
}
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $Directory 'RayXDesktop.exe'))
$setup = [Reflection.Assembly]::LoadFrom((Join-Path $Directory 'EnableChildSessions.exe'))
$instance = [Reflection.BindingFlags]'Instance,NonPublic'

foreach ($tag in @('de', 'en', 'es', 'fr', 'it', 'pl', 'pt-br')) {
    $name = "RayXDesktop.Language.$tag"
    $expected = [IO.File]::ReadAllText((Join-Path $Directory "languages\$tag.xml"))
    foreach ($executable in @($assembly, $setup)) {
        $stream = $executable.GetManifestResourceStream($name)
        if ($null -eq $stream) { throw "Missing $name in $($executable.GetName().Name)." }
        $reader = [IO.StreamReader]::new($stream)
        try {
            if ($reader.ReadToEnd() -cne $expected) { throw "Packaged and embedded $tag translations differ." }
        }
        finally { $reader.Dispose() }
    }
}

$window = [Activator]::CreateInstance($assembly.GetType('ChildSessionDesktop.ChromeForm', $true), $true)
$desktop = [Activator]::CreateInstance($assembly.GetType('ChildSessionDesktop.ChildSessionControl', $true), $true)
$workspace = $null
try {
    $type = $assembly.GetType('ChildSessionDesktop.SessionWorkspace', $true)
    $engine = [Enum]::Parse($assembly.GetType('ChildSessionDesktop.PreviewEngine', $true), 'Windows')
    $workspace = $type.GetConstructors($instance)[0].Invoke(@($desktop, 'UiSmokeTest', $engine))
    # Keep the live RDP control out of this check. The rest is the actual packaged workspace.
    $desktop.Parent.Controls.Remove($desktop)
    $window.AutoScaleMode = [Windows.Forms.AutoScaleMode]::None
    $window.ClientSize = [Drawing.Size]::new(900, 720)
    $window.Controls.Add($workspace)
    $window.Show()

    $type.GetMethod('ToggleSidebar', $instance).Invoke($workspace, @())
    $header = $type.GetField('header', $instance).GetValue($workspace)
    $sidebar = $type.GetField('sidebar', $instance).GetValue($workspace)
    $list = $type.GetField('list', $instance).GetValue($workspace)
    $band = $type.GetField('toolbarBand', $instance).GetValue($workspace)
    $setDpi = $type.GetMethod('SetSidebarDpi', $instance)
    $band.Height = 56
    $band.Visible = $true
    [Windows.Forms.Application]::DoEvents()
    $header.Child.UpdateLayout()
    if (-not $header.Visible -or $header.Child.ActualHeight -le 0) { throw 'The WPF header failed to lay out.' }

    $window.ClientSize = [Drawing.Size]::new(500, 250)
    [Windows.Forms.Application]::DoEvents()
    if ($header.Visible -or $list.Height -lt $list.Controls[0].Height) {
        throw 'The compact layout does not leave room for one full session card.'
    }

    $window.ClientSize = [Drawing.Size]::new(1600, 1000)
    $setDpi.Invoke($workspace, @(240))
    [Windows.Forms.Application]::DoEvents()
    # The runner's screen can constrain the requested window size, triggering compact mode.
    $expanded = $sidebar.ClientSize.Height -ge (3 * 105 + 160 + 135 + 90)
    if ($header.Visible -ne $expanded -or $header.Height -ne 160 -or $list.Controls[0].Height -ne 135) {
        throw "Unexpected 250% layout: sidebar $($sidebar.ClientSize.Height), header $($header.Height) visible $($header.Visible), card $($list.Controls[0].Height)."
    }
    Write-Host 'Verified both sets of embedded packs and the real portable workspace at normal, compact and simulated high DPI sizes.'
}
finally {
    $window.Dispose()
    if ($null -ne $workspace -and -not $workspace.IsDisposed) { $workspace.Dispose() }
    $desktop.Dispose()
}

<#
.SYNOPSIS
    Regenerates the README screenshots in docs\.

.DESCRIPTION
    Publishes the app, launches it on each panel page via the --show* development flags, and
    captures the panel window exactly - no shadow, no desktop margin, so the images match the
    540-pixel width the README lays out for.

    Only one copy of SonarTray runs at a time, so any instance already running is closed first
    and NOT restarted afterwards.

.PARAMETER Language
    "tr" or "en". Written to settings.json before launching, so the captures match the README
    they are for. The previous settings file is restored when the script finishes.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Capture-Screenshots.ps1 -Language tr
#>
[CmdletBinding()]
param(
    [ValidateSet('tr', 'en')]
    [string]$Language = 'tr',

    [string]$OutputDirectory = 'docs'
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
# The panel sets ShowInTaskbar=False, so Process.MainWindowHandle stays null and the window has
# to be found by enumerating the process's own top-level windows instead.
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class Win {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    /// <summary>PW_RENDERFULLCONTENT: asks the window to draw itself, desktop excluded.</summary>
    public const uint PW_RENDERFULLCONTENT = 2;

    /// <summary>Visible top-level windows of the process whose title matches exactly.</summary>
    public static IntPtr FindWindow(uint processId, string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam) {
            uint owner;
            GetWindowThreadProcessId(hWnd, out owner);
            if (owner != processId || !IsWindowVisible(hWnd)) return true;

            var text = new StringBuilder(256);
            GetWindowTextW(hWnd, text, text.Capacity);
            if (text.ToString() != title) return true;

            found = hWnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
'@

$repo = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $env:LOCALAPPDATA 'SonarTray\settings.json'
$backupPath = "$settingsPath.capture-backup"

function Stop-SonarTray {
    Get-Process SonarTray -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  stopping the running instance (pid $($_.Id))"
        $_.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 400
        if (-not $_.HasExited) { $_.Kill() }
    }
    Start-Sleep -Milliseconds 400
}

function Capture-Panel {
    param(
        [string]$Flag,
        [string]$FileName,
        [string]$WindowTitle = 'Sonar Mixer',

        # The on-screen display has rounded corners and sits over the desktop, so a screen grab
        # picks up the wallpaper through them. PrintWindow asks the window to redraw itself
        # instead, which keeps the corners the window's own colour.
        [switch]$PrintWindow
    )

    $exe = Join-Path $repo 'bin\Release\net8.0-windows\win-x64\publish\SonarTray.exe'
    $process = Start-Process -FilePath $exe -ArgumentList $Flag -PassThru

    try {
        # The panel fades in; give it time to settle before grabbing pixels.
        $handle = [IntPtr]::Zero
        foreach ($attempt in 1..40) {
            Start-Sleep -Milliseconds 250
            $handle = [Win]::FindWindow($process.Id, $WindowTitle)
            if ($handle -ne [IntPtr]::Zero) { break }
        }

        if ($handle -eq [IntPtr]::Zero) { throw "the panel never appeared for $Flag" }

        Start-Sleep -Milliseconds 700   # let the open animation finish

        $rect = New-Object Win+RECT
        [void][Win]::GetWindowRect($handle, [ref]$rect)
        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top

        $bitmap = New-Object System.Drawing.Bitmap $width, $height
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

        if ($PrintWindow) {
            $hdc = $graphics.GetHdc()
            $ok = [Win]::PrintWindow($handle, $hdc, [Win]::PW_RENDERFULLCONTENT)
            $graphics.ReleaseHdc($hdc)
            if (-not $ok) { throw "PrintWindow failed for $Flag" }
        }
        else {
            $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        }

        $target = Join-Path $repo "$OutputDirectory\$FileName"
        $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)

        $graphics.Dispose()
        $bitmap.Dispose()

        Write-Host ("  {0,-22} {1}x{2}" -f $FileName, $width, $height)
    }
    finally {
        if (-not $process.HasExited) {
            $process.CloseMainWindow() | Out-Null
            Start-Sleep -Milliseconds 400
            if (-not $process.HasExited) { $process.Kill() }
        }
        Start-Sleep -Milliseconds 300
    }
}

Write-Host "Publishing..."
& dotnet publish (Join-Path $repo 'SonarTray.csproj') -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true --nologo -v q | Out-Null

Stop-SonarTray

if (Test-Path $settingsPath) { Copy-Item $settingsPath $backupPath -Force }

try {
    $settings = if (Test-Path $settingsPath) { Get-Content $settingsPath -Raw | ConvertFrom-Json } else { [PSCustomObject]@{} }
    $settings | Add-Member -NotePropertyName Language -NotePropertyValue $Language -Force
    $settings | ConvertTo-Json | Set-Content $settingsPath -Encoding utf8

    Write-Host "Capturing ($Language):"
    $suffix = if ($Language -eq 'en') { '.en' } else { '' }
    Capture-Panel '--show'          "panel$suffix.png"
    Capture-Panel '--show-settings' "settings$suffix.png"
    Capture-Panel '--show-hotkeys'  "hotkeys$suffix.png"
    Capture-Panel '--show-osd'      "osd$suffix.png" 'SonarTray OSD' -PrintWindow
}
finally {
    if (Test-Path $backupPath) {
        Move-Item $backupPath $settingsPath -Force
        Write-Host "Settings restored."
    }
}

Write-Host "Done. SonarTray is not running; start it again yourself if you had it open."

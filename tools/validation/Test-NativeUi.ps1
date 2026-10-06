<#
.SYNOPSIS
Checks the published/production process through Windows UI Automation using isolated data.
.DESCRIPTION
Does not send physical keyboard or mouse input while the desktop is locked. Pattern invocation, modal
cancellation, state labels, process exit, and DPI-awareness are real OS paths, not mocked dialogs.
#>
param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$Artifacts)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PfrDpi {
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool AreDpiAwarenessContextsEqual(IntPtr left, IntPtr right);
}
'@
$Artifacts=[IO.Path]::GetFullPath($Artifacts)
$profile=Join-Path $Artifacts ('profiles/native-'+[guid]::NewGuid().ToString('N'))
$photos=Join-Path $Artifacts 'data/photos'
$records=[Collections.Generic.List[object]]::new()
function Find-Element($root,[string]$id) {
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id)
    $element=$root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
    if (!$element) { throw "Automation element missing: $id" }
    return $element
}
function Invoke-Element($root,[string]$id) {
    $element=Find-Element $root $id
    $pattern=$element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Start-Sleep -Milliseconds 250
}
$app=Start-Process -FilePath $Executable -ArgumentList @('--data-dir',('"'+$profile+'"'),'--folder',('"'+$photos+'"')) -WindowStyle Hidden -PassThru
try {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do { Start-Sleep -Milliseconds 100; $app.Refresh() } while (!$app.HasExited -and ($app.MainWindowHandle -eq [IntPtr]::Zero) -and $watch.Elapsed.TotalSeconds -lt 30)
    if ($app.HasExited -or ($app.MainWindowHandle -eq [IntPtr]::Zero)) { throw 'Process did not expose its folder window' }
    $root=[Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
    $dpi=[PfrDpi]::GetDpiForWindow($app.MainWindowHandle)
    $v2=[PfrDpi]::AreDpiAwarenessContextsEqual([PfrDpi]::GetWindowDpiAwarenessContext($app.MainWindowHandle),[IntPtr](-4))
    $records.Add([pscustomobject]@{Id='Native.Startup.Dpi'; Passed=$v2; Dpi=$dpi; PerMonitorV2=$v2; Title=$root.Current.Name})
    Start-Sleep -Seconds 2
    Invoke-Element $root 'FolderModeWindow.PhotoCard'
    $rating=Find-Element $root 'FolderModeWindow.SetRatingCommand.5'
    $rating.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 600
    $sessions=Get-ChildItem (Join-Path $profile 'Sessions') -Filter session.json -Recurse
    $saved=Get-Content $sessions[0].FullName -Raw | ConvertFrom-Json
    $records.Add([pscustomobject]@{Id='Native.Folder.Rating5'; Passed=(@($saved.Photos | Where-Object Rating -eq 5).Count -eq 1); Evidence='Actual UI InvokePattern and persisted session JSON'})
    Invoke-Element $root 'FolderModeWindow.OpenKeyboardShortcutsCommand'
    $dialogCondition=[Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$app.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'キーボードショートカット設定'))
    $dialogWatch=[Diagnostics.Stopwatch]::StartNew()
    do {
        $dialog=[Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants,$dialogCondition)
        if (!$dialog) { Start-Sleep -Milliseconds 100 }
    } while (!$dialog -and $dialogWatch.Elapsed.TotalSeconds -lt 10)
    if (!$dialog) { throw 'Shortcut modal did not open' }
    Invoke-Element $dialog 'KeyboardShortcutsWindow.Cancel_Click'
    $cancelWatch=[Diagnostics.Stopwatch]::StartNew()
    while (!$root.Current.IsEnabled -and $cancelWatch.Elapsed.TotalSeconds -lt 5) { Start-Sleep -Milliseconds 100 }
    $records.Add([pscustomobject]@{Id='Native.Shortcuts.Cancel'; Passed=!(Test-Path (Join-Path $profile 'folder-shortcuts.json')); Evidence='Actual native modal cancellation'})
    $app.CloseMainWindow() | Out-Null
    $exited=$app.WaitForExit(5000)
    $records.Add([pscustomobject]@{Id='Native.Exit'; Passed=($exited -and $app.ExitCode -eq 0)})
    $records | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Artifacts 'native-results.json') -Encoding utf8
    if (@($records | Where-Object {!$_.Passed}).Count) { throw 'Native assertion failed; inspect native-results.json' }
    Write-Output 'Native UI assertions passed'
}
catch {
    $records.Add([pscustomobject]@{Id='Native.Error'; Passed=$false; Evidence=$_.Exception.Message})
    $records | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Artifacts 'native-results.json') -Encoding utf8
    throw
}
finally {
    $app.Refresh()
    if (!$app.HasExited) { Stop-Process -Id $app.Id }
}

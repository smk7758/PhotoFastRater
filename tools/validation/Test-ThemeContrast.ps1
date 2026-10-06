<#
.SYNOPSIS
Calculates the project's palette contrast goals; does not replace rendered-state or human checks.
.DESCRIPTION
Uses sRGB relative luminance. Text targets 4.5:1, primary boundaries target 3:1. Disabled opacity,
third-party templates, image backgrounds, focus, and physical DPI need separate screen inspection.
#>
param([string]$Output = 'artifacts/ui-validation/theme-contrast.json')
$ErrorActionPreference = 'Stop'
[xml]$theme = Get-Content -LiteralPath src/PhotoFastRater.UI/Themes/DarkroomTheme.xaml -Raw
$colors = @{}
foreach ($brush in $theme.SelectNodes('//*[local-name()="SolidColorBrush"]')) {
    $colors[$brush.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')] = $brush.GetAttribute('Color')
}
function Get-Luminance([string]$color) {
    $channels = @(1,3,5 | ForEach-Object {
        $value = [Convert]::ToInt32($color.Substring($_,2),16)/255.0
        if ($value -le 0.04045) { $value/12.92 } else { [Math]::Pow(($value+0.055)/1.055,2.4) }
    })
    return 0.2126*$channels[0]+0.7152*$channels[1]+0.0722*$channels[2]
}
$checks = @(
    @('DarkroomPaperBrush','DarkroomInkBrush',4.5),
    @('DarkroomPaperBrush','DarkroomSlateBrush',4.5),
    @('DarkroomPaperBrush','DarkroomRaisedBrush',4.5),
    @('DarkroomMutedBrush','DarkroomSlateBrush',4.5),
    @('DarkroomMutedBrush','DarkroomRaisedBrush',4.5),
    @('DarkroomInkBrush','RatingAmberBrush',4.5),
    @('RejectRedBrush','DarkroomSlateBrush',4.5),
    @('DarkroomDividerBrush','DarkroomRaisedBrush',3.0)
)
$results = foreach($check in $checks) {
    $first=Get-Luminance $colors[$check[0]]; $second=Get-Luminance $colors[$check[1]]
    $ratio=([Math]::Max($first,$second)+0.05)/([Math]::Min($first,$second)+0.05)
    [pscustomobject]@{Foreground=$check[0];Background=$check[1];Ratio=$ratio;Minimum=$check[2];Passed=($ratio -ge $check[2])}
}
$results | ConvertTo-Json | Set-Content -LiteralPath $Output -Encoding utf8
$results | Format-Table Foreground,Background,@{Name='Ratio';Expression={'{0:F2}' -f $_.Ratio}},Passed
if($results.Passed -contains $false) { exit 1 }

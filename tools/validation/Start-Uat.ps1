<#
.SYNOPSIS
Opens the interactive validation build with copied photos and an isolated user profile.
.DESCRIPTION
Reuses the UAT profile so restart checks retain ratings and settings. Never points at the normal
user profile. Run UIValidation prepare-uat and publish first; no original photograph is modified.
#>
param([switch]$FolderMode)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifacts = Join-Path $repository 'artifacts/ui-validation'
$executable = Join-Path $artifacts 'profiles/publish/PhotoFastRater.UI.exe'
$profile = Join-Path $artifacts 'profiles/uat-user'
$pathFile = Join-Path $artifacts 'uat-data-path.txt'
if (!(Test-Path -LiteralPath $executable) -or !(Test-Path -LiteralPath $pathFile)) {
    throw '検証版のpublishとUIValidation prepare-uatが必要です。BUILD_NOTES.mdを参照してください。'
}
$photos = [IO.Path]::GetFullPath([IO.File]::ReadAllText($pathFile).Trim())
$allowedData = [IO.Path]::GetFullPath((Join-Path $artifacts 'data')) + [IO.Path]::DirectorySeparatorChar
if (!$photos.StartsWith($allowedData, [StringComparison]::OrdinalIgnoreCase) -or !(Test-Path -LiteralPath $photos)) {
    throw 'UAT素材の保存先が専用データ領域の外にあるか、存在しません。'
}
$arguments = @('--data-dir', '"' + $profile + '"')
if ($FolderMode) { $arguments += @('--folder', '"' + $photos + '"') }
Write-Output "検証プロファイル: $profile"
Write-Output "取り込むコピー素材: $photos"
Start-Process -FilePath $executable -ArgumentList $arguments

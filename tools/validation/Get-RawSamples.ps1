<#
.SYNOPSIS
Obtains individually licensed CC0 samples for reproducible embedded-preview tests.
.DESCRIPTION
Records the published and actual digest; original downloads are never edited or committed.
#>
param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$rows = (Invoke-RestMethod 'https://raw.pixls.us/json/getrepository.php').data
$manifest = @()
foreach ($extension in @('CR2','CR3','NEF','ARW','DNG','ORF','RAF','RW2','RAW')) {
    $candidates = foreach ($row in $rows) {
        if ($row[5] -notmatch 'publicdomain/zero/1.0' -or $row[7] -notmatch "\.$extension</a>") { continue }
        $url = [regex]::Match($row[7], "href='([^']+)'").Groups[1].Value
        $expected = [regex]::Match($row[7], "sha256 Checksum'>([a-f0-9]{64})").Groups[1].Value
        $sizeMatch = [regex]::Match($row[7], '\(([\d.]+)MB\)')
        $size = if ($sizeMatch.Success) { [double]::Parse($sizeMatch.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) } else { [double]::PositiveInfinity }
        [pscustomobject]@{Url=$url; ExpectedSha256=$expected; Make=$row[0]; Camera=$row[1]; Mode=$row[2]; SizeMB=$size}
    }
    $sample = $candidates | Sort-Object SizeMB | Select-Object -First 1
    if (!$sample) {
        $manifest += [pscustomobject]@{Format=$extension; Status='Unavailable'; Reason='No CC0 entry found'}
        continue
    }
    $path = Join-Path $Destination "sample.$extension"
    try {
        if (!(Test-Path -LiteralPath $path)) { Invoke-WebRequest $sample.Url -OutFile $path }
        $digest = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($digest -ne $sample.ExpectedSha256) { throw "Digest mismatch for $extension" }
        $manifest += [pscustomobject]@{Format=$extension; Status='Downloaded'; File=$path; Url=$sample.Url; License='CC0-1.0'; Camera="$($sample.Make) $($sample.Camera)"; Mode=$sample.Mode; SizeBytes=(Get-Item -LiteralPath $path).Length; Sha256=$digest}
        Write-Output "Downloaded $extension ($($sample.SizeMB) MB)"
    }
    catch {
        $manifest += [pscustomobject]@{Format=$extension; Status='Unavailable'; Url=$sample.Url; Reason=$_.Exception.Message}
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Destination 'manifest.json') -Encoding utf8
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Destination 'manifest.json') -Encoding utf8

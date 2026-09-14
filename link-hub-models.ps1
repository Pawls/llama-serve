# Hardlinks GGUFs from the Hugging Face hub cache into the flat <pub>\<repo> library that
# LM Studio and presets.ini read. Idempotent and never destructive: existing targets of the
# same size are skipped, size mismatches are reported and left alone. Dry run unless -Apply.
param(
    [switch]$Apply,
    [string]$Hub  = 'F:\AI\models\llm\hub',
    [string]$Flat = 'F:\AI\models\llm',
    # Not chat models.
    [string[]]$Exclude = @('unslothai/Qwen3-ASR-0.6B-GGUF')
)
$ErrorActionPreference = 'Stop'

foreach ($repoDir in Get-ChildItem $Hub -Directory -Filter 'models--*') {
    $parts = $repoDir.Name -split '--', 3
    if ($parts.Count -ne 3) { continue }
    $pub, $repo = $parts[1], $parts[2]
    if ("$pub/$repo" -in $Exclude) { Write-Host "exclude  $pub/$repo"; continue }

    $snapRoot = Join-Path $repoDir.FullName 'snapshots'
    $refFile  = Join-Path $repoDir.FullName 'refs\main'
    if (-not (Test-Path $snapRoot)) { continue }
    $main = if (Test-Path $refFile) { (Get-Content $refFile -Raw).Trim() } else { $null }

    # A snapshot only holds the files that were downloaded at that revision, so a quant fetched
    # before main moved lives only in an older snapshot. Main wins; otherwise the newest snapshot.
    $snapshots = Get-ChildItem $snapRoot -Directory |
        Sort-Object @{ e = { $_.Name -eq $main }; Descending = $true }, @{ e = 'LastWriteTime'; Descending = $true }
    $chosen = [ordered]@{}
    foreach ($snap in $snapshots) {
        foreach ($f in Get-ChildItem $snap.FullName -Recurse -File -Filter *.gguf -Force) {
            $rel = $f.FullName.Substring($snap.FullName.Length + 1)
            if (-not $chosen.Contains($rel)) { $chosen[$rel] = @{ Entry = $f; Rev = $snap.Name } }
        }
    }
    if ($chosen.Count -eq 0) { continue }

    # Reuse an existing flat folder that differs only by the -GGUF suffix, so LM Studio
    # does not list the same model twice.
    $dest = Join-Path $Flat "$pub\$repo"
    $alt  = Join-Path $Flat "$pub\$($repo -replace '-GGUF$', '')"
    if (-not (Test-Path $dest) -and $alt -ne $dest -and (Test-Path $alt)) { $dest = $alt }

    foreach ($rel in $chosen.Keys) {
        $entry = $chosen[$rel].Entry
        $rev   = $chosen[$rel].Rev
        $note  = if ($rev -eq $main) { '' } else { "  (non-main revision $($rev.Substring(0, 7)))" }

        $source = if ($entry.LinkType -eq 'SymbolicLink') {
            [IO.Path]::GetFullPath((Join-Path $entry.DirectoryName $entry.Target))
        } elseif ($entry.LinkType) {
            Write-Warning "unsupported link type $($entry.LinkType): $($entry.FullName)"; continue
        } else { $entry.FullName }

        if ([IO.Path]::GetPathRoot($source) -ne [IO.Path]::GetPathRoot($dest)) {
            throw "cross-volume source, refusing to continue: $source"
        }
        $srcItem = Get-Item $source
        if ($srcItem.LinkType) { throw "blob is itself a link, refusing to continue: $source" }

        $target = Join-Path $dest $rel
        if (Test-Path $target) {
            $tLen = (Get-Item $target).Length
            if ($tLen -eq $srcItem.Length) { Write-Host "skip     $target$note" }
            else { Write-Warning ("MISMATCH $target  flat={0:n0}  hub={1:n0}{2}" -f $tLen, $srcItem.Length, $note) }
            continue
        }

        Write-Host ("link     {0}  ({1:n1} GiB){2}" -f $target, ($srcItem.Length / 1GB), $note)
        if ($Apply) {
            New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
            New-Item -ItemType HardLink -Path $target -Target $source | Out-Null
        }
    }
}
if (-not $Apply) { Write-Host "`nDry run. Re-run with -Apply to create the links." }

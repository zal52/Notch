param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $gitArgs = @('-c', "safe.directory=$($root.Replace('\','/'))")
    $files = @(git @gitArgs ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository files.' }
    $findings = [System.Collections.Generic.List[string]]::new()
    $badPath = '(^|/)(\.env($|\.)|\.tools/|artifacts/|dist/|bin/|obj/|Previews/|ClipboardHistory/|\.codex/|\.agents/)|\.(pfx|p12|pem|key|dmp|dump|log|zip|7z)$|(^|/)(secrets|settings)\.json($|\.)'
    $secret = '-----BEGIN ([A-Z ]+ )?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9_-]{24,}|(api[_-]?key|password|secret|token)[[:space:]]*[:=][[:space:]]*["''][^"'']{8,}["'']'
    foreach ($file in $files) {
        if ($file -match $badPath -and $file -notmatch '(^|/)\.env\.example$') { $findings.Add("Excluded/private file: $file") }
        if (Test-Path -LiteralPath $file -PathType Leaf) {
            if ((Get-Item -LiteralPath $file).Length -gt 2MB) { $findings.Add("Review large file: $file") }
        }
    }
    $hits = @(git @gitArgs grep -I -l -i -E --untracked -e $secret -- .)
    if ($LASTEXITCODE -gt 1) { throw 'Working-tree scan failed.' }
    foreach ($hit in $hits) { $findings.Add("Possible secret: $hit") }
    $commits = @(git @gitArgs rev-list --all)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate history.' }
    foreach ($commit in $commits) {
        $hits = @(git @gitArgs grep -I -l -i -E -e $secret $commit -- .)
        if ($LASTEXITCODE -gt 1) { throw 'History scan failed.' }
        foreach ($hit in $hits) { $findings.Add("Possible historical secret: $hit") }
        $paths = @(git @gitArgs ls-tree -r --name-only $commit)
        if ($LASTEXITCODE -ne 0) { throw 'History path scan failed.' }
        foreach ($path in $paths) {
            if ($path -match $badPath -and $path -notmatch '(^|/)\.env\.example$') { $findings.Add("Private path in history: ${commit}:$path") }
        }
    }
    if ($findings.Count -gt 0) {
        $findings | Sort-Object -Unique | Write-Output
        throw 'Publication check needs review. Secret values were not printed.'
    }
    Write-Output "Checked $($files.Count) candidate files and $($commits.Count) commits: no matches for configured secret/private-file patterns. Manual review is still required."
} finally { Pop-Location }

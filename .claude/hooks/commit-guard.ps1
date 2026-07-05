# commit-guard - PreToolUse hook for Bash|PowerShell.
# Blocks `git commit` while untracked source files exist. Reason: commit f34887c exists
# solely because a new .cs file was forgotten from its commit (lesson L-001 in
# .claude/harness/LESSONS.md). The SDK csproj auto-globs *.cs, so git add is the ONLY
# registration step and forgetting it is silent until someone builds from that commit.
# Bypass: append the token [allow-untracked] to the commit command when exclusion is
# intentional, and state the reason in the handoff report.
# Design: fail-open (any internal error => exit 0). Exit 2 blocks and feeds stderr to the model.
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $payload = $raw | ConvertFrom-Json
    $cmd = [string]$payload.tool_input.command
    if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }
    # Match "git [flags] commit" incl. `git -C <path> commit`, `git --no-pager commit`.
    if ($cmd -notmatch '\bgit(\s+-{1,2}\S+(\s+\S+)?)*\s+commit\b') { exit 0 }
    if ($cmd -match '\[allow-untracked\]') { exit 0 }

    $repo = $env:CLAUDE_PROJECT_DIR
    if ([string]::IsNullOrWhiteSpace($repo)) { $repo = 'D:\Github\Inventory_Kamera' }
    # -uall: list files inside untracked directories individually (a bare
    # `?? .claude/` directory line would otherwise hide every file within it).
    $lines = & git -C $repo status --porcelain -uall
    if (-not $lines) { exit 0 }

    $sourceExt = '\.(cs|csproj|sln|resx|settings|config|props|targets|json|ps1|md|yml|yaml)$'
    $untracked = @($lines | Where-Object {
        $_ -match '^\?\?' -and ($_ -replace '^\?\?\s+', '') -match $sourceExt -and $_ -notmatch '\.bak$'
    } | ForEach-Object { $_ -replace '^\?\?\s+', '' })

    if ($untracked.Count -eq 0) { exit 0 }

    $shown = $untracked | Select-Object -First 10
    $msg = "[commit-guard] Blocked: untracked source files exist and would be silently left out of this commit (this exact mistake produced fix-up commit f34887c):`n"
    $msg += ($shown | ForEach-Object { "  ?? $_" }) -join "`n"
    if ($untracked.Count -gt 10) { $msg += "`n  ... and $($untracked.Count - 10) more" }
    $msg += "`nUsual fix: git add the files that belong to this change (CLAUDE.md iron rule 3)."
    $msg += "`nIf exclusion is INTENTIONAL: re-run with the bypass token appended as a comment,"
    $msg += "`n  e.g. git commit -m `"...`" # [allow-untracked]"
    $msg += "`nand state the reason in your handoff report. Hook: .claude/hooks/commit-guard.ps1"
    [Console]::Error.WriteLine($msg)
    exit 2
} catch {
    exit 0
}

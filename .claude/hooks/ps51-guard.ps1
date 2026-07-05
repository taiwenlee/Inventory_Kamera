# ps51-guard - PreToolUse hook for the PowerShell tool.
# This machine's PowerShell is 5.1: '&&' and '||' are parser errors, and weak models
# write them by reflex, fail, and retry with partial state. Block BEFORE execution.
# Design: fail-open (any internal error => exit 0). Exit 2 blocks the tool call and
# feeds stderr back to the model. See .claude/harness/diagnostic-report.md Leak #3.
try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $payload = $raw | ConvertFrom-Json
    if ($payload.tool_name -ne 'PowerShell') { exit 0 }
    $cmd = [string]$payload.tool_input.command
    if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }
    if ($cmd -match '&&|\|\|') {
        [Console]::Error.WriteLine("[ps51-guard] Blocked: this machine's PowerShell is 5.1 - '&&' and '||' are parser errors there. Rewrite with ';' or 'if (`$?) { ... }', or use the Bash tool for chained commands (preferred, per CLAUDE.md iron rule 6). Hook: .claude/hooks/ps51-guard.ps1")
        exit 2
    }
    exit 0
} catch {
    exit 0
}

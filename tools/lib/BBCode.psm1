# Me turn our Markdown (README subset) into Nexus Mods BBCode. Keep this file ASCII (PS 5.1 read it as ANSI).
# Supported: # headings, **bold**, *italic*, `code`, [text](url), ![img](url), - / 1. lists, > quote, ``` blocks,
# --- rule, tables (turned into lists: Nexus BBCode has no tables).
Set-StrictMode -Version 3.0

function Convert-Inline([string]$text) {
    if ($null -eq $text) { return '' }
    # Me protect `code` first so its inside no get bold/italic.
    $codes = New-Object System.Collections.Generic.List[string]
    $text = [regex]::Replace($text, '`([^`]+)`', { param($m) $codes.Add($m.Groups[1].Value); "@@CODE$($codes.Count - 1)@@" })
    # Images, then links. Relative links (repo paths) mean nothing on Nexus: keep only the text.
    $text = [regex]::Replace($text, '!\[([^\]]*)\]\((https?://[^)\s]+)\)', '[img]$2[/img]')
    $text = [regex]::Replace($text, '\[([^\]]+)\]\((https?://[^)\s]+)\)', '[url=$2]$1[/url]')
    $text = [regex]::Replace($text, '\[([^\]]+)\]\(([^)\s]+)\)', '$1')
    $text = [regex]::Replace($text, '\*\*(.+?)\*\*', '[b]$1[/b]')
    $text = [regex]::Replace($text, '(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])', '[i]$1[/i]')
    $text = [regex]::Replace($text, '(?<![\w_])_(?!\s)(.+?)(?<!\s)_(?![\w_])', '[i]$1[/i]')
    for ($i = 0; $i -lt $codes.Count; $i++) { $text = $text.Replace("@@CODE$i@@", "[font=Courier New]$($codes[$i])[/font]") }
    $text
}

function Split-TableRow([string]$line) {
    $l = $line.Trim()
    if ($l.StartsWith('|')) { $l = $l.Substring(1) }
    if ($l.EndsWith('|')) { $l = $l.Substring(0, $l.Length - 1) }
    @($l -split '(?<!\\)\|' | ForEach-Object { $_.Trim() -replace '\\\|', '|' })
}

# Me render table rows as list items. Config tables (Section|Setting|Default|Description) get a nice shape.
function Convert-Table([string[]]$rows) {
    $header = Split-TableRow $rows[0]
    $out = New-Object System.Collections.Generic.List[string]
    $out.Add('[list]')
    $isConfig = ($header.Count -eq 4 -and $header[0] -eq 'Section' -and $header[1] -eq 'Setting')
    foreach ($row in $rows | Select-Object -Skip 2) {
        $c = Split-TableRow $row
        if ($isConfig -and $c.Count -ge 4) {
            $out.Add("[*][b]$($c[0]).$($c[1])[/b] (default: $(Convert-Inline $c[2])): $(Convert-Inline $c[3])")
        } else {
            $first = Convert-Inline $c[0]
            $rest = @($c | Select-Object -Skip 1 | ForEach-Object { Convert-Inline $_ }) -join ' - '
            if ($rest) { $out.Add("[*][b]$first[/b] - $rest") } else { $out.Add("[*][b]$first[/b]") }
        }
    }
    $out.Add('[/list]')
    $out
}

function ConvertTo-NexusBBCode([string]$Markdown) {
    $lines = ($Markdown -replace "`r", '') -split "`n"
    $out = New-Object System.Collections.Generic.List[string]
    $i = 0
    while ($i -lt $lines.Count) {
        $line = $lines[$i]

        if ($line -match '^```') {
            $block = New-Object System.Collections.Generic.List[string]
            $i++
            while ($i -lt $lines.Count -and $lines[$i] -notmatch '^```') { $block.Add($lines[$i]); $i++ }
            $out.Add('[code]' + ($block -join "`n") + '[/code]')
            $i++; continue
        }
        if ($line -match '^(#{1,6})\s+(.*)$') {
            $size = @{ 1 = 6; 2 = 5; 3 = 4 }[$Matches[1].Length]
            if (-not $size) { $size = 3 }
            $out.Add("[size=$size][b]$(Convert-Inline $Matches[2])[/b][/size]")
            $i++; continue
        }
        if ($line -match '^\s*(-{3,}|\*{3,})\s*$') { $out.Add('[line]'); $i++; continue }
        if ($line -match '^\s*\|' -and $i + 1 -lt $lines.Count -and $lines[$i + 1] -match '^\s*\|?\s*:?-{3,}') {
            $rows = New-Object System.Collections.Generic.List[string]
            while ($i -lt $lines.Count -and $lines[$i] -match '^\s*\|') { $rows.Add($lines[$i]); $i++ }
            foreach ($l in Convert-Table $rows) { $out.Add($l) }
            continue
        }
        if ($line -match '^\s*([-*]|\d+\.)\s+') {
            $ordered = $line -match '^\s*\d+\.'
            $out.Add($(if ($ordered) { '[list=1]' } else { '[list]' }))
            while ($i -lt $lines.Count -and ($lines[$i] -match '^\s*([-*]|\d+\.)\s+' -or ($lines[$i] -match '^\s{2,}\S' -and $out.Count -gt 0))) {
                if ($lines[$i] -match '^\s*(?:[-*]|\d+\.)\s+(.*)$') { $out.Add('[*]' + (Convert-Inline $Matches[1])) }
                else { $out[$out.Count - 1] += ' ' + (Convert-Inline $lines[$i].Trim()) } # wrapped line of same item
                $i++
            }
            $out.Add('[/list]')
            continue
        }
        if ($line -match '^>\s?(.*)$') {
            $quote = New-Object System.Collections.Generic.List[string]
            while ($i -lt $lines.Count -and $lines[$i] -match '^>\s?(.*)$') { $quote.Add((Convert-Inline $Matches[1])); $i++ }
            $out.Add('[quote]' + ($quote -join "`n") + '[/quote]')
            continue
        }
        # Plain paragraph: Markdown glue soft-wrapped lines into one; me do same so Nexus no break mid-sentence.
        if ($line.Trim().Length -gt 0) {
            $para = (Convert-Inline $line.Trim())
            $i++
            while ($i -lt $lines.Count -and $lines[$i].Trim().Length -gt 0 -and $lines[$i] -notmatch '^(#|```|\s*[-*]\s|\s*\d+\.\s|>|\s*\|)') {
                $para += ' ' + (Convert-Inline $lines[$i].Trim()); $i++
            }
            $out.Add($para)
            continue
        }
        $out.Add('')
        $i++
    }
    ($out -join "`n").Trim()
}

# Me cut a Markdown doc into ("## Title", body) pieces so callers can drop/replace sections.
function Split-MarkdownSections([string]$Markdown) {
    $sections = New-Object System.Collections.Generic.List[object]
    $current = [pscustomobject]@{ Title = ''; Lines = New-Object System.Collections.Generic.List[string] }
    foreach ($line in (($Markdown -replace "`r", '') -split "`n")) {
        if ($line -match '^## (.+)$') {
            $sections.Add($current)
            $current = [pscustomobject]@{ Title = $Matches[1].Trim(); Lines = New-Object System.Collections.Generic.List[string] }
        }
        $current.Lines.Add($line)
    }
    $sections.Add($current)
    $sections
}

Export-ModuleMember -Function ConvertTo-NexusBBCode, Split-MarkdownSections

# Me turn our Markdown (README subset) into Nexus Mods BBCode. Keep this file ASCII (PS 5.1 read it as ANSI).
# Supported: # headings, **bold**, *italic*, ***both***, `code`, [text](https-url), ![img](https-url) (relative images
# dropped: Nexus needs absolute URLs), - / * / 1. lists (nested items folded into parent: Nexus breaks on nested
# [list]), > quote, ``` blocks (also inside list items), --- rule, tables (turned into lists: Nexus has no tables).
Set-StrictMode -Version 3.0

function Convert-Inline([string]$text) {
    if ($null -eq $text) { return '' }
    # Me hide things the bold/italic rules must not touch behind @@Pn@@ placeholders, put them back at the end.
    $keep = New-Object System.Collections.Generic.List[string]
    $text = [regex]::Replace($text, '`([^`]+)`', { param($m) $keep.Add("[font=Courier New]$($m.Groups[1].Value)[/font]"); "@@P$($keep.Count - 1)@@" })
    $text = [regex]::Replace($text, '\\([\\`*_\[\]()#!|>-])', { param($m) $keep.Add($m.Groups[1].Value); "@@P$($keep.Count - 1)@@" })
    $url = '(https?://(?:[^()\s]|\([^()\s]*\))+)'
    $text = [regex]::Replace($text, "!\[[^\]]*\]\($url\)", { param($m) $keep.Add("[img]$($m.Groups[1].Value)[/img]"); "@@P$($keep.Count - 1)@@" })
    $text = [regex]::Replace($text, '!\[[^\]]*\]\([^)\s]*\)', '')
    $text = [regex]::Replace($text, "\[([^\]]+)\]\($url\)", { param($m) $keep.Add("[url=$($m.Groups[2].Value)]"); "@@P$($keep.Count - 1)@@$($m.Groups[1].Value)[/url]" })
    # Relative links (repo paths) mean nothing on Nexus: keep only the text.
    $text = [regex]::Replace($text, '\[([^\]]+)\]\(([^)\s]+)\)', '$1')
    $text = [regex]::Replace($text, '\*\*\*(?!\s)(.+?)(?<!\s)\*\*\*', '[b][i]$1[/i][/b]')
    $text = [regex]::Replace($text, '\*\*(.+?)\*\*', '[b]$1[/b]')
    $text = [regex]::Replace($text, '(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])', '[i]$1[/i]')
    $text = [regex]::Replace($text, '(?<![\w_])_(?!\s)(.+?)(?<!\s)_(?![\w_])', '[i]$1[/i]')
    # Reverse order: a placeholder can sit inside another one's text.
    for ($i = $keep.Count - 1; $i -ge 0; $i--) { $text = $text.Replace("@@P$i@@", $keep[$i]) }
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
    $header = @(Split-TableRow $rows[0])
    $out = New-Object System.Collections.Generic.List[string]
    $out.Add('[list]')
    $isConfig = ($header.Count -eq 4 -and $header[0] -eq 'Section' -and $header[1] -eq 'Setting')
    foreach ($row in $rows | Select-Object -Skip 2) {
        $c = @(Split-TableRow $row)
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

$script:ListItem = '^(\s*)([-*+]|\d+\.)\s+(.*)$'
$script:BlockStart = '^(#|\s{0,3}```|\s*[-*+]\s|\s*\d+\.\s|>|\s*\|)'

# -DropTitle: skip the first "# Title" line (the Nexus page already shows the mod name).
function ConvertTo-NexusBBCode([string]$Markdown, [switch]$DropTitle) {
    $lines = @(($Markdown -replace "`r", '') -split "`n")
    if ($DropTitle) {
        $first = [array]::FindIndex($lines, [Predicate[string]] { param($l) $l.Trim().Length -gt 0 })
        if ($first -ge 0 -and $lines[$first] -match '^#\s') { $lines = @($lines | Select-Object -Skip ($first + 1)) }
    }
    $out = New-Object System.Collections.Generic.List[string]
    $i = 0
    while ($i -lt $lines.Count) {
        $line = $lines[$i]

        if ($line -match '^\s{0,3}```') {
            $block = New-Object System.Collections.Generic.List[string]
            $i++
            while ($i -lt $lines.Count -and $lines[$i] -notmatch '^\s{0,3}```') { $block.Add($lines[$i]); $i++ }
            $out.Add('[code]' + ($block -join "`n") + '[/code]')
            $i++; continue
        }
        # Nexus has no heading tags; its own template use [size=4][b]. H1 5, H2 4, deeper = bold only.
        if ($line -match '^(#{1,6})\s+(.*)$') {
            $size = @{ 1 = 5; 2 = 4 }[$Matches[1].Length]
            $inner = "[b]$(Convert-Inline $Matches[2])[/b]"
            if ($size) { $inner = "[size=$size]$inner[/size]" }
            $out.Add($inner)
            $i++; continue
        }
        if ($line -match '^\s*(-{3,}|\*{3,})\s*$') { $out.Add('[line]'); $i++; continue }
        if ($line -match '^\s*\|' -and $i + 1 -lt $lines.Count -and $lines[$i + 1] -match '^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$') {
            $rows = New-Object System.Collections.Generic.List[string]
            while ($i -lt $lines.Count -and $lines[$i] -match '^\s*\|') { $rows.Add($lines[$i]); $i++ }
            foreach ($l in Convert-Table $rows.ToArray()) { $out.Add($l) }
            continue
        }
        $lm = [regex]::Match($line, $script:ListItem)
        if ($lm.Success) {
            # Me use Match objects, not -match: a second -match overwrite $Matches.
            $ordered = [char]::IsDigit($lm.Groups[2].Value[0])
            $baseIndent = $lm.Groups[1].Value.Length
            $items = New-Object System.Collections.Generic.List[string]
            $blocks = New-Object System.Collections.Generic.List[string]
            while ($i -lt $lines.Count) {
                $l = $lines[$i]
                $im = [regex]::Match($l, $script:ListItem)
                if ($im.Success) {
                    $isOrdered = [char]::IsDigit($im.Groups[2].Value[0])
                    # Nested or other-type item: fold into parent (no nested [list] on Nexus).
                    if ($items.Count -gt 0 -and ($im.Groups[1].Value.Length -gt $baseIndent -or $isOrdered -ne $ordered)) { $items[$items.Count - 1] += ' - ' + $im.Groups[3].Value }
                    else { $items.Add($im.Groups[3].Value) }
                    $i++; continue
                }
                if ($l -match '^\s+```' -and $items.Count -gt 0) {
                    # Fenced block inside the item: keep it in the item so numbering goes on.
                    $block = New-Object System.Collections.Generic.List[string]
                    $i++
                    while ($i -lt $lines.Count -and $lines[$i] -notmatch '^\s*```') { $block.Add($lines[$i].Trim()); $i++ }
                    $blocks.Add('[code]' + ($block -join "`n") + '[/code]')
                    $items[$items.Count - 1] += " @@BLOCK$($blocks.Count - 1)@@"
                    $i++; continue
                }
                if ($l -match '^\s{2,}\S' -and $items.Count -gt 0) { $items[$items.Count - 1] += ' ' + $l.Trim(); $i++; continue }
                if ($l.Trim().Length -eq 0) {
                    # Loose list: blank lines inside it. Go on only if the list itself goes on.
                    $j = $i
                    while ($j -lt $lines.Count -and $lines[$j].Trim().Length -eq 0) { $j++ }
                    if ($j -lt $lines.Count) {
                        $nm = [regex]::Match($lines[$j], $script:ListItem)
                        if ($nm.Success -and ($nm.Groups[1].Value.Length -gt $baseIndent -or [char]::IsDigit($nm.Groups[2].Value[0]) -eq $ordered)) { $i = $j; continue }
                        if (-not $nm.Success -and $lines[$j] -match '^\s{2,}\S') { $i = $j; continue }
                    }
                }
                break
            }
            $out.Add($(if ($ordered) { '[list=1]' } else { '[list]' }))
            foreach ($it in $items) {
                $text = Convert-Inline $it
                for ($b = 0; $b -lt $blocks.Count; $b++) { $text = $text.Replace("@@BLOCK$b@@", $blocks[$b]) }
                $out.Add('[*]' + $text)
            }
            $out.Add('[/list]')
            continue
        }
        if ($line -match '^>\s?(.*)$') {
            $quote = New-Object System.Collections.Generic.List[string]
            while ($i -lt $lines.Count -and $lines[$i] -match '^>\s?(.*)$') { $quote.Add($Matches[1].Trim()); $i++ }
            $out.Add('[quote]' + (Convert-Inline ($quote -join ' ')) + '[/quote]')
            continue
        }
        # Paragraph: join soft-wrapped lines FIRST, then convert once, so bold/links across a wrap still work.
        if ($line.Trim().Length -gt 0) {
            $para = $line.Trim()
            $i++
            while ($i -lt $lines.Count -and $lines[$i].Trim().Length -gt 0 -and $lines[$i] -notmatch $script:BlockStart) {
                $para += ' ' + $lines[$i].Trim(); $i++
            }
            $out.Add((Convert-Inline $para))
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

# Me make changelog text Nexus-ready: one plain line per entry (Nexus draw the bullets itself).
function Format-NexusChangelog([string]$Markdown) {
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($l in (($Markdown -replace "`r", '') -split "`n")) {
        if ($l -match '^\s*$' -or $l -match '^#') { continue }
        if ($l -match '^\s*[-*+]\s+(.*)$') { $items.Add($Matches[1].Trim()) }
        elseif ($items.Count) { $items[$items.Count - 1] += ' ' + $l.Trim() }
        else { $items.Add($l.Trim()) }
    }
    ($items | ForEach-Object { $_ -replace '\*\*|__|`', '' }) -join "`n"
}

Export-ModuleMember -Function ConvertTo-NexusBBCode, Split-MarkdownSections, Format-NexusChangelog

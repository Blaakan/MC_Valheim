<#
.SYNOPSIS
    Regression tests for the tooling itself (no game needed): every script parses and is ASCII, the Markdown ->
    Nexus BBCode converter handles known edge cases, the TESTING.md parser, changelog formatting, the ModIdea split.
    Run after changing anything in tools/. Exit code 0 = pass.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\Common.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'lib\BBCode.psm1') -Force

$fails = 0
function Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    if ($Ok) { Write-Ok $Name } else { Write-Fail "$Name $Detail"; $script:fails++ }
}
function Same([string]$Name, [string]$Actual, [string]$Expected) {
    Check $Name ($Actual -ceq $Expected) "`n      expected: $($Expected -replace "`n", '\n')`n      actual:   $($Actual -replace "`n", '\n')"
}
$nl = "`n"

Write-Step 'Scripts'
Check 'all tool scripts parse and are ASCII' (Test-ScriptsParse)

Write-Step 'BBCode inline'
Same 'bold, italic, code' (ConvertTo-NexusBBCode 'a **b** *c* `d_e*f*`') 'a [b]b[/b] [i]c[/i] [font=Courier New]d_e*f*[/font]'
Same 'bold+italic nest properly' (ConvertTo-NexusBBCode '***very***') '[b][i]very[/i][/b]'
Same 'underscore inside word untouched' (ConvertTo-NexusBBCode 'MC_Valheim and snake_case') 'MC_Valheim and snake_case'
Same 'link with parentheses' (ConvertTo-NexusBBCode '[wiki](https://x.com/Crossbow_(weapon))') '[url=https://x.com/Crossbow_(weapon)]wiki[/url]'
Same 'no italic inside URLs' (ConvertTo-NexusBBCode '[a](https://x.com/_a) and [b](https://y.com/b_)') '[url=https://x.com/_a]a[/url] and [url=https://y.com/b_]b[/url]'
Same 'relative link keeps text' (ConvertTo-NexusBBCode '[doc](docs/x.md)') 'doc'
Same 'relative image dropped' (ConvertTo-NexusBBCode 'see ![shot](screens/a.png) here') 'see  here'
Same 'absolute image' (ConvertTo-NexusBBCode '![s](https://i.com/a.png)') '[img]https://i.com/a.png[/img]'
Same 'escaped underscore' (ConvertTo-NexusBBCode '\_x\_') '_x_'

Write-Step 'BBCode blocks'
Same 'bold across soft wrap' (ConvertTo-NexusBBCode "keep **the mod${nl}updated** now") 'keep [b]the mod updated[/b] now'
Same 'title dropped' (ConvertTo-NexusBBCode "# T${nl}${nl}Body" -DropTitle) 'Body'
Same 'headings' (ConvertTo-NexusBBCode "## A${nl}### B") "[size=4][b]A[/b][/size]${nl}[b]B[/b]"
Same 'loose numbered list stays one list' (ConvertTo-NexusBBCode "1. a${nl}${nl}2. b${nl}${nl}3. c") "[list=1]${nl}[*]a${nl}[*]b${nl}[*]c${nl}[/list]"
Same 'nested items folded into parent' (ConvertTo-NexusBBCode "1. First:${nl}   - x${nl}   - y${nl}2. Second") "[list=1]${nl}[*]First: - x - y${nl}[*]Second${nl}[/list]"
Same 'code block inside item keeps numbering' (ConvertTo-NexusBBCode "1. Open:${nl}${nl}   ``````${nl}   a/b.cfg${nl}   ``````${nl}${nl}2. Edit") "[list=1]${nl}[*]Open: [code]a/b.cfg[/code]${nl}[*]Edit${nl}[/list]"
Same 'wrapped list item with link' (ConvertTo-NexusBBCode "- [see the${nl}  wiki](https://w.com) for **every${nl}  bow**") "[list]${nl}[*][url=https://w.com]see the wiki[/url] for [b]every bow[/b]${nl}[/list]"
Same 'one-column table' (ConvertTo-NexusBBCode "| Name |${nl}|---|${nl}| Alpha |") "[list]${nl}[*][b]Alpha[/b]${nl}[/list]"
Same 'short GFM delimiter' (ConvertTo-NexusBBCode "| A | B |${nl}|:--|:-:|${nl}| x | y |") "[list]${nl}[*][b]x[/b] - y${nl}[/list]"
Same 'config table' (ConvertTo-NexusBBCode "| Section | Setting | Default | Description |${nl}|---|---|---|---|${nl}| General | Enabled | ``true`` | On/off. |") "[list]${nl}[*][b]General.Enabled[/b] (default: [font=Courier New]true[/font]): On/off.${nl}[/list]"
Same 'fenced block' (ConvertTo-NexusBBCode "``````${nl}a${nl}  b${nl}``````") "[code]a${nl}  b[/code]"
Same 'quote' (ConvertTo-NexusBBCode "> hello${nl}> **world**") '[quote]hello [b]world[/b][/quote]'

Write-Step 'Changelog'
Same 'nexus changelog lines' (Format-NexusChangelog ('- **Fix** the `thing`.' + $nl + '- Second entry' + $nl + '  wrapped.')) "Fix the thing.${nl}Second entry wrapped."

Write-Step 'TESTING.md parser'
$tmp = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($tmp, '')
    Check 'empty file gives no items (no crash)' (@(Get-TestItems $tmp).Count -eq 0)
    [IO.File]::WriteAllText($tmp, "## S${nl}- [x] **T01 a**${nl}* [ ] **T02 b**${nl}  wrapped${nl}- [~] **T03 c**${nl}- [!] **T04 d**${nl}- [-] **T05 e**${nl}+ [X] **T06 f**")
    $items = @(Get-TestItems $tmp)
    Check 'all bullet styles parsed' ($items.Count -eq 6) "got $($items.Count)"
    Check 'unknown mark counts as todo' (($items | Where-Object Id -eq 'T03').Mark -eq ' ')
    Check 'uppercase X counts as pass' (($items | Where-Object Id -eq 'T06').Mark -eq 'x')
    Check 'wrapped line joined' (($items | Where-Object Id -eq 'T02').Text -match 'wrapped$')
} finally { Remove-Item $tmp -Force }

Write-Step 'ModIdea'
Same 'several ideas split and trimmed' ((Split-ModIdea ' Trinket revamp ; Adrenaline revamp;') -join '|') 'Trinket revamp|Adrenaline revamp'
Check 'several ideas give an array' (@(Split-ModIdea 'A;B').Count -eq 2)
Same 'single idea kept whole' ((Split-ModIdea 'Sneak revamp') -join '|') 'Sneak revamp'
Check 'single idea gives one item' (@(Split-ModIdea 'Sneak revamp').Count -eq 1)
Check 'empty text gives no idea' (@(Split-ModIdea '').Count -eq 0)
Check 'blank text gives no idea' (@(Split-ModIdea ' ; ').Count -eq 0)
Check 'null gives no idea' (@(Split-ModIdea $null).Count -eq 0)

Write-Host ''
if ($fails -eq 0) { Write-Host 'TOOL TESTS PASSED' -ForegroundColor Green; exit 0 }
Write-Host "TOOL TESTS FAILED ($fails)" -ForegroundColor Red
exit 1

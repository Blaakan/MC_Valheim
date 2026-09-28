# Me draw placeholder 256x256 mod icon: category color + feature initials. Real art replace it later.
Set-StrictMode -Version 3.0

$script:CategoryColors = @{
    Combat      = '#A83232'
    Exploration = '#2F6FA3'
    Farming     = '#4E8A3A'
    Cooking     = '#C27C2C'
    Building    = '#7A5A3A'
    Crafting    = '#5E6472'
    UX          = '#6A4C9C'
    Core        = '#333333'
}

function Get-FeatureInitials([string]$FeatureId) {
    # Me split "Crossbow.StaysLoaded" -> Crossbow, Stays, Loaded -> "CSL".
    $words = [regex]::Matches($FeatureId, '[A-Z][a-z0-9]*|[a-z0-9]+') | ForEach-Object { $_.Value }
    $initials = ($words | ForEach-Object { $_.Substring(0, 1).ToUpper() }) -join ''
    if ($initials.Length -gt 4) { $initials = $initials.Substring(0, 4) }
    $initials
}

function New-ModIcon([string]$Path, [string]$Category, [string]$FeatureId) {
    Add-Type -AssemblyName System.Drawing
    $hex = $script:CategoryColors[$Category]
    if (-not $hex) { $hex = '#333333' }
    $bg = [System.Drawing.ColorTranslator]::FromHtml($hex)
    $bmp = New-Object System.Drawing.Bitmap 256, 256
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'AntiAliasGridFit'
        $g.Clear($bg)
        $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 0, 0, 0))
        $g.FillRectangle($dark, 0, 196, 256, 60)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 255, 255, 255)), 6
        $g.DrawRectangle($pen, 3, 3, 249, 249)
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
        $white = [System.Drawing.Brushes]::White
        $initials = Get-FeatureInitials $FeatureId
        $size = 96
        if ($initials.Length -ge 4) { $size = 72 }
        $big = New-Object System.Drawing.Font 'Segoe UI', $size, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $g.DrawString($initials, $big, $white, (New-Object System.Drawing.RectangleF 0, 10, 256, 186), $fmt)
        $small = New-Object System.Drawing.Font 'Segoe UI', 28, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $g.DrawString($Category.ToUpper(), $small, $white, (New-Object System.Drawing.RectangleF 0, 196, 256, 60), $fmt)
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $g.Dispose(); $bmp.Dispose()
    }
}

Export-ModuleMember -Function New-ModIcon, Get-FeatureInitials

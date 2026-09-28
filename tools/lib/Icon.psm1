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

# Me draw placeholder page banner: category-colored gradient, big title, summary, small footer label.
# Real screenshots are better; this just make sure every page has a clean primary image.
function New-ModBanner([string]$Path, [string]$Title, [string]$Subtitle, [string]$Category, [string]$Footer,
                       [int]$Width = 1280, [int]$Height = 720) {
    Add-Type -AssemblyName System.Drawing
    $hex = $script:CategoryColors[$Category]
    if (-not $hex) { $hex = '#333333' }
    $base = [System.Drawing.ColorTranslator]::FromHtml($hex)
    $dark = [System.Drawing.Color]::FromArgb(255, [int]($base.R * 0.35), [int]($base.G * 0.35), [int]($base.B * 0.35))
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = 'AntiAlias'
        $g.TextRenderingHint = 'AntiAliasGridFit'
        $rect = New-Object System.Drawing.Rectangle 0, 0, $Width, $Height
        $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $base, $dark, 35.0
        $g.FillRectangle($grad, $rect)
        $s = $Height / 720.0
        $white = [System.Drawing.Brushes]::White
        $soft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(215, 255, 255, 255))
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = 'Near'; $fmt.LineAlignment = 'Near'; $fmt.Trimming = 'EllipsisWord'
        $titleFont = New-Object System.Drawing.Font 'Segoe UI', ([float](84 * $s)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $subFont = New-Object System.Drawing.Font 'Segoe UI', ([float](34 * $s)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
        $footFont = New-Object System.Drawing.Font 'Segoe UI', ([float](28 * $s)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        $pad = 80 * $s
        $g.DrawString($Title, $titleFont, $white, (New-Object System.Drawing.RectangleF $pad, (150 * $s), ($Width - 2 * $pad), (210 * $s)), $fmt)
        $g.DrawString($Subtitle, $subFont, $soft, (New-Object System.Drawing.RectangleF $pad, (370 * $s), ($Width - 2 * $pad), (220 * $s)), $fmt)
        $band = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 0, 0, 0))
        $g.FillRectangle($band, 0, [int]($Height - 90 * $s), $Width, [int](90 * $s))
        $g.DrawString($Footer, $footFont, $white, (New-Object System.Drawing.RectangleF $pad, ($Height - 70 * $s), ($Width - 2 * $pad), (60 * $s)), $fmt)
        $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $g.Dispose(); $bmp.Dispose()
    }
}

Export-ModuleMember -Function New-ModIcon, Get-FeatureInitials, New-ModBanner


Add-Type -AssemblyName System.Drawing
$pngPath = Resolve-Path "icon.png"
if (Test-Path $pngPath) {
    Write-Host "Found png at $pngPath"
    $bmp = [System.Drawing.Bitmap]::FromFile($pngPath)
    $handle = $bmp.GetHicon()
    $ico = [System.Drawing.Icon]::FromHandle($handle)
    $fs = New-Object System.IO.FileStream("icon.ico", "Create")
    $ico.Save($fs)
    $fs.Close()
    Write-Host "Success: icon.ico created."
} else {
    Write-Host "Error: icon.png not found."
}

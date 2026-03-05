$folder = "d:\Apps\Unity\Project\Live2D"
$files = Get-ChildItem -Path $folder -Filter *.cs -Recurse -File

try {
    # For PowerShell Core/7, we need to register the provider to get GBK.
    # Windows PowerShell 5.1 has it natively.
    [System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
} catch { }

$gbkEncoding = [System.Text.Encoding]::GetEncoding("GBK")
$utf8BomEncoding = New-Object System.Text.UTF8Encoding $true

$count = 0
foreach ($file in $files) {
    try {
        $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
        if ($bytes.Length -eq 0) { continue }
        
        # Check for UTF-8 BOM
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
            continue 
        }
        
        # Check for UTF-16 BOMs
        if ($bytes.Length -ge 2 -and (($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) -or ($bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF))) {
            continue
        }
        
        # Check for non-ASCII characters
        $isAscii = $true
        foreach ($b in $bytes) {
            if ($b -gt 127) {
                $isAscii = $false
                break
            }
        }
        
        # If it's purely ASCII, it doesn't meet the "is GBK/GB2312" condition (as GBK specific chars > 127) 
        # and it's also a valid UTF-8 file. So skip it.
        if ($isAscii) { continue }

        # Check if it is a valid UTF-8 without BOM
        $isUtf8 = $true
        try {
            $strictUtf8 = New-Object System.Text.UTF8Encoding $false, $true
            $null = $strictUtf8.GetString($bytes)
        } catch {
            $isUtf8 = $false
        }
        
        # If it's not valid UTF-8, assume GBK
        if (-not $isUtf8) {
            Write-Host "Converting: $($file.Name) ($($file.DirectoryName))"
            $text = $gbkEncoding.GetString($bytes)
            [System.IO.File]::WriteAllText($file.FullName, $text, $utf8BomEncoding)
            $count++
        }
    } catch {
        Write-Warning "Could not process $($file.FullName) : $_"
    }
}

Write-Host "---"
Write-Host "Scan complete. Converted $count file(s) from GBK/GB2312 to UTF-8 with BOM."

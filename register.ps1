# Registers codeviewer in the Windows "Open with" menu for code file types (HKCU only, no admin).
# - Always added to the Open With dropdown for every listed extension.
# - Becomes the DEFAULT app for extensions that nothing else has claimed.
# - For extensions already owned by another app (Windows UserChoice), pick
#   "Open with > codeviewer > Always" once, or use Settings > Default apps > codeviewer.
# Run unregister.ps1 to undo.

$exe = Join-Path $PSScriptRoot "dist\codeviewer.exe"
if (-not (Test-Path $exe)) { Write-Error "Not found: $exe  (run: dotnet publish -c Release -o dist)"; exit 1 }

$progId = "codeviewer.file"
$extensions = @(
    ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".py", ".pyw", ".java", ".cs",
    ".c", ".h", ".cpp", ".hpp", ".cc", ".go", ".rs", ".php", ".kt", ".proto",
    ".tf", ".tfvars", ".tfstate", ".json", ".jsonc", ".yml", ".yaml", ".xml",
    ".csproj", ".props", ".targets", ".config", ".xaml", ".svg", ".resx", ".plist",
    ".html", ".htm", ".css", ".scss", ".sql", ".sh", ".bash", ".zsh",
    ".ps1", ".psm1", ".psd1", ".md", ".markdown", ".bat", ".cmd",
    ".ini", ".env", ".properties", ".toml", ".editorconfig", ".gitignore", ".mk",
    ".txt", ".log", ".csv"
)

$classes = "HKCU:\Software\Classes"

# ProgID: what "open with codeviewer" means
New-Item -Path "$classes\$progId\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$classes\$progId" -Name "(Default)" -Value "Code File"
New-Item -Path "$classes\$progId\DefaultIcon" -Force | Out-Null
Set-ItemProperty -Path "$classes\$progId\DefaultIcon" -Name "(Default)" -Value "`"$exe`",0"
Set-ItemProperty -Path "$classes\$progId\shell\open\command" -Name "(Default)" -Value "`"$exe`" `"%1`""

# Application registration: makes codeviewer.exe show up in the Open With list by name
$appKey = "$classes\Applications\codeviewer.exe"
New-Item -Path "$appKey\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "$appKey" -Name "FriendlyAppName" -Value "codeviewer"
Set-ItemProperty -Path "$appKey\shell\open\command" -Name "(Default)" -Value "`"$exe`" `"%1`""
New-Item -Path "$appKey\SupportedTypes" -Force | Out-Null
foreach ($ext in $extensions) {
    New-ItemProperty -Path "$appKey\SupportedTypes" -Name $ext -Value "" -PropertyType String -Force | Out-Null
}

# Capabilities + RegisteredApplications: makes it appear in Settings > Default apps
$capKey = "HKCU:\Software\codeviewer\Capabilities"
New-Item -Path "$capKey\FileAssociations" -Force | Out-Null
Set-ItemProperty -Path $capKey -Name "ApplicationName" -Value "codeviewer"
Set-ItemProperty -Path $capKey -Name "ApplicationDescription" -Value "Lightweight code viewer and editor"
foreach ($ext in $extensions) {
    New-ItemProperty -Path "$capKey\FileAssociations" -Name $ext -Value $progId -PropertyType String -Force | Out-Null
}
New-Item -Path "HKCU:\Software\RegisteredApplications" -Force -ErrorAction SilentlyContinue | Out-Null
New-ItemProperty -Path "HKCU:\Software\RegisteredApplications" -Name "codeviewer" `
    -Value "Software\codeviewer\Capabilities" -PropertyType String -Force | Out-Null

# Per extension: add to Open With dropdown; claim as default only where nothing else has
$claimed = @()
foreach ($ext in $extensions) {
    $extKey = "$classes\$ext"
    New-Item -Path "$extKey\OpenWithProgids" -Force | Out-Null
    New-ItemProperty -Path "$extKey\OpenWithProgids" -Name $progId -Value ([byte[]]@()) -PropertyType None -Force | Out-Null

    $current = (Get-Item $extKey).GetValue("")
    if ([string]::IsNullOrEmpty($current) -or $current -eq $progId) {
        Set-ItemProperty -Path $extKey -Name "(Default)" -Value $progId
        $claimed += $ext
    }
}

# tell Explorer associations changed
Add-Type -Namespace Win32 -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);'
[Win32.Shell]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Registered codeviewer for $($extensions.Count) extensions (Open With dropdown everywhere)."
Write-Host "Set as default for: $($claimed -join ' ')"
$skipped = $extensions | Where-Object { $claimed -notcontains $_ }
if ($skipped) {
    Write-Host "Already owned by another app (pick 'Open with > codeviewer > Always' once to switch): $($skipped -join ' ')"
}

# Removes everything register.ps1 added (HKCU only).

$progId = "codeviewer.file"
$classes = "HKCU:\Software\Classes"

foreach ($extKey in Get-ChildItem $classes -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -like ".*" }) {
    $path = $extKey.PSPath
    # drop default association if it points at us
    if ((Get-Item $path).GetValue("") -eq $progId) {
        Remove-ItemProperty -Path $path -Name "(Default)" -ErrorAction SilentlyContinue
    }
    Remove-ItemProperty -Path "$path\OpenWithProgids" -Name $progId -ErrorAction SilentlyContinue
}

Remove-Item -Path "$classes\$progId" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$classes\Applications\codeviewer.exe" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\codeviewer" -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\RegisteredApplications" -Name "codeviewer" -ErrorAction SilentlyContinue

Add-Type -Namespace Win32 -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);'
[Win32.Shell]::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "codeviewer unregistered."

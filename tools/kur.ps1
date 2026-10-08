# Düzeltici'yi derler ve Başlat menüsüne kısayol ekler.
#
# Kısayol imzasız Duzeltici.exe yerine Microsoft imzalı dotnet.exe ile Duzeltici.dll'i çalıştırır:
# Windows'un Akıllı Uygulama Denetimi yeni derlenmiş imzasız exe'leri zaman zaman engelliyor.
# conhost --headless konsol penceresi açılmasını önler.
#
# Kullanım:  powershell -ExecutionPolicy Bypass -File tools\kur.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }

# Çalışıyorsa kapat (dll üzerine yazılabilsin).
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='Duzeltici.exe'" |
    Where-Object { $_.CommandLine -match 'Duzeltici\.(dll|exe)' -and $_.CommandLine -notmatch 'Tests' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

& $dotnet build "$root\src\Duzeltici" -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız" }

$dll = "$root\src\Duzeltici\bin\Release\net10.0-windows\Duzeltici.dll"
$conhost = "$env:SystemRoot\System32\conhost.exe"
$link = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Düzeltici.lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($link)
$shortcut.TargetPath = $conhost
$shortcut.Arguments = "--headless `"$dotnet`" `"$dll`""
$shortcut.WorkingDirectory = Split-Path $dll
$shortcut.IconLocation = "$(Split-Path $dll)\app.ico"
$shortcut.Description = "Yazarken yazım hatalarını düzeltir"
$shortcut.Save()

Start-Process $conhost -ArgumentList "--headless `"$dotnet`" `"$dll`""
Write-Host "Kuruldu. Başlat menüsünde 'Düzeltici'; simge sağ alttaki ^ okunun içinde."

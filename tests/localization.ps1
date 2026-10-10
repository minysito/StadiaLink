param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../.build/localization'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rows = Get-Content "$root/app/assets/locales.tsv" -Encoding UTF8 | Where-Object { $_.Trim() }
$keys = @{}
foreach ($row in $rows) {
    $fields = $row.Split('|')
    if ($fields.Count -ne 4 -or ($fields | Where-Object { -not $_.Trim() })) { throw "Incomplete locale row: $row" }
    if ($keys.ContainsKey($fields[0])) { throw "Duplicate locale key: $($fields[0])" }
    $keys[$fields[0]] = $true
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force $output | Out-Null
$process = Start-Process "$root/release/StadiaLink.exe" -ArgumentList '--languages-test',('"'+$output+'"') -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -or -not (Test-Path "$output/result.txt")) { throw 'Language rendering checks failed. Close any other StadiaLink instance before running.' }
$leaks = Get-ChildItem $output -Filter '*.txt' | Where-Object Name -Match '^(en|fr|de)-' | Select-String -Pattern '\b(Estándar|Mando|Ajustes|Perfiles|Batería|Cargando|Entrada|Salida|Umbral|Sin guardar|Zona muerta|Gatillo izquierdo|Gatillo derecho|Asistente|Preferencias|Conectado por|Recorrido analógico)\b'
if ($leaks) { throw ($leaks | Out-String) }
Get-Content "$output/result.txt"
Write-Output "PASS: $($rows.Count) complete locale entries; no Spanish UI labels in English, French or German."

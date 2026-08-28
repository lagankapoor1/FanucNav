# Build FanucNav for 64-bit Notepad++ and emit a drop-in plugins folder.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$outDir = Join-Path $root "Plugin\FanucNav"
$objDir = Join-Path $root "obj"
$samples = Join-Path $root "samples"
New-Item -ItemType Directory -Force -Path $outDir, $objDir, $samples | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$ilasm = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\ilasm.exe"
$refDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

$files = @(
  "$src\DllExportAttribute.cs",
  "$src\PluginInfrastructure\PluginInfrastructure.cs",
  "$src\Fanuc\Models.cs",
  "$src\Fanuc\LsParser.cs",
  "$src\Fanuc\RobotIdent.cs",
  "$src\Fanuc\ProgramMap.cs",
  "$src\Fanuc\BackupCompare.cs",
  "$src\Fanuc\RobotIndex.cs",
  "$src\Fanuc\MacroTable.cs",
  "$src\Fanuc\RegTable.cs",
  "$src\Forms\RenumberDialog.cs",
  "$src\Forms\UiTheme.cs",
  "$src\Forms\FlowCanvas.cs",
  "$src\Forms\NavPanel.cs",
  "$src\Main.cs",
  "$src\UnmanagedExports.cs"
)

$refs = @(
  "System.dll",
  "System.Core.dll",
  "System.Drawing.dll",
  "System.Windows.Forms.dll",
  "System.IO.Compression.dll",
  "System.IO.Compression.FileSystem.dll"
) | ForEach-Object { "/r:$refDir\$_" }

$rawDll = Join-Path $objDir "FanucNav.raw.dll"
$modelSrc = Join-Path $root "models\r2000ic270f"
$modelDst = Join-Path $outDir "models\r2000ic270f"
if (Test-Path $modelSrc) {
  New-Item -ItemType Directory -Force -Path $modelDst | Out-Null
  Copy-Item "$modelSrc\*" $modelDst -Force
}
Write-Host "Compiling..."
& $csc /nologo /target:library /platform:x64 /optimize+ /unsafe- /define:TRACE `
  /out:$rawDll @refs @files
if ($LASTEXITCODE -ne 0) { throw "csc failed" }

function Find-Ildasm {
  $candidates = @(
    (Get-Command ildasm.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
    "$env:ProgramFiles(x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\x64\ildasm.exe",
    "$env:ProgramFiles(x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\ildasm.exe",
    "$env:ProgramFiles(x86)\Microsoft SDKs\Windows\v8.1A\bin\NETFX 4.5.1 Tools\x64\ildasm.exe"
  )
  foreach ($c in $candidates) {
    if ($c -and (Test-Path $c)) { return $c }
  }
  $pkgDir = Join-Path $objDir "ildasm-pkg"
  $exe = Join-Path $pkgDir "runtimes\win-x64\native\ildasm.exe"
  if (Test-Path $exe) { return $exe }
  $nupkg = Join-Path $objDir "ildasm.nupkg"
  Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/runtime.win-x64.Microsoft.NETCore.ILDAsm/8.0.0" -OutFile $nupkg
  if (Test-Path $pkgDir) { Remove-Item $pkgDir -Recurse -Force }
  New-Item -ItemType Directory -Force -Path $pkgDir | Out-Null
  Copy-Item $nupkg (Join-Path $pkgDir "ildasm.zip")
  Expand-Archive (Join-Path $pkgDir "ildasm.zip") -DestinationPath $pkgDir -Force
  if (Test-Path $exe) { return $exe }
  return $null
}

function Add-Exports([string]$ilPath) {
  $exports = @("isUnicode", "setInfo", "getFuncsArray", "messageProc", "getName", "beNotified")
  $lines = Get-Content $ilPath
  $out = New-Object System.Collections.Generic.List[string]
  $ord = 1
  $pending = $null
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if ($line -match '^\.corflags') { $line = ".corflags 0x00000000" }
    if ($line -match '^\s*\.method ') {
      $last = [Math]::Min($i + 5, $lines.Count - 1)
      $window = ($lines[$i..$last] -join " ")
      foreach ($name in $exports) {
        if ($window -match ("\b" + $name + "\s*\(") -and $window -match "cil managed") {
          $pending = $name
          break
        }
      }
    }
    $out.Add($line)
    if ($pending -and $line -match '^\s*\{\s*$') {
      $out.Add("    .export [$ord] as $pending")
      $ord++
      $pending = $null
    }
  }
  if ($ord -ne 7) { throw "Expected 6 exports, inserted $($ord - 1)" }
  Set-Content -Path $ilPath -Value $out -Encoding ASCII
}

$ildasm = Find-Ildasm
if (-not $ildasm) { throw "ildasm.exe not found. Install .NET SDK / Windows SDK, then rebuild." }

$il = Join-Path $objDir "FanucNav.il"
$res = Join-Path $objDir "FanucNav.res"
Write-Host "Disassembling with $ildasm"
& $ildasm /out=$il /utf8 $rawDll
if ($LASTEXITCODE -ne 0) { throw "ildasm failed" }
Add-Exports $il

$finalDll = Join-Path $outDir "FanucNav.dll"
$ilasmArgs = @("/nologo", "/dll", "/quiet", "/output=$finalDll", "/X64")
if (Test-Path $res) { $ilasmArgs += "/resource=$res" }
$ilasmArgs += $il
Write-Host "Assembling exports..."
& $ilasm @ilasmArgs
if ($LASTEXITCODE -ne 0) { throw "ilasm failed" }

# Smoke-test parser (no NPP needed)
$smokeDll = Join-Path $objDir "FanucNav.Tests.exe"
$testFiles = $files + @(
  "$root\tests\ParserSmoke.cs",
  "$root\tests\SmokeMain.cs"
)
& $csc /nologo /target:exe /platform:x64 /out:$smokeDll @refs @testFiles
if ($LASTEXITCODE -eq 0) {
  & $smokeDll $samples
}

Write-Host ""
Write-Host "Plugin folder ready:"
Write-Host "  $outDir"
Get-Item $finalDll | Format-List FullName, Length, LastWriteTime

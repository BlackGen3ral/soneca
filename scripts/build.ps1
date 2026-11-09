param([string]$Version = '1.3.1', [string]$Dotnet = 'dotnet', [string]$InnoCompiler = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
 & $PSScriptRoot\icon.ps1
 & $Dotnet publish Soneca\Soneca.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o dist\app
 if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar.' }
 $test = Start-Process -FilePath "$root\dist\app\Soneca.exe" -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
 if ($test.ExitCode -ne 0) { throw (Get-Content "$root\dist\app\self-test-result.txt" -Raw) }
 Get-Content "$root\dist\app\self-test-result.txt"
 Remove-Item -LiteralPath "$root\dist\app\self-test-result.txt"
 if (!$InnoCompiler) {
  $InnoCompiler = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "${env:ProgramFiles}\Inno Setup 7\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
 }
 if (!$InnoCompiler) { throw 'Informe o caminho do compilador Inno Setup.' }
 & $InnoCompiler '/Q' "/DAppVersion=$Version" installer\Soneca.iss
 if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar instalador.' }
 $hash = (Get-FileHash dist\Soneca-Setup.exe -Algorithm SHA256).Hash.ToLowerInvariant()
 [IO.File]::WriteAllText((Join-Path $root 'dist\SHA256SUMS.txt'), "$hash  Soneca-Setup.exe`n")
 Copy-Item dist\Soneca-Setup.exe dist\ArthoAI-Setup.exe -Force
 Copy-Item dist\Soneca-Setup.exe dist\ArthoControl-Setup.exe -Force
 [IO.File]::AppendAllText((Join-Path $root 'dist\SHA256SUMS.txt'), "$hash  ArthoAI-Setup.exe`n$hash  ArthoControl-Setup.exe`n")
} finally { Pop-Location }

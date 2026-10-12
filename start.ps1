param([switch]$Build, [switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$qaRoot = $PSScriptRoot
$qaFrontend = Join-Path $qaRoot 'frontend'
$qaApiDirectory = Join-Path $qaRoot 'backend\Financements.QualityApi'
$qaApiDll = Join-Path $qaApiDirectory 'bin\Debug\net10.0-windows\Financements.QualityApi.dll'
$qaPilotDll = Join-Path $qaApiDirectory 'bin\Debug\net10.0-windows\Financements.LegacyPilot.dll'
if ($Build -or !(Test-Path -LiteralPath $qaApiDll) -or !(Test-Path -LiteralPath $qaPilotDll) -or !(Test-Path -LiteralPath (Join-Path $qaFrontend 'dist\index.html'))) {
    Push-Location $qaFrontend
    try {
        & npm.cmd ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Installation npm échouée.' }
        & npm.cmd run build
        if ($LASTEXITCODE -ne 0) { throw 'Build frontend échoué.' }
    } finally { Pop-Location }
    & dotnet build (Join-Path $qaApiDirectory 'Financements.QualityApi.csproj') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build API échoué.' }
}
$qaTokenFile = Join-Path $qaRoot '.local\api-token.txt'
$qaStartedProcess = $null
try {
    $qaConnected = $false
    if (Test-Path -LiteralPath $qaTokenFile) {
        $qaToken = (Get-Content -LiteralPath $qaTokenFile -Raw).Trim()
        try {
            $null = Invoke-RestMethod 'http://127.0.0.1:5178/api/agent/status' -Headers @{Authorization = 'Bearer ' + $qaToken} -TimeoutSec 2
            $qaConnected = $true
        } catch { }
    }
    if (!$qaConnected) {
        $qaStartedProcess = Start-Process -FilePath 'dotnet' -ArgumentList ('"' + $qaApiDll + '"') -WorkingDirectory $qaApiDirectory -WindowStyle Hidden -PassThru
        for ($qaAttempt = 0; $qaAttempt -lt 40; $qaAttempt++) {
            if ($qaStartedProcess.HasExited) { throw 'L’API ne démarre pas. Vérifiez que le port 5178 est libre.' }
            if (Test-Path -LiteralPath $qaTokenFile) {
                $qaToken = (Get-Content -LiteralPath $qaTokenFile -Raw).Trim()
                try {
                    $null = Invoke-RestMethod 'http://127.0.0.1:5178/api/agent/status' -Headers @{Authorization = 'Bearer ' + $qaToken} -TimeoutSec 1
                    $qaConnected = $true
                    break
                } catch { }
            }
            Start-Sleep -Milliseconds 250
        }
    }
    if (!$qaConnected) { throw 'L’API locale ne répond pas.' }
    if (!$NoBrowser) { Start-Process ('http://127.0.0.1:5178/#token=' + $qaToken) }
    Write-Output 'Quality Automation disponible : http://127.0.0.1:5178'
    Write-Output 'Le navigateur utilise le jeton local sans l’envoyer dans l’URL HTTP.'
} catch {
    if ($qaStartedProcess -and !$qaStartedProcess.HasExited) { Stop-Process -Id $qaStartedProcess.Id }
    throw
}

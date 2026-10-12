param([string]$RunId)
$ErrorActionPreference = 'Stop'
$qaRoot = Split-Path $PSScriptRoot -Parent
$qaToken = (Get-Content -LiteralPath (Join-Path $qaRoot '.local\api-token.txt') -Raw).Trim()
$qaHeaders = @{Authorization = 'Bearer ' + $qaToken}
$qaBase = 'http://127.0.0.1:5178'
function Assert-HttpStatus([string]$Url, [int]$Expected, [hashtable]$Headers = @{}) {
    try { $qaResponse = Invoke-WebRequest -Uri $Url -Headers $Headers -UseBasicParsing; $qaStatusCode = [int]$qaResponse.StatusCode }
    catch { if (!$_.Exception.Response) { throw }; $qaStatusCode = [int]$_.Exception.Response.StatusCode }
    if ($qaStatusCode -ne $Expected) { throw "HTTP $qaStatusCode, attendu $Expected pour $Url" }
    Write-Output "OK HTTP $Expected"
}
Assert-HttpStatus "$qaBase/api/scenarios" 401
Assert-HttpStatus "$qaBase/api/scenarios" 403 @{Authorization = 'Bearer ' + $qaToken; Origin = 'https://example.com'}
Assert-HttpStatus "$qaBase/api/scenarios" 200 $qaHeaders
Assert-HttpStatus "$qaBase/api/runs/11111111-1111-1111-1111-111111111111" 404 $qaHeaders
$qaScenarios = Invoke-RestMethod "$qaBase/api/scenarios" -Headers $qaHeaders
if ($qaScenarios.Count -ne 10) { throw 'Le catalogue doit contenir dix scénarios.' }
if (@($qaScenarios | Where-Object { $_.disponible }).Count -ne 1) { throw 'Seul le scénario de navigation doit être activable.' }
Write-Output 'OK catalogue et indisponibilité des opérations non implémentées'
if ($RunId) {
    $qaResult = Invoke-RestMethod "$qaBase/api/runs/$RunId/result" -Headers $qaHeaders
    $qaLogs = Invoke-RestMethod "$qaBase/api/runs/$RunId/logs" -Headers $qaHeaders
    if ($qaResult.status -in @('RUNNING','QUEUED')) { throw 'Choisir une exécution terminée.' }
    if ($qaResult.events.Count -ne $qaLogs.Count) { throw 'Les logs et le rapport divergent.' }
    $qaEvents = Invoke-WebRequest "$qaBase/api/runs/$RunId/events" -Headers $qaHeaders -UseBasicParsing
    if ($qaEvents.Headers['Content-Type'] -notlike 'text/event-stream*' -or $qaEvents.Content -notmatch '^data: ') { throw 'Flux SSE invalide.' }
    $qaScreenshots = Invoke-RestMethod "$qaBase/api/runs/$RunId/screenshots" -Headers $qaHeaders
    foreach ($qaName in $qaScreenshots) { Assert-HttpStatus "$qaBase/api/runs/$RunId/screenshots/$qaName" 200 $qaHeaders }
    Write-Output ('OK rapport, logs, flux SSE et captures : ' + $qaResult.status)
}

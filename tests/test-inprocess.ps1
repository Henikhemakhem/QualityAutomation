param([string]$BaseUrl = 'http://127.0.0.1:5178')
$ErrorActionPreference = 'Stop'
$qaRoot = Split-Path $PSScriptRoot -Parent
$qaToken = (Get-Content -LiteralPath (Join-Path $qaRoot '.local/api-token.txt') -Raw).Trim()
$qaHeaders = @{ Authorization = 'Bearer ' + $qaToken }
$qaBody = '{"scenarioId":"ouvrir-emprunt","emprunt":2261}'
$qaFirst = Invoke-RestMethod "$BaseUrl/api/runs" -Headers $qaHeaders -Method Post -ContentType 'application/json' -Body $qaBody
try {
    try {
        $null = Invoke-RestMethod "$BaseUrl/api/runs" -Headers $qaHeaders -Method Post -ContentType 'application/json' -Body $qaBody
        throw 'Une seconde exécution a été acceptée avant la fin de la première.'
    }
    catch {
        if (!$_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 409) { throw }
    }
    $null = Invoke-WebRequest "$BaseUrl/api/runs/$($qaFirst.id)/cancel" -Headers $qaHeaders -Method Post -UseBasicParsing
    for ($qaAttempt = 0; $qaAttempt -lt 80; $qaAttempt++) {
        $qaFinal = Invoke-RestMethod "$BaseUrl/api/runs/$($qaFirst.id)" -Headers $qaHeaders
        if ($qaFinal.status -notin @('QUEUED', 'RUNNING')) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($qaFinal.status -ne 'BLOCKED') { throw "Annulation non confirmée : $($qaFinal.status)" }
    if (@($qaFinal.steps | Where-Object { $_.name -ne 'lancer_financements' -and $_.status -ne 'NOT_EXECUTED' }).Count -ne 0) {
        throw 'Une étape dépendante a été exécutée après annulation.'
    }
    $qaArtifact = Join-Path $qaRoot "results/$($qaFirst.id)"
    if (!(Test-Path (Join-Path $qaArtifact 'agent-result.json')) -or !(Test-Path (Join-Path $qaArtifact 'agent.jsonl'))) {
        throw 'Rapport ou progression JSONL du pilote absent.'
    }
    Write-Output "OK STA in-process : conflit 409, annulation BLOCKED, étapes suivantes NOT_EXECUTED, rapport et JSONL ($($qaFirst.id))"
}
finally {
    if ($qaFirst.id) {
        try { $null = Invoke-WebRequest "$BaseUrl/api/runs/$($qaFirst.id)/cancel" -Headers $qaHeaders -Method Post -UseBasicParsing } catch { }
    }
}

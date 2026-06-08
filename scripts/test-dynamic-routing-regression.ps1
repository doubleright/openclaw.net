param(
    [string]$SamplesPath = 'tests/routing-eval',
    [string]$OutputRoot = 'artifacts/testing/dynamic-routing',
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')
Push-Location $repoRoot

function Get-TierIndex {
    param([string]$Tier)

    if ([string]::IsNullOrWhiteSpace($Tier)) {
        return $null
    }

    switch ($Tier.Trim().ToUpperInvariant()) {
        'T0' { return 0 }
        'T1' { return 1 }
        'T2' { return 2 }
        'T3' { return 3 }
        default {
            if ($Tier -match '^[0-3]$') {
                return [int]$Tier
            }

            throw "Unsupported tier value '$Tier'. Expected T0-T3 or 0-3."
        }
    }
}

function Get-TierName {
    param([int]$TierIndex)

    return "T$TierIndex"
}

function Get-BaselinePrediction {
    param(
        [pscustomobject]$Row,
        [string]$Strategy
    )

    switch ($Strategy) {
        'alwaysT2' { return 2 }
        'ruleOnly' { return (Get-TierIndex $Row.ruleOnlyTier) }
        'classifierPlusRules' { return (Get-TierIndex $Row.combinedTier) }
        default { throw "Unknown strategy '$Strategy'." }
    }
}

function New-ConfusionMatrix {
    $matrix = @{}
    foreach ($actual in 0..3) {
        $row = @{}
        foreach ($predicted in 0..3) {
            $row["T$predicted"] = 0
        }
        $matrix["T$actual"] = $row
    }
    return $matrix
}

function Measure-Baseline {
    param(
        [string]$Name,
        [pscustomobject[]]$Rows
    )

    $matrix = New-ConfusionMatrix
    $correct = 0
    $underRouting = 0

    foreach ($row in $Rows) {
        $expectedIndex = Get-TierIndex $row.expectedTier
        $predictedIndex = Get-TierIndex (Get-BaselinePrediction -Row $row -Strategy $Name)
        $actualTier = Get-TierName $expectedIndex
        $predictedTier = Get-TierName $predictedIndex

        $matrix[$actualTier][$predictedTier] += 1
        if ($expectedIndex -eq $predictedIndex) {
            $correct += 1
        }
        if ($predictedIndex -lt $expectedIndex) {
            $underRouting += 1
        }

    }

    $perTier = @()
    foreach ($tierIndex in 0..3) {
        $tierName = Get-TierName $tierIndex
        $tp = [int]$matrix[$tierName][$tierName]
        $fp = 0
        $fn = 0

        foreach ($actual in 0..3) {
            if ($actual -ne $tierIndex) {
                $fp += [int]$matrix["T$actual"][$tierName]
            }
        }

        foreach ($predicted in 0..3) {
            if ($predicted -ne $tierIndex) {
                $fn += [int]$matrix[$tierName]["T$predicted"]
            }
        }

        $precision = if (($tp + $fp) -gt 0) { $tp / [double]($tp + $fp) } else { 0 }
        $recall = if (($tp + $fn) -gt 0) { $tp / [double]($tp + $fn) } else { 0 }
        $f1 = if (($precision + $recall) -gt 0) { 2 * $precision * $recall / ($precision + $recall) } else { 0 }

        $perTier += [pscustomobject]@{
            tier = $tierName
            precision = [math]::Round($precision, 4)
            recall = [math]::Round($recall, 4)
            f1 = [math]::Round($f1, 4)
        }
    }

    $macroF1 = ($perTier | Measure-Object -Property f1 -Average).Average

    return [pscustomobject]@{
        name = $Name
        accuracy = [math]::Round($correct / [double]$Rows.Count, 4)
        macroF1 = [math]::Round($macroF1, 4)
        underRoutingRisk = [math]::Round($underRouting / [double]$Rows.Count, 4)
        correct = $correct
        total = $Rows.Count
        perTier = $perTier
    }
}

try {
    $samplesRoot = Resolve-Path (Join-Path $repoRoot $SamplesPath)
    $outputRootFull = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot))

    $rows = @()
    foreach ($path in Get-ChildItem -Path $samplesRoot -Filter '*.json' -File | Sort-Object FullName) {
        $content = Get-Content -LiteralPath $path.FullName -Raw | ConvertFrom-Json
        if ($content -is [System.Array]) {
            $rows += $content
        }
        elseif ($content.PSObject.Properties.Name -contains 'items') {
            $rows += @($content.items)
        }
        else {
            $rows += $content
        }
    }

    if ($rows.Count -eq 0) {
        throw "No routing evaluation samples were found under $samplesRoot."
    }

    foreach ($row in $rows) {
        foreach ($field in 'id', 'prompt', 'expectedTier', 'ruleOnlyTier', 'combinedTier') {
            if ([string]::IsNullOrWhiteSpace($row.$field)) {
                throw "Routing sample is missing required field '$field'."
            }
        }

        [void](Get-TierIndex $row.expectedTier)
        [void](Get-TierIndex $row.ruleOnlyTier)
        [void](Get-TierIndex $row.combinedTier)
    }

    $startedAt = [DateTimeOffset]::UtcNow
    $reportId = "routing_eval_{0}_{1}" -f $startedAt.ToString('yyyyMMddHHmmss'), ([guid]::NewGuid().ToString('N'))
    $runDirectory = Join-Path $outputRootFull $reportId
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null

    $baselines = @(
        Measure-Baseline -Name 'alwaysT2' -Rows $rows
        Measure-Baseline -Name 'ruleOnly' -Rows $rows
        Measure-Baseline -Name 'classifierPlusRules' -Rows $rows
    )

    $completedAt = [DateTimeOffset]::UtcNow
    $combined = $baselines | Where-Object { $_.name -eq 'classifierPlusRules' } | Select-Object -First 1
    $ruleOnly = $baselines | Where-Object { $_.name -eq 'ruleOnly' } | Select-Object -First 1
    $alwaysT2 = $baselines | Where-Object { $_.name -eq 'alwaysT2' } | Select-Object -First 1

    $gateIssues = @()
    if ($combined.accuracy -lt $ruleOnly.accuracy) {
        $gateIssues += "classifierPlusRules accuracy ($($combined.accuracy)) is below ruleOnly accuracy ($($ruleOnly.accuracy))."
    }
    if ($combined.underRoutingRisk -gt $alwaysT2.underRoutingRisk) {
        $gateIssues += "classifierPlusRules underRoutingRisk ($($combined.underRoutingRisk)) is above alwaysT2 underRoutingRisk ($($alwaysT2.underRoutingRisk))."
    }

    $summary = [pscustomobject]@{
        totalSamples = $rows.Count
        baselines = $baselines | ForEach-Object {
            [pscustomobject]@{
                name = $_.name
                accuracy = $_.accuracy
                macroF1 = $_.macroF1
                underRoutingRisk = $_.underRoutingRisk
            }
        }
        gatePassed = ($gateIssues.Count -eq 0)
        gateIssues = $gateIssues
    }

    $sampleResults = foreach ($row in $rows) {
        [pscustomobject]@{
            id = $row.id
            expectedTier = Get-TierName (Get-TierIndex $row.expectedTier)
            alwaysT2 = Get-TierName (Get-BaselinePrediction -Row $row -Strategy 'alwaysT2')
            ruleOnly = Get-TierName (Get-BaselinePrediction -Row $row -Strategy 'ruleOnly')
            classifierPlusRules = Get-TierName (Get-BaselinePrediction -Row $row -Strategy 'classifierPlusRules')
            riskFlags = @($row.riskFlags)
        }
    }

    $jsonReport = [pscustomobject]@{
        runId = $reportId
        startedAtUtc = $startedAt
        completedAtUtc = $completedAt
        samplesPath = $samplesRoot.Path
        summary = $summary
        baselines = $baselines
        samples = $sampleResults
    }

    $jsonPath = Join-Path $runDirectory 'report.json'
    $markdownPath = Join-Path $runDirectory 'report.md'
    $jsonReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding utf8

    $markdown = New-Object System.Text.StringBuilder
    [void]$markdown.AppendLine('# Dynamic Routing Evaluation Report')
    [void]$markdown.AppendLine('')
    [void]$markdown.AppendLine("- Run ID: $reportId")
    [void]$markdown.AppendLine("- Samples: $($rows.Count)")
    [void]$markdown.AppendLine("- Samples Path: $samplesRoot")
    [void]$markdown.AppendLine("- Gate Passed: $($summary.gatePassed)")
    [void]$markdown.AppendLine('')
    [void]$markdown.AppendLine('| Baseline | Accuracy | Macro F1 | Under-routing risk |')
    [void]$markdown.AppendLine('| --- | ---: | ---: | ---: |')
    foreach ($baseline in $baselines) {
        [void]$markdown.AppendLine("| $($baseline.name) | $($baseline.accuracy) | $($baseline.macroF1) | $($baseline.underRoutingRisk) |")
    }
    [void]$markdown.AppendLine('')
    [void]$markdown.AppendLine('## Per-tier F1')
    [void]$markdown.AppendLine('')
    [void]$markdown.AppendLine('| Baseline | T0 | T1 | T2 | T3 |')
    [void]$markdown.AppendLine('| --- | ---: | ---: | ---: | ---: |')
    foreach ($baseline in $baselines) {
        [void]$markdown.AppendLine("| $($baseline.name) | $($baseline.perTier[0].f1) | $($baseline.perTier[1].f1) | $($baseline.perTier[2].f1) | $($baseline.perTier[3].f1) |")
    }
    if ($gateIssues.Count -gt 0) {
        [void]$markdown.AppendLine('')
        [void]$markdown.AppendLine('## Gate Issues')
        [void]$markdown.AppendLine('')
        foreach ($issue in $gateIssues) {
            [void]$markdown.AppendLine("- $issue")
        }
    }

    Set-Content -LiteralPath $markdownPath -Value $markdown.ToString() -Encoding utf8

    Write-Output "run_id=$reportId"
    Write-Output "json_report=$jsonPath"
    Write-Output "markdown_report=$markdownPath"
    Write-Output "summary=$($summary.totalSamples) samples"
    Write-Output "gate_passed=$($summary.gatePassed)"

    if ($Strict -and -not $summary.gatePassed) {
        foreach ($issue in $gateIssues) {
            Write-Error $issue
        }
        exit 1
    }
}
finally {
    Pop-Location
}

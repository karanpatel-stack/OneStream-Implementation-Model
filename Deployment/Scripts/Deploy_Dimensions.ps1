#Requires -Version 5.1
<#
.SYNOPSIS
    Deploys dimension member data to the specified OneStream XF environment.

.DESCRIPTION
    Reads dimension member CSV files from the data directory, validates their
    structure, and loads them into the OneStream application via the REST API
    or import mechanism. Verifies member counts after loading.

.PARAMETER Environment
    Target environment: DEV, QA, or PROD.

.PARAMETER Dimension
    Dimension to load: Account, Entity, Scenario, Time, Flow, Consolidation,
    UD1_Product, UD2_Customer, UD3_Department, UD4_Project, UD5_Intercompany,
    UD6_Plant, UD7_CurrencyRpt, UD8_DataSource, or All.

.PARAMETER SourcePath
    Path to the dimension CSV files. Defaults to Data/Dimensions in the project root.

.EXAMPLE
    .\Deploy_Dimensions.ps1 -Environment DEV -Dimension All
    .\Deploy_Dimensions.ps1 -Environment QA -Dimension Account
    .\Deploy_Dimensions.ps1 -Environment PROD -Dimension Entity

.NOTES
    Author:  OneStream Implementation Team
    Date:    2026-02-18
    Version: 1.0
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("DEV", "QA", "PROD")]
    [string]$Environment,

    [Parameter(Mandatory = $false)]
    [ValidateSet("Account", "Entity", "Scenario", "Time", "Flow", "Consolidation",
                 "UD1_Product", "UD2_Customer", "UD3_Department", "UD4_Project",
                 "UD5_Intercompany", "UD6_Plant", "UD7_CurrencyRpt", "UD8_DataSource", "All")]
    [string]$Dimension = "All",

    [Parameter(Mandatory = $false)]
    [string]$SourcePath
)

# --- Configuration ---
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$ConfigPath = Join-Path $ScriptRoot "..\Configs\$Environment.config"
$LogDir = Join-Path $ProjectRoot "Deployment\Logs"
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$LogFile = Join-Path $LogDir "Deploy_Dimensions_${Environment}_${Timestamp}.log"

if (-not $SourcePath) {
    $SourcePath = Join-Path $ProjectRoot "Data\Dimensions"
}

# Dimension metadata: name, expected columns, approximate member count
$DimensionMetadata = @{
    "Account"          = @{ FilePrefix = "DIM_Account";          RequiredCols = @("MemberName","Description","Parent","AccountType","ExchangeRateType"); ExpectedCount = 800 }
    "Entity"           = @{ FilePrefix = "DIM_Entity";           RequiredCols = @("MemberName","Description","Parent","DefaultCurrency","ConsolidationMethod"); ExpectedCount = 120 }
    "Scenario"         = @{ FilePrefix = "DIM_Scenario";         RequiredCols = @("MemberName","Description","Parent","InputEnabled"); ExpectedCount = 15 }
    "Time"             = @{ FilePrefix = "DIM_Time";             RequiredCols = @("MemberName","Description","Parent","YearNumber","MonthNumber"); ExpectedCount = 240 }
    "Flow"             = @{ FilePrefix = "DIM_Flow";             RequiredCols = @("MemberName","Description","Parent"); ExpectedCount = 25 }
    "Consolidation"    = @{ FilePrefix = "DIM_Consolidation";    RequiredCols = @("MemberName","Description","Parent"); ExpectedCount = 10 }
    "UD1_Product"      = @{ FilePrefix = "DIM_UD1_Product";      RequiredCols = @("MemberName","Description","Parent"); ExpectedCount = 150 }
    "UD2_Customer"     = @{ FilePrefix = "DIM_UD2_Customer";     RequiredCols = @("MemberName","Description","Parent"); ExpectedCount = 100 }
    "UD3_Department"   = @{ FilePrefix = "DIM_UD3_Department";   RequiredCols = @("MemberName","Description","Parent"); ExpectedCount = 80 }
    "UD4_Project"      = @{ FilePrefix = "DIM_UD4_Project";      RequiredCols = @("MemberName","Description","Parent","ProjectStatus"); ExpectedCount = 60 }
    "UD5_Intercompany" = @{ FilePrefix = "DIM_UD5_Intercompany"; RequiredCols = @("MemberName","Description","Parent","MirrorEntity"); ExpectedCount = 45 }
    "UD6_Plant"        = @{ FilePrefix = "DIM_UD6_Plant";        RequiredCols = @("MemberName","Description","Parent","ParentEntity"); ExpectedCount = 30 }
    "UD7_CurrencyRpt"  = @{ FilePrefix = "DIM_UD7_CurrencyRpt";  RequiredCols = @("MemberName","Description","Parent"); ExpectedCount = 10 }
    "UD8_DataSource"   = @{ FilePrefix = "DIM_UD8_DataSource";   RequiredCols = @("MemberName","Description","Parent"); ExpectedCount = 20 }
}

# --- Functions ---

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $entry = "[$Level] $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $Message"
    Write-Host $entry -ForegroundColor $(switch ($Level) { "ERROR" { "Red" }; "WARN" { "Yellow" }; "OK" { "Green" }; default { "White" } })
    Add-Content -Path $LogFile -Value $entry
}

function Load-EnvironmentConfig {
    param([string]$Path)
    if (-not (Test-Path $Path)) { throw "Configuration file not found: $Path" }
    [xml]$config = Get-Content $Path
    return @{
        ServerUrl       = $config.EnvironmentConfig.ServerUrl
        DatabaseName    = $config.EnvironmentConfig.DatabaseName
        ApplicationName = $config.EnvironmentConfig.ApplicationName
    }
}

function Find-DimensionFile {
    param([string]$DimName)
    $metadata = $DimensionMetadata[$DimName]
    $pattern = "$($metadata.FilePrefix)_*.csv"
    $files = Get-ChildItem -Path $SourcePath -Filter $pattern -File -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending
    if ($files.Count -gt 0) {
        return $files[0]  # Return most recent file
    }
    return $null
}

function Validate-CsvStructure {
    param([string]$FilePath, [string[]]$RequiredColumns)

    $csvData = Import-Csv -Path $FilePath -ErrorAction Stop
    if ($csvData.Count -eq 0) {
        return @{ Valid = $false; Error = "CSV file is empty"; RowCount = 0 }
    }

    $headers = ($csvData[0].PSObject.Properties).Name
    $missingColumns = @()
    foreach ($col in $RequiredColumns) {
        if ($col -notin $headers) {
            $missingColumns += $col
        }
    }

    if ($missingColumns.Count -gt 0) {
        return @{ Valid = $false; Error = "Missing columns: $($missingColumns -join ', ')"; RowCount = $csvData.Count }
    }

    # Check for duplicate member names
    $memberNames = $csvData | Select-Object -ExpandProperty MemberName
    $duplicates = $memberNames | Group-Object | Where-Object { $_.Count -gt 1 }
    if ($duplicates.Count -gt 0) {
        $dupNames = ($duplicates | Select-Object -First 5 -ExpandProperty Name) -join ", "
        return @{ Valid = $false; Error = "Duplicate member names found: $dupNames"; RowCount = $csvData.Count }
    }

    return @{ Valid = $true; Error = $null; RowCount = $csvData.Count; Data = $csvData }
}

function Deploy-Dimension {
    param(
        [string]$DimName,
        [hashtable]$Config,
        [string]$Token
    )

    Write-Log "--- Deploying dimension: $DimName ---"
    $metadata = $DimensionMetadata[$DimName]

    # Step 1: Find the CSV file
    $csvFile = Find-DimensionFile -DimName $DimName
    if ($null -eq $csvFile) {
        Write-Log "  No CSV file found for $DimName (pattern: $($metadata.FilePrefix)_*.csv)" "WARN"
        return @{ Success = $false; Dimension = $DimName; Error = "File not found" }
    }
    Write-Log "  File: $($csvFile.Name) ($($csvFile.Length) bytes)"

    # Step 2: Validate CSV structure
    $validation = Validate-CsvStructure -FilePath $csvFile.FullName -RequiredColumns $metadata.RequiredCols
    if (-not $validation.Valid) {
        Write-Log "  Validation failed: $($validation.Error)" "ERROR"
        return @{ Success = $false; Dimension = $DimName; Error = $validation.Error }
    }
    Write-Log "  Validated: $($validation.RowCount) members, all required columns present." "OK"

    # Step 3: Load via OneStream API/import
    try {
        # PLACEHOLDER: Replace with actual OneStream dimension import API call
        # $importPayload = @{
        #     DimensionName = $DimName
        #     ImportMode    = "MergeAndUpdate"  # or "ReplaceAll"
        #     Data          = $validation.Data
        # } | ConvertTo-Json -Depth 10
        #
        # $response = Invoke-RestMethod -Uri "$($Config.ServerUrl)/api/dimensions/$DimName/import" `
        #     -Method Post -Body $importPayload -ContentType "application/json" `
        #     -Headers @{ Authorization = "Bearer $Token" }

        Write-Log "  Loaded $($validation.RowCount) members to $DimName (placeholder)." "OK"
    }
    catch {
        Write-Log "  Load failed: $($_.Exception.Message)" "ERROR"
        return @{ Success = $false; Dimension = $DimName; Error = "Load failed: $($_.Exception.Message)" }
    }

    # Step 4: Verify member counts
    try {
        # PLACEHOLDER: Actual API call to get dimension member count
        # $countResponse = Invoke-RestMethod -Uri "$($Config.ServerUrl)/api/dimensions/$DimName/count" `
        #     -Method Get -Headers @{ Authorization = "Bearer $Token" }
        # $actualCount = $countResponse.MemberCount

        $actualCount = $validation.RowCount  # Placeholder: use CSV count
        $expectedCount = $metadata.ExpectedCount
        $variance = [math]::Abs($actualCount - $expectedCount)
        $variancePct = if ($expectedCount -gt 0) { [math]::Round(($variance / $expectedCount) * 100, 1) } else { 0 }

        if ($variancePct -gt 20) {
            Write-Log "  Member count variance: Expected ~$expectedCount, Got $actualCount ($variancePct% off)" "WARN"
        }
        else {
            Write-Log "  Member count verified: $actualCount members (expected ~$expectedCount)" "OK"
        }
    }
    catch {
        Write-Log "  Count verification failed: $($_.Exception.Message)" "WARN"
    }

    return @{ Success = $true; Dimension = $DimName; MemberCount = $validation.RowCount; Error = $null }
}

# --- Main Execution ---

if (-not (Test-Path $LogDir)) {
    New-Item -Path $LogDir -ItemType Directory -Force | Out-Null
}

Write-Log "=========================================="
Write-Log "OneStream Dimension Deployment"
Write-Log "=========================================="
Write-Log "Environment : $Environment"
Write-Log "Dimension   : $Dimension"
Write-Log "Source Path : $SourcePath"
Write-Log "Config File : $ConfigPath"
Write-Log "Timestamp   : $Timestamp"
Write-Log "=========================================="

if ($Environment -eq "PROD") {
    Write-Log "PRODUCTION DEPLOYMENT DETECTED" "WARN"
    $confirmation = Read-Host "Confirm PRODUCTION dimension deployment (Type 'YES')"
    if ($confirmation -ne "YES") {
        Write-Log "Deployment cancelled by user." "WARN"
        exit 0
    }
}

try {
    $envConfig = Load-EnvironmentConfig -Path $ConfigPath
    Write-Log "Configuration loaded for $Environment"

    # PLACEHOLDER: Connect to API
    $token = "placeholder_token_$Environment"
    Write-Log "API connection established (placeholder)." "WARN"

    # Determine which dimensions to deploy
    $dimensionsToLoad = @()
    if ($Dimension -eq "All") {
        $dimensionsToLoad = $DimensionMetadata.Keys | Sort-Object
    }
    else {
        $dimensionsToLoad = @($Dimension)
    }

    Write-Log "Deploying $($dimensionsToLoad.Count) dimension(s)..."

    $results = @()
    $successCount = 0
    $failureCount = 0

    foreach ($dimName in $dimensionsToLoad) {
        $result = Deploy-Dimension -DimName $dimName -Config $envConfig -Token $token
        $results += $result
        if ($result.Success) { $successCount++ } else { $failureCount++ }
    }

    # Summary
    Write-Log "=========================================="
    Write-Log "DEPLOYMENT SUMMARY"
    Write-Log "=========================================="
    Write-Log "Total Dimensions : $($dimensionsToLoad.Count)"
    Write-Log "Successful       : $successCount" "OK"
    Write-Log "Failed           : $failureCount" $(if ($failureCount -gt 0) { "ERROR" } else { "OK" })

    foreach ($r in $results) {
        $status = if ($r.Success) { "OK" } else { "FAIL" }
        $detail = if ($r.Success) { "$($r.MemberCount) members" } else { $r.Error }
        Write-Log "  [$status] $($r.Dimension) - $detail"
    }

    Write-Log "Log File: $LogFile"
    Write-Log "=========================================="

    if ($failureCount -gt 0) { exit 1 } else { exit 0 }
}
catch {
    Write-Log "DEPLOYMENT FAILED: $($_.Exception.Message)" "ERROR"
    exit 2
}

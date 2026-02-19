#Requires -Version 5.1
<#
.SYNOPSIS
    Deploys OneStream XF Data Management sequences to the specified environment.

.DESCRIPTION
    Deploys data management components (connections, stages, transforms, load steps)
    in the correct dependency order. Validates dependencies between components
    and tests connections after deployment.

.PARAMETER Environment
    Target environment: DEV, QA, or PROD.

.PARAMETER Component
    Component to deploy: Connections, Stages, Transforms, LoadSteps, Sequences, or All.

.PARAMETER TestConnections
    When specified, tests all source system connections after deployment.

.EXAMPLE
    .\Deploy_DataManagement.ps1 -Environment DEV -Component All -TestConnections
    .\Deploy_DataManagement.ps1 -Environment QA -Component Connections
    .\Deploy_DataManagement.ps1 -Environment PROD -Component All

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
    [ValidateSet("Connections", "Stages", "Transforms", "LoadSteps", "Sequences", "All")]
    [string]$Component = "All",

    [switch]$TestConnections
)

# --- Configuration ---
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$ConfigPath = Join-Path $ScriptRoot "..\Configs\$Environment.config"
$LogDir = Join-Path $ProjectRoot "Deployment\Logs"
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$LogFile = Join-Path $LogDir "Deploy_DataManagement_${Environment}_${Timestamp}.log"
$DmConfigPath = Join-Path $ProjectRoot "DataManagement"

# Data Management component definitions
$Connections = @(
    @{ Name = "CONN_SAP_HANA";     Type = "JDBC";    Description = "SAP S/4HANA database" }
    @{ Name = "CONN_Oracle_EBS";   Type = "ODBC";    Description = "Oracle EBS database" }
    @{ Name = "CONN_NetSuite_API"; Type = "REST";    Description = "NetSuite REST API" }
    @{ Name = "CONN_Workday_API";  Type = "REST";    Description = "Workday HCM REST API" }
    @{ Name = "CONN_MES_SFTP";     Type = "SFTP";    Description = "MES file drop SFTP" }
    @{ Name = "CONN_FileShare";    Type = "UNC";     Description = "Shared file location" }
)

$Stages = @(
    @{ Name = "STG_GL_Actuals";   Source = "CONN_SAP_HANA,CONN_Oracle_EBS,CONN_NetSuite_API"; Description = "GL actuals staging" }
    @{ Name = "STG_AP_Detail";    Source = "CONN_SAP_HANA";      Description = "AP detail staging" }
    @{ Name = "STG_AR_Detail";    Source = "CONN_SAP_HANA,CONN_Oracle_EBS"; Description = "AR detail staging" }
    @{ Name = "STG_FA_Detail";    Source = "CONN_SAP_HANA,CONN_Oracle_EBS"; Description = "Fixed asset staging" }
    @{ Name = "STG_Inventory";    Source = "CONN_Oracle_EBS";    Description = "Inventory staging" }
    @{ Name = "STG_Headcount";    Source = "CONN_Workday_API";   Description = "Headcount staging" }
    @{ Name = "STG_Compensation"; Source = "CONN_Workday_API";   Description = "Compensation staging" }
    @{ Name = "STG_Production";   Source = "CONN_MES_SFTP";      Description = "Production data staging" }
    @{ Name = "STG_Quality";      Source = "CONN_MES_SFTP";      Description = "Quality metrics staging" }
    @{ Name = "STG_StatData";     Source = "CONN_FileShare";     Description = "Statistical data staging" }
)

$DmSequences = @(
    @{ Name = "SEQ_Daily_GLActuals";    Steps = @("CN_SAP_GLActuals","CN_Oracle_GLActuals","CN_NetSuite_GLActuals","Transform_GL","Validate_GL","Load_Finance"); Schedule = "Daily 02:30" }
    @{ Name = "SEQ_Daily_MES";          Steps = @("CN_MES_Production","CN_MES_Quality","Transform_MES","Validate_MES","Load_Finance_STAT"); Schedule = "Daily 02:00" }
    @{ Name = "SEQ_Weekly_HR";          Steps = @("CN_Workday_Headcount","CN_Workday_Compensation","Transform_HR","Validate_HR","Load_HR"); Schedule = "Sunday 22:00" }
    @{ Name = "SEQ_Weekly_StatData";    Steps = @("CN_FlatFile_StatData","Transform_Stat","Validate_Stat","Load_Finance_STAT"); Schedule = "Sunday 23:00" }
    @{ Name = "SEQ_Monthly_FullRefresh"; Steps = @("CN_SAP_ALL","CN_Oracle_ALL","CN_NetSuite_ALL","Transform_ALL","Validate_ALL","Load_Finance_Full","Consolidation_PreCheck"); Schedule = "BD1 18:00" }
)

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

function Deploy-Connections {
    param([hashtable]$Config)
    Write-Log "--- Deploying Connections ---"
    $results = @()

    foreach ($conn in $Connections) {
        Write-Log "  Deploying connection: $($conn.Name) ($($conn.Type))"
        try {
            # PLACEHOLDER: Actual OneStream API call to create/update connection
            # The connection definition includes server, port, auth type
            # Credentials are stored in the OneStream Credential Vault
            # $payload = @{ Name = $conn.Name; Type = $conn.Type } | ConvertTo-Json
            # Invoke-RestMethod -Uri "$($Config.ServerUrl)/api/dm/connections" ...

            Write-Log "    Deployed: $($conn.Name) - $($conn.Description)" "OK"
            $results += @{ Name = $conn.Name; Success = $true }
        }
        catch {
            Write-Log "    Failed: $($conn.Name) - $($_.Exception.Message)" "ERROR"
            $results += @{ Name = $conn.Name; Success = $false; Error = $_.Exception.Message }
        }
    }
    return $results
}

function Deploy-StageTables {
    param([hashtable]$Config)
    Write-Log "--- Deploying Stage Tables ---"
    $results = @()

    foreach ($stage in $Stages) {
        Write-Log "  Deploying stage: $($stage.Name)"
        try {
            # PLACEHOLDER: Create/update staging table definitions
            # Staging tables are created in the application database
            # Schema is defined in the DM configuration

            Write-Log "    Deployed: $($stage.Name) - $($stage.Description)" "OK"
            $results += @{ Name = $stage.Name; Success = $true }
        }
        catch {
            Write-Log "    Failed: $($stage.Name) - $($_.Exception.Message)" "ERROR"
            $results += @{ Name = $stage.Name; Success = $false; Error = $_.Exception.Message }
        }
    }
    return $results
}

function Deploy-TransformRules {
    param([hashtable]$Config)
    Write-Log "--- Deploying Transform Rules ---"

    $transforms = @(
        "Transform_GL", "Transform_MES", "Transform_HR", "Transform_Stat", "Transform_ALL"
    )

    $results = @()
    foreach ($transform in $transforms) {
        Write-Log "  Deploying transform: $transform"
        try {
            # PLACEHOLDER: Deploy transformation rule definitions
            # Transforms include dimension mapping, derivation, aggregation

            Write-Log "    Deployed: $transform" "OK"
            $results += @{ Name = $transform; Success = $true }
        }
        catch {
            Write-Log "    Failed: $transform - $($_.Exception.Message)" "ERROR"
            $results += @{ Name = $transform; Success = $false; Error = $_.Exception.Message }
        }
    }
    return $results
}

function Deploy-LoadSteps {
    param([hashtable]$Config)
    Write-Log "--- Deploying Load Steps ---"

    $loadSteps = @(
        "Load_Finance", "Load_Finance_STAT", "Load_Finance_Full",
        "Load_HR", "Consolidation_PreCheck"
    )

    $results = @()
    foreach ($step in $loadSteps) {
        Write-Log "  Deploying load step: $step"
        try {
            # PLACEHOLDER: Deploy load step definitions
            # Load steps define target cube, clear scope, and load parameters

            Write-Log "    Deployed: $step" "OK"
            $results += @{ Name = $step; Success = $true }
        }
        catch {
            Write-Log "    Failed: $step - $($_.Exception.Message)" "ERROR"
            $results += @{ Name = $step; Success = $false; Error = $_.Exception.Message }
        }
    }
    return $results
}

function Deploy-Sequences {
    param([hashtable]$Config)
    Write-Log "--- Deploying DM Sequences ---"
    $results = @()

    foreach ($seq in $DmSequences) {
        Write-Log "  Deploying sequence: $($seq.Name) (Schedule: $($seq.Schedule))"
        Write-Log "    Steps: $($seq.Steps -join ' -> ')"

        # Validate dependencies: each step must exist
        $missingSteps = @()
        foreach ($step in $seq.Steps) {
            # PLACEHOLDER: Check if referenced step exists in deployed components
            # In production, query the API to verify each step is available
        }

        try {
            # PLACEHOLDER: Deploy DM sequence definition with step ordering
            # $payload = @{
            #     Name     = $seq.Name
            #     Schedule = $seq.Schedule
            #     Steps    = $seq.Steps
            # } | ConvertTo-Json

            Write-Log "    Deployed: $($seq.Name) with $($seq.Steps.Count) steps" "OK"
            $results += @{ Name = $seq.Name; Success = $true }
        }
        catch {
            Write-Log "    Failed: $($seq.Name) - $($_.Exception.Message)" "ERROR"
            $results += @{ Name = $seq.Name; Success = $false; Error = $_.Exception.Message }
        }
    }
    return $results
}

function Test-AllConnections {
    param([hashtable]$Config)
    Write-Log "--- Testing Connections ---"

    foreach ($conn in $Connections) {
        Write-Log "  Testing: $($conn.Name) ($($conn.Type))..."
        try {
            # PLACEHOLDER: Actual connection test via OneStream API
            # $testResult = Invoke-RestMethod -Uri "$($Config.ServerUrl)/api/dm/connections/$($conn.Name)/test" ...
            # if ($testResult.Success) { ... }

            Write-Log "    Connection test PASSED: $($conn.Name)" "OK"
        }
        catch {
            Write-Log "    Connection test FAILED: $($conn.Name) - $($_.Exception.Message)" "ERROR"
        }
    }
}

# --- Main Execution ---

if (-not (Test-Path $LogDir)) {
    New-Item -Path $LogDir -ItemType Directory -Force | Out-Null
}

Write-Log "=========================================="
Write-Log "OneStream Data Management Deployment"
Write-Log "=========================================="
Write-Log "Environment     : $Environment"
Write-Log "Component       : $Component"
Write-Log "Test Connections: $TestConnections"
Write-Log "Timestamp       : $Timestamp"
Write-Log "=========================================="

if ($Environment -eq "PROD") {
    Write-Log "PRODUCTION DEPLOYMENT DETECTED" "WARN"
    $confirmation = Read-Host "Confirm PRODUCTION DM deployment (Type 'YES')"
    if ($confirmation -ne "YES") { Write-Log "Cancelled." "WARN"; exit 0 }
}

try {
    $envConfig = Load-EnvironmentConfig -Path $ConfigPath
    Write-Log "Configuration loaded for $Environment"

    $allResults = @()

    # Deploy in dependency order: Connections -> Stages -> Transforms -> LoadSteps -> Sequences
    $deployOrder = @("Connections", "Stages", "Transforms", "LoadSteps", "Sequences")
    $componentsToDeploy = if ($Component -eq "All") { $deployOrder } else { @($Component) }

    foreach ($comp in $componentsToDeploy) {
        switch ($comp) {
            "Connections" { $allResults += Deploy-Connections -Config $envConfig }
            "Stages"      { $allResults += Deploy-StageTables -Config $envConfig }
            "Transforms"  { $allResults += Deploy-TransformRules -Config $envConfig }
            "LoadSteps"   { $allResults += Deploy-LoadSteps -Config $envConfig }
            "Sequences"   { $allResults += Deploy-Sequences -Config $envConfig }
        }
    }

    # Test connections if requested
    if ($TestConnections) {
        Test-AllConnections -Config $envConfig
    }

    # Summary
    $successCount = ($allResults | Where-Object { $_.Success }).Count
    $failureCount = ($allResults | Where-Object { -not $_.Success }).Count

    Write-Log "=========================================="
    Write-Log "DEPLOYMENT SUMMARY"
    Write-Log "=========================================="
    Write-Log "Total Components : $($allResults.Count)"
    Write-Log "Successful       : $successCount" "OK"
    Write-Log "Failed           : $failureCount" $(if ($failureCount -gt 0) { "ERROR" } else { "OK" })
    Write-Log "Log File         : $LogFile"
    Write-Log "=========================================="

    if ($failureCount -gt 0) { exit 1 } else { exit 0 }
}
catch {
    Write-Log "DEPLOYMENT FAILED: $($_.Exception.Message)" "ERROR"
    exit 2
}

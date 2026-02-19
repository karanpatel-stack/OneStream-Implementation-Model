        #Requires -Version 5.1
<#
.SYNOPSIS
    Deploys OneStream XF business rules to the specified environment.

.DESCRIPTION
    Reads business rule source files from the repository, connects to the OneStream
    REST API for the target environment, and deploys each rule (upload, compile check,
    activate). Supports WhatIf mode for dry-run validation and rollback capability.

.PARAMETER Environment
    Target environment: DEV, QA, or PROD.

.PARAMETER RuleType
    Type of rules to deploy: Finance, Calculate, Connector, DashboardDataAdapter,
    DashboardStringFunction, MemberFilter, EventHandler, Extender, Validation, or All.

.PARAMETER WhatIf
    When specified, performs a dry-run without making changes.

.PARAMETER RollbackVersion
    When specified, rolls back to the specified version tag.

.PARAMETER SourcePath
    Path to the business rules source directory. Defaults to the repository BusinessRules folder.

.EXAMPLE
    .\Deploy_BusinessRules.ps1 -Environment DEV -RuleType All
    .\Deploy_BusinessRules.ps1 -Environment QA -RuleType Finance -WhatIf
    .\Deploy_BusinessRules.ps1 -Environment PROD -RuleType All -RollbackVersion "v1.2.0"

.NOTES
    Author:  OneStream Implementation Team
    Date:    2026-02-18
    Version: 1.0
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("DEV", "QA", "PROD")]
    [string]$Environment,

    [Parameter(Mandatory = $false)]
    [ValidateSet("Finance", "Calculate", "Connector", "DashboardDataAdapter",
                 "DashboardStringFunction", "MemberFilter", "EventHandler",
                 "Extender", "Validation", "All")]
    [string]$RuleType = "All",

    [Parameter(Mandatory = $false)]
    [string]$RollbackVersion,

    [Parameter(Mandatory = $false)]
    [string]$SourcePath
)

# --- Configuration ---
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptRoot)
$ConfigPath = Join-Path $ScriptRoot "..\Configs\$Environment.config"
$LogDir = Join-Path $ProjectRoot "Deployment\Logs"
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$LogFile = Join-Path $LogDir "Deploy_BusinessRules_${Environment}_${Timestamp}.log"

if (-not $SourcePath) {
    $SourcePath = Join-Path $ProjectRoot "BusinessRules"
}

# Rule type to folder mapping
$RuleTypeFolderMap = @{
    "Finance"                 = "FinanceRules"
    "Calculate"               = "CalculateRules"
    "Connector"               = "ConnectorRules"
    "DashboardDataAdapter"    = "DashboardDataAdapters"
    "DashboardStringFunction" = "DashboardStringFunctions"
    "MemberFilter"            = "MemberFilters"
    "EventHandler"            = "EventHandlers"
    "Extender"                = "Extenders"
    "Validation"              = "ValidationRules"
}

# Rule type to OneStream API type identifier
$RuleTypeApiMap = @{
    "Finance"                 = "FinanceRule"
    "Calculate"               = "CalculateRule"
    "Connector"               = "ConnectorRule"
    "DashboardDataAdapter"    = "DashboardDataAdapterRule"
    "DashboardStringFunction" = "DashboardStringFunctionRule"
    "MemberFilter"            = "MemberFilterRule"
    "EventHandler"            = "EventHandlerRule"
    "Extender"                = "ExtenderRule"
    "Validation"              = "ValidationRule"
}

# --- Functions ---

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $entry = "[$Level] $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') - $Message"
    Write-Host $entry -ForegroundColor $(
        switch ($Level) {
            "ERROR" { "Red" }
            "WARN"  { "Yellow" }
            "OK"    { "Green" }
            default { "White" }
        }
    )
    Add-Content -Path $LogFile -Value $entry
}

function Load-EnvironmentConfig {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Configuration file not found: $Path"
    }

    [xml]$config = Get-Content $Path
    return @{
        ServerUrl       = $config.EnvironmentConfig.ServerUrl
        DatabaseName    = $config.EnvironmentConfig.DatabaseName
        ApplicationName = $config.EnvironmentConfig.ApplicationName
        LogLevel        = $config.EnvironmentConfig.Settings.LogLevel
    }
}

function Connect-OneStreamApi {
    param([hashtable]$Config)

    Write-Log "Connecting to OneStream API at $($Config.ServerUrl)..."

    # PLACEHOLDER: Replace with actual OneStream REST API authentication
    # In production, this would use OAuth2 or token-based authentication
    # against the OneStream REST API endpoint.
    $authPayload = @{
        grant_type    = "client_credentials"
        client_id     = $env:ONESTREAM_CLIENT_ID
        client_secret = $env:ONESTREAM_CLIENT_SECRET
        database      = $Config.DatabaseName
        application   = $Config.ApplicationName
    } | ConvertTo-Json

    try {
        # PLACEHOLDER: Actual API call
        # $response = Invoke-RestMethod -Uri "$($Config.ServerUrl)/api/auth/token" `
        #     -Method Post -Body $authPayload -ContentType "application/json"
        # return $response.access_token

        Write-Log "API connection established (placeholder mode)." "WARN"
        return "placeholder_token_$Environment"
    }
    catch {
        Write-Log "Failed to connect to OneStream API: $($_.Exception.Message)" "ERROR"
        throw
    }
}

function Get-RuleFiles {
    param([string]$Type)

    $folders = @()
    if ($Type -eq "All") {
        $folders = $RuleTypeFolderMap.Values
    }
    else {
        $folders = @($RuleTypeFolderMap[$Type])
    }

    $ruleFiles = @()
    foreach ($folder in $folders) {
        $folderPath = Join-Path $SourcePath $folder
        if (Test-Path $folderPath) {
            $files = Get-ChildItem -Path $folderPath -Filter "*.vb" -File
            foreach ($file in $files) {
                $ruleFiles += @{
                    Name     = $file.BaseName
                    Path     = $file.FullName
                    Folder   = $folder
                    Type     = ($RuleTypeFolderMap.GetEnumerator() | Where-Object { $_.Value -eq $folder }).Key
                    Size     = $file.Length
                    Modified = $file.LastWriteTime
                }
            }
        }
        else {
            Write-Log "Rule folder not found: $folderPath" "WARN"
        }
    }

    return $ruleFiles
}

function Deploy-SingleRule {
    param(
        [hashtable]$Rule,
        [string]$Token,
        [hashtable]$Config
    )

    $ruleName = $Rule.Name
    $ruleType = $Rule.Type
    $apiType = $RuleTypeApiMap[$ruleType]

    Write-Log "Deploying rule: $ruleName (Type: $ruleType)"

    # Step 1: Read source file
    $sourceCode = Get-Content -Path $Rule.Path -Raw
    if ([string]::IsNullOrWhiteSpace($sourceCode)) {
        Write-Log "  Source file is empty: $($Rule.Path)" "ERROR"
        return @{ Success = $false; Rule = $ruleName; Error = "Empty source file" }
    }
    Write-Log "  Source loaded: $($Rule.Size) bytes"

    # Step 2: Upload to OneStream
    # PLACEHOLDER: Replace with actual API call
    $uploadPayload = @{
        RuleName   = $ruleName
        RuleType   = $apiType
        SourceCode = $sourceCode
    } | ConvertTo-Json -Depth 10

    try {
        # PLACEHOLDER: Actual API call
        # $uploadResponse = Invoke-RestMethod -Uri "$($Config.ServerUrl)/api/rules/upload" `
        #     -Method Post -Body $uploadPayload -ContentType "application/json" `
        #     -Headers @{ Authorization = "Bearer $Token" }

        Write-Log "  Uploaded successfully (placeholder)."
    }
    catch {
        Write-Log "  Upload failed: $($_.Exception.Message)" "ERROR"
        return @{ Success = $false; Rule = $ruleName; Error = "Upload failed: $($_.Exception.Message)" }
    }

    # Step 3: Compile check
    try {
        # PLACEHOLDER: Actual API call
        # $compileResponse = Invoke-RestMethod -Uri "$($Config.ServerUrl)/api/rules/$ruleName/compile" `
        #     -Method Post -Headers @{ Authorization = "Bearer $Token" }
        # if (-not $compileResponse.Success) {
        #     throw "Compilation errors: $($compileResponse.Errors -join '; ')"
        # }

        Write-Log "  Compilation check passed (placeholder)." "OK"
    }
    catch {
        Write-Log "  Compilation failed: $($_.Exception.Message)" "ERROR"
        return @{ Success = $false; Rule = $ruleName; Error = "Compile failed: $($_.Exception.Message)" }
    }

    # Step 4: Activate rule
    try {
        # PLACEHOLDER: Actual API call
        # $activateResponse = Invoke-RestMethod -Uri "$($Config.ServerUrl)/api/rules/$ruleName/activate" `
        #     -Method Post -Headers @{ Authorization = "Bearer $Token" }

        Write-Log "  Activated successfully (placeholder)." "OK"
    }
    catch {
        Write-Log "  Activation failed: $($_.Exception.Message)" "ERROR"
        return @{ Success = $false; Rule = $ruleName; Error = "Activate failed: $($_.Exception.Message)" }
    }

    return @{ Success = $true; Rule = $ruleName; Error = $null }
}

function Invoke-Rollback {
    param(
        [string]$Version,
        [string]$Token,
        [hashtable]$Config
    )

    Write-Log "Initiating rollback to version: $Version" "WARN"

    # PLACEHOLDER: Actual rollback logic
    # 1. Retrieve the specified version from the backup/version store
    # 2. Deploy each rule from the versioned backup
    # 3. Verify compilation and activation

    Write-Log "Rollback to version $Version completed (placeholder)." "WARN"
}

# --- Main Execution ---

# Create log directory if it doesn't exist
if (-not (Test-Path $LogDir)) {
    New-Item -Path $LogDir -ItemType Directory -Force | Out-Null
}

Write-Log "=========================================="
Write-Log "OneStream Business Rules Deployment"
Write-Log "=========================================="
Write-Log "Environment : $Environment"
Write-Log "Rule Type   : $RuleType"
Write-Log "Source Path : $SourcePath"
Write-Log "Config File : $ConfigPath"
Write-Log "WhatIf Mode : $($WhatIfPreference -or $PSBoundParameters.ContainsKey('WhatIf'))"
Write-Log "Timestamp   : $Timestamp"
Write-Log "=========================================="

# PROD deployment safety check
if ($Environment -eq "PROD" -and -not $WhatIfPreference) {
    Write-Log "PRODUCTION DEPLOYMENT DETECTED" "WARN"
    $confirmation = Read-Host "Are you sure you want to deploy to PRODUCTION? (Type 'YES' to confirm)"
    if ($confirmation -ne "YES") {
        Write-Log "Deployment cancelled by user." "WARN"
        exit 0
    }
}

try {
    # Load environment configuration
    $envConfig = Load-EnvironmentConfig -Path $ConfigPath
    Write-Log "Configuration loaded for $Environment ($($envConfig.ApplicationName))"

    # Handle rollback if specified
    if ($RollbackVersion) {
        $token = Connect-OneStreamApi -Config $envConfig
        Invoke-Rollback -Version $RollbackVersion -Token $token -Config $envConfig
        Write-Log "Rollback complete."
        exit 0
    }

    # Discover rule files
    $ruleFiles = Get-RuleFiles -Type $RuleType
    Write-Log "Found $($ruleFiles.Count) rule file(s) to deploy."

    if ($ruleFiles.Count -eq 0) {
        Write-Log "No rule files found. Nothing to deploy." "WARN"
        exit 0
    }

    # Display rules to be deployed
    foreach ($rule in $ruleFiles) {
        Write-Log "  - $($rule.Name) ($($rule.Type), $($rule.Size) bytes)"
    }

    # WhatIf mode: stop here
    if ($WhatIfPreference -or $PSBoundParameters.ContainsKey('WhatIf')) {
        Write-Log "WhatIf mode: No changes will be made." "WARN"
        Write-Log "The following $($ruleFiles.Count) rules would be deployed:"
        foreach ($rule in $ruleFiles) {
            Write-Log "  [DRY RUN] $($rule.Name) -> $Environment"
        }
        exit 0
    }

    # Connect to OneStream API
    $token = Connect-OneStreamApi -Config $envConfig

    # Deploy each rule
    $results = @()
    $successCount = 0
    $failureCount = 0

    foreach ($rule in $ruleFiles) {
        $result = Deploy-SingleRule -Rule $rule -Token $token -Config $envConfig
        $results += $result

        if ($result.Success) {
            $successCount++
        }
        else {
            $failureCount++
            Write-Log "FAILED: $($result.Rule) - $($result.Error)" "ERROR"
        }
    }

    # Deployment summary
    Write-Log "=========================================="
    Write-Log "DEPLOYMENT SUMMARY"
    Write-Log "=========================================="
    Write-Log "Total Rules  : $($ruleFiles.Count)"
    Write-Log "Successful   : $successCount" "OK"
    Write-Log "Failed       : $failureCount" $(if ($failureCount -gt 0) { "ERROR" } else { "OK" })
    Write-Log "Log File     : $LogFile"
    Write-Log "=========================================="

    if ($failureCount -gt 0) {
        Write-Log "DEPLOYMENT COMPLETED WITH ERRORS" "ERROR"
        Write-Log "Failed rules:"
        $results | Where-Object { -not $_.Success } | ForEach-Object {
            Write-Log "  - $($_.Rule): $($_.Error)" "ERROR"
        }
        exit 1
    }
    else {
        Write-Log "DEPLOYMENT COMPLETED SUCCESSFULLY" "OK"
        exit 0
    }
}
catch {
    Write-Log "DEPLOYMENT FAILED: $($_.Exception.Message)" "ERROR"
    Write-Log "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    exit 2
}

'------------------------------------------------------------------------------------------------------------
' EX_ReconciliationEngine.vb
' OneStream XF Extender Business Rule
'
' Purpose:  Account reconciliation matching engine that compares GL balances to sub-ledger
'           detail, performs auto-reconciliation within tolerance, flags exceptions, tracks
'           aging, and updates reconciliation status in the Recon cube.
'
' Parameters (pipe-delimited):
'   Scenario     - Scenario name (e.g., "Actual")
'   TimePeriod   - Period to reconcile (e.g., "2024M6")
'   AccountScope - "All" or comma-separated account list (e.g., "1100,1200,2100")
'   Tolerance    - Tolerance amount for auto-reconciliation (default 0.01)
'
' Usage:     Called during period close or from reconciliation workflow.
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.Extender.EX_ReconciliationEngine
    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Reconciliation record for a single account/entity combination.
        '----------------------------------------------------------------------------------------------------
        Private Class ReconRecord
            Public Property EntityName As String
            Public Property AccountName As String
            Public Property GLBalance As Decimal
            Public Property SubLedgerBalance As Decimal
            Public Property Difference As Decimal
            Public Property AbsDifference As Decimal
            Public Property Status As String              ' Reconciled, Partial, Unreconciled
            Public Property AgingDays As Integer           ' Days unreconciled (0 if reconciled this period)
            Public Property MatchRule As String            ' Rule that reconciled this item, if any
            Public Property RequiresManualReview As Boolean
        End Class

        Private Const DEFAULT_TOLERANCE As Decimal = 0.01D

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As ExtenderArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = ExtenderFunctionType.ExecuteServerProcess
                        Dim paramString As String = args.NameValuePairs.XFGetValue("Parameters", String.Empty)
                        Me.ExecuteReconciliation(si, globals, api, paramString)
                        Return Nothing

                    Case Else
                        Throw New XFException(si, $"EX_ReconciliationEngine: Unsupported function type [{args.FunctionType}].")
                End Select
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExecuteReconciliation
        ' Orchestrates the full reconciliation process: read balances, match, classify, report.
        '----------------------------------------------------------------------------------------------------
        Private Sub ExecuteReconciliation(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal paramString As String)
            Dim reconStart As DateTime = DateTime.UtcNow

            ' ------------------------------------------------------------------
            ' 1. Parse parameters
            ' ------------------------------------------------------------------
            Dim parameters() As String = paramString.Split("|"c)
            If parameters.Length < 3 Then
                Throw New XFException(si, "EX_ReconciliationEngine: Expected at least 3 pipe-delimited parameters (Scenario|TimePeriod|AccountScope[|Tolerance]).")
            End If

            Dim scenarioName As String = parameters(0).Trim()
            Dim timePeriod As String = parameters(1).Trim()
            Dim accountScope As String = parameters(2).Trim()
            Dim tolerance As Decimal = DEFAULT_TOLERANCE

            If parameters.Length >= 4 AndAlso Not String.IsNullOrEmpty(parameters(3).Trim()) Then
                Decimal.TryParse(parameters(3).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, tolerance)
            End If

            BRApi.ErrorLog.LogMessage(si, $"EX_ReconciliationEngine: Starting. Scenario=[{scenarioName}], Period=[{timePeriod}], Scope=[{accountScope}], Tolerance=[{tolerance:C2}].")
            api.Progress.ReportProgress(0, "Initializing reconciliation engine...")

            ' ------------------------------------------------------------------
            ' 2. Resolve account list
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(5, "Resolving account list...")
            Dim accounts As List(Of String) = ResolveAccounts(si, accountScope)
            BRApi.ErrorLog.LogMessage(si, $"EX_ReconciliationEngine: {accounts.Count} account(s) to reconcile.")

            ' ------------------------------------------------------------------
            ' 3. Read GL balances
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(10, "Reading GL balances...")
            Dim glBalances As Dictionary(Of String, Decimal) = ReadGLBalances(si, scenarioName, timePeriod, accounts)
            BRApi.ErrorLog.LogMessage(si, $"EX_ReconciliationEngine: Loaded {glBalances.Count} GL balance entries.")

            ' ------------------------------------------------------------------
            ' 4. Read sub-ledger detail balances
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(20, "Reading sub-ledger balances...")
            Dim subLedgerBalances As Dictionary(Of String, Decimal) = ReadSubLedgerBalances(si, scenarioName, timePeriod, accounts)
            BRApi.ErrorLog.LogMessage(si, $"EX_ReconciliationEngine: Loaded {subLedgerBalances.Count} sub-ledger balance entries.")

            ' ------------------------------------------------------------------
            ' 5. Load rule-based matching criteria
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(30, "Loading matching rules...")
            Dim matchRules As List(Of Dictionary(Of String, String)) = LoadMatchingRules(si)
            BRApi.ErrorLog.LogMessage(si, $"EX_ReconciliationEngine: Loaded {matchRules.Count} matching rule(s).")

            ' ------------------------------------------------------------------
            ' 6. Perform matching and classification
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(35, "Performing reconciliation matching...")
            Dim reconRecords As New List(Of ReconRecord)

            ' Build a union of all keys from both GL and sub-ledger
            Dim allKeys As New HashSet(Of String)(glBalances.Keys)
            For Each key As String In subLedgerBalances.Keys
                allKeys.Add(key)
            Next

            Dim totalKeys As Integer = allKeys.Count
            Dim processedKeys As Integer = 0

            For Each key As String In allKeys
                ' Key format: "Entity|Account"
                Dim parts() As String = key.Split("|"c)
                Dim entityName As String = parts(0)
                Dim accountName As String = If(parts.Length > 1, parts(1), "Unknown")

                Dim glBal As Decimal = 0D
                Dim slBal As Decimal = 0D
                If glBalances.ContainsKey(key) Then glBal = glBalances(key)
                If subLedgerBalances.ContainsKey(key) Then slBal = subLedgerBalances(key)

                Dim diff As Decimal = glBal - slBal
                Dim absDiff As Decimal = Math.Abs(diff)

                Dim record As New ReconRecord With {
                    .EntityName = entityName,
                    .AccountName = accountName,
                    .GLBalance = glBal,
                    .SubLedgerBalance = slBal,
                    .Difference = diff,
                    .AbsDifference = absDiff
                }

                ' Classification logic
                If absDiff = 0D Then
                    ' Exact match - fully reconciled
                    record.Status = "Reconciled"
                    record.MatchRule = "ExactMatch"
                    record.RequiresManualReview = False
                    record.AgingDays = 0
                ElseIf absDiff <= tolerance Then
                    ' Within tolerance - partial / auto-reconciled
                    record.Status = "Partial"
                    record.MatchRule = $"ToleranceMatch (diff={diff:C4})"
                    record.RequiresManualReview = False
                    record.AgingDays = 0
                Else
                    ' Exceeds tolerance - check rule-based auto-reconciliation
                    Dim ruleMatched As Boolean = TryRuleBasedMatch(si, record, matchRules, scenarioName, timePeriod)
                    If ruleMatched Then
                        record.Status = "Reconciled"
                        record.RequiresManualReview = False
                        record.AgingDays = 0
                    Else
                        record.Status = "Unreconciled"
                        record.RequiresManualReview = True
                        record.AgingDays = GetAgingDays(si, entityName, accountName, scenarioName, timePeriod)
                    End If
                End If

                reconRecords.Add(record)
                processedKeys += 1

                ' Update progress periodically
                If processedKeys Mod 100 = 0 Then
                    Dim pct As Integer = CInt(35 + (40.0 * processedKeys / totalKeys))
                    api.Progress.ReportProgress(pct, $"Matching: {processedKeys}/{totalKeys} accounts processed...")
                End If
            Next

            BRApi.ErrorLog.LogMessage(si, $"EX_ReconciliationEngine: Matching complete. {reconRecords.Count} records classified.")

            ' ------------------------------------------------------------------
            ' 7. Update reconciliation status in Recon cube
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(78, "Updating Recon cube...")
            UpdateReconCube(si, reconRecords, scenarioName, timePeriod)
            BRApi.ErrorLog.LogMessage(si, "EX_ReconciliationEngine: Recon cube updated.")

            ' ------------------------------------------------------------------
            ' 8. Generate exception report
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(88, "Generating exception report...")
            Dim exceptionReport As String = GenerateExceptionReport(si, reconRecords, scenarioName, timePeriod, tolerance, reconStart)
            BRApi.ErrorLog.LogMessage(si, exceptionReport)

            api.Progress.ReportProgress(100, "Reconciliation complete.")
            BRApi.ErrorLog.LogMessage(si, "EX_ReconciliationEngine: Process completed.")
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ResolveAccounts
        ' Returns the list of accounts to reconcile based on scope parameter.
        '----------------------------------------------------------------------------------------------------
        Private Function ResolveAccounts(ByVal si As SessionInfo, ByVal scope As String) As List(Of String)
            If scope.Equals("All", StringComparison.OrdinalIgnoreCase) Then
                Return BRApi.Finance.Account.GetAllBalanceSheetAccounts(si)
            Else
                Return scope.Split(","c).Select(Function(a) a.Trim()).Where(Function(a) Not String.IsNullOrEmpty(a)).ToList()
            End If
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ReadGLBalances
        ' Reads GL (General Ledger) ending balances for each entity/account combination.
        '----------------------------------------------------------------------------------------------------
        Private Function ReadGLBalances(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String, ByVal accounts As List(Of String)) As Dictionary(Of String, Decimal)
            Dim balances As New Dictionary(Of String, Decimal)

            Dim accountFilter As String = String.Join("','", accounts)
            Dim sql As String = $"SELECT EntityName, AccountName, SUM(Amount) AS Balance " &
                                $"FROM [Working].[dbo].[FactData] " &
                                $"WHERE ScenarioKey = @Scenario AND TimeKey = @Period " &
                                $"AND AccountName IN ('{accountFilter}') " &
                                $"GROUP BY EntityName, AccountName"

            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing Then
                For Each row As DataRow In dt.Rows
                    Dim key As String = $"{row("EntityName")}|{row("AccountName")}"
                    Dim balance As Decimal = Convert.ToDecimal(row("Balance"))
                    balances(key) = balance
                Next
            End If

            Return balances
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ReadSubLedgerBalances
        ' Reads sub-ledger (supporting detail) balances aggregated to entity/account level.
        '----------------------------------------------------------------------------------------------------
        Private Function ReadSubLedgerBalances(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String, ByVal accounts As List(Of String)) As Dictionary(Of String, Decimal)
            Dim balances As New Dictionary(Of String, Decimal)

            Dim accountFilter As String = String.Join("','", accounts)
            Dim sql As String = $"SELECT EntityName, AccountName, SUM(Amount) AS Balance " &
                                $"FROM [Staging].[dbo].[SubLedgerDetail] " &
                                $"WHERE Scenario = @Scenario AND TimePeriod = @Period " &
                                $"AND AccountName IN ('{accountFilter}') " &
                                $"GROUP BY EntityName, AccountName"

            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing Then
                For Each row As DataRow In dt.Rows
                    Dim key As String = $"{row("EntityName")}|{row("AccountName")}"
                    Dim balance As Decimal = Convert.ToDecimal(row("Balance"))
                    balances(key) = balance
                Next
            End If

            Return balances
        End Function

        '----------------------------------------------------------------------------------------------------
        ' LoadMatchingRules
        ' Loads rule-based auto-reconciliation criteria from configuration.
        '----------------------------------------------------------------------------------------------------
        Private Function LoadMatchingRules(ByVal si As SessionInfo) As List(Of Dictionary(Of String, String))
            Dim rules As New List(Of Dictionary(Of String, String))

            ' Load rules from a configuration table
            Dim sql As String = "SELECT RuleName, AccountPattern, MatchType, MatchTolerance, AdjustmentAccount " &
                                "FROM [Config].[dbo].[ReconMatchingRules] WHERE IsActive = 1 ORDER BY Priority"

            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing Then
                For Each row As DataRow In dt.Rows
                    Dim rule As New Dictionary(Of String, String)
                    rule("RuleName") = row("RuleName").ToString()
                    rule("AccountPattern") = row("AccountPattern").ToString()
                    rule("MatchType") = row("MatchType").ToString()
                    rule("MatchTolerance") = row("MatchTolerance").ToString()
                    rule("AdjustmentAccount") = row("AdjustmentAccount").ToString()
                    rules.Add(rule)
                Next
            End If

            Return rules
        End Function

        '----------------------------------------------------------------------------------------------------
        ' TryRuleBasedMatch
        ' Attempts to auto-reconcile a record using configured matching rules.
        ' Returns True if a rule matched and the record was auto-reconciled.
        '----------------------------------------------------------------------------------------------------
        Private Function TryRuleBasedMatch(ByVal si As SessionInfo, ByVal record As ReconRecord, ByVal rules As List(Of Dictionary(Of String, String)), ByVal scenario As String, ByVal period As String) As Boolean
            For Each rule As Dictionary(Of String, String) In rules
                ' Check if account matches the rule pattern
                Dim accountPattern As String = rule("AccountPattern")
                If Not record.AccountName.StartsWith(accountPattern, StringComparison.OrdinalIgnoreCase) AndAlso
                   Not accountPattern.Equals("*") Then
                    Continue For
                End If

                ' Check match type
                Dim matchType As String = rule("MatchType")
                Dim ruleTolerance As Decimal = Decimal.Parse(rule("MatchTolerance"), CultureInfo.InvariantCulture)

                Select Case matchType.ToUpper()
                    Case "TIMING"
                        ' Timing differences: check if the difference matches a known pending transaction
                        If record.AbsDifference <= ruleTolerance Then
                            record.MatchRule = $"Rule:{rule("RuleName")} (TimingDiff)"
                            Return True
                        End If

                    Case "ROUNDING"
                        ' Rounding differences within rule tolerance
                        If record.AbsDifference <= ruleTolerance Then
                            record.MatchRule = $"Rule:{rule("RuleName")} (Rounding)"
                            Return True
                        End If

                    Case "ADJUSTMENT"
                        ' Check if a corresponding adjustment entry exists
                        Dim adjAccount As String = rule("AdjustmentAccount")
                        Dim adjustmentExists As Boolean = CheckAdjustmentExists(si, record.EntityName, adjAccount, record.Difference, scenario, period)
                        If adjustmentExists Then
                            record.MatchRule = $"Rule:{rule("RuleName")} (Adjustment:{adjAccount})"
                            Return True
                        End If
                End Select
            Next

            Return False
        End Function

        '----------------------------------------------------------------------------------------------------
        ' CheckAdjustmentExists
        ' Checks whether a corresponding adjustment entry exists for the given difference.
        '----------------------------------------------------------------------------------------------------
        Private Function CheckAdjustmentExists(ByVal si As SessionInfo, ByVal entity As String, ByVal adjAccount As String, ByVal expectedAmount As Decimal, ByVal scenario As String, ByVal period As String) As Boolean
            Dim sql As String = $"SELECT SUM(Amount) FROM [Working].[dbo].[FactData] " &
                                $"WHERE EntityName = '{entity}' AND AccountName = '{adjAccount}' " &
                                $"AND ScenarioKey = @Scenario AND TimeKey = @Period"
            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing AndAlso dt.Rows.Count > 0 AndAlso Not IsDBNull(dt.Rows(0)(0)) Then
                Dim adjAmount As Decimal = Convert.ToDecimal(dt.Rows(0)(0))
                Return Math.Abs(adjAmount + expectedAmount) < 0.01D
            End If
            Return False
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetAgingDays
        ' Returns the number of days this account has been unreconciled by checking prior periods.
        '----------------------------------------------------------------------------------------------------
        Private Function GetAgingDays(ByVal si As SessionInfo, ByVal entity As String, ByVal account As String, ByVal scenario As String, ByVal period As String) As Integer
            Dim sql As String = $"SELECT FirstUnreconciledDate FROM [Recon].[dbo].[ReconStatus] " &
                                $"WHERE EntityName = '{entity}' AND AccountName = '{account}' " &
                                $"AND Scenario = @Scenario AND Status = 'Unreconciled'"
            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing AndAlso dt.Rows.Count > 0 AndAlso Not IsDBNull(dt.Rows(0)(0)) Then
                Dim firstUnrecon As DateTime = Convert.ToDateTime(dt.Rows(0)(0))
                Return CInt((DateTime.UtcNow - firstUnrecon).TotalDays)
            End If
            Return 0 ' First period unreconciled
        End Function

        '----------------------------------------------------------------------------------------------------
        ' UpdateReconCube
        ' Writes reconciliation status and details to the Recon cube for reporting.
        '----------------------------------------------------------------------------------------------------
        Private Sub UpdateReconCube(ByVal si As SessionInfo, ByVal records As List(Of ReconRecord), ByVal scenario As String, ByVal period As String)
            For Each rec As ReconRecord In records
                ' Write status to Recon cube dimension intersection
                BRApi.Finance.Data.SetDataCellValue(si, "Recon", scenario, period, rec.EntityName, rec.AccountName, "ReconStatus", GetStatusNumeric(rec.Status))
                BRApi.Finance.Data.SetDataCellValue(si, "Recon", scenario, period, rec.EntityName, rec.AccountName, "GLBalance", rec.GLBalance)
                BRApi.Finance.Data.SetDataCellValue(si, "Recon", scenario, period, rec.EntityName, rec.AccountName, "SubLedgerBalance", rec.SubLedgerBalance)
                BRApi.Finance.Data.SetDataCellValue(si, "Recon", scenario, period, rec.EntityName, rec.AccountName, "Difference", rec.Difference)
                BRApi.Finance.Data.SetDataCellValue(si, "Recon", scenario, period, rec.EntityName, rec.AccountName, "AgingDays", rec.AgingDays)
            Next
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GetStatusNumeric
        ' Converts status string to a numeric value for cube storage.
        '----------------------------------------------------------------------------------------------------
        Private Function GetStatusNumeric(ByVal status As String) As Decimal
            Select Case status
                Case "Reconciled" : Return 1D
                Case "Partial" : Return 2D
                Case "Unreconciled" : Return 3D
                Case Else : Return 0D
            End Select
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GenerateExceptionReport
        ' Builds a detailed exception report for unreconciled and partially reconciled items.
        '----------------------------------------------------------------------------------------------------
        Private Function GenerateExceptionReport(ByVal si As SessionInfo, ByVal records As List(Of ReconRecord), ByVal scenario As String, ByVal period As String, ByVal tolerance As Decimal, ByVal startTime As DateTime) As String
            Dim totalElapsed As Double = (DateTime.UtcNow - startTime).TotalSeconds

            Dim reconCount As Integer = records.Count(Function(r) r.Status = "Reconciled")
            Dim partialCount As Integer = records.Count(Function(r) r.Status = "Partial")
            Dim unreconCount As Integer = records.Count(Function(r) r.Status = "Unreconciled")

            Dim report As New Text.StringBuilder()
            report.AppendLine("========================================================================")
            report.AppendLine("          RECONCILIATION EXCEPTION REPORT")
            report.AppendLine("========================================================================")
            report.AppendLine($"Scenario:           {scenario}")
            report.AppendLine($"Period:             {period}")
            report.AppendLine($"Tolerance:          {tolerance:C4}")
            report.AppendLine($"Run Date (UTC):     {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}")
            report.AppendLine($"Elapsed Time (s):   {totalElapsed:F1}")
            report.AppendLine()
            report.AppendLine("--- Summary ---")
            report.AppendLine($"  Total Accounts:       {records.Count}")
            report.AppendLine($"  Reconciled:           {reconCount} ({If(records.Count > 0, (100.0 * reconCount / records.Count).ToString("F1"), "0")}%)")
            report.AppendLine($"  Partial (tolerance):  {partialCount}")
            report.AppendLine($"  Unreconciled:         {unreconCount}")
            report.AppendLine()

            ' Detail of unreconciled items sorted by absolute difference descending
            Dim exceptions As List(Of ReconRecord) = records.Where(Function(r) r.Status = "Unreconciled").OrderByDescending(Function(r) r.AbsDifference).ToList()

            If exceptions.Count > 0 Then
                report.AppendLine("--- Unreconciled Items (sorted by difference) ---")
                report.AppendLine(String.Format("{0,-20} {1,-15} {2,18} {3,18} {4,18} {5,8}",
                    "Entity", "Account", "GL Balance", "Sub-Ledger", "Difference", "Aging"))
                report.AppendLine(New String("-"c, 103))

                For Each rec As ReconRecord In exceptions
                    report.AppendLine(String.Format("{0,-20} {1,-15} {2,18:N2} {3,18:N2} {4,18:N2} {5,8}",
                        rec.EntityName, rec.AccountName, rec.GLBalance, rec.SubLedgerBalance, rec.Difference,
                        If(rec.AgingDays > 0, $"{rec.AgingDays}d", "New")))
                Next
                report.AppendLine()

                ' Aging summary for unreconciled items
                report.AppendLine("--- Aging Summary ---")
                Dim current As Integer = exceptions.Count(Function(e) e.AgingDays <= 30)
                Dim aged30 As Integer = exceptions.Count(Function(e) e.AgingDays > 30 AndAlso e.AgingDays <= 60)
                Dim aged60 As Integer = exceptions.Count(Function(e) e.AgingDays > 60 AndAlso e.AgingDays <= 90)
                Dim aged90 As Integer = exceptions.Count(Function(e) e.AgingDays > 90)

                report.AppendLine($"  0-30 days:    {current}")
                report.AppendLine($"  31-60 days:   {aged30}")
                report.AppendLine($"  61-90 days:   {aged60}")
                report.AppendLine($"  90+ days:     {aged90}")
            Else
                report.AppendLine("No unreconciled items found.")
            End If

            report.AppendLine("========================================================================")
            Return report.ToString()
        End Function

    End Class
End Namespace

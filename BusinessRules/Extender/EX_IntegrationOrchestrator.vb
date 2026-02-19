'------------------------------------------------------------------------------------------------------------
' EX_IntegrationOrchestrator.vb
' OneStream XF Extender Business Rule
'
' Purpose:  Multi-source ETL coordinator that manages a defined integration sequence across
'           SAP, Oracle, NetSuite, Workday, and MES source systems. Provides retry logic,
'           dependency management, row-count tracking at each pipeline stage, and comprehensive
'           reporting with notification support.
'
' Parameters (pipe-delimited):
'   Scenario      - Scenario name (e.g., "Actual")
'   TimePeriod    - Period to integrate (e.g., "2024M6")
'   SourceFilter  - "All" or comma-separated source names (e.g., "SAP,Oracle")
'
' Usage:     Called from Data Management sequence or scheduled process.
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

Namespace OneStream.BusinessRule.Extender.EX_IntegrationOrchestrator
    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Integration source definition: name, connector BR, dependency, and processing order.
        '----------------------------------------------------------------------------------------------------
        Private Class IntegrationSource
            Public Property SourceName As String
            Public Property ConnectorBRName As String
            Public Property TransformBRName As String
            Public Property DependsOn As String          ' Name of source that must complete first
            Public Property ProcessingOrder As Integer
            Public Property IsEnabled As Boolean

            ' Row-count tracking at each pipeline stage
            Public Property RowsExtracted As Long
            Public Property RowsValidated As Long
            Public Property RowsTransformed As Long
            Public Property RowsLoaded As Long
            Public Property RowsRejected As Long

            Public Property Status As String             ' Pending, Running, Success, Failed, Skipped
            Public Property ErrorMessage As String
            Public Property ElapsedSeconds As Double
        End Class

        Private Const MAX_RETRIES As Integer = 3
        Private Const BASE_BACKOFF_MS As Integer = 2000  ' 2 seconds base, doubles each retry

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As ExtenderArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = ExtenderFunctionType.ExecuteServerProcess
                        Dim paramString As String = args.NameValuePairs.XFGetValue("Parameters", String.Empty)
                        Me.ExecuteIntegration(si, globals, api, paramString)
                        Return Nothing

                    Case Else
                        Throw New XFException(si, $"EX_IntegrationOrchestrator: Unsupported function type [{args.FunctionType}].")
                End Select
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExecuteIntegration
        ' Main orchestration: builds source definitions, applies filters, and processes each source
        ' through the full ETL pipeline with retry logic and dependency management.
        '----------------------------------------------------------------------------------------------------
        Private Sub ExecuteIntegration(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal paramString As String)
            Dim overallStart As DateTime = DateTime.UtcNow

            ' ------------------------------------------------------------------
            ' 1. Parse parameters
            ' ------------------------------------------------------------------
            Dim parameters() As String = paramString.Split("|"c)
            If parameters.Length < 3 Then
                Throw New XFException(si, "EX_IntegrationOrchestrator: Expected 3 pipe-delimited parameters (Scenario|TimePeriod|SourceFilter).")
            End If

            Dim scenarioName As String = parameters(0).Trim()
            Dim timePeriod As String = parameters(1).Trim()
            Dim sourceFilter As String = parameters(2).Trim()

            BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: Starting. Scenario=[{scenarioName}], Period=[{timePeriod}], Filter=[{sourceFilter}].")
            api.Progress.ReportProgress(0, "Initializing integration orchestrator...")

            ' ------------------------------------------------------------------
            ' 2. Build integration source definitions (ordered sequence)
            ' ------------------------------------------------------------------
            Dim sources As List(Of IntegrationSource) = BuildSourceDefinitions()

            ' ------------------------------------------------------------------
            ' 3. Apply source filter
            ' ------------------------------------------------------------------
            If Not sourceFilter.Equals("All", StringComparison.OrdinalIgnoreCase) Then
                Dim allowedSources() As String = sourceFilter.Split(","c)
                Dim allowedSet As New HashSet(Of String)(allowedSources.Select(Function(s) s.Trim()), StringComparer.OrdinalIgnoreCase)

                For Each src As IntegrationSource In sources
                    If Not allowedSet.Contains(src.SourceName) Then
                        src.IsEnabled = False
                        src.Status = "Skipped"
                    End If
                Next

                BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: Source filter applied. {sources.Count(Function(s) s.IsEnabled)} source(s) enabled.")
            End If

            ' ------------------------------------------------------------------
            ' 4. Process each source in order
            ' ------------------------------------------------------------------
            Dim enabledSources As List(Of IntegrationSource) = sources.Where(Function(s) s.IsEnabled).OrderBy(Function(s) s.ProcessingOrder).ToList()
            Dim totalSources As Integer = enabledSources.Count
            Dim completedSources As Integer = 0

            For Each source As IntegrationSource In enabledSources
                Dim sourceStart As DateTime = DateTime.UtcNow
                Dim progressPct As Integer = CInt(5 + (80.0 * completedSources / totalSources))
                api.Progress.ReportProgress(progressPct, $"Processing source [{source.SourceName}] ({completedSources + 1}/{totalSources})...")

                ' Check dependency
                If Not String.IsNullOrEmpty(source.DependsOn) Then
                    Dim dependency As IntegrationSource = sources.FirstOrDefault(Function(s) s.SourceName.Equals(source.DependsOn, StringComparison.OrdinalIgnoreCase))
                    If dependency IsNot Nothing AndAlso dependency.Status <> "Success" Then
                        source.Status = "Skipped"
                        source.ErrorMessage = $"Dependency [{source.DependsOn}] did not complete successfully (status: {dependency.Status})."
                        BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: Skipping [{source.SourceName}] - {source.ErrorMessage}")
                        completedSources += 1
                        Continue For
                    End If
                End If

                ' Execute ETL pipeline with retry logic
                source.Status = "Running"
                Dim success As Boolean = False

                For attempt As Integer = 1 To MAX_RETRIES
                    Try
                        BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: [{source.SourceName}] attempt {attempt}/{MAX_RETRIES}.")

                        ' Step A: Connection test
                        api.Progress.ReportProgress(progressPct, $"[{source.SourceName}] Testing connection (attempt {attempt})...")
                        TestConnection(si, source)

                        ' Step B: Extraction
                        api.Progress.ReportProgress(progressPct + 2, $"[{source.SourceName}] Extracting data...")
                        source.RowsExtracted = ExecuteExtraction(si, source, scenarioName, timePeriod)
                        BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: [{source.SourceName}] Extracted {source.RowsExtracted:N0} rows.")

                        ' Step C: Staging validation
                        api.Progress.ReportProgress(progressPct + 4, $"[{source.SourceName}] Validating staged data...")
                        Dim validationResult As Long() = ValidateStagedData(si, source, scenarioName, timePeriod)
                        source.RowsValidated = validationResult(0)
                        source.RowsRejected = validationResult(1)
                        BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: [{source.SourceName}] Validated {source.RowsValidated:N0}, Rejected {source.RowsRejected:N0}.")

                        ' Step D: Transformation
                        api.Progress.ReportProgress(progressPct + 6, $"[{source.SourceName}] Transforming data...")
                        source.RowsTransformed = ExecuteTransformation(si, source, scenarioName, timePeriod)
                        BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: [{source.SourceName}] Transformed {source.RowsTransformed:N0} rows.")

                        ' Step E: Load to target cube
                        api.Progress.ReportProgress(progressPct + 8, $"[{source.SourceName}] Loading data to target cube...")
                        source.RowsLoaded = ExecuteLoad(si, source, scenarioName, timePeriod)
                        BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: [{source.SourceName}] Loaded {source.RowsLoaded:N0} rows.")

                        ' Step F: Post-load validation
                        api.Progress.ReportProgress(progressPct + 10, $"[{source.SourceName}] Validating loaded data...")
                        ValidateLoadedData(si, source, scenarioName, timePeriod)

                        ' Mark success
                        source.Status = "Success"
                        success = True
                        Exit For

                    Catch retryEx As Exception
                        source.ErrorMessage = retryEx.Message
                        BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: [{source.SourceName}] attempt {attempt} FAILED: {retryEx.Message}")

                        If attempt < MAX_RETRIES Then
                            ' Exponential backoff: 2s, 4s, 8s
                            Dim backoffMs As Integer = BASE_BACKOFF_MS * CInt(Math.Pow(2, attempt - 1))
                            BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: [{source.SourceName}] Retrying in {backoffMs}ms...")
                            Thread.Sleep(backoffMs)
                        End If
                    End Try
                Next

                If Not success Then
                    source.Status = "Failed"
                    BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: [{source.SourceName}] FAILED after {MAX_RETRIES} attempts.")
                End If

                source.ElapsedSeconds = (DateTime.UtcNow - sourceStart).TotalSeconds
                completedSources += 1
            Next

            ' ------------------------------------------------------------------
            ' 5. Generate integration summary report
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(90, "Generating integration summary...")
            Dim summaryReport As String = GenerateSummaryReport(si, sources, scenarioName, timePeriod, overallStart)
            BRApi.ErrorLog.LogMessage(si, summaryReport)

            ' ------------------------------------------------------------------
            ' 6. Send notification
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(95, "Sending notifications...")
            Dim hasFailures As Boolean = sources.Any(Function(s) s.Status = "Failed")
            SendNotification(si, scenarioName, timePeriod, hasFailures, summaryReport)

            api.Progress.ReportProgress(100, "Integration orchestration complete.")
            BRApi.ErrorLog.LogMessage(si, "EX_IntegrationOrchestrator: Process completed.")
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' BuildSourceDefinitions
        ' Defines the ordered set of integration sources with their connector and transform BRs.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildSourceDefinitions() As List(Of IntegrationSource)
            Dim sources As New List(Of IntegrationSource)

            ' SAP - GL and Financial data (no dependency, runs first)
            sources.Add(New IntegrationSource With {
                .SourceName = "SAP",
                .ConnectorBRName = "CN_SAP_Extract",
                .TransformBRName = "TR_SAP_Transform",
                .DependsOn = Nothing,
                .ProcessingOrder = 1,
                .IsEnabled = True,
                .Status = "Pending"
            })

            ' Oracle - ERP financials (no dependency, can run in parallel with SAP conceptually)
            sources.Add(New IntegrationSource With {
                .SourceName = "Oracle",
                .ConnectorBRName = "CN_Oracle_Extract",
                .TransformBRName = "TR_Oracle_Transform",
                .DependsOn = Nothing,
                .ProcessingOrder = 2,
                .IsEnabled = True,
                .Status = "Pending"
            })

            ' NetSuite - Subsidiary data (depends on Oracle GL being loaded first)
            sources.Add(New IntegrationSource With {
                .SourceName = "NetSuite",
                .ConnectorBRName = "CN_NetSuite_Extract",
                .TransformBRName = "TR_NetSuite_Transform",
                .DependsOn = "Oracle",
                .ProcessingOrder = 3,
                .IsEnabled = True,
                .Status = "Pending"
            })

            ' Workday - HR and headcount data (depends on SAP for cost center mapping)
            sources.Add(New IntegrationSource With {
                .SourceName = "Workday",
                .ConnectorBRName = "CN_Workday_Extract",
                .TransformBRName = "TR_Workday_Transform",
                .DependsOn = "SAP",
                .ProcessingOrder = 4,
                .IsEnabled = True,
                .Status = "Pending"
            })

            ' MES - Manufacturing execution / production data (depends on SAP)
            sources.Add(New IntegrationSource With {
                .SourceName = "MES",
                .ConnectorBRName = "CN_MES_Extract",
                .TransformBRName = "TR_MES_Transform",
                .DependsOn = "SAP",
                .ProcessingOrder = 5,
                .IsEnabled = True,
                .Status = "Pending"
            })

            Return sources
        End Function

        '----------------------------------------------------------------------------------------------------
        ' TestConnection
        ' Validates connectivity to the source system before attempting extraction.
        '----------------------------------------------------------------------------------------------------
        Private Sub TestConnection(ByVal si As SessionInfo, ByVal source As IntegrationSource)
            Dim isConnected As Boolean = BRApi.Utilities.ExternalConnector.TestConnection(si, source.ConnectorBRName)
            If Not isConnected Then
                Throw New XFException(si, $"Connection test failed for source [{source.SourceName}] using connector [{source.ConnectorBRName}].")
            End If
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ExecuteExtraction
        ' Calls the connector business rule to extract data from the source system into staging.
        '----------------------------------------------------------------------------------------------------
        Private Function ExecuteExtraction(ByVal si As SessionInfo, ByVal source As IntegrationSource, ByVal scenario As String, ByVal period As String) As Long
            Dim extractParams As String = $"{scenario}|{period}"
            BRApi.Utilities.ExternalConnector.Execute(si, source.ConnectorBRName, extractParams)

            ' Query staging to get extracted row count
            Dim sql As String = $"SELECT COUNT(*) FROM [Staging].[dbo].[Stage_{source.SourceName}] WHERE Scenario = @Scenario AND TimePeriod = @Period AND BatchStatus = 'Extracted'"
            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing AndAlso dt.Rows.Count > 0 Then
                Return Convert.ToInt64(dt.Rows(0)(0))
            End If
            Return 0
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ValidateStagedData
        ' Validates extracted data in staging: checks required fields, data types, ranges.
        ' Returns an array: (0) = valid rows, (1) = rejected rows.
        '----------------------------------------------------------------------------------------------------
        Private Function ValidateStagedData(ByVal si As SessionInfo, ByVal source As IntegrationSource, ByVal scenario As String, ByVal period As String) As Long()
            ' Execute validation stored procedure or BR
            Dim validationSql As String = $"EXEC [Staging].[dbo].[sp_Validate_{source.SourceName}] @Scenario = '{scenario}', @Period = '{period}'"
            BRApi.Database.ExecuteActionQuery(si, validationSql, True)

            ' Count valid rows
            Dim validSql As String = $"SELECT COUNT(*) FROM [Staging].[dbo].[Stage_{source.SourceName}] WHERE Scenario = @Scenario AND TimePeriod = @Period AND BatchStatus = 'Validated'"
            Dim dtValid As DataTable = BRApi.Database.ExecuteSql(si, validSql, True)
            Dim validCount As Long = If(dtValid IsNot Nothing AndAlso dtValid.Rows.Count > 0, Convert.ToInt64(dtValid.Rows(0)(0)), 0)

            ' Count rejected rows
            Dim rejectSql As String = $"SELECT COUNT(*) FROM [Staging].[dbo].[Stage_{source.SourceName}] WHERE Scenario = @Scenario AND TimePeriod = @Period AND BatchStatus = 'Rejected'"
            Dim dtReject As DataTable = BRApi.Database.ExecuteSql(si, rejectSql, True)
            Dim rejectCount As Long = If(dtReject IsNot Nothing AndAlso dtReject.Rows.Count > 0, Convert.ToInt64(dtReject.Rows(0)(0)), 0)

            Return New Long() {validCount, rejectCount}
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExecuteTransformation
        ' Runs transformation business rule to map source data to target dimension members.
        '----------------------------------------------------------------------------------------------------
        Private Function ExecuteTransformation(ByVal si As SessionInfo, ByVal source As IntegrationSource, ByVal scenario As String, ByVal period As String) As Long
            Dim transformParams As String = $"{scenario}|{period}"
            BRApi.BusinessRules.ExecuteBusinessRule(si, source.TransformBRName, transformParams)

            ' Count transformed rows
            Dim sql As String = $"SELECT COUNT(*) FROM [Staging].[dbo].[Stage_{source.SourceName}] WHERE Scenario = @Scenario AND TimePeriod = @Period AND BatchStatus = 'Transformed'"
            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing AndAlso dt.Rows.Count > 0 Then
                Return Convert.ToInt64(dt.Rows(0)(0))
            End If
            Return 0
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExecuteLoad
        ' Loads transformed data from staging into the target finance cube.
        '----------------------------------------------------------------------------------------------------
        Private Function ExecuteLoad(ByVal si As SessionInfo, ByVal source As IntegrationSource, ByVal scenario As String, ByVal period As String) As Long
            BRApi.Finance.Data.LoadDataFromStaging(si, source.SourceName, scenario, period)

            ' Count loaded rows
            Dim sql As String = $"SELECT COUNT(*) FROM [Staging].[dbo].[Stage_{source.SourceName}] WHERE Scenario = @Scenario AND TimePeriod = @Period AND BatchStatus = 'Loaded'"
            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing AndAlso dt.Rows.Count > 0 Then
                Return Convert.ToInt64(dt.Rows(0)(0))
            End If
            Return 0
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ValidateLoadedData
        ' Post-load validation: verifies row counts, control totals, and dimension member validity.
        '----------------------------------------------------------------------------------------------------
        Private Sub ValidateLoadedData(ByVal si As SessionInfo, ByVal source As IntegrationSource, ByVal scenario As String, ByVal period As String)
            ' Verify the loaded count matches transformed count
            If source.RowsLoaded <> source.RowsTransformed Then
                BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: WARNING [{source.SourceName}] Load mismatch - Transformed={source.RowsTransformed:N0}, Loaded={source.RowsLoaded:N0}.")
            End If

            ' Check for orphaned dimension members (unmapped)
            Dim orphanSql As String = $"SELECT COUNT(*) FROM [Staging].[dbo].[Stage_{source.SourceName}] WHERE Scenario = @Scenario AND TimePeriod = @Period AND BatchStatus = 'Loaded' AND MappingStatus = 'Unmapped'"
            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, orphanSql, True)
            Dim orphanCount As Long = If(dt IsNot Nothing AndAlso dt.Rows.Count > 0, Convert.ToInt64(dt.Rows(0)(0)), 0)

            If orphanCount > 0 Then
                BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: WARNING [{source.SourceName}] {orphanCount:N0} rows with unmapped dimension members.")
            End If
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GenerateSummaryReport
        ' Builds a comprehensive report of all source processing results.
        '----------------------------------------------------------------------------------------------------
        Private Function GenerateSummaryReport(ByVal si As SessionInfo, ByVal sources As List(Of IntegrationSource), ByVal scenario As String, ByVal period As String, ByVal overallStart As DateTime) As String
            Dim totalElapsed As Double = (DateTime.UtcNow - overallStart).TotalSeconds
            Dim report As New Text.StringBuilder()

            report.AppendLine("========================================================================")
            report.AppendLine("           INTEGRATION ORCHESTRATOR SUMMARY REPORT")
            report.AppendLine("========================================================================")
            report.AppendLine($"Scenario:        {scenario}")
            report.AppendLine($"Period:          {period}")
            report.AppendLine($"Run Date (UTC):  {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}")
            report.AppendLine($"Total Elapsed:   {totalElapsed:F1} seconds")
            report.AppendLine()

            ' Source-level detail
            report.AppendLine("--- Source Processing Detail ---")
            report.AppendLine(String.Format("{0,-12} {1,-10} {2,12} {3,12} {4,12} {5,12} {6,12} {7,10}",
                "Source", "Status", "Extracted", "Validated", "Transformed", "Loaded", "Rejected", "Time (s)"))
            report.AppendLine(New String("-"c, 96))

            For Each src As IntegrationSource In sources
                report.AppendLine(String.Format("{0,-12} {1,-10} {2,12:N0} {3,12:N0} {4,12:N0} {5,12:N0} {6,12:N0} {7,10:F1}",
                    src.SourceName,
                    src.Status,
                    src.RowsExtracted,
                    src.RowsValidated,
                    src.RowsTransformed,
                    src.RowsLoaded,
                    src.RowsRejected,
                    src.ElapsedSeconds))
            Next

            report.AppendLine(New String("-"c, 96))

            ' Totals
            Dim totalExtracted As Long = sources.Sum(Function(s) s.RowsExtracted)
            Dim totalValidated As Long = sources.Sum(Function(s) s.RowsValidated)
            Dim totalTransformed As Long = sources.Sum(Function(s) s.RowsTransformed)
            Dim totalLoaded As Long = sources.Sum(Function(s) s.RowsLoaded)
            Dim totalRejected As Long = sources.Sum(Function(s) s.RowsRejected)

            report.AppendLine(String.Format("{0,-12} {1,-10} {2,12:N0} {3,12:N0} {4,12:N0} {5,12:N0} {6,12:N0}",
                "TOTALS", "", totalExtracted, totalValidated, totalTransformed, totalLoaded, totalRejected))
            report.AppendLine()

            ' Failed sources detail
            Dim failedSources As List(Of IntegrationSource) = sources.Where(Function(s) s.Status = "Failed").ToList()
            If failedSources.Count > 0 Then
                report.AppendLine("--- Failed Sources ---")
                For Each src As IntegrationSource In failedSources
                    report.AppendLine($"  {src.SourceName}: {src.ErrorMessage}")
                Next
                report.AppendLine()
            End If

            ' Skipped sources
            Dim skippedSources As List(Of IntegrationSource) = sources.Where(Function(s) s.Status = "Skipped").ToList()
            If skippedSources.Count > 0 Then
                report.AppendLine("--- Skipped Sources ---")
                For Each src As IntegrationSource In skippedSources
                    report.AppendLine($"  {src.SourceName}: {If(String.IsNullOrEmpty(src.ErrorMessage), "Excluded by filter", src.ErrorMessage)}")
                Next
            End If

            report.AppendLine("========================================================================")
            Return report.ToString()
        End Function

        '----------------------------------------------------------------------------------------------------
        ' SendNotification
        ' Sends email notification to administrators on completion or failure.
        '----------------------------------------------------------------------------------------------------
        Private Sub SendNotification(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String, ByVal hasFailures As Boolean, ByVal report As String)
            Try
                Dim subject As String = If(hasFailures,
                    $"[ALERT] Integration Failed - {scenario} / {period}",
                    $"[OK] Integration Complete - {scenario} / {period}")

                Dim recipientList As String = "integration-admin@company.com;cfo-office@company.com"
                If hasFailures Then
                    recipientList &= ";it-support@company.com"
                End If

                BRApi.Utilities.SendMail(si, recipientList, subject, report)
                BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: Notification sent to [{recipientList}].")
            Catch mailEx As Exception
                BRApi.ErrorLog.LogMessage(si, $"EX_IntegrationOrchestrator: WARNING - Failed to send notification: {mailEx.Message}")
            End Try
        End Sub

    End Class
End Namespace

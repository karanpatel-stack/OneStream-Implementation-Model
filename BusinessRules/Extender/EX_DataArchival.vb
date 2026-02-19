'------------------------------------------------------------------------------------------------------------
' EX_DataArchival.vb
' OneStream XF Extender Business Rule
'
' Purpose:  Period-end data archival process. Copies closed-period data from the working cube
'           to an archive cube, verifies completeness, locks the period, generates an audit
'           manifest, compresses data, and cleans up staging tables.
'
' Parameters (pipe-delimited):
'   Scenario   - Scenario name (e.g., "Actual")
'   TimePeriod - Period to archive (e.g., "2024M6")
'
' Usage:     Triggered after period close or from a scheduled Data Management job.
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

Namespace OneStream.BusinessRule.Extender.EX_DataArchival
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As ExtenderArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = ExtenderFunctionType.ExecuteServerProcess
                        Dim paramString As String = args.NameValuePairs.XFGetValue("Parameters", String.Empty)
                        Me.ExecuteDataArchival(si, globals, api, paramString)
                        Return Nothing

                    Case Else
                        Throw New XFException(si, $"EX_DataArchival: Unsupported function type [{args.FunctionType}].")
                End Select
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExecuteDataArchival
        ' Orchestrates the full archival workflow: copy, verify, lock, manifest, compress, cleanup.
        '----------------------------------------------------------------------------------------------------
        Private Sub ExecuteDataArchival(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal paramString As String)
            Dim archivalStart As DateTime = DateTime.UtcNow

            ' ------------------------------------------------------------------
            ' 1. Parse parameters
            ' ------------------------------------------------------------------
            Dim parameters() As String = paramString.Split("|"c)
            If parameters.Length < 2 Then
                Throw New XFException(si, "EX_DataArchival: Expected 2 pipe-delimited parameters (Scenario|TimePeriod).")
            End If

            Dim scenarioName As String = parameters(0).Trim()
            Dim timePeriod As String = parameters(1).Trim()

            BRApi.ErrorLog.LogMessage(si, $"EX_DataArchival: Starting archival for Scenario=[{scenarioName}], Period=[{timePeriod}].")
            api.Progress.ReportProgress(0, $"Initializing archival for {scenarioName} / {timePeriod}...")

            ' ------------------------------------------------------------------
            ' 2. Validate period is closed
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(5, "Validating period status...")
            Dim periodStatus As String = BRApi.Finance.Time.GetPeriodStatus(si, scenarioName, timePeriod)

            If Not periodStatus.Equals("Closed", StringComparison.OrdinalIgnoreCase) Then
                Throw New XFException(si, $"EX_DataArchival: Period [{timePeriod}] status is [{periodStatus}]. Only closed periods can be archived.")
            End If

            BRApi.ErrorLog.LogMessage(si, $"EX_DataArchival: Period [{timePeriod}] status confirmed as Closed.")

            ' ------------------------------------------------------------------
            ' 3. Copy data from working cube to archive cube
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(10, "Copying data to archive cube...")
            Dim sourceCube As String = "Working"
            Dim archiveCube As String = "Archive"
            Dim sourceRowCount As Long = 0
            Dim archiveRowCount As Long = 0

            ' Get source row count before copy
            sourceRowCount = GetCubeRowCount(si, sourceCube, scenarioName, timePeriod)
            BRApi.ErrorLog.LogMessage(si, $"EX_DataArchival: Source cube [{sourceCube}] row count = {sourceRowCount:N0}.")

            If sourceRowCount = 0 Then
                BRApi.ErrorLog.LogMessage(si, "EX_DataArchival: WARNING - Source cube has zero rows. Archival will proceed but verify this is expected.")
            End If

            ' Execute the data copy to archive
            CopyDataToArchive(si, sourceCube, archiveCube, scenarioName, timePeriod)
            BRApi.ErrorLog.LogMessage(si, "EX_DataArchival: Data copy to archive cube completed.")

            ' ------------------------------------------------------------------
            ' 4. Verify archive completeness
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(40, "Verifying archive completeness...")
            archiveRowCount = GetCubeRowCount(si, archiveCube, scenarioName, timePeriod)
            BRApi.ErrorLog.LogMessage(si, $"EX_DataArchival: Archive cube [{archiveCube}] row count = {archiveRowCount:N0}.")

            If archiveRowCount <> sourceRowCount Then
                Throw New XFException(si, $"EX_DataArchival: VERIFICATION FAILED. Source rows [{sourceRowCount:N0}] <> Archive rows [{archiveRowCount:N0}]. Archival aborted.")
            End If

            BRApi.ErrorLog.LogMessage(si, "EX_DataArchival: Archive verification PASSED - row counts match.")

            ' ------------------------------------------------------------------
            ' 5. Lock the archived period (mark as read-only)
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(55, "Locking archived period...")
            LockPeriod(si, scenarioName, timePeriod)
            BRApi.ErrorLog.LogMessage(si, $"EX_DataArchival: Period [{timePeriod}] locked as read-only.")

            ' ------------------------------------------------------------------
            ' 6. Generate archive manifest
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(65, "Generating archive manifest...")
            Dim manifest As String = GenerateArchiveManifest(si, archiveCube, scenarioName, timePeriod, sourceRowCount, archiveRowCount)
            BRApi.ErrorLog.LogMessage(si, manifest)

            ' ------------------------------------------------------------------
            ' 7. Compress archived data
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(75, "Compressing archived data...")
            CompressArchiveData(si, archiveCube, scenarioName, timePeriod)
            BRApi.ErrorLog.LogMessage(si, "EX_DataArchival: Archive data compression completed.")

            ' ------------------------------------------------------------------
            ' 8. Clean up staging tables for archived period
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(85, "Cleaning up staging tables...")
            Dim cleanedRows As Long = CleanupStagingTables(si, scenarioName, timePeriod)
            BRApi.ErrorLog.LogMessage(si, $"EX_DataArchival: Cleaned {cleanedRows:N0} rows from staging tables.")

            ' ------------------------------------------------------------------
            ' 9. Log archival completion for audit trail
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(95, "Logging audit record...")
            Dim totalElapsed As Double = (DateTime.UtcNow - archivalStart).TotalSeconds

            Dim auditEntry As New Text.StringBuilder()
            auditEntry.AppendLine("=== DATA ARCHIVAL AUDIT LOG ===")
            auditEntry.AppendLine($"Scenario:            {scenarioName}")
            auditEntry.AppendLine($"Period:              {timePeriod}")
            auditEntry.AppendLine($"Source Cube:         {sourceCube}")
            auditEntry.AppendLine($"Archive Cube:        {archiveCube}")
            auditEntry.AppendLine($"Rows Archived:       {archiveRowCount:N0}")
            auditEntry.AppendLine($"Verification:        PASSED")
            auditEntry.AppendLine($"Period Locked:       Yes")
            auditEntry.AppendLine($"Staging Cleaned:     {cleanedRows:N0} rows")
            auditEntry.AppendLine($"Archived By:         {si.UserName}")
            auditEntry.AppendLine($"Archived At (UTC):   {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}")
            auditEntry.AppendLine($"Elapsed Time (s):    {totalElapsed:F1}")

            BRApi.ErrorLog.LogMessage(si, auditEntry.ToString())
            WriteAuditRecord(si, scenarioName, timePeriod, archiveRowCount, totalElapsed)

            api.Progress.ReportProgress(100, "Data archival complete.")
            BRApi.ErrorLog.LogMessage(si, "EX_DataArchival: Archival process completed successfully.")
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GetCubeRowCount
        ' Returns the number of data rows in a cube for the given scenario/period intersection.
        '----------------------------------------------------------------------------------------------------
        Private Function GetCubeRowCount(ByVal si As SessionInfo, ByVal cubeName As String, ByVal scenario As String, ByVal period As String) As Long
            Dim sql As String = $"SELECT COUNT(*) FROM [{cubeName}].[dbo].[FactData] WHERE ScenarioKey = @Scenario AND TimeKey = @Period"
            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing AndAlso dt.Rows.Count > 0 Then
                Return Convert.ToInt64(dt.Rows(0)(0))
            End If
            Return 0
        End Function

        '----------------------------------------------------------------------------------------------------
        ' CopyDataToArchive
        ' Copies all data rows from the source cube to the archive cube for the specified slice.
        '----------------------------------------------------------------------------------------------------
        Private Sub CopyDataToArchive(ByVal si As SessionInfo, ByVal sourceCube As String, ByVal archiveCube As String, ByVal scenario As String, ByVal period As String)
            BRApi.Finance.Data.CopyCubeData(si, sourceCube, archiveCube, scenario, period)
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' LockPeriod
        ' Marks the period as read-only so no further data entry or calculations can occur.
        '----------------------------------------------------------------------------------------------------
        Private Sub LockPeriod(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String)
            BRApi.Finance.Time.SetPeriodStatus(si, scenario, period, "Locked")
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GenerateArchiveManifest
        ' Builds a detailed manifest listing entities and accounts with row counts.
        '----------------------------------------------------------------------------------------------------
        Private Function GenerateArchiveManifest(ByVal si As SessionInfo, ByVal cubeName As String, ByVal scenario As String, ByVal period As String, ByVal sourceRows As Long, ByVal archiveRows As Long) As String
            Dim manifest As New Text.StringBuilder()
            manifest.AppendLine("=== ARCHIVE MANIFEST ===")
            manifest.AppendLine($"Generated:     {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
            manifest.AppendLine($"Scenario:      {scenario}")
            manifest.AppendLine($"Period:        {period}")
            manifest.AppendLine($"Source Rows:   {sourceRows:N0}")
            manifest.AppendLine($"Archive Rows:  {archiveRows:N0}")
            manifest.AppendLine()

            ' Entity-level breakdown
            manifest.AppendLine("--- Entity Breakdown ---")
            Dim entitySql As String = $"SELECT EntityName, COUNT(*) AS RowCnt FROM [{cubeName}].[dbo].[FactData] WHERE ScenarioKey = @Scenario AND TimeKey = @Period GROUP BY EntityName ORDER BY EntityName"
            Dim dtEntities As DataTable = BRApi.Database.ExecuteSql(si, entitySql, True)
            If dtEntities IsNot Nothing Then
                For Each row As DataRow In dtEntities.Rows
                    manifest.AppendLine($"  {row("EntityName").ToString().PadRight(30)} {Convert.ToInt64(row("RowCnt")):N0} rows")
                Next
            End If

            manifest.AppendLine()

            ' Account-level summary
            manifest.AppendLine("--- Account Summary ---")
            Dim accountSql As String = $"SELECT AccountName, COUNT(*) AS RowCnt FROM [{cubeName}].[dbo].[FactData] WHERE ScenarioKey = @Scenario AND TimeKey = @Period GROUP BY AccountName ORDER BY RowCnt DESC"
            Dim dtAccounts As DataTable = BRApi.Database.ExecuteSql(si, accountSql, True)
            If dtAccounts IsNot Nothing Then
                Dim topN As Integer = Math.Min(20, dtAccounts.Rows.Count)
                For i As Integer = 0 To topN - 1
                    Dim row As DataRow = dtAccounts.Rows(i)
                    manifest.AppendLine($"  {row("AccountName").ToString().PadRight(30)} {Convert.ToInt64(row("RowCnt")):N0} rows")
                Next
                If dtAccounts.Rows.Count > 20 Then
                    manifest.AppendLine($"  ... and {dtAccounts.Rows.Count - 20} more accounts")
                End If
            End If

            Return manifest.ToString()
        End Function

        '----------------------------------------------------------------------------------------------------
        ' CompressArchiveData
        ' Triggers compression on the archive cube data for storage optimization.
        '----------------------------------------------------------------------------------------------------
        Private Sub CompressArchiveData(ByVal si As SessionInfo, ByVal cubeName As String, ByVal scenario As String, ByVal period As String)
            ' Invoke cube-level compression or database-level page compression
            BRApi.Finance.Data.CompressCubeData(si, cubeName, scenario, period)
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' CleanupStagingTables
        ' Removes staging data for the archived period to reclaim space.
        '----------------------------------------------------------------------------------------------------
        Private Function CleanupStagingTables(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String) As Long
            Dim deletedRows As Long = 0
            Dim stagingTables As New List(Of String) From {
                "StageGL", "StageSubLedger", "StageHR", "StageFixedAssets", "StageMappingLog"
            }

            For Each tableName As String In stagingTables
                Try
                    Dim sql As String = $"DELETE FROM [Staging].[dbo].[{tableName}] WHERE ScenarioKey = @Scenario AND TimeKey = @Period"
                    Dim affected As Integer = BRApi.Database.ExecuteActionQuery(si, sql, True)
                    deletedRows += affected
                    BRApi.ErrorLog.LogMessage(si, $"EX_DataArchival: Cleaned {affected:N0} rows from [{tableName}].")
                Catch ex As Exception
                    BRApi.ErrorLog.LogMessage(si, $"EX_DataArchival: WARNING - Failed to clean [{tableName}]: {ex.Message}")
                End Try
            Next

            Return deletedRows
        End Function

        '----------------------------------------------------------------------------------------------------
        ' WriteAuditRecord
        ' Persists an audit record for compliance tracking.
        '----------------------------------------------------------------------------------------------------
        Private Sub WriteAuditRecord(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String, ByVal rowCount As Long, ByVal elapsedSeconds As Double)
            Dim sql As String = "INSERT INTO [Audit].[dbo].[ArchivalLog] (Scenario, TimePeriod, RowsArchived, ArchivedBy, ArchivedAtUtc, ElapsedSeconds) " &
                                "VALUES (@Scenario, @Period, @RowCount, @User, @ArchiveDate, @Elapsed)"
            BRApi.Database.ExecuteActionQuery(si, sql, True)
        End Sub

    End Class
End Namespace

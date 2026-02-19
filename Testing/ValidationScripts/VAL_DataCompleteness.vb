'------------------------------------------------------------------------------------------------------------
' OneStream XF Validation Script: VAL_DataCompleteness
'------------------------------------------------------------------------------------------------------------
' Purpose:     Checks that all entities have submitted data for the current period by validating
'              that a set of required accounts have non-zero values. Reports missing accounts
'              per entity and an overall completeness score.
'
' Required Accounts:
'   TotalRevenue, TotalCOGS, TotalOPEX, Cash, TotalAssets, TotalLiabilities
'
' Output:      DataTable with columns: Entity, RequiredAccount, HasValue, Value, Status (Pass/Fail)
'              Plus a summary row per entity showing completeness percentage.
'
' Author:      OneStream Administrator
' Created:     2026-02-18
' Modified:    2026-02-18
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Math
Imports Microsoft.VisualBasic
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.DashboardDataAdapter.VAL_DataCompleteness

    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for the data completeness validation rule.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As DashboardDataAdapterArgs) As Object
            Try
                ' Build the results DataTable
                Dim resultsTable As New DataTable("DataCompletenessValidation")
                resultsTable.Columns.Add("Entity", GetType(String))
                resultsTable.Columns.Add("RequiredAccount", GetType(String))
                resultsTable.Columns.Add("HasValue", GetType(Boolean))
                resultsTable.Columns.Add("Value", GetType(Double))
                resultsTable.Columns.Add("Status", GetType(String))

                ' Summary table for per-entity completeness scores
                Dim summaryTable As New DataTable("CompletenessSummary")
                summaryTable.Columns.Add("Entity", GetType(String))
                summaryTable.Columns.Add("TotalRequired", GetType(Integer))
                summaryTable.Columns.Add("TotalPresent", GetType(Integer))
                summaryTable.Columns.Add("MissingAccounts", GetType(String))
                summaryTable.Columns.Add("CompletenessPercent", GetType(Double))
                summaryTable.Columns.Add("Status", GetType(String))

                ' All leaf entities that should have submitted data
                Dim leafEntities As String() = { _
                    "Plant_US01_Detroit", "Plant_US02_Houston", "Plant_US03_Charlotte", _
                    "Plant_CA01_Toronto", "Plant_MX01_Monterrey", _
                    "Plant_DE01_Munich", "Plant_DE02_Stuttgart", _
                    "Plant_UK01_Birmingham", "Plant_FR01_Lyon", _
                    "Plant_CN01_Shanghai", "Plant_CN02_Shenzhen", _
                    "Plant_JP01_Osaka", "Plant_IN01_Pune", _
                    "SS_IT", "SS_HR", "SS_Finance" _
                }

                ' Required accounts that must have non-zero values for data to be considered complete
                Dim requiredAccounts As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                requiredAccounts.Add("TotalRevenue", "Total Revenue")
                requiredAccounts.Add("TotalCOGS", "Total Cost of Goods Sold")
                requiredAccounts.Add("TotalOPEX", "Total Operating Expenses")
                requiredAccounts.Add("Cash", "Cash and Cash Equivalents")
                requiredAccounts.Add("TotalAssets", "Total Assets")
                requiredAccounts.Add("TotalLiabilities", "Total Liabilities")

                ' Minimum completeness percentage to pass (100% = all required accounts have data)
                Dim minCompletenessForPass As Double = 100.0

                Dim overallPresent As Integer = 0
                Dim overallRequired As Integer = 0

                BRApi.ErrorLog.LogMessage(si, "VAL_DataCompleteness: Starting data completeness validation...")

                For Each entityName As String In leafEntities
                    Dim entityPresent As Integer = 0
                    Dim missingList As New List(Of String)

                    For Each acct As KeyValuePair(Of String, String) In requiredAccounts
                        ' Read the account value for the current period
                        Dim pov As String = $"E#{entityName}:A#{acct.Key}:C#C_Local:S#Actual:F#F_Closing:O#O_None"
                        Dim cellValue As Double = BRApi.Finance.Data.GetDataCell(si, pov).CellAmount

                        Dim hasValue As Boolean = (cellValue <> 0)
                        Dim status As String = If(hasValue, "PASS", "FAIL")

                        If hasValue Then
                            entityPresent += 1
                        Else
                            missingList.Add(acct.Key)
                        End If

                        ' Add detail row
                        Dim detailRow As DataRow = resultsTable.NewRow()
                        detailRow("Entity") = entityName
                        detailRow("RequiredAccount") = $"{acct.Key} ({acct.Value})"
                        detailRow("HasValue") = hasValue
                        detailRow("Value") = Math.Round(cellValue, 2)
                        detailRow("Status") = status
                        resultsTable.Rows.Add(detailRow)
                    Next

                    ' Calculate entity completeness percentage
                    Dim totalRequired As Integer = requiredAccounts.Count
                    Dim completenessPercent As Double = (CDbl(entityPresent) / CDbl(totalRequired)) * 100.0
                    Dim entityStatus As String = If(completenessPercent >= minCompletenessForPass, "PASS", "FAIL")

                    Dim missingStr As String = If(missingList.Count > 0, String.Join(", ", missingList), "None")

                    ' Add summary row
                    Dim summRow As DataRow = summaryTable.NewRow()
                    summRow("Entity") = entityName
                    summRow("TotalRequired") = totalRequired
                    summRow("TotalPresent") = entityPresent
                    summRow("MissingAccounts") = missingStr
                    summRow("CompletenessPercent") = Math.Round(completenessPercent, 1)
                    summRow("Status") = entityStatus
                    summaryTable.Rows.Add(summRow)

                    overallPresent += entityPresent
                    overallRequired += totalRequired

                    ' Log failures
                    If entityStatus = "FAIL" Then
                        BRApi.ErrorLog.LogMessage(si, _
                            $"  FAIL: [{entityName}] Completeness={completenessPercent:F1}%, Missing: {missingStr}")
                    End If
                Next

                ' Overall completeness score
                Dim overallPct As Double = If(overallRequired > 0, (CDbl(overallPresent) / CDbl(overallRequired)) * 100.0, 0)
                Dim overallFailCount As Integer = summaryTable.Select("Status = 'FAIL'").Length

                BRApi.ErrorLog.LogMessage(si, _
                    $"VAL_DataCompleteness: Overall Score = {overallPct:F1}% ({overallPresent}/{overallRequired})")
                BRApi.ErrorLog.LogMessage(si, _
                    $"VAL_DataCompleteness: Entities: {leafEntities.Length} total, {leafEntities.Length - overallFailCount} pass, {overallFailCount} fail")

                ' Return a DataSet containing both detail and summary tables
                Dim resultSet As New DataSet("DataCompletenessResults")
                resultSet.Tables.Add(resultsTable)
                resultSet.Tables.Add(summaryTable)

                Return resultSet

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "VAL_DataCompleteness.Main", ex.Message))
            End Try
        End Function

    End Class

End Namespace

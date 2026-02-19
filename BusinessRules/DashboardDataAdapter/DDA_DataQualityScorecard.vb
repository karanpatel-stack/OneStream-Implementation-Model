'------------------------------------------------------------------------------------------------------------
' DDA_DataQualityScorecard
' Dashboard DataAdapter Business Rule
' Purpose: Data quality metrics tracking validation rule pass/fail by entity and submission status
'------------------------------------------------------------------------------------------------------------
Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.Linq
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_DataQualityScorecard
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("DataQualityScorecard")
                        dt.Columns.Add("EntityName", GetType(String))
                        dt.Columns.Add("TotalRules", GetType(Integer))
                        dt.Columns.Add("PassedRules", GetType(Integer))
                        dt.Columns.Add("FailedRules", GetType(Integer))
                        dt.Columns.Add("DQScore", GetType(Double))
                        dt.Columns.Add("SubmissionStatus", GetType(String))
                        dt.Columns.Add("LastUpdated", GetType(String))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim parentEntity As String = args.NameValuePairs.XFGetValue("ParentEntity", "AllEntities")

                        ' Define entities to evaluate
                        Dim entities As New List(Of String)()
                        entities.Add("Entity_US01")
                        entities.Add("Entity_US02")
                        entities.Add("Entity_UK01")
                        entities.Add("Entity_DE01")
                        entities.Add("Entity_FR01")
                        entities.Add("Entity_CN01")
                        entities.Add("Entity_JP01")
                        entities.Add("Entity_BR01")
                        entities.Add("Entity_IN01")
                        entities.Add("Entity_MX01")

                        ' Define data quality validation rules
                        Dim validationRules As New List(Of Tuple(Of String, String, String))()
                        validationRules.Add(Tuple.Create("BS Balances", "A#TotalAssets", "A#TotalLiabilitiesEquity"))
                        validationRules.Add(Tuple.Create("Revenue NonNeg", "A#Revenue", ""))
                        validationRules.Add(Tuple.Create("IC Elimination", "A#IC_Elim_Check", ""))
                        validationRules.Add(Tuple.Create("COGS vs Revenue", "A#COGS", "A#Revenue"))
                        validationRules.Add(Tuple.Create("Cash NonNeg", "A#CashAndEquivalents", ""))
                        validationRules.Add(Tuple.Create("NI Crossfoot", "A#NetIncome_IS", "A#NetIncome_RE"))
                        validationRules.Add(Tuple.Create("Fixed Assets Roll", "A#FA_RollForward_Check", ""))
                        validationRules.Add(Tuple.Create("Equity Roll", "A#Equity_RollForward_Check", ""))

                        For Each entityName In entities
                            Dim totalRules As Integer = validationRules.Count
                            Dim passedRules As Integer = 0
                            Dim failedRules As Integer = 0

                            For Each rule In validationRules
                                Dim ruleName As String = rule.Item1
                                Dim account1 As String = rule.Item2
                                Dim account2 As String = rule.Item3

                                Dim ruleResult As Boolean = EvaluateRule(si, scenarioName, timeName, entityName, ruleName, account1, account2)
                                If ruleResult Then
                                    passedRules += 1
                                Else
                                    failedRules += 1
                                End If
                            Next

                            ' Calculate data quality score as a percentage
                            Dim dqScore As Double = 0
                            If totalRules > 0 Then
                                dqScore = (CDbl(passedRules) / CDbl(totalRules)) * 100
                            End If

                            ' Determine submission status from workflow
                            Dim submissionStatus As String = GetSubmissionStatus(si, scenarioName, timeName, entityName)

                            ' Get last updated timestamp from workflow metadata
                            Dim lastUpdated As String = GetLastUpdatedDate(si, scenarioName, timeName, entityName)

                            Dim row As DataRow = dt.NewRow()
                            row("EntityName") = entityName.Replace("Entity_", "")
                            row("TotalRules") = totalRules
                            row("PassedRules") = passedRules
                            row("FailedRules") = failedRules
                            row("DQScore") = Math.Round(dqScore, 1)
                            row("SubmissionStatus") = submissionStatus
                            row("LastUpdated") = lastUpdated
                            dt.Rows.Add(row)
                        Next

                        ' Sort by DQ score ascending so worst entities appear first
                        dt.DefaultView.Sort = "DQScore ASC"
                        Dim sortedDt As DataTable = dt.DefaultView.ToTable()

                        Return sortedDt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        Private Function EvaluateRule(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal ruleName As String, ByVal account1 As String, ByVal account2 As String) As Boolean
            Try
                Dim value1 As Double = FetchAmount(si, scenario, timeName, entity, account1)

                Select Case ruleName
                    Case "BS Balances"
                        ' Assets must equal Liabilities + Equity within tolerance
                        Dim value2 As Double = FetchAmount(si, scenario, timeName, entity, account2)
                        Return Math.Abs(value1 - value2) < 1.0

                    Case "Revenue NonNeg"
                        ' Revenue should be non-negative (credit balance)
                        Return value1 >= 0

                    Case "IC Elimination"
                        ' IC elimination check account should be zero after eliminations
                        Return Math.Abs(value1) < 1.0

                    Case "COGS vs Revenue"
                        ' COGS should not exceed revenue (gross margin should be positive)
                        Dim revenue As Double = FetchAmount(si, scenario, timeName, entity, account2)
                        If revenue = 0 Then Return True
                        Return (value1 / revenue) <= 1.0

                    Case "Cash NonNeg"
                        ' Cash position should not be negative
                        Return value1 >= 0

                    Case "NI Crossfoot"
                        ' Net income on IS should match retained earnings movement
                        Dim value2 As Double = FetchAmount(si, scenario, timeName, entity, account2)
                        Return Math.Abs(value1 - value2) < 1.0

                    Case "Fixed Assets Roll", "Equity Roll"
                        ' Roll-forward check account should be zero (beginning + activity = ending)
                        Return Math.Abs(value1) < 1.0

                    Case Else
                        Return True
                End Select
            Catch
                Return False
            End Try
        End Function

        Private Function GetSubmissionStatus(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String) As String
            Try
                ' Query workflow profile status for the entity
                Dim wfStatus As String = FetchWorkflowText(si, scenario, timeName, entity, "A#WF_SubmissionStatus")
                If Not String.IsNullOrEmpty(wfStatus) Then Return wfStatus
            Catch
                ' Default to Not Submitted on error
            End Try
            Return "Not Submitted"
        End Function

        Private Function GetLastUpdatedDate(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String) As String
            Try
                Dim dateText As String = FetchWorkflowText(si, scenario, timeName, entity, "A#WF_LastUpdated")
                If Not String.IsNullOrEmpty(dateText) Then Return dateText
            Catch
                ' Default on error
            End Try
            Return "N/A"
        End Function

        Private Function FetchAmount(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
            End Try
            Return 0
        End Function

        Private Function FetchWorkflowText(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String) As String
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount.ToString()
                End If
            Catch
            End Try
            Return String.Empty
        End Function

    End Class
End Namespace

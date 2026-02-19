'------------------------------------------------------------------------------------------------------------
' DDA_ConsolidationStatus
' Dashboard DataAdapter Business Rule
' Purpose: Close and consolidation workflow status tracking by entity for the current period
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_ConsolidationStatus
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("ConsolidationStatus")
                        dt.Columns.Add("EntityName", GetType(String))
                        dt.Columns.Add("Region", GetType(String))
                        dt.Columns.Add("CurrentStep", GetType(String))
                        dt.Columns.Add("StepNumber", GetType(Integer))
                        dt.Columns.Add("TotalSteps", GetType(Integer))
                        dt.Columns.Add("PctComplete", GetType(Double))
                        dt.Columns.Add("Status", GetType(String))
                        dt.Columns.Add("DueDate", GetType(String))
                        dt.Columns.Add("SubmittedBy", GetType(String))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim workflowProfile As String = args.NameValuePairs.XFGetValue("WorkflowProfile", "MonthlyClose")

                        ' Define the close process steps in order
                        Dim closeSteps As New List(Of String)()
                        closeSteps.Add("Data Load")
                        closeSteps.Add("Journal Entries")
                        closeSteps.Add("Allocations")
                        closeSteps.Add("IC Transactions")
                        closeSteps.Add("IC Reconciliation")
                        closeSteps.Add("Local Adjustments")
                        closeSteps.Add("Translation")
                        closeSteps.Add("Elimination")
                        closeSteps.Add("Consolidation")
                        closeSteps.Add("Review & Certify")

                        Dim totalSteps As Integer = closeSteps.Count

                        ' Define entities with their regions and due dates
                        Dim entityDefinitions As New List(Of Tuple(Of String, String, String))()
                        entityDefinitions.Add(Tuple.Create("Entity_US01", "North America", "WD+3"))
                        entityDefinitions.Add(Tuple.Create("Entity_US02", "North America", "WD+3"))
                        entityDefinitions.Add(Tuple.Create("Entity_CA01", "North America", "WD+3"))
                        entityDefinitions.Add(Tuple.Create("Entity_UK01", "Europe", "WD+4"))
                        entityDefinitions.Add(Tuple.Create("Entity_DE01", "Europe", "WD+4"))
                        entityDefinitions.Add(Tuple.Create("Entity_FR01", "Europe", "WD+4"))
                        entityDefinitions.Add(Tuple.Create("Entity_CN01", "Asia Pacific", "WD+5"))
                        entityDefinitions.Add(Tuple.Create("Entity_JP01", "Asia Pacific", "WD+5"))
                        entityDefinitions.Add(Tuple.Create("Entity_IN01", "Asia Pacific", "WD+5"))
                        entityDefinitions.Add(Tuple.Create("Entity_BR01", "Latin America", "WD+4"))
                        entityDefinitions.Add(Tuple.Create("Entity_MX01", "Latin America", "WD+4"))

                        For Each entDef In entityDefinitions
                            Dim entityName As String = entDef.Item1
                            Dim region As String = entDef.Item2
                            Dim dueDate As String = entDef.Item3

                            ' Retrieve workflow step status from the engine
                            Dim currentStepNumber As Integer = GetCurrentStepNumber(si, scenarioName, timeName, entityName, workflowProfile, totalSteps)
                            Dim currentStepName As String = "Not Started"
                            If currentStepNumber > 0 AndAlso currentStepNumber <= closeSteps.Count Then
                                currentStepName = closeSteps(currentStepNumber - 1)
                            ElseIf currentStepNumber > closeSteps.Count Then
                                currentStepName = "Complete"
                            End If

                            ' Calculate percent complete
                            Dim pctComplete As Double = 0
                            If currentStepNumber > totalSteps Then
                                pctComplete = 1.0
                            ElseIf currentStepNumber > 0 Then
                                pctComplete = CDbl(currentStepNumber - 1) / CDbl(totalSteps)
                            End If

                            ' Determine overall status
                            Dim status As String = "NotStarted"
                            If currentStepNumber > totalSteps Then
                                status = "Complete"
                            ElseIf currentStepNumber > 0 Then
                                status = "InProgress"
                            End If

                            ' Get the user who last submitted/updated workflow
                            Dim submittedBy As String = GetSubmittedByUser(si, scenarioName, timeName, entityName, workflowProfile)

                            ' Calculate actual due date based on close calendar
                            Dim resolvedDueDate As String = ResolveDueDate(timeName, dueDate)

                            Dim row As DataRow = dt.NewRow()
                            row("EntityName") = entityName.Replace("Entity_", "")
                            row("Region") = region
                            row("CurrentStep") = currentStepName
                            row("StepNumber") = currentStepNumber
                            row("TotalSteps") = totalSteps
                            row("PctComplete") = Math.Round(pctComplete, 4)
                            row("Status") = status
                            row("DueDate") = resolvedDueDate
                            row("SubmittedBy") = submittedBy
                            dt.Rows.Add(row)
                        Next

                        ' Sort by PctComplete ascending so entities needing attention appear first
                        dt.DefaultView.Sort = "PctComplete ASC"
                        Dim sortedDt As DataTable = dt.DefaultView.ToTable()

                        Return sortedDt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        Private Function GetCurrentStepNumber(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal workflowProfile As String, ByVal totalSteps As Integer) As Integer
            Try
                ' Fetch the workflow step number from a designated account
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:A#WF_CurrentStep:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return CInt(dataCell.CellAmount)
                End If
            Catch
                ' Return 0 indicating not started
            End Try
            Return 0
        End Function

        Private Function GetSubmittedByUser(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal workflowProfile As String) As String
            Try
                ' Attempt to retrieve the submitter from workflow metadata via text data cell
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:A#WF_SubmittedBy:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    ' The cell amount might encode a user ID; return it as a string
                    Dim userId As Integer = CInt(dataCell.CellAmount)
                    If userId > 0 Then Return "User_" & userId.ToString()
                End If
            Catch
                ' Return empty on failure
            End Try
            Return String.Empty
        End Function

        Private Function ResolveDueDate(ByVal timeName As String, ByVal dueDateCode As String) As String
            ' Convert a working day offset code (e.g., "WD+3") into an actual calendar date
            Try
                Dim year As Integer = Integer.Parse(timeName.Substring(0, 4))
                Dim month As Integer = Integer.Parse(timeName.Substring(5))

                ' The due date is relative to the first day of the following month
                Dim nextMonth As Integer = month + 1
                Dim nextYear As Integer = year
                If nextMonth > 12 Then
                    nextMonth = 1
                    nextYear += 1
                End If

                Dim baseDate As New DateTime(nextYear, nextMonth, 1)
                Dim workingDaysOffset As Integer = Integer.Parse(dueDateCode.Replace("WD+", ""))

                ' Count working days from base date
                Dim daysAdded As Integer = 0
                Dim currentDate As DateTime = baseDate
                While daysAdded < workingDaysOffset
                    currentDate = currentDate.AddDays(1)
                    If currentDate.DayOfWeek <> DayOfWeek.Saturday AndAlso currentDate.DayOfWeek <> DayOfWeek.Sunday Then
                        daysAdded += 1
                    End If
                End While

                Return currentDate.ToString("yyyy-MM-dd")
            Catch
                Return dueDateCode
            End Try
        End Function

    End Class
End Namespace

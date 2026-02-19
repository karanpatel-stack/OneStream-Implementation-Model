'------------------------------------------------------------------------------------------------------------
' DDA_PeoplePlanning
' Dashboard DataAdapter Business Rule
' Purpose: Headcount and compensation analysis by entity, department, and position type
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_PeoplePlanning
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("PeoplePlanning")
                        dt.Columns.Add("Entity", GetType(String))
                        dt.Columns.Add("Department", GetType(String))
                        dt.Columns.Add("FTECount", GetType(Double))
                        dt.Columns.Add("BudgetFTE", GetType(Double))
                        dt.Columns.Add("AvgBaseSalary", GetType(Double))
                        dt.Columns.Add("AvgTotalComp", GetType(Double))
                        dt.Columns.Add("TotalLaborCost", GetType(Double))
                        dt.Columns.Add("BudgetLaborCost", GetType(Double))
                        dt.Columns.Add("Variance", GetType(Double))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim budgetScenario As String = args.NameValuePairs.XFGetValue("BudgetScenario", "Budget")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim entityFilter As String = args.NameValuePairs.XFGetValue("Entity", "AllEntities")

                        ' Define entities to analyze
                        Dim entities As New List(Of String)()
                        entities.Add("Entity_US01")
                        entities.Add("Entity_US02")
                        entities.Add("Entity_UK01")
                        entities.Add("Entity_DE01")
                        entities.Add("Entity_CN01")
                        entities.Add("Entity_IN01")

                        ' Define departments tracked as UD2 members
                        Dim departments As New List(Of String)()
                        departments.Add("Sales")
                        departments.Add("Marketing")
                        departments.Add("Finance")
                        departments.Add("Operations")
                        departments.Add("Engineering")
                        departments.Add("HR")
                        departments.Add("IT")
                        departments.Add("Executive")

                        ' Account members for people planning metrics
                        Dim acctFTE As String = "A#FTE_Count"
                        Dim acctBaseSalary As String = "A#Base_Salary_Total"
                        Dim acctBenefits As String = "A#Benefits_Total"
                        Dim acctTotalComp As String = "A#Total_Compensation"
                        Dim acctTotalLaborCost As String = "A#Total_Labor_Cost"

                        For Each entityName In entities
                            ' Apply entity filter if specified
                            If entityFilter <> "AllEntities" AndAlso entityName <> entityFilter Then Continue For

                            For Each dept In departments
                                Dim udDept As String = "U2#Dept_" & dept

                                ' Fetch actual headcount and compensation
                                Dim fteCount As Double = FetchPeopleMetric(si, scenarioName, timeName, entityName, acctFTE, udDept)
                                Dim baseSalaryTotal As Double = FetchPeopleMetric(si, scenarioName, timeName, entityName, acctBaseSalary, udDept)
                                Dim benefitsTotal As Double = FetchPeopleMetric(si, scenarioName, timeName, entityName, acctBenefits, udDept)
                                Dim totalCompensation As Double = FetchPeopleMetric(si, scenarioName, timeName, entityName, acctTotalComp, udDept)
                                Dim totalLaborCost As Double = FetchPeopleMetric(si, scenarioName, timeName, entityName, acctTotalLaborCost, udDept)

                                ' Fetch budget headcount and labor cost
                                Dim budgetFTE As Double = FetchPeopleMetric(si, budgetScenario, timeName, entityName, acctFTE, udDept)
                                Dim budgetLaborCost As Double = FetchPeopleMetric(si, budgetScenario, timeName, entityName, acctTotalLaborCost, udDept)

                                ' Skip departments with no headcount in either actual or budget
                                If fteCount = 0 AndAlso budgetFTE = 0 Then Continue For

                                ' Calculate averages per FTE
                                Dim avgBaseSalary As Double = 0
                                Dim avgTotalComp As Double = 0
                                If fteCount > 0 Then
                                    avgBaseSalary = baseSalaryTotal / fteCount
                                    avgTotalComp = totalCompensation / fteCount
                                End If

                                ' Calculate variance (budget minus actual; positive = under budget)
                                Dim variance As Double = budgetLaborCost - totalLaborCost

                                Dim row As DataRow = dt.NewRow()
                                row("Entity") = entityName.Replace("Entity_", "")
                                row("Department") = dept
                                row("FTECount") = Math.Round(fteCount, 1)
                                row("BudgetFTE") = Math.Round(budgetFTE, 1)
                                row("AvgBaseSalary") = Math.Round(avgBaseSalary, 2)
                                row("AvgTotalComp") = Math.Round(avgTotalComp, 2)
                                row("TotalLaborCost") = Math.Round(totalLaborCost, 2)
                                row("BudgetLaborCost") = Math.Round(budgetLaborCost, 2)
                                row("Variance") = Math.Round(variance, 2)
                                dt.Rows.Add(row)
                            Next
                        Next

                        Return dt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        Private Function FetchPeopleMetric(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String, ByVal udDept As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:{4}:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account, udDept)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
                ' Return zero on failure
            End Try
            Return 0
        End Function

    End Class
End Namespace

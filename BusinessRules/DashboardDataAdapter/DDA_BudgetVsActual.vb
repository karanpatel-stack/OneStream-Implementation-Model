'------------------------------------------------------------------------------------------------------------
' DDA_BudgetVsActual
' Dashboard DataAdapter Business Rule
' Purpose: Budget vs Actual analysis with flex budget calculations and prior year comparison
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_BudgetVsActual
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("BudgetVsActual")
                        dt.Columns.Add("AccountName", GetType(String))
                        dt.Columns.Add("Actual", GetType(Double))
                        dt.Columns.Add("StaticBudget", GetType(Double))
                        dt.Columns.Add("FlexBudget", GetType(Double))
                        dt.Columns.Add("StaticVariance", GetType(Double))
                        dt.Columns.Add("FlexVariance", GetType(Double))
                        dt.Columns.Add("PriorYear", GetType(Double))
                        dt.Columns.Add("YoYVariance", GetType(Double))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim budgetScenario As String = args.NameValuePairs.XFGetValue("BudgetScenario", "Budget")
                        Dim priorScenario As String = args.NameValuePairs.XFGetValue("PriorScenario", "Actual")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", "Corporate")

                        ' Derive prior year time
                        Dim priorTimeName As String = DerivePriorYearTime(timeName)

                        ' Calculate the volume flex factor: actual units / budget units
                        Dim actualVolume As Double = FetchAmount(si, scenarioName, timeName, entityName, "A#Units_Sold")
                        Dim budgetVolume As Double = FetchAmount(si, budgetScenario, timeName, entityName, "A#Units_Sold")
                        Dim flexFactor As Double = 1.0
                        If budgetVolume > 0 Then
                            flexFactor = actualVolume / budgetVolume
                        End If

                        ' Define accounts to analyze with flex applicability flag
                        ' True = variable cost (apply flex), False = fixed cost (no flex adjustment)
                        Dim accountDefinitions As New List(Of Tuple(Of String, String, Boolean))()
                        accountDefinitions.Add(Tuple.Create("Revenue", "A#Revenue", True))
                        accountDefinitions.Add(Tuple.Create("Direct Materials", "A#DirectMaterials", True))
                        accountDefinitions.Add(Tuple.Create("Direct Labor", "A#DirectLabor", True))
                        accountDefinitions.Add(Tuple.Create("Variable Overhead", "A#VariableOverhead", True))
                        accountDefinitions.Add(Tuple.Create("Fixed Overhead", "A#FixedOverhead", False))
                        accountDefinitions.Add(Tuple.Create("COGS", "A#COGS", True))
                        accountDefinitions.Add(Tuple.Create("Gross Profit", "A#GrossProfit", True))
                        accountDefinitions.Add(Tuple.Create("Sales & Marketing", "A#SalesMarketing", False))
                        accountDefinitions.Add(Tuple.Create("General & Admin", "A#GeneralAdmin", False))
                        accountDefinitions.Add(Tuple.Create("R&D", "A#RandD", False))
                        accountDefinitions.Add(Tuple.Create("Total OPEX", "A#TotalOpex", False))
                        accountDefinitions.Add(Tuple.Create("EBITDA", "A#EBITDA", True))
                        accountDefinitions.Add(Tuple.Create("Net Income", "A#NetIncome", True))

                        For Each acctDef In accountDefinitions
                            Dim accountName As String = acctDef.Item1
                            Dim accountMember As String = acctDef.Item2
                            Dim isVariable As Boolean = acctDef.Item3

                            ' Fetch actual, static budget, and prior year values
                            Dim actualValue As Double = FetchAmount(si, scenarioName, timeName, entityName, accountMember)
                            Dim staticBudget As Double = FetchAmount(si, budgetScenario, timeName, entityName, accountMember)
                            Dim priorYear As Double = FetchAmount(si, priorScenario, priorTimeName, entityName, accountMember)

                            ' Calculate flex budget: adjust variable costs by volume factor, keep fixed costs unchanged
                            Dim flexBudget As Double = staticBudget
                            If isVariable Then
                                flexBudget = staticBudget * flexFactor
                            End If

                            ' Calculate variances
                            Dim staticVariance As Double = actualValue - staticBudget
                            Dim flexVariance As Double = actualValue - flexBudget
                            Dim yoyVariance As Double = actualValue - priorYear

                            Dim row As DataRow = dt.NewRow()
                            row("AccountName") = accountName
                            row("Actual") = Math.Round(actualValue, 2)
                            row("StaticBudget") = Math.Round(staticBudget, 2)
                            row("FlexBudget") = Math.Round(flexBudget, 2)
                            row("StaticVariance") = Math.Round(staticVariance, 2)
                            row("FlexVariance") = Math.Round(flexVariance, 2)
                            row("PriorYear") = Math.Round(priorYear, 2)
                            row("YoYVariance") = Math.Round(yoyVariance, 2)
                            dt.Rows.Add(row)
                        Next

                        Return dt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
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
                ' Return zero on failure
            End Try
            Return 0
        End Function

        Private Function DerivePriorYearTime(ByVal timeName As String) As String
            Try
                Dim yearStr As String = timeName.Substring(0, 4)
                Dim remainder As String = timeName.Substring(4)
                Dim year As Integer = Integer.Parse(yearStr)
                Return (year - 1).ToString() & remainder
            Catch
                Return timeName
            End Try
        End Function

    End Class
End Namespace

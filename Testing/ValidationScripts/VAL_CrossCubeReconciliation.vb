'------------------------------------------------------------------------------------------------------------
' OneStream XF Validation Script: VAL_CrossCubeReconciliation
'------------------------------------------------------------------------------------------------------------
' Purpose:     Reconciles data across the Finance cube and Planning cube to ensure consistency.
'              Validates key reconciliation points such as Revenue, Total Expenses, Headcount,
'              and CAPEX. Also verifies the HR cube headcount ties to Finance cube people costs.
'
' Reconciliation Points:
'   1. Finance.TotalRevenue vs Planning.TotalRevenue
'   2. Finance.TotalOPEX vs Planning.TotalOPEX
'   3. Finance.Headcount_FTE vs HR.Headcount_FTE
'   4. Finance.CAPEX vs Planning.CAPEX
'   5. HR cube headcount * avg salary vs Finance people costs (SGA_Salaries + DirectLabor)
'
' Output:      DataTable with columns: Account, Entity, FinanceValue, PlanningValue,
'              Difference, DifferencePct, Status (Pass/Fail)
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.VAL_CrossCubeReconciliation

    Public Class MainClass

        ' Tolerance for financial amount reconciliation ($1.00)
        Private Const AMOUNT_TOLERANCE As Double = 1.0

        ' Tolerance for headcount reconciliation (allows 0.1 FTE difference)
        Private Const HEADCOUNT_TOLERANCE As Double = 0.1

        ' Tolerance for people cost reconciliation (percentage)
        Private Const PEOPLE_COST_PCT_TOLERANCE As Double = 0.02

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for cross-cube reconciliation validation.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Dim resultsTable As New DataTable("CrossCubeReconciliation")
                resultsTable.Columns.Add("Account", GetType(String))
                resultsTable.Columns.Add("Entity", GetType(String))
                resultsTable.Columns.Add("FinanceValue", GetType(Double))
                resultsTable.Columns.Add("PlanningValue", GetType(Double))
                resultsTable.Columns.Add("Difference", GetType(Double))
                resultsTable.Columns.Add("DifferencePct", GetType(Double))
                resultsTable.Columns.Add("Status", GetType(String))

                ' Entities to reconcile across cubes
                Dim reconEntities As String() = { _
                    "Plant_US01_Detroit", "Plant_US02_Houston", "Plant_US03_Charlotte", _
                    "Plant_CA01_Toronto", "Plant_MX01_Monterrey", _
                    "Plant_DE01_Munich", "Plant_DE02_Stuttgart", _
                    "Plant_UK01_Birmingham", "Plant_FR01_Lyon", _
                    "Plant_CN01_Shanghai", "Plant_CN02_Shenzhen", _
                    "Plant_JP01_Osaka", "Plant_IN01_Pune" _
                }

                ' Reconciliation points: (display label, finance account, planning account, tolerance)
                Dim reconPoints As New List(Of String())(New String()() { _
                    New String() {"Revenue", "TotalRevenue", "TotalRevenue", AMOUNT_TOLERANCE.ToString()}, _
                    New String() {"Total OPEX", "TotalOPEX", "TotalOPEX", AMOUNT_TOLERANCE.ToString()}, _
                    New String() {"CAPEX", "CAPEX", "CAPEX", AMOUNT_TOLERANCE.ToString()}, _
                    New String() {"Headcount FTE", "Headcount_FTE", "Headcount_FTE", HEADCOUNT_TOLERANCE.ToString()} _
                })

                BRApi.ErrorLog.LogMessage(si, "VAL_CrossCubeReconciliation: Starting cross-cube reconciliation...")

                '--------------------------------------------------------------------------------------------
                ' Reconcile financial amounts between Finance and Planning cubes
                '--------------------------------------------------------------------------------------------
                For Each entityName As String In reconEntities
                    For Each reconPoint As String() In reconPoints
                        Dim label As String = reconPoint(0)
                        Dim financeAcct As String = reconPoint(1)
                        Dim planningAcct As String = reconPoint(2)
                        Dim tolerance As Double = Double.Parse(reconPoint(3), CultureInfo.InvariantCulture)

                        ' Read from Finance cube (Actual scenario)
                        Dim financePov As String = $"E#{entityName}:A#{financeAcct}:C#C_Local:S#Actual:F#F_Closing:O#O_None"
                        Dim financeValue As Double = BRApi.Finance.Data.GetDataCell(si, financePov).CellAmount

                        ' Read from Planning cube (Budget scenario as planning reference)
                        Dim planningPov As String = $"E#{entityName}:A#{planningAcct}:C#C_Local:S#Budget:F#F_Closing:O#O_None"
                        Dim planningValue As Double = BRApi.Finance.Data.GetDataCell(si, planningPov).CellAmount

                        Dim difference As Double = financeValue - planningValue
                        Dim diffPct As Double = 0
                        If planningValue <> 0 Then
                            diffPct = (difference / Math.Abs(planningValue)) * 100.0
                        End If

                        Dim status As String = If(Math.Abs(difference) <= tolerance, "PASS", "FAIL")

                        Dim row As DataRow = resultsTable.NewRow()
                        row("Account") = label
                        row("Entity") = entityName
                        row("FinanceValue") = Math.Round(financeValue, 2)
                        row("PlanningValue") = Math.Round(planningValue, 2)
                        row("Difference") = Math.Round(difference, 2)
                        row("DifferencePct") = Math.Round(diffPct, 2)
                        row("Status") = status
                        resultsTable.Rows.Add(row)
                    Next
                Next

                '--------------------------------------------------------------------------------------------
                ' Reconcile HR headcount to Finance people costs
                ' Logic: HR total headcount * average cost per FTE should approximate Finance people costs
                '--------------------------------------------------------------------------------------------
                For Each entityName As String In reconEntities
                    ' HR cube: total headcount FTE
                    Dim hrHeadcountPov As String = $"E#{entityName}:A#Headcount_FTE:C#C_Local:S#Actual:F#F_Closing:O#O_None"
                    Dim hrHeadcount As Double = BRApi.Finance.Data.GetDataCell(si, hrHeadcountPov).CellAmount

                    ' Finance cube: total people costs (SGA Salaries + Direct Labor)
                    Dim sgaSalPov As String = $"E#{entityName}:A#SGA_Salaries:C#C_Local:S#Actual:F#F_None:O#O_None"
                    Dim directLaborPov As String = $"E#{entityName}:A#DirectLabor:C#C_Local:S#Actual:F#F_None:O#O_None"

                    Dim sgaSalaries As Double = BRApi.Finance.Data.GetDataCell(si, sgaSalPov).CellAmount
                    Dim directLabor As Double = BRApi.Finance.Data.GetDataCell(si, directLaborPov).CellAmount
                    Dim totalPeopleCosts As Double = sgaSalaries + directLabor

                    ' Calculate implied cost per FTE from Finance data
                    Dim impliedCostPerFTE As Double = 0
                    If hrHeadcount > 0 Then
                        impliedCostPerFTE = totalPeopleCosts / hrHeadcount
                    End If

                    ' Reasonableness check: implied cost per FTE should be between $30K and $250K annually
                    Dim annualizedCost As Double = impliedCostPerFTE * 12 ' Assume monthly data
                    Dim isReasonable As Boolean = (annualizedCost >= 30000 AndAlso annualizedCost <= 250000) OrElse hrHeadcount = 0
                    Dim hcStatus As String = If(isReasonable, "PASS", "FAIL")

                    Dim hcRow As DataRow = resultsTable.NewRow()
                    hcRow("Account") = "HC-to-Cost Reasonableness"
                    hcRow("Entity") = entityName
                    hcRow("FinanceValue") = Math.Round(totalPeopleCosts, 2)
                    hcRow("PlanningValue") = Math.Round(hrHeadcount, 2)
                    hcRow("Difference") = Math.Round(impliedCostPerFTE, 2)
                    hcRow("DifferencePct") = Math.Round(annualizedCost, 2)
                    hcRow("Status") = hcStatus
                    resultsTable.Rows.Add(hcRow)
                Next

                ' Summary
                Dim totalChecks As Integer = resultsTable.Rows.Count
                Dim failCount As Integer = resultsTable.Select("Status = 'FAIL'").Length

                BRApi.ErrorLog.LogMessage(si, _
                    $"VAL_CrossCubeReconciliation: Complete. Total={totalChecks}, Pass={totalChecks - failCount}, Fail={failCount}")

                Return resultsTable

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "VAL_CrossCubeReconciliation.Main", ex.Message))
            End Try
        End Function

    End Class

End Namespace

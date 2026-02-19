'------------------------------------------------------------------------------------------------------------
' CR_CapacityUtilization
' Calculate Business Rule - Machine/Plant Capacity Analysis
'
' Purpose:  Reads available and actual capacity hours by machine center/plant, calculates
'           utilization percentages, determines theoretical vs practical capacity, identifies
'           bottleneck resources, calculates cost of unused capacity, and writes utilization
'           metrics to statistical accounts.
'
' Scope:    Finance - Calculate
' Version:  1.0
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.Finance.CR_CapacityUtilization

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    BRApi.ErrorLog.LogMessage(si, "CR_CapacityUtilization: Starting capacity utilization analysis.")

                    '--- Define machine centers / plants ---
                    Dim machineCenters As New List(Of String) From {
                        "MC_CNCMilling", "MC_Lathe", "MC_StampPress", "MC_InjectionMold",
                        "MC_WeldStation", "MC_PaintLine", "MC_AssemblyLine", "MC_TestStation"
                    }

                    '--- Practical capacity factor: practical = theoretical x factor ---
                    '    Accounts for scheduled maintenance, changeovers, breaks, etc.
                    Const PRACTICAL_CAPACITY_FACTOR As Double = 0.85

                    '--- Bottleneck threshold: centers above this utilization are bottlenecks ---
                    Const BOTTLENECK_THRESHOLD As Double = 0.90

                    Dim plantTotalAvailable As Double = 0
                    Dim plantTotalActual As Double = 0
                    Dim plantTotalFixedCost As Double = 0
                    Dim plantTotalUnusedCapacityCost As Double = 0
                    Dim bottleneckCount As Integer = 0

                    ' Track the highest utilization to identify primary bottleneck
                    Dim maxUtilization As Double = 0
                    Dim primaryBottleneck As String = ""

                    For Each mc As String In machineCenters
                        Dim entity As String = "E#" & mc

                        ' ============================================================
                        ' Read capacity data
                        ' ============================================================
                        ' Theoretical capacity = total hours if running 24/7 with no downtime
                        Dim theoreticalCapacity As Double = ReadCell(si, api, "A#CAP_TheoreticalHours", entity)
                        ' Actual utilization hours
                        Dim actualHours As Double = ReadCell(si, api, "A#CAP_ActualHours", entity)
                        ' Fixed cost associated with this machine center
                        Dim fixedCostTotal As Double = ReadCell(si, api, "A#CAP_FixedCost", entity)

                        If theoreticalCapacity <= 0 Then
                            BRApi.ErrorLog.LogMessage(si, "CR_CapacityUtilization: Skipping " & mc & " - zero theoretical capacity.")
                            Continue For
                        End If

                        ' ============================================================
                        ' Calculate practical capacity
                        ' Practical = Theoretical x Practical Factor (e.g., 85%)
                        ' ============================================================
                        Dim practicalCapacity As Double = Math.Round(theoreticalCapacity * PRACTICAL_CAPACITY_FACTOR, 2)

                        ' ============================================================
                        ' Calculate utilization percentages
                        ' ============================================================
                        Dim theoreticalUtilPct As Double = Math.Round(actualHours / theoreticalCapacity, 6)
                        Dim practicalUtilPct As Double = 0
                        If practicalCapacity > 0 Then
                            practicalUtilPct = Math.Round(actualHours / practicalCapacity, 6)
                        End If

                        ' Cap at 100% for reporting (overtime can exceed practical capacity)
                        Dim cappedPracticalUtilPct As Double = Math.Min(practicalUtilPct, 1.0)

                        ' ============================================================
                        ' Identify bottleneck resources
                        ' ============================================================
                        Dim isBottleneck As Integer = 0
                        If practicalUtilPct >= BOTTLENECK_THRESHOLD Then
                            isBottleneck = 1
                            bottleneckCount += 1
                        End If

                        If practicalUtilPct > maxUtilization Then
                            maxUtilization = practicalUtilPct
                            primaryBottleneck = mc
                        End If

                        ' ============================================================
                        ' Calculate cost of unused capacity
                        ' Unused Capacity Cost = Fixed Cost x (1 - Utilization %)
                        ' ============================================================
                        Dim unusedHours As Double = Math.Max(practicalCapacity - actualHours, 0)
                        Dim costPerPracticalHour As Double = 0
                        If practicalCapacity > 0 Then
                            costPerPracticalHour = fixedCostTotal / practicalCapacity
                        End If
                        Dim unusedCapacityCost As Double = Math.Round(costPerPracticalHour * unusedHours, 2)

                        ' ============================================================
                        ' Write utilization metrics to statistical accounts
                        ' ============================================================
                        WriteCell(si, api, "A#CAP_PracticalHours", entity, practicalCapacity)
                        WriteCell(si, api, "A#CAP_UnusedHours", entity, unusedHours)
                        WriteCell(si, api, "A#CAP_TheoreticalUtilPct", entity, theoreticalUtilPct)
                        WriteCell(si, api, "A#CAP_PracticalUtilPct", entity, cappedPracticalUtilPct)
                        WriteCell(si, api, "A#CAP_IsBottleneck", entity, isBottleneck)
                        WriteCell(si, api, "A#CAP_CostPerHour", entity, costPerPracticalHour)
                        WriteCell(si, api, "A#CAP_UnusedCapacityCost", entity, unusedCapacityCost)

                        '--- Accumulate plant totals ---
                        plantTotalAvailable += practicalCapacity
                        plantTotalActual += actualHours
                        plantTotalFixedCost += fixedCostTotal
                        plantTotalUnusedCapacityCost += unusedCapacityCost

                        BRApi.ErrorLog.LogMessage(si, "CR_CapacityUtilization: " & mc _
                            & " | Practical=" & practicalCapacity.ToString("N0") & "hrs" _
                            & " | Actual=" & actualHours.ToString("N0") & "hrs" _
                            & " | Util=" & (cappedPracticalUtilPct * 100).ToString("N1") & "%" _
                            & " | Bottleneck=" & If(isBottleneck = 1, "YES", "No") _
                            & " | UnusedCost=" & unusedCapacityCost.ToString("N2"))
                    Next

                    ' ============================================================
                    ' Write plant-level aggregated metrics
                    ' ============================================================
                    Dim plantEntity As String = "E#Plant_Total"
                    Dim plantUtilPct As Double = If(plantTotalAvailable > 0,
                        Math.Round(plantTotalActual / plantTotalAvailable, 6), 0)

                    WriteCell(si, api, "A#CAP_PracticalHours", plantEntity, plantTotalAvailable)
                    WriteCell(si, api, "A#CAP_ActualHours", plantEntity, plantTotalActual)
                    WriteCell(si, api, "A#CAP_UnusedHours", plantEntity, Math.Max(plantTotalAvailable - plantTotalActual, 0))
                    WriteCell(si, api, "A#CAP_PracticalUtilPct", plantEntity, plantUtilPct)
                    WriteCell(si, api, "A#CAP_UnusedCapacityCost", plantEntity, plantTotalUnusedCapacityCost)
                    WriteCell(si, api, "A#CAP_BottleneckCount", plantEntity, bottleneckCount)

                    BRApi.ErrorLog.LogMessage(si, "CR_CapacityUtilization: Completed. Plant Utilization=" _
                        & (plantUtilPct * 100).ToString("N1") & "%" _
                        & " | Bottlenecks=" & bottleneckCount.ToString() _
                        & " | Primary=" & primaryBottleneck _
                        & " | Unused Cost=" & plantTotalUnusedCapacityCost.ToString("N2"))
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFaborException(si, "CR_CapacityUtilization", ex.Message, ex))
            End Try
        End Function

        Private Function ReadCell(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                  ByVal acct As String, ByVal entity As String) As Double
            Try
                Dim pov As String = api.Pov.Scenario.Name & ":" & api.Pov.Time.Name & ":" _
                    & entity & ":" & acct & ":F#Periodic:O#Top:I#Top:C1#Top:C2#Top:C3#Top:C4#Top"
                Dim dc As DataCell = BRApi.Finance.Data.GetDataCell(si, pov, False)
                Return dc.CellAmount
            Catch ex As Exception
                Return 0
            End Try
        End Function

        Private Sub WriteCell(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                              ByVal acct As String, ByVal entity As String, ByVal amount As Double)
            Try
                api.Data.SetDataCell(si, acct, entity, "F#Periodic", "O#Top", "I#Top",
                                     "C1#Top", "C2#Top", "C3#Top", "C4#Top", amount, True)
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "CR_CapacityUtilization.WriteCell: Error - " & acct & " - " & ex.Message)
            End Try
        End Sub

    End Class

End Namespace

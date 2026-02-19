'------------------------------------------------------------------------------------------------------------
' CR_ProductionEfficiency
' Calculate Business Rule - OEE and Production Metrics
'
' Purpose:  Calculates Overall Equipment Effectiveness (OEE) and related production KPIs
'           including Availability, Performance, Quality, Yield Rate, Scrap Rate, and
'           Throughput. Writes all metrics to statistical accounts for operational reporting.
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

Namespace OneStream.BusinessRule.Finance.CR_ProductionEfficiency

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    BRApi.ErrorLog.LogMessage(si, "CR_ProductionEfficiency: Starting OEE and production metrics calculation.")

                    '--- Define machine centers / production areas ---
                    Dim machineCenters As New List(Of String) From {
                        "MC_CNCMilling", "MC_Lathe", "MC_StampPress", "MC_InjectionMold",
                        "MC_WeldStation", "MC_PaintLine", "MC_AssemblyLine"
                    }

                    '--- Aggregated values for plant-level OEE ---
                    Dim plantTotalGoodCount As Double = 0
                    Dim plantTotalCount As Double = 0
                    Dim plantTotalInput As Double = 0
                    Dim plantTotalScrap As Double = 0
                    Dim plantWeightedOEE As Double = 0
                    Dim plantTotalPlannedTime As Double = 0

                    For Each mc As String In machineCenters
                        Dim entity As String = "E#" & mc

                        ' ============================================================
                        ' Read raw production data
                        ' ============================================================
                        Dim plannedProductionTime As Double = ReadCell(si, api, "A#STAT_PlannedProdTime", entity)      ' Hours
                        Dim downtime As Double = ReadCell(si, api, "A#STAT_Downtime", entity)                          ' Hours
                        Dim idealCycleTime As Double = ReadCell(si, api, "A#STAT_IdealCycleTime", entity)              ' Hours per unit
                        Dim totalCount As Double = ReadCell(si, api, "A#STAT_TotalCount", entity)                      ' Total units produced (incl. defects)
                        Dim goodCount As Double = ReadCell(si, api, "A#STAT_GoodCount", entity)                        ' Units passing quality
                        Dim totalInput As Double = ReadCell(si, api, "A#STAT_TotalInput", entity)                      ' Total material input units
                        Dim scrapQty As Double = ReadCell(si, api, "A#STAT_ScrapQty", entity)                          ' Scrapped units
                        Dim periodHours As Double = ReadCell(si, api, "A#STAT_PeriodHours", entity)                    ' Total hours in period

                        '--- Validate inputs to prevent division by zero ---
                        If plannedProductionTime <= 0 Then
                            BRApi.ErrorLog.LogMessage(si, "CR_ProductionEfficiency: Skipping " & mc & " - zero planned production time.")
                            Continue For
                        End If

                        Dim runTime As Double = plannedProductionTime - downtime
                        If runTime < 0 Then runTime = 0

                        ' ============================================================
                        ' OEE Component 1: Availability
                        ' Availability = (Planned Production Time - Downtime) / Planned Production Time
                        ' ============================================================
                        Dim availability As Double = 0
                        If plannedProductionTime > 0 Then
                            availability = Math.Round((plannedProductionTime - downtime) / plannedProductionTime, 6)
                        End If

                        ' ============================================================
                        ' OEE Component 2: Performance
                        ' Performance = (Ideal Cycle Time x Total Count) / Run Time
                        ' ============================================================
                        Dim performance As Double = 0
                        If runTime > 0 Then
                            performance = Math.Round((idealCycleTime * totalCount) / runTime, 6)
                        End If
                        ' Cap performance at 1.0 (100%) to handle data anomalies
                        If performance > 1.0 Then performance = 1.0

                        ' ============================================================
                        ' OEE Component 3: Quality
                        ' Quality = Good Count / Total Count
                        ' ============================================================
                        Dim quality As Double = 0
                        If totalCount > 0 Then
                            quality = Math.Round(goodCount / totalCount, 6)
                        End If

                        ' ============================================================
                        ' OEE = Availability x Performance x Quality
                        ' ============================================================
                        Dim oee As Double = Math.Round(availability * performance * quality, 6)

                        ' ============================================================
                        ' Yield Rate = Good Output / Total Input
                        ' ============================================================
                        Dim yieldRate As Double = 0
                        If totalInput > 0 Then
                            yieldRate = Math.Round(goodCount / totalInput, 6)
                        End If

                        ' ============================================================
                        ' Scrap Rate = Scrap Quantity / Total Production
                        ' ============================================================
                        Dim scrapRate As Double = 0
                        If totalCount > 0 Then
                            scrapRate = Math.Round(scrapQty / totalCount, 6)
                        End If

                        ' ============================================================
                        ' Throughput = Units / Time Period (units per hour)
                        ' ============================================================
                        Dim throughput As Double = 0
                        If periodHours > 0 Then
                            throughput = Math.Round(goodCount / periodHours, 4)
                        End If

                        ' ============================================================
                        ' Write all KPIs to statistical accounts
                        ' ============================================================
                        WriteCell(si, api, "A#KPI_Availability", entity, availability)
                        WriteCell(si, api, "A#KPI_Performance", entity, performance)
                        WriteCell(si, api, "A#KPI_Quality", entity, quality)
                        WriteCell(si, api, "A#KPI_OEE", entity, oee)
                        WriteCell(si, api, "A#KPI_YieldRate", entity, yieldRate)
                        WriteCell(si, api, "A#KPI_ScrapRate", entity, scrapRate)
                        WriteCell(si, api, "A#KPI_Throughput", entity, throughput)
                        WriteCell(si, api, "A#KPI_RunTime", entity, runTime)

                        '--- Accumulate plant totals ---
                        plantTotalGoodCount += goodCount
                        plantTotalCount += totalCount
                        plantTotalInput += totalInput
                        plantTotalScrap += scrapQty
                        plantWeightedOEE += oee * plannedProductionTime
                        plantTotalPlannedTime += plannedProductionTime

                        BRApi.ErrorLog.LogMessage(si, "CR_ProductionEfficiency: " & mc _
                            & " | OEE=" & (oee * 100).ToString("N2") & "%" _
                            & " | Avail=" & (availability * 100).ToString("N2") & "%" _
                            & " | Perf=" & (performance * 100).ToString("N2") & "%" _
                            & " | Qual=" & (quality * 100).ToString("N2") & "%" _
                            & " | Yield=" & (yieldRate * 100).ToString("N2") & "%" _
                            & " | Scrap=" & (scrapRate * 100).ToString("N2") & "%")
                    Next

                    ' ============================================================
                    ' Write plant-level aggregated KPIs
                    ' ============================================================
                    Dim plantEntity As String = "E#Plant_Total"
                    Dim plantOEE As Double = If(plantTotalPlannedTime > 0, Math.Round(plantWeightedOEE / plantTotalPlannedTime, 6), 0)
                    Dim plantYield As Double = If(plantTotalInput > 0, Math.Round(plantTotalGoodCount / plantTotalInput, 6), 0)
                    Dim plantScrap As Double = If(plantTotalCount > 0, Math.Round(plantTotalScrap / plantTotalCount, 6), 0)

                    WriteCell(si, api, "A#KPI_OEE", plantEntity, plantOEE)
                    WriteCell(si, api, "A#KPI_YieldRate", plantEntity, plantYield)
                    WriteCell(si, api, "A#KPI_ScrapRate", plantEntity, plantScrap)
                    WriteCell(si, api, "A#STAT_TotalGoodCount", plantEntity, plantTotalGoodCount)
                    WriteCell(si, api, "A#STAT_TotalScrap", plantEntity, plantTotalScrap)

                    BRApi.ErrorLog.LogMessage(si, "CR_ProductionEfficiency: Plant OEE=" & (plantOEE * 100).ToString("N2") & "% - Calculation complete.")
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFaborException(si, "CR_ProductionEfficiency", ex.Message, ex))
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
                BRApi.ErrorLog.LogMessage(si, "CR_ProductionEfficiency.WriteCell: Error - " & acct & " - " & ex.Message)
            End Try
        End Sub

    End Class

End Namespace

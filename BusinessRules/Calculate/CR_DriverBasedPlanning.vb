'------------------------------------------------------------------------------------------------------------
' CR_DriverBasedPlanning
' Calculate Business Rule - Comprehensive Planning Calculation Engine
'
' Purpose:  Implements a full driver-based planning model covering revenue (units x price x mix
'           x seasonality), material cost (units x BOM cost x inflation), labor cost (headcount
'           x compensation x burden), overhead (activity drivers), SG&A, marketing, R&D, and
'           supports top-down/bottom-up planning modes with scenario seeding.
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

Namespace OneStream.BusinessRule.Finance.CR_DriverBasedPlanning

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    BRApi.ErrorLog.LogMessage(si, "CR_DriverBasedPlanning: Starting driver-based planning engine.")

                    Dim scenarioName As String = api.Pov.Scenario.Name
                    Dim timeName As String = api.Pov.Time.Name

                    '--- Determine planning mode ---
                    Dim planningMode As String = GetPlanningMode(si, api)
                    BRApi.ErrorLog.LogMessage(si, "CR_DriverBasedPlanning: Planning mode = " & planningMode)

                    '--- Handle scenario seeding if this is a new planning cycle ---
                    Dim isSeedRequired As Boolean = (ReadCell(si, api, "A#PLAN_SeedFlag", "E#Corporate") = 1)
                    If isSeedRequired Then
                        PerformScenarioSeeding(si, api, scenarioName)
                    End If

                    '--- Define planning entities (business units) ---
                    Dim businessUnits As New List(Of String) From {
                        "BU_NorthAmerica", "BU_Europe", "BU_AsiaPac", "BU_LatAm"
                    }

                    '--- Define product lines within each BU ---
                    Dim productLines As New List(Of String) From {
                        "PL_Enterprise", "PL_MidMarket", "PL_SMB"
                    }

                    '--- Seasonal factors by month index (1=Jan through 12=Dec) ---
                    Dim seasonalFactors As New Dictionary(Of Integer, Double) From {
                        {1, 0.75}, {2, 0.80}, {3, 0.95}, {4, 1.00}, {5, 1.05}, {6, 1.10},
                        {7, 0.90}, {8, 0.85}, {9, 1.00}, {10, 1.10}, {11, 1.15}, {12, 1.20}
                    }

                    Dim monthIndex As Integer = GetMonthIndex(timeName)
                    Dim seasonalFactor As Double = 1.0
                    If seasonalFactors.ContainsKey(monthIndex) Then
                        seasonalFactor = seasonalFactors(monthIndex)
                    End If

                    Dim corpTotalRevenue As Double = 0
                    Dim corpTotalCOGS As Double = 0
                    Dim corpTotalOpex As Double = 0

                    For Each bu As String In businessUnits
                        Dim buRevenue As Double = 0
                        Dim buCOGS As Double = 0

                        For Each pl As String In productLines
                            Dim entity As String = "E#" & bu & "_" & pl

                            ' ============================================================
                            ' REVENUE: Units x Price per Unit x Channel Mix x Seasonal Factor
                            ' ============================================================
                            Dim plannedUnits As Double = ReadCell(si, api, "A#PLAN_Units", entity)
                            Dim pricePerUnit As Double = ReadCell(si, api, "A#PLAN_PricePerUnit", entity)
                            Dim channelMixPct As Double = ReadCell(si, api, "A#PLAN_ChannelMixPct", entity)
                            If channelMixPct = 0 Then channelMixPct = 1.0

                            Dim revenue As Double = Math.Round(plannedUnits * pricePerUnit * channelMixPct * seasonalFactor, 2)

                            ' ============================================================
                            ' MATERIAL COST: Units x BOM Cost x Inflation Factor
                            ' ============================================================
                            Dim bomCostPerUnit As Double = ReadCell(si, api, "A#PLAN_BOMCostPerUnit", entity)
                            Dim inflationFactor As Double = ReadCell(si, api, "A#PLAN_InflationFactor", entity)
                            If inflationFactor = 0 Then inflationFactor = 1.0

                            Dim materialCost As Double = Math.Round(
                                plannedUnits * bomCostPerUnit * inflationFactor * seasonalFactor, 2)

                            ' ============================================================
                            ' LABOR COST: Headcount x Avg Compensation x Burden Rate
                            ' ============================================================
                            Dim headcount As Double = ReadCell(si, api, "A#PLAN_Headcount", entity)
                            Dim avgCompensation As Double = ReadCell(si, api, "A#PLAN_AvgCompensation", entity)
                            Dim burdenRate As Double = ReadCell(si, api, "A#PLAN_BurdenRate", entity)
                            If burdenRate = 0 Then burdenRate = 1.0

                            ' Monthly labor = annual compensation / 12 x burden rate
                            Dim laborCost As Double = Math.Round(headcount * (avgCompensation / 12) * burdenRate, 2)

                            ' ============================================================
                            ' OVERHEAD: Activity Drivers (machine hours x rate + labor hours x rate)
                            ' ============================================================
                            Dim plannedMachineHours As Double = ReadCell(si, api, "A#PLAN_MachineHours", entity)
                            Dim machineRate As Double = ReadCell(si, api, "A#PLAN_MachineRate", entity)
                            Dim plannedLaborHours As Double = ReadCell(si, api, "A#PLAN_LaborHours", entity)
                            Dim laborOHRate As Double = ReadCell(si, api, "A#PLAN_LaborOHRate", entity)

                            Dim overhead As Double = Math.Round(
                                (plannedMachineHours * machineRate) + (plannedLaborHours * laborOHRate), 2)

                            Dim totalCOGS As Double = materialCost + laborCost + overhead

                            '--- Write product-level P&L ---
                            WriteCell(si, api, "A#PLAN_Revenue", entity, revenue)
                            WriteCell(si, api, "A#PLAN_MaterialCost", entity, materialCost)
                            WriteCell(si, api, "A#PLAN_LaborCost", entity, laborCost)
                            WriteCell(si, api, "A#PLAN_Overhead", entity, overhead)
                            WriteCell(si, api, "A#PLAN_TotalCOGS", entity, totalCOGS)
                            WriteCell(si, api, "A#PLAN_GrossProfit", entity, revenue - totalCOGS)
                            WriteCell(si, api, "A#PLAN_GrossMarginPct", entity,
                                If(revenue > 0, Math.Round((revenue - totalCOGS) / revenue, 6), 0))

                            buRevenue += revenue
                            buCOGS += totalCOGS
                        Next

                        ' ============================================================
                        ' SG&A: Revenue-Based % or Fixed + Variable Components
                        ' ============================================================
                        Dim buEntity As String = "E#" & bu
                        Dim sgaFixedCost As Double = ReadCell(si, api, "A#PLAN_SGAFixed", buEntity)
                        Dim sgaVariablePct As Double = ReadCell(si, api, "A#PLAN_SGAVariablePct", buEntity)
                        Dim sgaTotal As Double = Math.Round(sgaFixedCost + (buRevenue * sgaVariablePct), 2)

                        ' ============================================================
                        ' MARKETING: % of Revenue by Product Line
                        ' ============================================================
                        Dim marketingPct As Double = ReadCell(si, api, "A#PLAN_MarketingPct", buEntity)
                        Dim marketingCost As Double = Math.Round(buRevenue * marketingPct, 2)

                        ' ============================================================
                        ' R&D: Fixed Budgets + Project-Based
                        ' ============================================================
                        Dim rdFixedBudget As Double = ReadCell(si, api, "A#PLAN_RDFixedBudget", buEntity)
                        Dim rdProjectCost As Double = ReadCell(si, api, "A#PLAN_RDProjectCost", buEntity)
                        Dim rdTotal As Double = rdFixedBudget + rdProjectCost

                        Dim totalOpex As Double = sgaTotal + marketingCost + rdTotal

                        '--- Write BU-level operating expenses ---
                        WriteCell(si, api, "A#PLAN_SGATotal", buEntity, sgaTotal)
                        WriteCell(si, api, "A#PLAN_MarketingCost", buEntity, marketingCost)
                        WriteCell(si, api, "A#PLAN_RDTotal", buEntity, rdTotal)
                        WriteCell(si, api, "A#PLAN_TotalOpex", buEntity, totalOpex)
                        WriteCell(si, api, "A#PLAN_BURevenue", buEntity, buRevenue)
                        WriteCell(si, api, "A#PLAN_BUCOGS", buEntity, buCOGS)
                        WriteCell(si, api, "A#PLAN_OperatingIncome", buEntity, buRevenue - buCOGS - totalOpex)

                        corpTotalRevenue += buRevenue
                        corpTotalCOGS += buCOGS
                        corpTotalOpex += totalOpex

                        BRApi.ErrorLog.LogMessage(si, "CR_DriverBasedPlanning: " & bu _
                            & " | Revenue=" & buRevenue.ToString("N2") _
                            & " | COGS=" & buCOGS.ToString("N2") _
                            & " | Opex=" & totalOpex.ToString("N2") _
                            & " | OpIncome=" & (buRevenue - buCOGS - totalOpex).ToString("N2"))
                    Next

                    '--- Write corporate totals ---
                    Dim corpEntity As String = "E#Corporate"
                    WriteCell(si, api, "A#PLAN_TotalRevenue", corpEntity, corpTotalRevenue)
                    WriteCell(si, api, "A#PLAN_TotalCOGS", corpEntity, corpTotalCOGS)
                    WriteCell(si, api, "A#PLAN_TotalOpex", corpEntity, corpTotalOpex)
                    WriteCell(si, api, "A#PLAN_TotalOperatingIncome", corpEntity,
                        corpTotalRevenue - corpTotalCOGS - corpTotalOpex)

                    BRApi.ErrorLog.LogMessage(si, "CR_DriverBasedPlanning: Completed. Corp Revenue=" _
                        & corpTotalRevenue.ToString("N2") & " OpIncome=" _
                        & (corpTotalRevenue - corpTotalCOGS - corpTotalOpex).ToString("N2"))
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFaborException(si, "CR_DriverBasedPlanning", ex.Message, ex))
            End Try
        End Function

        ''' <summary>
        ''' Determines planning mode: TopDown or BottomUp based on a flag in the cube.
        ''' </summary>
        Private Function GetPlanningMode(ByVal si As SessionInfo, ByVal api As FinanceRulesApi) As String
            Dim modeFlag As Double = ReadCell(si, api, "A#PLAN_ModeFlag", "E#Corporate")
            ' 1 = TopDown, 2 = BottomUp
            Return If(modeFlag = 1, "TopDown", "BottomUp")
        End Function

        ''' <summary>
        ''' Performs scenario seeding by copying prior year data and applying a growth rate.
        ''' Seeds driver values for the target planning scenario.
        ''' </summary>
        Private Sub PerformScenarioSeeding(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                           ByVal targetScenario As String)
            Try
                BRApi.ErrorLog.LogMessage(si, "CR_DriverBasedPlanning: Performing scenario seeding for " & targetScenario)

                Dim seedAccounts As New List(Of String) From {
                    "A#PLAN_Units", "A#PLAN_PricePerUnit", "A#PLAN_BOMCostPerUnit",
                    "A#PLAN_Headcount", "A#PLAN_AvgCompensation"
                }

                Dim seedEntities As New List(Of String) From {
                    "E#BU_NorthAmerica", "E#BU_Europe", "E#BU_AsiaPac", "E#BU_LatAm"
                }

                ' Growth rate for seeding
                Dim growthRate As Double = ReadCell(si, api, "A#PLAN_GrowthRate", "E#Corporate")
                If growthRate = 0 Then growthRate = 0.05  ' Default 5% growth

                For Each entity As String In seedEntities
                    For Each acct As String In seedAccounts
                        ' Read prior year actual value
                        Dim priorYearValue As Double = ReadCellWithScenario(si, api, acct, entity, "S#Actual")
                        If priorYearValue <> 0 Then
                            Dim seededValue As Double = Math.Round(priorYearValue * (1 + growthRate), 2)
                            WriteCell(si, api, acct, entity, seededValue)
                        End If
                    Next
                Next

                ' Clear the seed flag so seeding does not re-run
                WriteCell(si, api, "A#PLAN_SeedFlag", "E#Corporate", 0)

                BRApi.ErrorLog.LogMessage(si, "CR_DriverBasedPlanning: Scenario seeding completed with growth rate=" _
                    & (growthRate * 100).ToString("N1") & "%")

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "CR_DriverBasedPlanning.PerformScenarioSeeding: Error - " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Extracts the month index (1-12) from a OneStream time member name.
        ''' Expects format like "2025M1", "2025M12", etc.
        ''' </summary>
        Private Function GetMonthIndex(ByVal timeName As String) As Integer
            Try
                Dim mPos As Integer = timeName.IndexOf("M")
                If mPos >= 0 AndAlso mPos < timeName.Length - 1 Then
                    Dim monthStr As String = timeName.Substring(mPos + 1)
                    Dim monthNum As Integer
                    If Integer.TryParse(monthStr, monthNum) Then
                        Return monthNum
                    End If
                End If
            Catch
                ' Fall through to default
            End Try
            Return 1
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

        Private Function ReadCellWithScenario(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                              ByVal acct As String, ByVal entity As String,
                                              ByVal scenario As String) As Double
            Try
                Dim pov As String = scenario & ":" & api.Pov.Time.Name & ":" _
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
                BRApi.ErrorLog.LogMessage(si, "CR_DriverBasedPlanning.WriteCell: Error - " & acct & " - " & ex.Message)
            End Try
        End Sub

    End Class

End Namespace

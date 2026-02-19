'------------------------------------------------------------------------------------------------------------
' CR_OverheadAbsorption
' Calculate Business Rule - Factory Overhead Absorption
'
' Purpose:  Reads budgeted overhead and activity levels by cost center, calculates predetermined
'           overhead rates, applies rates to actual activity to derive absorbed overhead, and
'           computes over/under absorption variances.
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

Namespace OneStream.BusinessRule.Finance.CR_OverheadAbsorption

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    '--- Define cost centers that incur factory overhead ---
                    Dim costCenters As New List(Of String) From {
                        "CC_Fabrication", "CC_Assembly", "CC_QualityControl", "CC_Maintenance", "CC_Utilities"
                    }

                    '--- Overhead component accounts ---
                    Dim overheadComponents As New List(Of String) From {
                        "A#OH_Depreciation", "A#OH_IndirectLabor", "A#OH_Supplies",
                        "A#OH_Insurance", "A#OH_PropertyTax", "A#OH_Utilities"
                    }

                    BRApi.ErrorLog.LogMessage(si, "CR_OverheadAbsorption: Starting overhead absorption calculation.")

                    Dim totalActualOverhead As Double = 0
                    Dim totalAbsorbedOverhead As Double = 0

                    For Each cc As String In costCenters
                        Dim entityMember As String = "E#" & cc

                        '--- Read budgeted overhead for this cost center ---
                        Dim budgetedOverhead As Double = ReadDataCell(si, api, "A#OH_BudgetedTotal", entityMember, "S#Budget")
                        If budgetedOverhead = 0 Then
                            BRApi.ErrorLog.LogMessage(si, "CR_OverheadAbsorption: WARNING - Zero budgeted overhead for " & cc)
                            Continue For
                        End If

                        '--- Read budgeted activity levels ---
                        Dim budgetedMachineHours As Double = ReadDataCell(si, api, "A#STAT_BudgetedMachineHrs", entityMember, "S#Budget")
                        Dim budgetedLaborHours As Double = ReadDataCell(si, api, "A#STAT_BudgetedLaborHrs", entityMember, "S#Budget")

                        '--- Read actual activity levels ---
                        Dim actualMachineHours As Double = ReadDataCell(si, api, "A#STAT_ActualMachineHrs", entityMember, "")
                        Dim actualLaborHours As Double = ReadDataCell(si, api, "A#STAT_ActualLaborHrs", entityMember, "")

                        '--- Calculate predetermined overhead rates ---
                        ' Use machine hours as primary driver; fall back to labor hours
                        Dim predeterminedRate As Double = 0
                        Dim actualDriverHours As Double = 0
                        Dim driverType As String = ""

                        If budgetedMachineHours > 0 Then
                            predeterminedRate = budgetedOverhead / budgetedMachineHours
                            actualDriverHours = actualMachineHours
                            driverType = "MachineHrs"
                        ElseIf budgetedLaborHours > 0 Then
                            predeterminedRate = budgetedOverhead / budgetedLaborHours
                            actualDriverHours = actualLaborHours
                            driverType = "LaborHrs"
                        Else
                            BRApi.ErrorLog.LogMessage(si, "CR_OverheadAbsorption: WARNING - No valid driver for " & cc)
                            Continue For
                        End If

                        '--- Calculate absorbed overhead = predetermined rate x actual activity ---
                        Dim absorbedOverhead As Double = Math.Round(predeterminedRate * actualDriverHours, 2)

                        '--- Read actual overhead incurred for this period ---
                        Dim actualOverhead As Double = 0
                        For Each comp As String In overheadComponents
                            actualOverhead += ReadDataCell(si, api, comp, entityMember, "")
                        Next

                        '--- Calculate over/under absorption ---
                        ' Positive = Over-absorbed (favorable), Negative = Under-absorbed (unfavorable)
                        Dim absorptionVariance As Double = absorbedOverhead - actualOverhead

                        '--- Accumulate totals ---
                        totalActualOverhead += actualOverhead
                        totalAbsorbedOverhead += absorbedOverhead

                        '--- Write results ---
                        WriteDataCell(si, api, "A#OH_PredeterminedRate", entityMember, predeterminedRate)
                        WriteDataCell(si, api, "A#OH_AbsorbedOverhead", entityMember, absorbedOverhead)
                        WriteDataCell(si, api, "A#OH_ActualOverhead", entityMember, actualOverhead)
                        WriteDataCell(si, api, "A#OH_AbsorptionVariance", entityMember, absorptionVariance)

                        '--- Write over/under to separate accounts for reporting ---
                        If absorptionVariance > 0 Then
                            WriteDataCell(si, api, "A#OH_OverAbsorbed", entityMember, absorptionVariance)
                            WriteDataCell(si, api, "A#OH_UnderAbsorbed", entityMember, 0)
                        Else
                            WriteDataCell(si, api, "A#OH_OverAbsorbed", entityMember, 0)
                            WriteDataCell(si, api, "A#OH_UnderAbsorbed", entityMember, Math.Abs(absorptionVariance))
                        End If

                        BRApi.ErrorLog.LogMessage(si, "CR_OverheadAbsorption: " & cc _
                            & " | Driver=" & driverType _
                            & " | Rate=" & predeterminedRate.ToString("N4") _
                            & " | Absorbed=" & absorbedOverhead.ToString("N2") _
                            & " | Actual=" & actualOverhead.ToString("N2") _
                            & " | Variance=" & absorptionVariance.ToString("N2"))
                    Next

                    '--- Write consolidated totals ---
                    Dim totalVariance As Double = totalAbsorbedOverhead - totalActualOverhead
                    WriteDataCell(si, api, "A#OH_TotalAbsorbed", "E#Total_Manufacturing", totalAbsorbedOverhead)
                    WriteDataCell(si, api, "A#OH_TotalActual", "E#Total_Manufacturing", totalActualOverhead)
                    WriteDataCell(si, api, "A#OH_TotalVariance", "E#Total_Manufacturing", totalVariance)

                    BRApi.ErrorLog.LogMessage(si, "CR_OverheadAbsorption: Completed. Total Variance=" & totalVariance.ToString("N2"))
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFaborException(si, "CR_OverheadAbsorption", ex.Message, ex))
            End Try
        End Function

        ''' <summary>
        ''' Reads a data cell. If scenarioOverride is empty, uses current POV scenario.
        ''' </summary>
        Private Function ReadDataCell(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                      ByVal accountMember As String, ByVal entityMember As String,
                                      ByVal scenarioOverride As String) As Double
            Try
                Dim scenario As String = If(String.IsNullOrEmpty(scenarioOverride), api.Pov.Scenario.Name, scenarioOverride)
                Dim povExpr As String = scenario & ":" & api.Pov.Time.Name & ":" _
                    & entityMember & ":" & accountMember & ":F#Periodic:O#Top:I#Top:C1#Top:C2#Top:C3#Top:C4#Top"
                Dim dc As DataCell = BRApi.Finance.Data.GetDataCell(si, povExpr, False)
                Return dc.CellAmount
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "CR_OverheadAbsorption.ReadDataCell: Error - " & ex.Message)
                Return 0
            End Try
        End Function

        ''' <summary>
        ''' Writes a calculated data cell to the cube.
        ''' </summary>
        Private Sub WriteDataCell(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                  ByVal accountMember As String, ByVal entityMember As String, ByVal amount As Double)
            Try
                api.Data.SetDataCell(si, accountMember, entityMember, "F#Periodic", "O#Top", "I#Top",
                                     "C1#Top", "C2#Top", "C3#Top", "C4#Top", amount, True)
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "CR_OverheadAbsorption.WriteDataCell: Error writing " & accountMember & " - " & ex.Message)
            End Try
        End Sub

    End Class

End Namespace

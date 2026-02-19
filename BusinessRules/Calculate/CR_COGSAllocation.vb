'------------------------------------------------------------------------------------------------------------
' CR_COGSAllocation
' Calculate Business Rule - COGS Allocation by Production Line
'
' Purpose:  Reads total COGS pool (materials, labor, overhead), retrieves allocation drivers
'           (machine hours, labor hours, units produced) per production line, calculates
'           allocation rates, and writes allocated COGS amounts to product-level accounts.
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

Namespace OneStream.BusinessRule.Finance.CR_COGSAllocation

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                '--- Only execute during Calculate phase ---
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    Dim scenarioName As String = api.Pov.Scenario.Name
                    Dim timeName As String = api.Pov.Time.Name

                    '--- Define production lines ---
                    Dim productionLines As New List(Of String) From {
                        "PL_Assembly", "PL_Machining", "PL_Finishing", "PL_Packaging", "PL_Electronics"
                    }

                    '--- Define COGS component accounts ---
                    Dim acctMaterialPool As String = "A#COGS_MaterialPool"
                    Dim acctLaborPool As String = "A#COGS_LaborPool"
                    Dim acctOverheadPool As String = "A#COGS_OverheadPool"

                    '--- Read total COGS pool amounts from the consolidated entity ---
                    Dim totalMaterialCost As Double = GetAmount(si, api, acctMaterialPool, "E#Total_Manufacturing")
                    Dim totalLaborCost As Double = GetAmount(si, api, acctLaborPool, "E#Total_Manufacturing")
                    Dim totalOverheadCost As Double = GetAmount(si, api, acctOverheadPool, "E#Total_Manufacturing")

                    BRApi.ErrorLog.LogMessage(si, "CR_COGSAllocation: Total Pools - Material=" & totalMaterialCost.ToString("N2") _
                        & " Labor=" & totalLaborCost.ToString("N2") & " Overhead=" & totalOverheadCost.ToString("N2"))

                    '--- Read allocation driver totals ---
                    Dim totalMachineHours As Double = GetDriverTotal(si, api, productionLines, "A#STAT_MachineHours")
                    Dim totalLaborHours As Double = GetDriverTotal(si, api, productionLines, "A#STAT_LaborHours")
                    Dim totalUnitsProduced As Double = GetDriverTotal(si, api, productionLines, "A#STAT_UnitsProduced")

                    '--- Guard against zero denominators ---
                    If totalMachineHours = 0 OrElse totalLaborHours = 0 OrElse totalUnitsProduced = 0 Then
                        BRApi.ErrorLog.LogMessage(si, "CR_COGSAllocation: WARNING - One or more driver totals are zero. " _
                            & "MachineHrs=" & totalMachineHours.ToString() & " LaborHrs=" & totalLaborHours.ToString() _
                            & " Units=" & totalUnitsProduced.ToString())
                        Return Nothing
                    End If

                    '--- Calculate allocation rates ---
                    ' Materials allocated on units produced
                    Dim materialRatePerUnit As Double = totalMaterialCost / totalUnitsProduced
                    ' Labor allocated on labor hours
                    Dim laborRatePerHour As Double = totalLaborCost / totalLaborHours
                    ' Overhead allocated on machine hours
                    Dim overheadRatePerMachineHr As Double = totalOverheadCost / totalMachineHours

                    BRApi.ErrorLog.LogMessage(si, "CR_COGSAllocation: Rates - Material/Unit=" & materialRatePerUnit.ToString("N4") _
                        & " Labor/Hr=" & laborRatePerHour.ToString("N4") _
                        & " OH/MachHr=" & overheadRatePerMachineHr.ToString("N4"))

                    '--- Allocate to each production line ---
                    Dim runningMaterial As Double = 0
                    Dim runningLabor As Double = 0
                    Dim runningOverhead As Double = 0

                    For i As Integer = 0 To productionLines.Count - 1
                        Dim pl As String = productionLines(i)
                        Dim isLast As Boolean = (i = productionLines.Count - 1)

                        '--- Read drivers for this production line ---
                        Dim plMachineHours As Double = GetAmount(si, api, "A#STAT_MachineHours", "E#" & pl)
                        Dim plLaborHours As Double = GetAmount(si, api, "A#STAT_LaborHours", "E#" & pl)
                        Dim plUnitsProduced As Double = GetAmount(si, api, "A#STAT_UnitsProduced", "E#" & pl)

                        '--- Calculate allocated amounts ---
                        Dim allocMaterial As Double
                        Dim allocLabor As Double
                        Dim allocOverhead As Double

                        If isLast Then
                            ' Last line gets remainder to avoid rounding differences
                            allocMaterial = totalMaterialCost - runningMaterial
                            allocLabor = totalLaborCost - runningLabor
                            allocOverhead = totalOverheadCost - runningOverhead
                        Else
                            allocMaterial = Math.Round(materialRatePerUnit * plUnitsProduced, 2)
                            allocLabor = Math.Round(laborRatePerHour * plLaborHours, 2)
                            allocOverhead = Math.Round(overheadRatePerMachineHr * plMachineHours, 2)
                        End If

                        runningMaterial += allocMaterial
                        runningLabor += allocLabor
                        runningOverhead += allocOverhead

                        '--- Split into actual vs standard cost ---
                        Dim stdMaterial As Double = GetAmount(si, api, "A#COGS_StdMaterial", "E#" & pl)
                        Dim stdLabor As Double = GetAmount(si, api, "A#COGS_StdLabor", "E#" & pl)
                        Dim stdOverhead As Double = GetAmount(si, api, "A#COGS_StdOverhead", "E#" & pl)

                        Dim materialVariance As Double = allocMaterial - stdMaterial
                        Dim laborVariance As Double = allocLabor - stdLabor
                        Dim overheadVariance As Double = allocOverhead - stdOverhead

                        '--- Write allocated COGS to product-level accounts ---
                        SetAmount(si, api, "A#COGS_AllocMaterial", "E#" & pl, allocMaterial)
                        SetAmount(si, api, "A#COGS_AllocLabor", "E#" & pl, allocLabor)
                        SetAmount(si, api, "A#COGS_AllocOverhead", "E#" & pl, allocOverhead)
                        SetAmount(si, api, "A#COGS_TotalAllocated", "E#" & pl, allocMaterial + allocLabor + allocOverhead)

                        '--- Write variance splits ---
                        SetAmount(si, api, "A#COGS_MaterialVariance", "E#" & pl, materialVariance)
                        SetAmount(si, api, "A#COGS_LaborVariance", "E#" & pl, laborVariance)
                        SetAmount(si, api, "A#COGS_OverheadVariance", "E#" & pl, overheadVariance)

                        BRApi.ErrorLog.LogMessage(si, "CR_COGSAllocation: " & pl _
                            & " - Material=" & allocMaterial.ToString("N2") _
                            & " Labor=" & allocLabor.ToString("N2") _
                            & " Overhead=" & allocOverhead.ToString("N2"))
                    Next

                    '--- Write allocation rate statistics for audit trail ---
                    SetAmount(si, api, "A#STAT_MaterialRatePerUnit", "E#Total_Manufacturing", materialRatePerUnit)
                    SetAmount(si, api, "A#STAT_LaborRatePerHour", "E#Total_Manufacturing", laborRatePerHour)
                    SetAmount(si, api, "A#STAT_OHRatePerMachineHr", "E#Total_Manufacturing", overheadRatePerMachineHr)

                    BRApi.ErrorLog.LogMessage(si, "CR_COGSAllocation: Allocation completed successfully.")
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFaborException(si, "CR_COGSAllocation", ex.Message, ex))
            End Try
        End Function

        ''' <summary>
        ''' Reads an amount from the data cell using the current POV with account and entity overrides.
        ''' </summary>
        Private Function GetAmount(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                   ByVal accountMember As String, ByVal entityMember As String) As Double
            Try
                Dim povExpression As String = api.Pov.Scenario.Name & ":" & api.Pov.Time.Name & ":" _
                    & entityMember & ":" & accountMember & ":F#Periodic:O#Top:I#Top:C1#Top:C2#Top:C3#Top:C4#Top"
                Dim dc As DataCell = BRApi.Finance.Data.GetDataCell(si, povExpression, False)
                Return dc.CellAmount
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "CR_COGSAllocation.GetAmount: Error reading " & accountMember & " / " & entityMember & " - " & ex.Message)
                Return 0
            End Try
        End Function

        ''' <summary>
        ''' Writes an amount to the specified account and entity intersection.
        ''' </summary>
        Private Sub SetAmount(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                              ByVal accountMember As String, ByVal entityMember As String, ByVal amount As Double)
            Try
                api.Data.SetDataCell(si, accountMember, entityMember, "F#Periodic", "O#Top", "I#Top",
                                     "C1#Top", "C2#Top", "C3#Top", "C4#Top", amount, True)
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "CR_COGSAllocation.SetAmount: Error writing " & accountMember & " / " & entityMember & " - " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Sums a statistical driver across all production lines.
        ''' </summary>
        Private Function GetDriverTotal(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                        ByVal productionLines As List(Of String), ByVal driverAccount As String) As Double
            Dim total As Double = 0
            For Each pl As String In productionLines
                total += GetAmount(si, api, driverAccount, "E#" & pl)
            Next
            Return total
        End Function

    End Class

End Namespace

'------------------------------------------------------------------------------------------------------------
' CR_CAPEXDepreciation.vb
' OneStream XF Calculate Business Rule
'
' Purpose:  Calculates depreciation schedules for capital assets. Supports straight-line and
'           double-declining balance methods with mid-month and mid-quarter conventions.
'           Handles asset additions, disposals (gain/loss), and maintains accumulated depreciation
'           running totals. Writes depreciation expense to P&L and accumulated depreciation to BS.
'
' Asset Classes:
'   Buildings    - 30-year useful life (Straight-Line)
'   Machinery    - 10-year useful life (Straight-Line or DDB)
'   Equipment    -  7-year useful life (DDB)
'   Vehicles     -  5-year useful life (DDB)
'   IT_Assets    -  3-year useful life (Straight-Line)
'
' Frequency: Monthly
' Scope:     All entities with fixed assets
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

Namespace OneStream.BusinessRule.Finance.CR_CAPEXDepreciation

    Public Class MainClass

        ' Represents a single asset class configuration
        Private Class AssetClassConfig
            Public Property ClassName As String
            Public Property UsefulLifeYears As Integer
            Public Property SalvageRatePct As Double          ' Salvage value as % of original cost
            Public Property DepreciationMethod As String      ' "SL" = Straight-Line, "DDB" = Double-Declining
            Public Property Convention As String              ' "MidMonth" or "MidQuarter"
        End Class

        '----------------------------------------------------------------------------------------------------
        ' Main entry point
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object

            Try
                If args.CalculationType <> FinanceRulesCalculationType.Calculate Then
                    Return Nothing
                End If

                Dim entityName As String = api.Entity.GetName()
                Dim scenarioName As String = api.Scenario.GetName()
                Dim timeName As String = api.Time.GetName()

                ' ---- Define Asset Class Configurations ----
                Dim assetClasses As New List(Of AssetClassConfig) From {
                    New AssetClassConfig With {
                        .ClassName = "Buildings", .UsefulLifeYears = 30,
                        .SalvageRatePct = 0.10, .DepreciationMethod = "SL", .Convention = "MidMonth"
                    },
                    New AssetClassConfig With {
                        .ClassName = "Machinery", .UsefulLifeYears = 10,
                        .SalvageRatePct = 0.05, .DepreciationMethod = "SL", .Convention = "MidMonth"
                    },
                    New AssetClassConfig With {
                        .ClassName = "Equipment", .UsefulLifeYears = 7,
                        .SalvageRatePct = 0.0, .DepreciationMethod = "DDB", .Convention = "MidQuarter"
                    },
                    New AssetClassConfig With {
                        .ClassName = "Vehicles", .UsefulLifeYears = 5,
                        .SalvageRatePct = 0.0, .DepreciationMethod = "DDB", .Convention = "MidQuarter"
                    },
                    New AssetClassConfig With {
                        .ClassName = "IT_Assets", .UsefulLifeYears = 3,
                        .SalvageRatePct = 0.0, .DepreciationMethod = "SL", .Convention = "MidMonth"
                    }
                }

                ' ---- Process Each Asset Class ----
                For Each ac As AssetClassConfig In assetClasses
                    CalculateDepreciationForClass(si, api, entityName, scenarioName, timeName, ac)
                Next

                ' ---- Trigger downstream balance sheet roll-forward ----
                api.Data.Calculate("A#CAPEX_AccumDepr_Total")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Calculates depreciation for a single asset class
        '----------------------------------------------------------------------------------------------------
        Private Sub CalculateDepreciationForClass(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                ByVal entityName As String, ByVal scenarioName As String, ByVal timeName As String, _
                ByVal ac As AssetClassConfig)

            ' ---- Read asset register inputs for this class ----
            ' Gross Cost (original acquisition cost for all assets in this class)
            Dim grossCostPov As String = String.Format( _
                "E#{0}:S#{1}:T#{2}:A#CAPEX_GrossCost:U1#{3}", _
                entityName, scenarioName, timeName, ac.ClassName)
            Dim grossCostCell As DataCell = BRApi.Finance.Data.GetDataCell(si, grossCostPov, True)
            Dim grossCost As Double = grossCostCell.CellAmount

            ' Salvage value
            Dim salvageValue As Double = grossCost * ac.SalvageRatePct

            ' Depreciable base
            Dim depreciableBase As Double = grossCost - salvageValue

            If depreciableBase <= 0 Then Exit Sub

            ' ---- Read prior period accumulated depreciation ----
            Dim priorAccumPov As String = String.Format( _
                "E#{0}:S#{1}:T#{2}:A#CAPEX_AccumDepr_Prior:U1#{3}", _
                entityName, scenarioName, timeName, ac.ClassName)
            Dim priorAccumCell As DataCell = BRApi.Finance.Data.GetDataCell(si, priorAccumPov, True)
            Dim priorAccumDepr As Double = priorAccumCell.CellAmount

            ' Current net book value before this period's depreciation
            Dim currentNBV As Double = grossCost - priorAccumDepr

            ' ---- Read mid-period convention factor ----
            ' Stored as fraction of the month/quarter for which depreciation is taken
            ' e.g., asset placed in service on the 15th of a 30-day month = 0.5
            Dim conventionFactorPov As String = String.Format( _
                "E#{0}:S#{1}:T#{2}:A#CAPEX_ConventionFactor:U1#{3}", _
                entityName, scenarioName, timeName, ac.ClassName)
            Dim conventionCell As DataCell = BRApi.Finance.Data.GetDataCell(si, conventionFactorPov, True)
            Dim conventionFactor As Double = conventionCell.CellAmount
            If conventionFactor <= 0 OrElse conventionFactor > 1 Then
                conventionFactor = 1.0  ' Full period if not specified
            End If

            ' ---- Calculate Monthly Depreciation ----
            Dim monthlyDepreciation As Double = 0
            Dim usefulLifeMonths As Integer = ac.UsefulLifeYears * 12

            Select Case ac.DepreciationMethod

                Case "SL"
                    ' Straight-Line: (Cost - Salvage) / Useful Life in months
                    If usefulLifeMonths > 0 Then
                        monthlyDepreciation = depreciableBase / usefulLifeMonths
                    End If
                    ' Apply convention factor for additions placed in service mid-period
                    monthlyDepreciation = monthlyDepreciation * conventionFactor

                Case "DDB"
                    ' Double-Declining Balance: (2 / Useful Life Years) * Book Value / 12
                    If ac.UsefulLifeYears > 0 AndAlso currentNBV > salvageValue Then
                        Dim annualRate As Double = 2.0 / CDbl(ac.UsefulLifeYears)
                        Dim annualDDB As Double = annualRate * currentNBV
                        monthlyDepreciation = annualDDB / 12.0

                        ' Ensure we do not depreciate below salvage value
                        If (currentNBV - monthlyDepreciation) < salvageValue Then
                            monthlyDepreciation = currentNBV - salvageValue
                        End If

                        ' Apply convention factor
                        monthlyDepreciation = monthlyDepreciation * conventionFactor
                    End If

            End Select

            ' Floor at zero -- cannot have negative depreciation
            If monthlyDepreciation < 0 Then monthlyDepreciation = 0

            ' ---- Read Asset Additions (mid-year additions) ----
            Dim additionsPov As String = String.Format( _
                "E#{0}:S#{1}:T#{2}:A#CAPEX_Additions:U1#{3}", _
                entityName, scenarioName, timeName, ac.ClassName)
            Dim additionsCell As DataCell = BRApi.Finance.Data.GetDataCell(si, additionsPov, True)
            Dim additionsAmount As Double = additionsCell.CellAmount

            ' Mid-year addition depreciation: half-month convention for the addition month
            Dim additionDepr As Double = 0
            If additionsAmount > 0 AndAlso usefulLifeMonths > 0 Then
                Dim additionSalvage As Double = additionsAmount * ac.SalvageRatePct
                Dim additionDepBase As Double = additionsAmount - additionSalvage
                ' First month gets half depreciation (mid-month convention)
                additionDepr = (additionDepBase / usefulLifeMonths) * 0.5
            End If

            ' Total depreciation expense for the period
            Dim totalDeprExpense As Double = monthlyDepreciation + additionDepr

            ' ---- Handle Asset Disposals ----
            Dim disposalCostPov As String = String.Format( _
                "E#{0}:S#{1}:T#{2}:A#CAPEX_DisposalCost:U1#{3}", _
                entityName, scenarioName, timeName, ac.ClassName)
            Dim disposalCell As DataCell = BRApi.Finance.Data.GetDataCell(si, disposalCostPov, True)
            Dim disposalCost As Double = disposalCell.CellAmount

            Dim disposalProceedsPov As String = String.Format( _
                "E#{0}:S#{1}:T#{2}:A#CAPEX_DisposalProceeds:U1#{3}", _
                entityName, scenarioName, timeName, ac.ClassName)
            Dim disposalProceedsCell As DataCell = BRApi.Finance.Data.GetDataCell(si, disposalProceedsPov, True)
            Dim disposalProceeds As Double = disposalProceedsCell.CellAmount

            ' Accumulated depreciation on disposed asset (read from register)
            Dim disposalAccumPov As String = String.Format( _
                "E#{0}:S#{1}:T#{2}:A#CAPEX_DisposalAccumDepr:U1#{3}", _
                entityName, scenarioName, timeName, ac.ClassName)
            Dim disposalAccumCell As DataCell = BRApi.Finance.Data.GetDataCell(si, disposalAccumPov, True)
            Dim disposalAccumDepr As Double = disposalAccumCell.CellAmount

            ' Net book value of disposed asset
            Dim disposalNBV As Double = disposalCost - disposalAccumDepr

            ' Gain / (Loss) on disposal
            Dim gainLossOnDisposal As Double = disposalProceeds - disposalNBV

            ' ---- Compute Updated Accumulated Depreciation ----
            ' = Prior Accum + Current Depr - Accum Depr removed for disposals
            Dim updatedAccumDepr As Double = priorAccumDepr + totalDeprExpense - disposalAccumDepr

            ' Updated Net Book Value
            Dim updatedNBV As Double = (grossCost + additionsAmount - disposalCost) - updatedAccumDepr

            ' ---- Write Depreciation Expense to P&L ----
            Dim deprExpPov As String = String.Format( _
                "E#{0}:S#{1}:T#{2}:A#PL_DepreciationExp:U1#{3}", _
                entityName, scenarioName, timeName, ac.ClassName)
            api.Data.SetDataCell(si, deprExpPov, totalDeprExpense, True)

            ' ---- Write Accumulated Depreciation to BS ----
            Dim accumDeprPov As String = String.Format( _
                "E#{0}:S#{1}:T#{2}:A#BS_AccumDepreciation:U1#{3}", _
                entityName, scenarioName, timeName, ac.ClassName)
            api.Data.SetDataCell(si, accumDeprPov, updatedAccumDepr, True)

            ' ---- Write Net Book Value to BS ----
            Dim nbvPov As String = String.Format( _
                "E#{0}:S#{1}:T#{2}:A#BS_NetBookValue:U1#{3}", _
                entityName, scenarioName, timeName, ac.ClassName)
            api.Data.SetDataCell(si, nbvPov, updatedNBV, True)

            ' ---- Write Gain/Loss on Disposal to P&L ----
            If Math.Abs(gainLossOnDisposal) > 0.01 Then
                Dim gainLossPov As String = String.Format( _
                    "E#{0}:S#{1}:T#{2}:A#PL_GainLossDisposal:U1#{3}", _
                    entityName, scenarioName, timeName, ac.ClassName)
                api.Data.SetDataCell(si, gainLossPov, gainLossOnDisposal, True)
            End If

        End Sub

    End Class

End Namespace

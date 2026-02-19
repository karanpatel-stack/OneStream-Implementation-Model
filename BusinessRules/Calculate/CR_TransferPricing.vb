'------------------------------------------------------------------------------------------------------------
' CR_TransferPricing.vb
' OneStream XF Calculate Business Rule
'
' Purpose:  Calculates arm's-length intercompany transfer pricing adjustments. Reads IC transactions,
'           determines applicable transfer pricing method by transaction type, applies markup rates
'           by entity pair and jurisdiction, and records adjustments for compliance documentation.
'
' Transfer Pricing Methods:
'   CUP    - Comparable Uncontrolled Price (standard goods)
'   CostPlus - Cost plus markup (manufacturing services)
'   ResalePrice - Resale Price Method (distribution entities)
'   TNMM   - Transactional Net Margin Method (complex arrangements)
'
' Frequency: Monthly
' Scope:     All entities with intercompany transactions
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

Namespace OneStream.BusinessRule.Finance.CR_TransferPricing

    Public Class MainClass

        ' Represents a transfer pricing configuration for an entity pair and transaction type
        Private Class TPConfig
            Public Property SellingEntity As String
            Public Property BuyingEntity As String
            Public Property TransactionType As String     ' Goods, MfgServices, Distribution, Complex
            Public Property Method As String              ' CUP, CostPlus, ResalePrice, TNMM
            Public Property MarkupRate As Double          ' Markup percentage (e.g., 0.10 = 10%)
            Public Property Jurisdiction As String        ' Tax jurisdiction for documentation
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

                ' ---- Define Transfer Pricing Configurations ----
                ' In production, these would come from a metadata or dashboard data source.
                Dim tpConfigs As New List(Of TPConfig) From {
                    ' US parent manufacturing to European distributor
                    New TPConfig With {
                        .SellingEntity = "US_Manufacturing", .BuyingEntity = "EU_Distribution",
                        .TransactionType = "Goods", .Method = "CUP",
                        .MarkupRate = 0.0, .Jurisdiction = "US-DE"
                    },
                    ' US parent providing management services to Asia subsidiary
                    New TPConfig With {
                        .SellingEntity = "US_HQ", .BuyingEntity = "APAC_Operations",
                        .TransactionType = "MfgServices", .Method = "CostPlus",
                        .MarkupRate = 0.08, .Jurisdiction = "US-SG"
                    },
                    ' European hub distributing to UK subsidiary
                    New TPConfig With {
                        .SellingEntity = "EU_Hub", .BuyingEntity = "UK_Sales",
                        .TransactionType = "Distribution", .Method = "ResalePrice",
                        .MarkupRate = 0.25, .Jurisdiction = "NL-UK"
                    },
                    ' Complex IP licensing arrangement
                    New TPConfig With {
                        .SellingEntity = "IP_HoldCo", .BuyingEntity = "US_Manufacturing",
                        .TransactionType = "Complex", .Method = "TNMM",
                        .MarkupRate = 0.05, .Jurisdiction = "IE-US"
                    },
                    ' Asia manufacturing to US distribution
                    New TPConfig With {
                        .SellingEntity = "CN_Manufacturing", .BuyingEntity = "US_Distribution",
                        .TransactionType = "Goods", .Method = "CUP",
                        .MarkupRate = 0.0, .Jurisdiction = "CN-US"
                    },
                    ' Cost plus for shared R&D services
                    New TPConfig With {
                        .SellingEntity = "US_RD_Center", .BuyingEntity = "EU_Hub",
                        .TransactionType = "MfgServices", .Method = "CostPlus",
                        .MarkupRate = 0.12, .Jurisdiction = "US-NL"
                    }
                }

                ' ---- Filter configurations relevant to the current entity ----
                ' Process where this entity is either the seller or buyer
                Dim relevantConfigs As New List(Of TPConfig)
                For Each cfg As TPConfig In tpConfigs
                    If cfg.SellingEntity = entityName OrElse cfg.BuyingEntity = entityName Then
                        relevantConfigs.Add(cfg)
                    End If
                Next

                If relevantConfigs.Count = 0 Then Return Nothing

                ' ---- Process Each Transfer Pricing Arrangement ----
                For Each cfg As TPConfig In relevantConfigs

                    ' Read the intercompany transaction amount (booked at transfer price)
                    Dim icTransPov As String = String.Format( _
                        "E#{0}:S#{1}:T#{2}:A#IC_TransactionAmount:U1#{3}:U2#{4}", _
                        cfg.SellingEntity, scenarioName, timeName, cfg.BuyingEntity, cfg.TransactionType)
                    Dim icTransCell As DataCell = BRApi.Finance.Data.GetDataCell(si, icTransPov, True)
                    Dim icTransAmount As Double = icTransCell.CellAmount

                    If Math.Abs(icTransAmount) < 0.01 Then Continue For

                    ' ---- Calculate Arm's-Length Price Based on Method ----
                    Dim armsLengthPrice As Double = 0
                    Dim tpAdjustment As Double = 0

                    Select Case cfg.Method

                        Case "CUP"
                            ' Comparable Uncontrolled Price: use market benchmark price
                            Dim marketPricePov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#TP_MarketBenchmarkPrice:U1#{3}", _
                                cfg.SellingEntity, scenarioName, timeName, cfg.TransactionType)
                            Dim marketPriceCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, marketPricePov, True)
                            armsLengthPrice = marketPriceCell.CellAmount

                            ' If no market benchmark, use the transaction amount (no adjustment)
                            If Math.Abs(armsLengthPrice) < 0.01 Then
                                armsLengthPrice = icTransAmount
                            End If
                            tpAdjustment = armsLengthPrice - icTransAmount

                        Case "CostPlus"
                            ' Cost Plus: arm's-length = cost base x (1 + markup)
                            Dim costBasePov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#TP_CostBase:U1#{3}:U2#{4}", _
                                cfg.SellingEntity, scenarioName, timeName, cfg.BuyingEntity, cfg.TransactionType)
                            Dim costBaseCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, costBasePov, True)
                            Dim costBase As Double = costBaseCell.CellAmount

                            If costBase > 0 Then
                                armsLengthPrice = costBase * (1.0 + cfg.MarkupRate)
                            Else
                                armsLengthPrice = icTransAmount  ' Fallback
                            End If
                            tpAdjustment = armsLengthPrice - icTransAmount

                        Case "ResalePrice"
                            ' Resale Price Method: arm's-length = resale price x (1 - gross margin)
                            Dim resalePricePov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#TP_ResalePrice:U1#{3}:U2#{4}", _
                                cfg.BuyingEntity, scenarioName, timeName, cfg.SellingEntity, cfg.TransactionType)
                            Dim resalePriceCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, resalePricePov, True)
                            Dim resalePrice As Double = resalePriceCell.CellAmount

                            If resalePrice > 0 Then
                                armsLengthPrice = resalePrice * (1.0 - cfg.MarkupRate)
                            Else
                                armsLengthPrice = icTransAmount
                            End If
                            tpAdjustment = armsLengthPrice - icTransAmount

                        Case "TNMM"
                            ' Transactional Net Margin Method: target net margin on costs
                            Dim operatingCostsPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#TP_OperatingCosts:U1#{3}:U2#{4}", _
                                cfg.SellingEntity, scenarioName, timeName, cfg.BuyingEntity, cfg.TransactionType)
                            Dim opCostsCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, operatingCostsPov, True)
                            Dim operatingCosts As Double = opCostsCell.CellAmount

                            If operatingCosts > 0 Then
                                ' Target: net profit margin of MarkupRate on total costs
                                Dim targetProfit As Double = operatingCosts * cfg.MarkupRate
                                armsLengthPrice = operatingCosts + targetProfit
                            Else
                                armsLengthPrice = icTransAmount
                            End If
                            tpAdjustment = armsLengthPrice - icTransAmount

                    End Select

                    ' ---- Record Transfer Pricing Adjustment ----
                    If Math.Abs(tpAdjustment) > 0.01 Then

                        ' Adjustment on seller side (increase/decrease revenue)
                        Dim sellerAdjPov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#TP_Adjustment:U1#{3}:U2#{4}", _
                            cfg.SellingEntity, scenarioName, timeName, cfg.BuyingEntity, cfg.TransactionType)
                        api.Data.SetDataCell(si, sellerAdjPov, tpAdjustment, True)

                        ' Offsetting adjustment on buyer side (increase/decrease cost)
                        Dim buyerAdjPov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#TP_Adjustment:U1#{3}:U2#{4}", _
                            cfg.BuyingEntity, scenarioName, timeName, cfg.SellingEntity, cfg.TransactionType)
                        api.Data.SetDataCell(si, buyerAdjPov, -tpAdjustment, True)

                    End If

                    ' ---- Record Arm's-Length Price for Documentation ----
                    Dim alPricePov As String = String.Format( _
                        "E#{0}:S#{1}:T#{2}:A#TP_ArmsLengthPrice:U1#{3}:U2#{4}", _
                        cfg.SellingEntity, scenarioName, timeName, cfg.BuyingEntity, cfg.TransactionType)
                    api.Data.SetDataCell(si, alPricePov, armsLengthPrice, True)

                    ' Record method used for audit trail
                    Dim methodFlagValue As Double = 0
                    Select Case cfg.Method
                        Case "CUP" : methodFlagValue = 1.0
                        Case "CostPlus" : methodFlagValue = 2.0
                        Case "ResalePrice" : methodFlagValue = 3.0
                        Case "TNMM" : methodFlagValue = 4.0
                    End Select
                    Dim methodPov As String = String.Format( _
                        "E#{0}:S#{1}:T#{2}:A#TP_MethodFlag:U1#{3}:U2#{4}", _
                        cfg.SellingEntity, scenarioName, timeName, cfg.BuyingEntity, cfg.TransactionType)
                    api.Data.SetDataCell(si, methodPov, methodFlagValue, True)

                Next ' config

                ' Trigger downstream IC elimination calculations
                api.Data.Calculate("A#TP_Adjustment")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try

        End Function

    End Class

End Namespace

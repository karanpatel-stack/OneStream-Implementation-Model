'------------------------------------------------------------------------------------------------------------
' OneStream XF Finance Business Rule: FR_EquityPickup
'------------------------------------------------------------------------------------------------------------
' Purpose:     Implements the equity method of accounting for investees where the investor holds
'              significant influence (typically 20-50% ownership) but not control.
'
' Equity Method Logic (per IAS 28 / ASC 323):
'   1. Identify equity-method investees from entity properties (flagged by FR_Consolidation)
'   2. Read investee's net income from their consolidated results
'   3. Calculate investor's share: Ownership % x Investee Net Income
'   4. Record equity pickup journal entry:
'        DR  Investment in Affiliate (BS asset)
'        CR  Equity in Earnings of Affiliate (P&L income)
'   5. Handle dividends received from investee:
'        DR  Cash / Dividend Receivable
'        CR  Investment in Affiliate (reduces the investment balance)
'   6. Handle FX impact when the investee operates in a foreign currency
'
' Note: Under equity method, the investee's individual accounts are NOT consolidated
'       line-by-line. Only the investor's share of net income flows through.
'
' Author:       OneStream Administrator
' Created:      2026-02-18
' Modified:     2026-02-18
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports Microsoft.VisualBasic
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.Finance.FR_EquityPickup

    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for equity pickup processing.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object
            Try
                If args.FinanceRulesEventType = FinanceRulesEventType.Calculate Then
                    Return Me.ProcessEquityPickup(si, globals, api, args)
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_EquityPickup.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessEquityPickup: Identifies equity method investees and calculates the investor's
        ' share of earnings for each.
        '----------------------------------------------------------------------------------------------------
        Private Function ProcessEquityPickup(ByVal si As SessionInfo, ByVal globals As BRGlobals, _
                                             ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                Dim investorEntity As String = api.Entity.GetName()

                ' Retrieve the list of equity method investees for this investor
                ' These are identified by the EquityMethodFlag marker written by FR_Consolidation
                Dim investees As List(Of InvesteeInfo) = Me.GetEquityMethodInvestees(si, api, investorEntity)

                If investees.Count = 0 Then
                    ' No equity method investees for this entity
                    Return Nothing
                End If

                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_EquityPickup: Processing {investees.Count} equity method investee(s) for [{investorEntity}]")

                For Each investee As InvesteeInfo In investees
                    ' Step 1: Read the investee's net income
                    Dim investeeNI As Double = Me.GetInvesteeNetIncome(si, api, investee.EntityName)

                    ' Step 2: Calculate the investor's share
                    Dim equityShare As Double = investeeNI * investee.OwnershipPct

                    ' Step 3: Record the equity pickup entry
                    Me.RecordEquityPickupEntry(si, api, investorEntity, investee.EntityName, equityShare)

                    ' Step 4: Handle dividends received from investee
                    Dim dividendsReceived As Double = Me.GetDividendsReceived(si, api, investorEntity, investee.EntityName)
                    If dividendsReceived <> 0 Then
                        Me.RecordDividendAdjustment(si, api, investorEntity, investee.EntityName, dividendsReceived)
                    End If

                    ' Step 5: Handle FX impact for foreign investees
                    Dim investeeCurrency As String = Me.GetInvesteeCurrency(si, api, investee.EntityName)
                    Dim investorCurrency As String = Me.GetInvestorCurrency(si, api, investorEntity)

                    If Not String.Equals(investeeCurrency, investorCurrency, StringComparison.OrdinalIgnoreCase) Then
                        Me.CalculateEquityFXImpact(si, api, investorEntity, investee, investeeNI)
                    End If

                    BRApi.ErrorLog.LogMessage(si, _
                        $"  Investee [{investee.EntityName}]: NI={investeeNI:N2}, Share({investee.OwnershipPct:P2})={equityShare:N2}, DivRcvd={dividendsReceived:N2}")
                Next

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_EquityPickup.ProcessEquityPickup", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetEquityMethodInvestees: Retrieves entities flagged for equity method treatment.
        ' Reads the A_EquityMethodFlag account values set by FR_Consolidation.
        '----------------------------------------------------------------------------------------------------
        Private Function GetEquityMethodInvestees(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                   ByVal investorEntity As String) As List(Of InvesteeInfo)
            Dim investees As New List(Of InvesteeInfo)

            Try
                ' Query entities that have been flagged as equity method investees
                ' The flag is stored in A_EquityMethodFlag with the IC dimension pointing to the investee
                Dim memberFilter As String = "E#[All Entities].Base"
                Dim memberList As List(Of MemberInfo) = BRApi.Finance.Members.GetMembersByFilter( _
                    si, BRApi.Finance.Dim.GetDimPk(si, DimType.Entity.Id), memberFilter)

                If memberList IsNot Nothing Then
                    For Each member As MemberInfo In memberList
                        Dim investeeName As String = member.Member.Name

                        ' Check if there is an equity method flag for this entity pair
                        Dim flagPov As String = _
                            $"E#{investorEntity}:A#A_EquityMethodFlag:C#C_Local:F#F_None:O#O_None:I#{investeeName}"
                        Dim flagValue As Double = api.Data.GetDataCell(flagPov).CellAmount

                        ' If ownership % is stored as the flag value and is between 20% and 50%
                        If flagValue >= 0.2 AndAlso flagValue <= 0.5 Then
                            investees.Add(New InvesteeInfo() With {
                                .EntityName = investeeName,
                                .OwnershipPct = flagValue
                            })
                        End If
                    Next
                End If

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_EquityPickup: WARNING - Error reading equity method flags: {ex.Message}")
            End Try

            Return investees
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetInvesteeNetIncome: Reads the investee's net income from their consolidated data.
        '----------------------------------------------------------------------------------------------------
        Private Function GetInvesteeNetIncome(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                              ByVal investeeName As String) As Double
            Try
                ' Read net income from the investee's consolidated results
                Dim niPov As String = $"E#{investeeName}:A#PL_NetIncome:C#C_Consolidated:F#F_None:O#O_None:I#I_None"
                Return api.Data.GetDataCell(niPov).CellAmount

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_EquityPickup: WARNING - Could not read NI for investee [{investeeName}]: {ex.Message}")
                Return 0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' RecordEquityPickupEntry: Creates the journal entry for equity pickup.
        '   DR  Investment in Affiliate (BS long-term asset)
        '   CR  Equity in Earnings of Affiliate (P&L other income)
        '----------------------------------------------------------------------------------------------------
        Private Sub RecordEquityPickupEntry(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                             ByVal investorEntity As String, ByVal investeeEntity As String, _
                                             ByVal equityShare As Double)
            Try
                ' Debit: Investment in Affiliate (increase asset)
                ' The IC dimension identifies which investee this relates to
                Dim investmentPov As String = _
                    $"E#{investorEntity}:A#BS_InvestmentInAffiliate:C#C_Local:F#F_None:O#O_None:I#{investeeEntity}"
                api.Data.SetDataCell(investmentPov, equityShare)

                ' Credit: Equity in Earnings (income recognition)
                ' Negative value represents a credit in OneStream convention for P&L
                Dim earningsPov As String = _
                    $"E#{investorEntity}:A#PL_EquityInEarnings:C#C_Local:F#F_None:O#O_None:I#{investeeEntity}"
                api.Data.SetDataCell(earningsPov, -equityShare)

                BRApi.ErrorLog.LogMessage(si, _
                    $"  Equity pickup entry: DR Investment={equityShare:N2}, CR Equity in Earnings={equityShare:N2}")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_EquityPickup.RecordEquityPickupEntry", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GetDividendsReceived: Reads dividends received by the investor from the investee.
        '----------------------------------------------------------------------------------------------------
        Private Function GetDividendsReceived(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                              ByVal investorEntity As String, ByVal investeeEntity As String) As Double
            Try
                Dim divPov As String = _
                    $"E#{investorEntity}:A#PL_DividendIncome:C#C_Local:F#F_None:O#O_None:I#{investeeEntity}"
                Return api.Data.GetDataCell(divPov).CellAmount

            Catch ex As Exception
                Return 0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' RecordDividendAdjustment: Under equity method, dividends from the investee reduce the
        ' carrying value of the investment (they are NOT income -- income is already recognized
        ' through equity pickup). This adjusts the investment balance.
        '
        '   DR  Cash / Dividend Receivable (already recorded by entity)
        '   CR  Investment in Affiliate (reduces the investment)
        '----------------------------------------------------------------------------------------------------
        Private Sub RecordDividendAdjustment(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                              ByVal investorEntity As String, ByVal investeeEntity As String, _
                                              ByVal dividendsReceived As Double)
            Try
                ' Reduce the investment balance by dividends received
                ' Credit (negative) to Investment in Affiliate
                Dim investmentAdjPov As String = _
                    $"E#{investorEntity}:A#BS_InvestmentInAffiliate_DivAdj:C#C_Local:F#F_None:O#O_None:I#{investeeEntity}"
                api.Data.SetDataCell(investmentAdjPov, -Math.Abs(dividendsReceived))

                ' Reclassify dividend income to equity method treatment
                ' The dividend income already booked should be reversed since equity pickup replaces it
                Dim divReclassPov As String = _
                    $"E#{investorEntity}:A#PL_DividendIncome_EqAdj:C#C_Local:F#F_None:O#O_None:I#{investeeEntity}"
                api.Data.SetDataCell(divReclassPov, Math.Abs(dividendsReceived))

                BRApi.ErrorLog.LogMessage(si, _
                    $"  Dividend adjustment: Investment reduced by {Math.Abs(dividendsReceived):N2}")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_EquityPickup.RecordDividendAdjustment", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GetInvesteeCurrency / GetInvestorCurrency: Read currency properties for FX handling.
        '----------------------------------------------------------------------------------------------------
        Private Function GetInvesteeCurrency(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                             ByVal entityName As String) As String
            Try
                Dim currency As String = BRApi.Finance.Entity.GetPropertyValue(si, entityName, "Currency")
                Return If(String.IsNullOrEmpty(currency), "USD", currency.Trim().ToUpper())
            Catch ex As Exception
                Return "USD"
            End Try
        End Function

        Private Function GetInvestorCurrency(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                             ByVal entityName As String) As String
            Try
                Dim currency As String = BRApi.Finance.Entity.GetPropertyValue(si, entityName, "Currency")
                Return If(String.IsNullOrEmpty(currency), "USD", currency.Trim().ToUpper())
            Catch ex As Exception
                Return "USD"
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' CalculateEquityFXImpact: Handles the foreign exchange impact on equity pickup when the
        ' investee reports in a different currency than the investor.
        '
        ' The equity pickup amount (calculated in investee's currency) must be translated to the
        ' investor's functional currency. The FX difference between the rate used for the equity
        ' pickup and the rate at which the investment is retranslated at period end creates an
        ' FX gain/loss recorded in OCI.
        '----------------------------------------------------------------------------------------------------
        Private Sub CalculateEquityFXImpact(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                             ByVal investorEntity As String, ByVal investee As InvesteeInfo, _
                                             ByVal investeeNI As Double)
            Try
                Dim investeeCurrency As String = Me.GetInvesteeCurrency(si, api, investee.EntityName)
                Dim investorCurrency As String = Me.GetInvestorCurrency(si, api, investorEntity)
                Dim currencyPair As String = $"{investeeCurrency}_{investorCurrency}"

                ' Read average rate for P&L translation and closing rate for BS retranslation
                Dim avgRatePov As String = $"S#Rates:E#{currencyPair}:A#FXRate_Avg:C#C_Local:F#F_None:O#O_None"
                Dim avgRate As Double = api.Data.GetDataCell(avgRatePov).CellAmount

                Dim closeRatePov As String = $"S#Rates:E#{currencyPair}:A#FXRate_Close:C#C_Local:F#F_None:O#O_None"
                Dim closeRate As Double = api.Data.GetDataCell(closeRatePov).CellAmount

                If avgRate = 0 OrElse closeRate = 0 Then
                    BRApi.ErrorLog.LogMessage(si, _
                        $"  WARNING: Missing FX rates for equity pickup FX calculation [{currencyPair}]")
                    Return
                End If

                ' Equity pickup at average rate (income statement rate)
                Dim equityShareLocal As Double = investeeNI * investee.OwnershipPct
                Dim equityShareAtAvg As Double = equityShareLocal * avgRate

                ' Investment retranslation at closing rate creates FX difference
                Dim equityShareAtClose As Double = equityShareLocal * closeRate
                Dim fxDifference As Double = equityShareAtClose - equityShareAtAvg

                ' Record FX impact in OCI
                Dim fxOCIPov As String = _
                    $"E#{investorEntity}:A#OCI_EquityPickupFX:C#C_Local:F#F_None:O#O_None:I#{investee.EntityName}"
                api.Data.SetDataCell(fxOCIPov, fxDifference)

                BRApi.ErrorLog.LogMessage(si, _
                    $"  Equity FX impact [{currencyPair}]: AtAvg={equityShareAtAvg:N2}, AtClose={equityShareAtClose:N2}, FXDiff={fxDifference:N2}")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_EquityPickup.CalculateEquityFXImpact", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' InvesteeInfo: Data structure to hold equity method investee details.
        '----------------------------------------------------------------------------------------------------
        Private Class InvesteeInfo
            Public Property EntityName As String
            Public Property OwnershipPct As Double
        End Class

    End Class

End Namespace

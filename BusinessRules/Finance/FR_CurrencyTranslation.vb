'------------------------------------------------------------------------------------------------------------
' OneStream XF Finance Business Rule: FR_CurrencyTranslation
'------------------------------------------------------------------------------------------------------------
' Purpose:     Multi-currency foreign exchange (FX) translation engine. Translates entity financial
'              data from local (functional) currency to the group reporting currency using the
'              appropriate exchange rate for each account type.
'
' Translation Rules (per IAS 21 / ASC 830):
'   - P&L accounts (Revenue, Expenses):        Average rate for the period
'   - Balance Sheet - Assets & Liabilities:     Closing (spot) rate at period end
'   - Equity - Common Stock, APIC:              Historical rate (rate at acquisition date)
'   - Equity - Retained Earnings:               Calculated (Opening RE + translated NI - translated Dividends)
'   - CTA (Cumulative Translation Adjustment):  Plug to balance BS, recorded in OCI
'
' FX Rate Sources:
'   Rates are read from a rate cube or table intersection. Convention:
'     - Average Rate:    S#Rates:A#FXRate_Avg:E#[CurrencyPair]
'     - Closing Rate:    S#Rates:A#FXRate_Close:E#[CurrencyPair]
'     - Historical Rate: S#Rates:A#FXRate_Hist:E#[CurrencyPair]
'
' Output:
'   Translated amounts are written to Consolidation member C_Translated.
'   CTA is written to Account A_CTA within Other Comprehensive Income.
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

Namespace OneStream.BusinessRule.Finance.FR_CurrencyTranslation

    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for currency translation processing.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object
            Try
                If args.FinanceRulesEventType = FinanceRulesEventType.Calculate Then
                    Return Me.ProcessTranslation(si, globals, api, args)
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_CurrencyTranslation.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessTranslation: Core translation workflow. Determines whether translation is needed
        ' (entity currency <> group currency) and applies the correct rate to each account type.
        '----------------------------------------------------------------------------------------------------
        Private Function ProcessTranslation(ByVal si As SessionInfo, ByVal globals As BRGlobals, _
                                            ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                Dim entityName As String = api.Entity.GetName()

                ' Retrieve the entity's local (functional) currency and the group reporting currency
                Dim localCurrency As String = Me.GetEntityCurrency(si, api, entityName)
                Dim groupCurrency As String = Me.GetGroupCurrency(si, api)

                ' If entity currency equals group currency, no translation is needed
                If String.Equals(localCurrency, groupCurrency, StringComparison.OrdinalIgnoreCase) Then
                    BRApi.ErrorLog.LogMessage(si, _
                        $"FR_CurrencyTranslation: Entity [{entityName}] already in group currency [{groupCurrency}]. No translation required.")
                    Return Nothing
                End If

                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_CurrencyTranslation: Translating [{entityName}] from [{localCurrency}] to [{groupCurrency}]")

                ' Build the currency pair identifier used to look up FX rates
                Dim currencyPair As String = $"{localCurrency}_{groupCurrency}"

                ' Retrieve exchange rates for the current period
                Dim avgRate As Double = Me.GetExchangeRate(si, api, currencyPair, RateType.Average)
                Dim closeRate As Double = Me.GetExchangeRate(si, api, currencyPair, RateType.Closing)
                Dim histRate As Double = Me.GetExchangeRate(si, api, currencyPair, RateType.Historical)

                BRApi.ErrorLog.LogMessage(si, _
                    $"  FX Rates [{currencyPair}]: Avg={avgRate:F6}, Close={closeRate:F6}, Hist={histRate:F6}")

                ' Validate that all rates are non-zero to prevent division errors
                If avgRate = 0 OrElse closeRate = 0 OrElse histRate = 0 Then
                    Throw New XFException(si, "FR_CurrencyTranslation", _
                        $"Missing FX rate(s) for currency pair [{currencyPair}]. Avg={avgRate}, Close={closeRate}, Hist={histRate}")
                End If

                '--------------------------------------------------------------------------------------------
                ' Step 1: Translate P&L accounts at average rate
                ' Revenue and expense accounts use the weighted average rate for the period,
                ' approximating the rate at which individual transactions occurred.
                '--------------------------------------------------------------------------------------------
                Me.TranslateAccountGroup(si, api, entityName, "A#PL_Total.Base", avgRate, "Average")

                '--------------------------------------------------------------------------------------------
                ' Step 2: Translate Balance Sheet - Assets at closing rate
                ' All asset accounts are translated at the period-end spot rate per IAS 21.
                '--------------------------------------------------------------------------------------------
                Me.TranslateAccountGroup(si, api, entityName, "A#BS_TotalAssets.Base", closeRate, "Closing")

                '--------------------------------------------------------------------------------------------
                ' Step 3: Translate Balance Sheet - Liabilities at closing rate
                '--------------------------------------------------------------------------------------------
                Me.TranslateAccountGroup(si, api, entityName, "A#BS_TotalLiabilities.Base", closeRate, "Closing")

                '--------------------------------------------------------------------------------------------
                ' Step 4: Translate Equity - Common Stock and APIC at historical rate
                ' These equity items are translated at the rate prevailing on the date of the
                ' original transaction (issuance date). This rate is fixed and does not change.
                '--------------------------------------------------------------------------------------------
                Me.TranslateAccountGroup(si, api, entityName, "A#EQ_CommonStock", histRate, "Historical")
                Me.TranslateAccountGroup(si, api, entityName, "A#EQ_APIC", histRate, "Historical")

                '--------------------------------------------------------------------------------------------
                ' Step 5: Calculate Retained Earnings translation
                ' RE is not translated at a single rate. Instead:
                '   Opening RE (translated) = Prior period closing RE (already translated)
                '   + Net Income (translated at average rate, done in Step 1)
                '   - Dividends (translated at rate on declaration date, approximated by average rate)
                '--------------------------------------------------------------------------------------------
                Me.CalculateRetainedEarningsTranslation(si, api, entityName, avgRate, histRate)

                '--------------------------------------------------------------------------------------------
                ' Step 6: Calculate CTA (Cumulative Translation Adjustment)
                ' CTA is the balancing plug that arises because BS and P&L use different rates.
                ' CTA = Total Assets (at close) - Total Liabilities (at close) - Equity (at hist/calc)
                ' The difference from the prior period CTA is recorded in OCI for the current period.
                '--------------------------------------------------------------------------------------------
                Me.CalculateCTA(si, api, entityName, closeRate, avgRate, histRate)

                BRApi.ErrorLog.LogMessage(si, $"FR_CurrencyTranslation: Translation complete for [{entityName}]")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_CurrencyTranslation.ProcessTranslation", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetEntityCurrency: Reads the local/functional currency assigned to the entity.
        ' The currency is stored as a property on the Entity dimension member.
        '----------------------------------------------------------------------------------------------------
        Private Function GetEntityCurrency(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                           ByVal entityName As String) As String
            Try
                Dim currency As String = BRApi.Finance.Entity.GetPropertyValue(si, entityName, "Currency")

                If String.IsNullOrEmpty(currency) Then
                    ' Default to USD if no currency is defined
                    BRApi.ErrorLog.LogMessage(si, $"  WARNING: No currency defined for [{entityName}], defaulting to USD")
                    Return "USD"
                End If

                Return currency.Trim().ToUpper()

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_CurrencyTranslation.GetEntityCurrency", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetGroupCurrency: Reads the group reporting currency from the scenario or application settings.
        '----------------------------------------------------------------------------------------------------
        Private Function GetGroupCurrency(ByVal si As SessionInfo, ByVal api As FinanceRulesApi) As String
            Try
                ' Group currency is typically defined at the scenario or application level
                Dim groupCurrency As String = BRApi.Finance.Scenario.GetPropertyValue( _
                    si, api.Pov.Scenario.GetName(), "ReportingCurrency")

                If String.IsNullOrEmpty(groupCurrency) Then
                    Return "USD" ' Default group reporting currency
                End If

                Return groupCurrency.Trim().ToUpper()

            Catch ex As Exception
                Return "USD"
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetExchangeRate: Retrieves the FX rate from the rate data cube.
        ' Rates are stored in a dedicated scenario/cube at specific account intersections.
        '----------------------------------------------------------------------------------------------------
        Private Function GetExchangeRate(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                         ByVal currencyPair As String, ByVal rateType As RateType) As Double
            Try
                ' Determine the rate account based on rate type
                Dim rateAccount As String
                Select Case rateType
                    Case RateType.Average
                        rateAccount = "FXRate_Avg"
                    Case RateType.Closing
                        rateAccount = "FXRate_Close"
                    Case RateType.Historical
                        rateAccount = "FXRate_Hist"
                    Case Else
                        rateAccount = "FXRate_Avg"
                End Select

                ' Read the FX rate from the rates cube
                ' POV: Use the rates scenario, the currency pair entity, and the rate account
                Dim ratePov As String = $"S#Rates:E#{currencyPair}:A#{rateAccount}:C#C_Local:F#F_None:O#O_None"
                Dim rate As Double = api.Data.GetDataCell(ratePov).CellAmount

                ' If rate is zero, attempt to read the inverse pair and invert
                If rate = 0 Then
                    Dim inversePair As String = currencyPair.Split("_"c)(1) & "_" & currencyPair.Split("_"c)(0)
                    Dim inverseRatePov As String = $"S#Rates:E#{inversePair}:A#{rateAccount}:C#C_Local:F#F_None:O#O_None"
                    Dim inverseRate As Double = api.Data.GetDataCell(inverseRatePov).CellAmount

                    If inverseRate <> 0 Then
                        rate = 1.0 / inverseRate
                    End If
                End If

                Return rate

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, $"  WARNING: Could not retrieve {rateType} rate for [{currencyPair}]: {ex.Message}")
                Return 0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' TranslateAccountGroup: Translates a group of accounts by applying the specified FX rate.
        ' Reads from C_Local and writes translated amounts to C_Translated.
        '----------------------------------------------------------------------------------------------------
        Private Sub TranslateAccountGroup(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                          ByVal entityName As String, ByVal accountFilter As String, _
                                          ByVal fxRate As Double, ByVal rateDescription As String)
            Try
                ' Source: local currency data in C_Local
                Dim sourcePov As String = $"E#{entityName}:C#C_Local"
                ' Destination: translated data in C_Translated
                Dim destPov As String = $"E#{entityName}:C#C_Translated"

                ' Apply the FX rate as a multiplication factor during the calculate/copy operation
                ' This translates all accounts in the filter from local currency to group currency
                api.Data.Calculate(sourcePov, destPov, accountFilter, fxRate)

                BRApi.ErrorLog.LogMessage(si, _
                    $"  Translated [{accountFilter}] at {rateDescription} rate ({fxRate:F6})")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_CurrencyTranslation.TranslateAccountGroup", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' CalculateRetainedEarningsTranslation: Handles the special translation of Retained Earnings.
        ' RE cannot be translated at a single rate because it is a cumulative balance built up
        ' over multiple periods, each with different exchange rates.
        '
        ' Formula: Translated RE = Opening RE (translated, from prior period)
        '                         + Net Income (already translated at average rate)
        '                         - Dividends (translated at average rate as approximation)
        '----------------------------------------------------------------------------------------------------
        Private Sub CalculateRetainedEarningsTranslation(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                          ByVal entityName As String, ByVal avgRate As Double, _
                                                          ByVal histRate As Double)
            Try
                ' Read prior period closing RE (already translated) as the opening balance
                Dim priorPeriodPov As String = $"E#{entityName}:A#EQ_RetainedEarnings:C#C_Translated:T#PriorPeriod"
                Dim openingRE As Double = api.Data.GetDataCell(priorPeriodPov).CellAmount

                ' Net Income is already translated at average rate during P&L translation
                Dim translatedNIPov As String = $"E#{entityName}:A#PL_NetIncome:C#C_Translated"
                Dim translatedNI As Double = api.Data.GetDataCell(translatedNIPov).CellAmount

                ' Dividends translated at average rate
                Dim localDividendsPov As String = $"E#{entityName}:A#EQ_Dividends:C#C_Local"
                Dim localDividends As Double = api.Data.GetDataCell(localDividendsPov).CellAmount
                Dim translatedDividends As Double = localDividends * avgRate

                ' Calculate translated Retained Earnings
                Dim translatedRE As Double = openingRE + translatedNI - translatedDividends

                ' Write the calculated translated RE
                Dim destPov As String = $"E#{entityName}:A#EQ_RetainedEarnings:C#C_Translated:F#F_None:O#O_None"
                api.Data.SetDataCell(destPov, translatedRE)

                BRApi.ErrorLog.LogMessage(si, _
                    $"  Retained Earnings translation: Opening={openingRE:N2} + NI={translatedNI:N2} - Div={translatedDividends:N2} = {translatedRE:N2}")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_CurrencyTranslation.CalculateRetainedEarningsTranslation", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' CalculateCTA: Computes the Cumulative Translation Adjustment.
        ' CTA arises because the Balance Sheet is translated at the closing rate while the P&L
        ' (which flows into Retained Earnings) is translated at the average rate.
        '
        ' CTA = Total Assets (at closing rate)
        '     - Total Liabilities (at closing rate)
        '     - Equity excl CTA (at historical/calculated rates)
        '
        ' The CTA ensures the translated Balance Sheet still balances (Assets = L + E).
        ' The period movement in CTA is recorded in Other Comprehensive Income (OCI).
        '----------------------------------------------------------------------------------------------------
        Private Sub CalculateCTA(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                 ByVal entityName As String, ByVal closeRate As Double, _
                                 ByVal avgRate As Double, ByVal histRate As Double)
            Try
                ' Read translated total assets (at closing rate)
                Dim translatedAssetsPov As String = $"E#{entityName}:A#BS_TotalAssets:C#C_Translated"
                Dim translatedAssets As Double = api.Data.GetDataCell(translatedAssetsPov).CellAmount

                ' Read translated total liabilities (at closing rate)
                Dim translatedLiabPov As String = $"E#{entityName}:A#BS_TotalLiabilities:C#C_Translated"
                Dim translatedLiabilities As Double = api.Data.GetDataCell(translatedLiabPov).CellAmount

                ' Read translated equity components (excl CTA)
                Dim translatedEquityExclCTAPov As String = $"E#{entityName}:A#EQ_TotalExclCTA:C#C_Translated"
                Dim translatedEquityExclCTA As Double = api.Data.GetDataCell(translatedEquityExclCTAPov).CellAmount

                ' CTA = Assets - Liabilities - Equity (excl CTA)
                ' This is the balancing amount that makes the BS balance after translation
                Dim ctaBalance As Double = translatedAssets - translatedLiabilities - translatedEquityExclCTA

                ' Write CTA to the OCI / CTA account
                Dim ctaPov As String = $"E#{entityName}:A#OCI_CTA:C#C_Translated:F#F_None:O#O_None"
                api.Data.SetDataCell(ctaPov, ctaBalance)

                ' Calculate the period movement in CTA for P&L / OCI reporting
                Dim priorCTAPov As String = $"E#{entityName}:A#OCI_CTA:C#C_Translated:T#PriorPeriod"
                Dim priorCTA As Double = api.Data.GetDataCell(priorCTAPov).CellAmount
                Dim ctaMovement As Double = ctaBalance - priorCTA

                ' Write CTA movement to the OCI P&L line
                Dim ctaMovPov As String = $"E#{entityName}:A#PL_OCI_CTA:C#C_Translated:F#F_None:O#O_None"
                api.Data.SetDataCell(ctaMovPov, ctaMovement)

                BRApi.ErrorLog.LogMessage(si, _
                    $"  CTA calculated: Balance={ctaBalance:N2}, Movement={ctaMovement:N2}")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_CurrencyTranslation.CalculateCTA", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' Enumeration for exchange rate types.
        '----------------------------------------------------------------------------------------------------
        Private Enum RateType
            Average = 0
            Closing = 1
            Historical = 2
        End Enum

    End Class

End Namespace

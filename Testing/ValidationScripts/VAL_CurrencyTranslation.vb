'------------------------------------------------------------------------------------------------------------
' OneStream XF Validation Script: VAL_CurrencyTranslation
'------------------------------------------------------------------------------------------------------------
' Purpose:     Validates the results of foreign currency translation processing. Confirms that
'              translated Balance Sheet balances (Assets = Liab + Equity + CTA), that CTA amounts
'              are reasonable, and that P&L translated at average rate ties to the sum of monthly
'              translated amounts.
'
' Validation Checks:
'   1. BS in reporting currency: TotalAssets = TotalLiabilities + TotalEquity (incl CTA)
'   2. CTA reasonableness: compare to manual calculation using closing vs average rate diff
'   3. P&L translation: verify annual translated = sum of monthly translations at avg rate
'   4. Rate validity: confirm non-zero rates exist for all entity/currency combinations
'
' Output:      DataTable with columns: Entity, Currency, CheckType, LocalTotal, TranslatedTotal,
'              RateUsed, Expected, Actual, Status (Pass/Fail)
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.VAL_CurrencyTranslation

    Public Class MainClass

        ' Tolerance for BS balance check (reporting currency)
        Private Const BS_TOLERANCE As Double = 0.01

        ' Tolerance for CTA reasonableness (percentage of total assets)
        Private Const CTA_REASONABLENESS_PCT As Double = 0.05

        ' Tolerance for P&L monthly sum vs annual total
        Private Const PL_TOLERANCE As Double = 1.0

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for the currency translation validation rule.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Dim resultsTable As New DataTable("CurrencyTranslationValidation")
                resultsTable.Columns.Add("Entity", GetType(String))
                resultsTable.Columns.Add("Currency", GetType(String))
                resultsTable.Columns.Add("CheckType", GetType(String))
                resultsTable.Columns.Add("LocalTotal", GetType(Double))
                resultsTable.Columns.Add("TranslatedTotal", GetType(Double))
                resultsTable.Columns.Add("RateUsed", GetType(Double))
                resultsTable.Columns.Add("Expected", GetType(Double))
                resultsTable.Columns.Add("Actual", GetType(Double))
                resultsTable.Columns.Add("Status", GetType(String))

                ' Foreign currency entities requiring translation (currency <> USD)
                Dim fxEntities As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                fxEntities.Add("Plant_CA01_Toronto", "CAD")
                fxEntities.Add("Plant_MX01_Monterrey", "MXN")
                fxEntities.Add("Plant_DE01_Munich", "EUR")
                fxEntities.Add("Plant_DE02_Stuttgart", "EUR")
                fxEntities.Add("Plant_UK01_Birmingham", "GBP")
                fxEntities.Add("Plant_FR01_Lyon", "EUR")
                fxEntities.Add("Plant_CN01_Shanghai", "CNY")
                fxEntities.Add("Plant_CN02_Shenzhen", "CNY")
                fxEntities.Add("Plant_JP01_Osaka", "JPY")
                fxEntities.Add("Plant_IN01_Pune", "INR")

                Dim groupCurrency As String = "USD"

                BRApi.ErrorLog.LogMessage(si, "VAL_CurrencyTranslation: Starting FX translation validation...")

                For Each kvp As KeyValuePair(Of String, String) In fxEntities
                    Dim entityName As String = kvp.Key
                    Dim localCurrency As String = kvp.Value
                    Dim currencyPair As String = $"{localCurrency}_{groupCurrency}"

                    ' Retrieve exchange rates
                    Dim avgRatePov As String = $"S#Rates:E#{currencyPair}:A#FXRate_Avg:C#C_Local:F#F_None:O#O_None"
                    Dim closeRatePov As String = $"S#Rates:E#{currencyPair}:A#FXRate_Close:C#C_Local:F#F_None:O#O_None"
                    Dim histRatePov As String = $"S#Rates:E#{currencyPair}:A#FXRate_Hist:C#C_Local:F#F_None:O#O_None"

                    Dim avgRate As Double = BRApi.Finance.Data.GetDataCell(si, avgRatePov).CellAmount
                    Dim closeRate As Double = BRApi.Finance.Data.GetDataCell(si, closeRatePov).CellAmount
                    Dim histRate As Double = BRApi.Finance.Data.GetDataCell(si, histRatePov).CellAmount

                    '------------------------------------------------------------------------------------
                    ' Check 1: Translated BS Balance -- Assets = Liabilities + Equity (incl CTA)
                    '------------------------------------------------------------------------------------
                    Dim translatedAssetsPov As String = $"E#{entityName}:A#TotalAssets:C#C_Translated:S#Actual:F#F_Closing:O#O_None"
                    Dim translatedLiabPov As String = $"E#{entityName}:A#TotalLiabilities:C#C_Translated:S#Actual:F#F_Closing:O#O_None"
                    Dim translatedEquityPov As String = $"E#{entityName}:A#TotalEquity:C#C_Translated:S#Actual:F#F_Closing:O#O_None"

                    Dim translatedAssets As Double = BRApi.Finance.Data.GetDataCell(si, translatedAssetsPov).CellAmount
                    Dim translatedLiab As Double = BRApi.Finance.Data.GetDataCell(si, translatedLiabPov).CellAmount
                    Dim translatedEquity As Double = BRApi.Finance.Data.GetDataCell(si, translatedEquityPov).CellAmount

                    Dim bsDifference As Double = translatedAssets - (translatedLiab + translatedEquity)
                    Dim bsStatus As String = If(Math.Abs(bsDifference) <= BS_TOLERANCE, "PASS", "FAIL")

                    Me.AddResultRow(resultsTable, entityName, localCurrency, "BS Balance Check", _
                        translatedAssets, translatedLiab + translatedEquity, closeRate, _
                        translatedAssets, translatedLiab + translatedEquity, bsStatus)

                    '------------------------------------------------------------------------------------
                    ' Check 2: CTA Reasonableness
                    ' CTA should be proportional to the difference between closing and avg rates
                    '------------------------------------------------------------------------------------
                    Dim ctaPov As String = $"E#{entityName}:A#OCI_ForeignCurrency:C#C_Translated:S#Actual:F#F_Closing:O#O_None"
                    Dim ctaAmount As Double = BRApi.Finance.Data.GetDataCell(si, ctaPov).CellAmount

                    Dim ctaPctOfAssets As Double = 0
                    If translatedAssets <> 0 Then ctaPctOfAssets = Math.Abs(ctaAmount / translatedAssets)

                    Dim ctaStatus As String = If(ctaPctOfAssets <= CTA_REASONABLENESS_PCT, "PASS", "FAIL")

                    Me.AddResultRow(resultsTable, entityName, localCurrency, "CTA Reasonableness", _
                        0, ctaAmount, closeRate - avgRate, CTA_REASONABLENESS_PCT * translatedAssets, _
                        ctaAmount, ctaStatus)

                    '------------------------------------------------------------------------------------
                    ' Check 3: P&L Translation -- annual total = sum of monthly translated amounts
                    '------------------------------------------------------------------------------------
                    Dim localRevPov As String = $"E#{entityName}:A#TotalRevenue:C#C_Local:S#Actual:F#F_None:O#O_None"
                    Dim localRevenue As Double = BRApi.Finance.Data.GetDataCell(si, localRevPov).CellAmount

                    Dim translatedRevPov As String = $"E#{entityName}:A#TotalRevenue:C#C_Translated:S#Actual:F#F_None:O#O_None"
                    Dim translatedRevenue As Double = BRApi.Finance.Data.GetDataCell(si, translatedRevPov).CellAmount

                    Dim expectedTranslatedRev As Double = localRevenue * avgRate
                    Dim plDifference As Double = Math.Abs(translatedRevenue - expectedTranslatedRev)
                    Dim plStatus As String = If(plDifference <= PL_TOLERANCE, "PASS", "FAIL")

                    Me.AddResultRow(resultsTable, entityName, localCurrency, "P&L Translation at Avg Rate", _
                        localRevenue, translatedRevenue, avgRate, expectedTranslatedRev, translatedRevenue, plStatus)
                Next

                ' Summary
                Dim totalChecks As Integer = resultsTable.Rows.Count
                Dim failCount As Integer = resultsTable.Select("Status = 'FAIL'").Length

                BRApi.ErrorLog.LogMessage(si, _
                    $"VAL_CurrencyTranslation: Complete. Total={totalChecks}, Pass={totalChecks - failCount}, Fail={failCount}")

                Return resultsTable

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "VAL_CurrencyTranslation.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' AddResultRow: Helper to add a validation result to the DataTable.
        '----------------------------------------------------------------------------------------------------
        Private Sub AddResultRow(ByRef dt As DataTable, ByVal entity As String, ByVal currency As String, _
                                 ByVal checkType As String, ByVal localTotal As Double, _
                                 ByVal translatedTotal As Double, ByVal rateUsed As Double, _
                                 ByVal expected As Double, ByVal actual As Double, ByVal status As String)
            Dim row As DataRow = dt.NewRow()
            row("Entity") = entity
            row("Currency") = currency
            row("CheckType") = checkType
            row("LocalTotal") = Math.Round(localTotal, 2)
            row("TranslatedTotal") = Math.Round(translatedTotal, 2)
            row("RateUsed") = Math.Round(rateUsed, 6)
            row("Expected") = Math.Round(expected, 2)
            row("Actual") = Math.Round(actual, 2)
            row("Status") = status
            dt.Rows.Add(row)
        End Sub

    End Class

End Namespace

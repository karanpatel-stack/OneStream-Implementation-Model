'------------------------------------------------------------------------------------------------------------
' OneStream XF Finance Business Rule: FR_FlowAnalysis
'------------------------------------------------------------------------------------------------------------
' Purpose:     Roll-forward / flow analysis engine for Balance Sheet accounts. Tracks movements
'              from opening balance to closing balance through categorized flows, ensuring that
'              the sum of all movements reconciles to the change in the BS position.
'
' Roll-Forward Structure:
'   Opening Balance (= Prior Period Closing)
'   + Operating Movements (input/calculated changes during the period)
'   + FX Translation Impact (currency retranslation effect on opening balance)
'   + Elimination Flows (consolidation adjustments affecting the balance)
'   + Acquisition Flows (balances from newly acquired entities)
'   - Disposal Flows (balances from disposed entities removed from group)
'   = Closing Balance
'
' Validation:
'   Opening + All Movements = Closing
'   Any discrepancy indicates a data integrity issue and is flagged.
'
' Flow Dimension Members (OneStream Flow / Movement dimension):
'   - F_Opening:        Opening balance carried forward from prior period
'   - F_Movement:       Operating movements (day-to-day business activity)
'   - F_FXImpact:       FX retranslation impact on opening balances
'   - F_Elimination:    Consolidation elimination adjustments
'   - F_Acquisition:    Balances from acquired entities entering the group
'   - F_Disposal:       Balances from disposed entities leaving the group
'   - F_Closing:        Calculated closing balance = sum of all flows
'   - F_Total:          Parent member aggregating all flows (= Closing)
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

Namespace OneStream.BusinessRule.Finance.FR_FlowAnalysis

    Public Class MainClass

        ' Tolerance for roll-forward reconciliation validation
        Private Const RECON_TOLERANCE As Double = 0.01

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for flow analysis processing.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object
            Try
                If args.FinanceRulesEventType = FinanceRulesEventType.Calculate Then
                    Return Me.ProcessFlowAnalysis(si, globals, api, args)
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_FlowAnalysis.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessFlowAnalysis: Orchestrates the roll-forward calculation for all BS accounts.
        ' Sets opening balances, calculates FX impact, and validates the reconciliation.
        '----------------------------------------------------------------------------------------------------
        Private Function ProcessFlowAnalysis(ByVal si As SessionInfo, ByVal globals As BRGlobals, _
                                              ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                Dim entityName As String = api.Entity.GetName()

                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_FlowAnalysis: Processing roll-forward analysis for entity [{entityName}]")

                ' Get all BS accounts that require flow analysis
                Dim bsAccounts As List(Of String) = Me.GetBalanceSheetAccounts(si, api)

                If bsAccounts.Count = 0 Then
                    BRApi.ErrorLog.LogMessage(si, "  No BS accounts found for flow analysis")
                    Return Nothing
                End If

                Dim validationErrors As New List(Of String)

                For Each accountName As String In bsAccounts
                    ' Step 1: Set opening balance from prior period closing
                    Dim openingBalance As Double = Me.SetOpeningBalance(si, api, entityName, accountName)

                    ' Step 2: Read operating movements (input data)
                    Dim movementAmount As Double = Me.GetMovementAmount(si, api, entityName, accountName)

                    ' Step 3: Calculate FX translation impact on opening balance
                    Dim fxImpact As Double = Me.CalculateFXImpact(si, api, entityName, accountName, openingBalance)

                    ' Step 4: Read elimination flows
                    Dim elimFlow As Double = Me.GetEliminationFlow(si, api, entityName, accountName)

                    ' Step 5: Read acquisition/disposal flows
                    Dim acquisitionFlow As Double = Me.GetAcquisitionFlow(si, api, entityName, accountName)
                    Dim disposalFlow As Double = Me.GetDisposalFlow(si, api, entityName, accountName)

                    ' Step 6: Calculate closing balance = Opening + all movements
                    Dim calculatedClosing As Double = openingBalance + movementAmount + fxImpact + _
                                                       elimFlow + acquisitionFlow + disposalFlow

                    ' Step 7: Write closing balance
                    Me.SetClosingBalance(si, api, entityName, accountName, calculatedClosing)

                    ' Step 8: Validate against the actual account balance
                    Dim actualClosing As Double = Me.GetActualClosingBalance(si, api, entityName, accountName)

                    If actualClosing <> 0 Then
                        Dim reconDifference As Double = Math.Abs(calculatedClosing - actualClosing)
                        If reconDifference > RECON_TOLERANCE Then
                            validationErrors.Add( _
                                $"Account [{accountName}]: Calculated={calculatedClosing:N2}, Actual={actualClosing:N2}, Diff={reconDifference:N2}")
                        End If
                    End If
                Next

                '--------------------------------------------------------------------------------------------
                ' Report validation results
                '--------------------------------------------------------------------------------------------
                If validationErrors.Count > 0 Then
                    BRApi.ErrorLog.LogMessage(si, _
                        $"FR_FlowAnalysis: WARNING - {validationErrors.Count} roll-forward reconciliation error(s):")
                    For Each errorMsg As String In validationErrors
                        BRApi.ErrorLog.LogMessage(si, $"  RECON ERROR: {errorMsg}")
                    Next
                Else
                    BRApi.ErrorLog.LogMessage(si, _
                        "FR_FlowAnalysis: All roll-forward reconciliations passed.")
                End If

                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_FlowAnalysis: Processing complete for [{entityName}], {bsAccounts.Count} accounts analyzed")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_FlowAnalysis.ProcessFlowAnalysis", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetBalanceSheetAccounts: Retrieves the list of BS accounts that require flow analysis.
        ' Uses the Account dimension to find all base-level BS accounts.
        '----------------------------------------------------------------------------------------------------
        Private Function GetBalanceSheetAccounts(ByVal si As SessionInfo, ByVal api As FinanceRulesApi) As List(Of String)
            Dim accounts As New List(Of String)

            Try
                ' Get all base-level accounts under the BS parent
                Dim accountFilter As String = "A#BS_Total.Base"
                Dim accountMembers As List(Of MemberInfo) = BRApi.Finance.Members.GetMembersByFilter( _
                    si, BRApi.Finance.Dim.GetDimPk(si, DimType.Account.Id), accountFilter)

                If accountMembers IsNot Nothing Then
                    For Each member As MemberInfo In accountMembers
                        accounts.Add(member.Member.Name)
                    Next
                End If

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, $"FR_FlowAnalysis: WARNING - Error reading BS accounts: {ex.Message}")
            End Try

            Return accounts
        End Function

        '----------------------------------------------------------------------------------------------------
        ' SetOpeningBalance: Reads the prior period closing balance and sets it as the current
        ' period opening balance. This is a fundamental accounting principle:
        ' Closing(T-1) = Opening(T)
        '----------------------------------------------------------------------------------------------------
        Private Function SetOpeningBalance(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                            ByVal entityName As String, ByVal accountName As String) As Double
            Try
                ' Read prior period closing balance
                Dim priorClosePov As String = _
                    $"E#{entityName}:A#{accountName}:C#C_Consolidated:F#F_Closing:O#O_None:I#I_None:T#PriorPeriod"
                Dim priorClosing As Double = api.Data.GetDataCell(priorClosePov).CellAmount

                ' Write as current period opening balance
                Dim openingPov As String = _
                    $"E#{entityName}:A#{accountName}:C#C_Consolidated:F#F_Opening:O#O_None:I#I_None"
                api.Data.SetDataCell(openingPov, priorClosing)

                Return priorClosing

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, _
                    $"  WARNING: Could not set opening balance for [{accountName}]: {ex.Message}")
                Return 0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetMovementAmount: Reads the operating movement for the current period.
        ' This represents the day-to-day business activity that changes the BS balance
        ' (e.g., new purchases increasing assets, payments reducing liabilities).
        '----------------------------------------------------------------------------------------------------
        Private Function GetMovementAmount(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                            ByVal entityName As String, ByVal accountName As String) As Double
            Try
                Dim movPov As String = _
                    $"E#{entityName}:A#{accountName}:C#C_Consolidated:F#F_Movement:O#O_None:I#I_None"
                Return api.Data.GetDataCell(movPov).CellAmount

            Catch ex As Exception
                Return 0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' CalculateFXImpact: Calculates the FX translation impact on the opening balance.
        ' When opening balances are retranslated at the current period's closing rate (instead
        ' of the prior period's closing rate), a difference arises. This represents the impact
        ' of exchange rate changes on the opening BS position.
        '
        ' FX Impact = Opening Balance x (Current Close Rate - Prior Close Rate)
        '           = Opening at Current Rate - Opening at Prior Rate
        '----------------------------------------------------------------------------------------------------
        Private Function CalculateFXImpact(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                            ByVal entityName As String, ByVal accountName As String, _
                                            ByVal openingBalance As Double) As Double
            Try
                ' Determine if the entity requires FX retranslation
                Dim entityCurrency As String = BRApi.Finance.Entity.GetPropertyValue(si, entityName, "Currency")
                Dim groupCurrency As String = "USD" ' Default group currency

                If String.IsNullOrEmpty(entityCurrency) OrElse _
                   String.Equals(entityCurrency.Trim(), groupCurrency, StringComparison.OrdinalIgnoreCase) Then
                    ' No FX impact for entities already in group currency
                    Return 0
                End If

                ' Read FX impact from a pre-calculated intersection
                ' (typically computed by FR_CurrencyTranslation and stored in F_FXImpact)
                Dim fxPov As String = _
                    $"E#{entityName}:A#{accountName}:C#C_Translated:F#F_FXImpact:O#O_None:I#I_None"
                Dim fxImpact As Double = api.Data.GetDataCell(fxPov).CellAmount

                ' If not pre-calculated, estimate using rate difference
                If fxImpact = 0 AndAlso openingBalance <> 0 Then
                    Dim currencyPair As String = $"{entityCurrency.Trim().ToUpper()}_{groupCurrency}"

                    ' Current period closing rate
                    Dim currentClosePov As String = $"S#Rates:E#{currencyPair}:A#FXRate_Close:C#C_Local:F#F_None:O#O_None"
                    Dim currentCloseRate As Double = api.Data.GetDataCell(currentClosePov).CellAmount

                    ' Prior period closing rate
                    Dim priorClosePov As String = $"S#Rates:E#{currencyPair}:A#FXRate_Close:C#C_Local:F#F_None:O#O_None:T#PriorPeriod"
                    Dim priorCloseRate As Double = api.Data.GetDataCell(priorClosePov).CellAmount

                    If currentCloseRate <> 0 AndAlso priorCloseRate <> 0 Then
                        ' FX Impact = Opening Balance in local currency x (current rate - prior rate)
                        ' Since opening balance is already translated, we need the local amount first
                        Dim openingLocal As Double = If(priorCloseRate <> 0, openingBalance / priorCloseRate, 0)
                        fxImpact = openingLocal * (currentCloseRate - priorCloseRate)
                    End If
                End If

                ' Write the FX impact to the flow dimension
                If fxImpact <> 0 Then
                    Dim fxWritePov As String = _
                        $"E#{entityName}:A#{accountName}:C#C_Consolidated:F#F_FXImpact:O#O_None:I#I_None"
                    api.Data.SetDataCell(fxWritePov, fxImpact)
                End If

                Return fxImpact

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, _
                    $"  WARNING: FX impact calculation failed for [{accountName}]: {ex.Message}")
                Return 0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetEliminationFlow: Reads elimination adjustments that affect the flow for this account.
        ' These are consolidation adjustments (IC eliminations, equity adjustments) that change
        ' the BS balance beyond operating movements.
        '----------------------------------------------------------------------------------------------------
        Private Function GetEliminationFlow(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                             ByVal entityName As String, ByVal accountName As String) As Double
            Try
                Dim elimPov As String = _
                    $"E#{entityName}:A#{accountName}:C#C_Elimination:F#F_Elimination:O#O_None:I#I_None"
                Return api.Data.GetDataCell(elimPov).CellAmount

            Catch ex As Exception
                Return 0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetAcquisitionFlow: Reads balances from newly acquired entities that enter the
        ' consolidation group during the current period. This flow represents the opening
        ' balances of the acquired entity at the acquisition date.
        '----------------------------------------------------------------------------------------------------
        Private Function GetAcquisitionFlow(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                             ByVal entityName As String, ByVal accountName As String) As Double
            Try
                Dim acqPov As String = _
                    $"E#{entityName}:A#{accountName}:C#C_Consolidated:F#F_Acquisition:O#O_None:I#I_None"
                Return api.Data.GetDataCell(acqPov).CellAmount

            Catch ex As Exception
                Return 0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetDisposalFlow: Reads balances from disposed entities leaving the consolidation group.
        ' This flow removes the disposed entity's balances from the group as of the disposal date.
        ' Typically a negative amount (reducing consolidated balances).
        '----------------------------------------------------------------------------------------------------
        Private Function GetDisposalFlow(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                          ByVal entityName As String, ByVal accountName As String) As Double
            Try
                Dim dispPov As String = _
                    $"E#{entityName}:A#{accountName}:C#C_Consolidated:F#F_Disposal:O#O_None:I#I_None"
                Return api.Data.GetDataCell(dispPov).CellAmount

            Catch ex As Exception
                Return 0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' SetClosingBalance: Writes the calculated closing balance to the F_Closing flow member.
        ' Closing = Opening + Movement + FX + Elimination + Acquisition + Disposal
        '----------------------------------------------------------------------------------------------------
        Private Sub SetClosingBalance(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                       ByVal entityName As String, ByVal accountName As String, _
                                       ByVal closingAmount As Double)
            Try
                Dim closePov As String = _
                    $"E#{entityName}:A#{accountName}:C#C_Consolidated:F#F_Closing:O#O_None:I#I_None"
                api.Data.SetDataCell(closePov, closingAmount)

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_FlowAnalysis.SetClosingBalance", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GetActualClosingBalance: Reads the actual account balance (non-flow view) for validation.
        ' This is the balance from the standard consolidation without flow decomposition.
        ' Used to validate that the roll-forward reconciles to the actual balance.
        '----------------------------------------------------------------------------------------------------
        Private Function GetActualClosingBalance(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                  ByVal entityName As String, ByVal accountName As String) As Double
            Try
                ' Read the account balance from the Total flow (which represents the actual balance)
                Dim actualPov As String = _
                    $"E#{entityName}:A#{accountName}:C#C_Consolidated:F#F_Total:O#O_None:I#I_None"
                Return api.Data.GetDataCell(actualPov).CellAmount

            Catch ex As Exception
                Return 0
            End Try
        End Function

    End Class

End Namespace

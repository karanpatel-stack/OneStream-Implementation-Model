'------------------------------------------------------------------------------------------------------------
' OneStream XF Finance Business Rule: FR_MinorityInterest
'------------------------------------------------------------------------------------------------------------
' Purpose:     Calculates Non-Controlling Interest (NCI), formerly known as Minority Interest,
'              for subsidiaries where the parent owns more than 50% but less than 100%.
'
' NCI Accounting (per IFRS 10 / ASC 810):
'   - When a subsidiary is fully consolidated (100% of accounts aggregated), the portion
'     not owned by the parent must be attributed to non-controlling shareholders.
'   - NCI % = (100% - Parent Ownership %)
'
' Calculations:
'   1. NCI Share of Net Income = Subsidiary Net Income x NCI %
'      - Recorded in P&L: reduces income attributable to parent, allocates to NCI
'   2. NCI Share of Equity = Subsidiary Total Equity x NCI %
'      - Recorded in BS: separate equity line item for NCI
'   3. NCI adjustments are recorded in the Consolidation = C_Elimination layer
'      (or a dedicated C_NCI member if available)
'
' Impact on Financial Statements:
'   - Income Statement: "Net Income attributable to NCI" shown below Net Income
'   - Balance Sheet: "Non-Controlling Interest" shown as a separate equity component
'   - Statement of Changes in Equity: NCI column tracks movements
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

Namespace OneStream.BusinessRule.Finance.FR_MinorityInterest

    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for minority interest / NCI processing.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object
            Try
                If args.FinanceRulesEventType = FinanceRulesEventType.Calculate Then
                    Return Me.ProcessMinorityInterest(si, globals, api, args)
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_MinorityInterest.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessMinorityInterest: Identifies subsidiaries with NCI and calculates the NCI share
        ' of net income and equity for each.
        '----------------------------------------------------------------------------------------------------
        Private Function ProcessMinorityInterest(ByVal si As SessionInfo, ByVal globals As BRGlobals, _
                                                  ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                Dim parentEntity As String = api.Entity.GetName()

                ' Only process NCI at parent entity level (entities that consolidate subsidiaries)
                If Not api.Entity.HasChildren() Then
                    Return Nothing
                End If

                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_MinorityInterest: Processing NCI for parent entity [{parentEntity}]")

                ' Get child entities and check ownership for each
                Dim childEntities As List(Of String) = Me.GetChildEntities(si, api, parentEntity)

                For Each childEntity As String In childEntities
                    ' Read ownership percentage from entity properties
                    Dim ownershipPct As Double = Me.GetOwnershipPercentage(si, api, childEntity)

                    ' NCI only applies when ownership is > 50% (full consolidation) but < 100%
                    If ownershipPct > 0.5 AndAlso ownershipPct < 1.0 Then
                        Dim nciPct As Double = 1.0 - ownershipPct

                        BRApi.ErrorLog.LogMessage(si, _
                            $"  Subsidiary [{childEntity}]: Ownership={ownershipPct:P2}, NCI%={nciPct:P2}")

                        ' Calculate NCI share of Net Income
                        Me.CalculateNCINetIncome(si, api, parentEntity, childEntity, nciPct)

                        ' Calculate NCI share of Total Equity
                        Me.CalculateNCIEquity(si, api, parentEntity, childEntity, nciPct)

                        ' Handle NCI movements (changes in NCI equity during the period)
                        Me.CalculateNCIMovements(si, api, parentEntity, childEntity, nciPct)
                    End If
                Next

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_MinorityInterest.ProcessMinorityInterest", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetChildEntities: Retrieves immediate child entities for NCI evaluation.
        '----------------------------------------------------------------------------------------------------
        Private Function GetChildEntities(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                          ByVal parentEntity As String) As List(Of String)
            Try
                Dim children As New List(Of String)
                Dim memberFilter As String = $"E#{parentEntity}.Children"
                Dim memberList As List(Of MemberInfo) = BRApi.Finance.Members.GetMembersByFilter( _
                    si, BRApi.Finance.Dim.GetDimPk(si, DimType.Entity.Id), memberFilter)

                If memberList IsNot Nothing Then
                    For Each member As MemberInfo In memberList
                        children.Add(member.Member.Name)
                    Next
                End If

                Return children

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_MinorityInterest.GetChildEntities", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetOwnershipPercentage: Reads ownership % from entity properties.
        '----------------------------------------------------------------------------------------------------
        Private Function GetOwnershipPercentage(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                 ByVal entityName As String) As Double
            Try
                Dim ownershipStr As String = BRApi.Finance.Entity.GetPropertyValue(si, entityName, "OwnershipPct")

                If Not String.IsNullOrEmpty(ownershipStr) Then
                    Dim pct As Double = 0
                    If Double.TryParse(ownershipStr, NumberStyles.Any, CultureInfo.InvariantCulture, pct) Then
                        Return pct
                    End If
                End If

                Return 1.0 ' Default to 100% (no NCI)

            Catch ex As Exception
                Return 1.0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' CalculateNCINetIncome: Calculates and records the NCI share of subsidiary net income.
        '
        ' The subsidiary is fully consolidated (100% of its P&L), so we must carve out the
        ' portion belonging to NCI shareholders:
        '   DR  Net Income Attributable to NCI (reduces parent's share)
        '   CR  NCI in Net Income (allocates to NCI)
        '----------------------------------------------------------------------------------------------------
        Private Sub CalculateNCINetIncome(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                           ByVal parentEntity As String, ByVal childEntity As String, _
                                           ByVal nciPct As Double)
            Try
                ' Read the subsidiary's consolidated net income
                Dim niPov As String = $"E#{childEntity}:A#PL_NetIncome:C#C_Consolidated:F#F_None:O#O_None:I#I_None"
                Dim subsidiaryNI As Double = api.Data.GetDataCell(niPov).CellAmount

                ' Calculate NCI share
                Dim nciNI As Double = subsidiaryNI * nciPct

                ' Record NCI allocation in P&L
                ' Debit: Reduce net income attributable to parent shareholders
                Dim nciNIDebitPov As String = _
                    $"E#{parentEntity}:A#PL_NI_AttrToNCI:C#C_Elimination:F#F_None:O#O_None:I#I_None"
                api.Data.SetDataCell(nciNIDebitPov, nciNI)

                ' Credit: NCI share of net income (shown as a separate P&L line below NI)
                Dim nciNICreditPov As String = _
                    $"E#{parentEntity}:A#PL_NCIShareOfNI:C#C_Elimination:F#F_None:O#O_None:I#I_None"
                api.Data.SetDataCell(nciNICreditPov, -nciNI)

                BRApi.ErrorLog.LogMessage(si, _
                    $"    NCI NI: Subsidiary NI={subsidiaryNI:N2} x NCI%={nciPct:P2} = NCI Share={nciNI:N2}")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_MinorityInterest.CalculateNCINetIncome", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' CalculateNCIEquity: Calculates and records the NCI share of subsidiary equity.
        '
        ' On the consolidated Balance Sheet, NCI is presented as a separate component of equity:
        '   DR  Subsidiary Equity Attributable to NCI (reduces parent equity)
        '   CR  Non-Controlling Interest (separate equity line)
        '----------------------------------------------------------------------------------------------------
        Private Sub CalculateNCIEquity(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                        ByVal parentEntity As String, ByVal childEntity As String, _
                                        ByVal nciPct As Double)
            Try
                ' Read the subsidiary's total equity (translated to group currency)
                Dim equityPov As String = $"E#{childEntity}:A#EQ_TotalEquity:C#C_Consolidated:F#F_None:O#O_None:I#I_None"
                Dim subsidiaryEquity As Double = api.Data.GetDataCell(equityPov).CellAmount

                ' Calculate NCI share of equity
                Dim nciEquity As Double = subsidiaryEquity * nciPct

                ' Record NCI equity on the consolidated Balance Sheet
                ' Debit: Reduce equity attributable to parent (offset to investment elimination)
                Dim nciEqDebitPov As String = _
                    $"E#{parentEntity}:A#EQ_AttrToNCI:C#C_Elimination:F#F_None:O#O_None:I#I_None"
                api.Data.SetDataCell(nciEqDebitPov, nciEquity)

                ' Credit: Non-Controlling Interest equity line
                Dim nciEqCreditPov As String = _
                    $"E#{parentEntity}:A#EQ_NonControllingInterest:C#C_Elimination:F#F_None:O#O_None:I#I_None"
                api.Data.SetDataCell(nciEqCreditPov, -nciEquity)

                BRApi.ErrorLog.LogMessage(si, _
                    $"    NCI Equity: Subsidiary Equity={subsidiaryEquity:N2} x NCI%={nciPct:P2} = NCI Equity={nciEquity:N2}")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_MinorityInterest.CalculateNCIEquity", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' CalculateNCIMovements: Tracks period-over-period changes in NCI equity balance.
        ' Movements include: NCI share of NI, NCI share of OCI, NCI share of dividends,
        ' and any changes in ownership percentage during the period.
        '
        ' Opening NCI + NCI NI + NCI OCI - NCI Dividends +/- Ownership Changes = Closing NCI
        '----------------------------------------------------------------------------------------------------
        Private Sub CalculateNCIMovements(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                           ByVal parentEntity As String, ByVal childEntity As String, _
                                           ByVal nciPct As Double)
            Try
                ' Read prior period NCI balance (opening NCI for current period)
                Dim priorNCIPov As String = _
                    $"E#{parentEntity}:A#EQ_NonControllingInterest:C#C_Elimination:T#PriorPeriod:F#F_None:O#O_None:I#I_None"
                Dim openingNCI As Double = api.Data.GetDataCell(priorNCIPov).CellAmount

                ' Read current period NCI share of net income (calculated above)
                Dim nciNIPov As String = _
                    $"E#{parentEntity}:A#PL_NCIShareOfNI:C#C_Elimination:F#F_None:O#O_None:I#I_None"
                Dim nciNI As Double = api.Data.GetDataCell(nciNIPov).CellAmount

                ' Read NCI share of OCI (translation adjustments, etc.)
                Dim subsidiaryOCIPov As String = _
                    $"E#{childEntity}:A#OCI_Total:C#C_Consolidated:F#F_None:O#O_None:I#I_None"
                Dim subsidiaryOCI As Double = api.Data.GetDataCell(subsidiaryOCIPov).CellAmount
                Dim nciOCI As Double = subsidiaryOCI * nciPct

                ' Read NCI share of dividends paid by subsidiary
                Dim subDivPov As String = _
                    $"E#{childEntity}:A#EQ_Dividends:C#C_Consolidated:F#F_None:O#O_None:I#I_None"
                Dim subsidiaryDividends As Double = api.Data.GetDataCell(subDivPov).CellAmount
                Dim nciDividends As Double = subsidiaryDividends * nciPct

                ' Calculate expected closing NCI
                Dim expectedClosingNCI As Double = openingNCI + nciNI + nciOCI - nciDividends

                ' Write NCI movement detail for reporting
                Dim nciOCIPov As String = _
                    $"E#{parentEntity}:A#EQ_NCI_OCI:C#C_Elimination:F#F_None:O#O_None:I#I_None"
                api.Data.SetDataCell(nciOCIPov, -nciOCI)

                Dim nciDivPov As String = _
                    $"E#{parentEntity}:A#EQ_NCI_Dividends:C#C_Elimination:F#F_None:O#O_None:I#I_None"
                api.Data.SetDataCell(nciDivPov, nciDividends)

                BRApi.ErrorLog.LogMessage(si, _
                    $"    NCI Movement: Open={openingNCI:N2} + NI={nciNI:N2} + OCI={nciOCI:N2} - Div={nciDividends:N2} = Close={expectedClosingNCI:N2}")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_MinorityInterest.CalculateNCIMovements", ex.Message))
            End Try
        End Sub

    End Class

End Namespace

'------------------------------------------------------------------------------------------------------------
' EH_ICMatchingCheck
' Event Handler Business Rule - Intercompany Balance Validation on Submit
'
' Purpose:  Validates intercompany (IC) transaction balances before allowing workflow
'           submission. Reads IC transaction pairs for the current entity, matches
'           IC Receivables vs. partner IC Payables and IC Revenue vs. partner IC COGS,
'           and blocks submission if any pair exceeds the tolerance threshold.
'
' IC Matching Rules:
'   - IC AR (Entity A -> Partner B) must match IC AP (Entity B -> Partner A)
'   - IC Revenue (Entity A -> Partner B) must match IC COGS (Entity B -> Partner A)
'   - Net difference per partner must be within $500 tolerance
'   - Any pair exceeding tolerance blocks submission
'   - Returns detailed list of unmatched/out-of-tolerance items
'
' Scope:    Event Handler
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
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.EventHandler.EH_ICMatchingCheck

    Public Class MainClass

        '--- Tolerance threshold for IC matching (absolute dollar amount) ---
        Private Const IC_TOLERANCE As Double = 500.0

        '--- IC account pairs to validate ---
        Private Const ACCT_IC_RECEIVABLE As String = "A#ICReceivables"
        Private Const ACCT_IC_PAYABLE As String = "A#ICPayables"
        Private Const ACCT_IC_REVENUE As String = "A#ICRevenue"
        Private Const ACCT_IC_COGS As String = "A#ICCOGS"

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As EventHandlerArgs) As Object
            Try
                '--- Only execute during workflow submission events ---
                If args.EventHandlerType <> EventHandlerType.WorkflowAction Then
                    Return Nothing
                End If

                '--- Extract POV context ---
                Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                Dim timeName As String = args.NameValuePairs.XFGetValue("Time", String.Empty)
                Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", String.Empty)

                If String.IsNullOrEmpty(timeName) Then
                    timeName = BRApi.Finance.Members.GetMemberName(si, DimType.Time.Id, si.WorkflowClusterPk.TimeId)
                End If
                If String.IsNullOrEmpty(entityName) Then
                    entityName = BRApi.Finance.Members.GetMemberName(si, DimType.Entity.Id, si.WorkflowClusterPk.EntityId)
                End If

                BRApi.ErrorLog.LogMessage(si, "EH_ICMatchingCheck: Starting IC validation for " &
                    scenarioName & "/" & timeName & "/" & entityName)

                '--- Get the list of IC partner entities for the current entity ---
                Dim icPartners As List(Of String) = GetICPartners(si, entityName)

                If icPartners.Count = 0 Then
                    BRApi.ErrorLog.LogMessage(si, "EH_ICMatchingCheck: No IC partners found. Skipping validation.")
                    Return Nothing
                End If

                '--- Validate IC balances against each partner ---
                Dim mismatches As New List(Of ICMismatch)

                For Each partnerEntity As String In icPartners
                    '--- Check IC AR vs. partner IC AP ---
                    ValidateICPair(si, scenarioName, timeName, entityName, partnerEntity,
                        ACCT_IC_RECEIVABLE, ACCT_IC_PAYABLE, "AR/AP", mismatches)

                    '--- Check IC Revenue vs. partner IC COGS ---
                    ValidateICPair(si, scenarioName, timeName, entityName, partnerEntity,
                        ACCT_IC_REVENUE, ACCT_IC_COGS, "Revenue/COGS", mismatches)
                Next

                '--- Report results ---
                If mismatches.Count > 0 Then
                    Dim errorDetails As New List(Of String)
                    For Each mismatch As ICMismatch In mismatches
                        Dim detail As String = String.Format(
                            "  Partner: {0}, Type: {1}, Entity Amount: {2:N2}, Partner Amount: {3:N2}, Difference: {4:N2}",
                            mismatch.PartnerEntity, mismatch.PairType,
                            mismatch.EntityAmount, mismatch.PartnerAmount, mismatch.Difference)
                        errorDetails.Add(detail)
                        BRApi.ErrorLog.LogMessage(si, "EH_ICMatchingCheck: MISMATCH - " & detail)
                    Next

                    Dim blockMessage As String = String.Format(
                        "IC matching validation failed. {0} pair(s) exceed the ${1:N0} tolerance threshold:" &
                        Environment.NewLine & String.Join(Environment.NewLine, errorDetails.ToArray()),
                        mismatches.Count, IC_TOLERANCE)

                    Throw New XFException(si, "EH_ICMatchingCheck", blockMessage)
                End If

                BRApi.ErrorLog.LogMessage(si, "EH_ICMatchingCheck: All IC pairs within tolerance. Validation passed.")
                Return Nothing

            Catch ex As XFException
                Throw
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "EH_ICMatchingCheck", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Retrieves the list of IC partner entities for the specified entity.
        ''' In OneStream, IC partners are typically identified through the ICP dimension
        ''' or by scanning for non-zero IC balances across entities.
        ''' </summary>
        Private Function GetICPartners(ByVal si As SessionInfo, ByVal entityName As String) As List(Of String)
            Dim partners As New List(Of String)

            Try
                '--- Get all entity members that could be IC partners ---
                ' Read the list of IC partner entities from a configured member list
                ' or by querying entities that have IC transactions with this entity
                Dim icPartnerList As String = String.Empty

                Try
                    icPartnerList = BRApi.Finance.Data.GetSubstVarValue(si, "ICPartners_" & entityName)
                Catch
                    ' Variable may not exist
                End Try

                If Not String.IsNullOrEmpty(icPartnerList) Then
                    '--- Parse semicolon-delimited partner list ---
                    Dim partnerNames() As String = icPartnerList.Split(";"c)
                    For Each partner As String In partnerNames
                        Dim trimmed As String = partner.Trim()
                        If Not String.IsNullOrEmpty(trimmed) AndAlso trimmed <> entityName Then
                            partners.Add(trimmed)
                        End If
                    Next
                Else
                    '--- Fallback: query the ICP dimension for all partners ---
                    Dim memberFilter As String = "E#Root.Base"
                    Dim memberList As List(Of MemberInfo) = BRApi.Finance.Members.GetMembersByFilter(
                        si, BRApi.Finance.Dim.GetDimPk(si, DimType.Entity.Id), memberFilter)

                    If memberList IsNot Nothing Then
                        For Each member As MemberInfo In memberList
                            If member.Member.Name <> entityName Then
                                partners.Add(member.Member.Name)
                            End If
                        Next
                    End If
                End If

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "EH_ICMatchingCheck.GetICPartners: Error - " & ex.Message)
            End Try

            Return partners
        End Function

        ''' <summary>
        ''' Validates a single IC transaction pair between the current entity and a partner.
        ''' Compares the entity's IC balance (e.g., IC AR) with the partner's corresponding
        ''' balance (e.g., IC AP), ensuring the difference is within tolerance.
        ''' </summary>
        Private Sub ValidateICPair(ByVal si As SessionInfo, ByVal scenario As String,
                                    ByVal time As String, ByVal entityName As String,
                                    ByVal partnerEntity As String,
                                    ByVal entityAccount As String, ByVal partnerAccount As String,
                                    ByVal pairType As String,
                                    ByVal mismatches As List(Of ICMismatch))
            Try
                '--- Read the entity's IC balance against this partner ---
                ' The ICP dimension member identifies the partner in the IC transaction
                Dim entityAmount As Double = GetICDataValue(si, scenario, time, entityName,
                    entityAccount, partnerEntity)

                '--- Read the partner's corresponding IC balance against this entity ---
                Dim partnerAmount As Double = GetICDataValue(si, scenario, time, partnerEntity,
                    partnerAccount, entityName)

                '--- Skip if both amounts are zero (no IC activity for this pair) ---
                If entityAmount = 0 AndAlso partnerAmount = 0 Then
                    Return
                End If

                '--- Calculate the difference and compare to tolerance ---
                ' Note: AR and AP have opposite signs, so we compare absolute values
                ' Convention: AR is positive (debit), AP is positive (credit stored as positive)
                ' The matching check compares: |Entity AR| vs |Partner AP|
                Dim difference As Double = Math.Abs(Math.Abs(entityAmount) - Math.Abs(partnerAmount))

                If difference > IC_TOLERANCE Then
                    mismatches.Add(New ICMismatch() With {
                        .PartnerEntity = partnerEntity,
                        .PairType = pairType,
                        .EntityAmount = entityAmount,
                        .PartnerAmount = partnerAmount,
                        .Difference = difference
                    })
                End If

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "EH_ICMatchingCheck.ValidateICPair: Error for " &
                    entityName & " / " & partnerEntity & " / " & pairType & " - " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Reads an IC data value from the finance cube, specifying the ICP partner dimension.
        ''' </summary>
        Private Function GetICDataValue(ByVal si As SessionInfo, ByVal scenario As String,
                                         ByVal time As String, ByVal entity As String,
                                         ByVal account As String, ByVal icpPartner As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#{4}:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, time, entity, account, icpPartner)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
            End Try
            Return 0
        End Function

        ''' <summary>
        ''' Holds details of an IC mismatch for reporting.
        ''' </summary>
        Private Class ICMismatch
            Public Property PartnerEntity As String
            Public Property PairType As String
            Public Property EntityAmount As Double
            Public Property PartnerAmount As Double
            Public Property Difference As Double
        End Class

    End Class

End Namespace

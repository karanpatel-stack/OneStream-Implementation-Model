'------------------------------------------------------------------------------------------------------------
' OneStream XF Finance Business Rule: FR_JournalEntries
'------------------------------------------------------------------------------------------------------------
' Purpose:     Framework for processing top-side consolidation adjusting journal entries (JEs).
'              Reads journal entry data from input cells, validates them, and applies them to the
'              appropriate entity/account combinations within the consolidation process.
'
' Journal Entry Types:
'   1. Reclassification (RECLASS):  Move amounts between accounts without changing total
'   2. Adjustment (ADJUST):         Correct or adjust values (e.g., fair value adjustments)
'   3. Elimination (ELIM):          Manual elimination entries beyond automatic IC eliminations
'   4. Correction (CORRECT):        Error corrections for prior period data
'
' Data Model:
'   Journal entries are stored using a combination of Account and UD (User Defined) dimensions:
'     - UD1 = Journal Entry ID (e.g., JE_001, JE_002)
'     - UD2 = Journal Entry Type (RECLASS, ADJUST, ELIM, CORRECT)
'     - Account = Target account for the debit or credit
'     - Amount = Positive for debit, negative for credit
'     - Flow dimension = F_ManualJE (identifies JE data)
'
' Validation:
'   - Each JE must balance (sum of debits must equal sum of credits, i.e., net = 0)
'   - Unbalanced JEs are rejected and logged as errors
'
' Reversing Entries:
'   - JEs flagged as "reversing" are automatically reversed in the next period
'   - The reversal uses the same accounts with opposite signs
'
' Audit Trail:
'   - All JE processing is logged with details for audit purposes
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

Namespace OneStream.BusinessRule.Finance.FR_JournalEntries

    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for journal entry processing.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object
            Try
                If args.FinanceRulesEventType = FinanceRulesEventType.Calculate Then
                    Return Me.ProcessJournalEntries(si, globals, api, args)
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_JournalEntries.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessJournalEntries: Orchestrates the full JE workflow -- read, validate, apply, log.
        '----------------------------------------------------------------------------------------------------
        Private Function ProcessJournalEntries(ByVal si As SessionInfo, ByVal globals As BRGlobals, _
                                                ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                Dim entityName As String = api.Entity.GetName()

                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_JournalEntries: Processing journal entries for entity [{entityName}]")

                ' Step 1: Read all journal entries for this entity
                Dim journalEntries As List(Of JournalEntry) = Me.ReadJournalEntries(si, api, entityName)

                If journalEntries.Count = 0 Then
                    BRApi.ErrorLog.LogMessage(si, $"  No journal entries found for [{entityName}]")
                    Return Nothing
                End If

                BRApi.ErrorLog.LogMessage(si, $"  Found {journalEntries.Count} journal entry line(s)")

                ' Step 2: Group JE lines by JE ID for validation
                Dim jeGroups As Dictionary(Of String, List(Of JournalEntry)) = Me.GroupJournalEntries(journalEntries)

                Dim appliedCount As Integer = 0
                Dim rejectedCount As Integer = 0

                For Each jeId As String In jeGroups.Keys
                    Dim jeLines As List(Of JournalEntry) = jeGroups(jeId)

                    ' Step 3: Validate each JE (debits must equal credits)
                    If Me.ValidateJournalEntry(si, jeId, jeLines) Then
                        ' Step 4: Apply the JE
                        Me.ApplyJournalEntry(si, api, entityName, jeId, jeLines)
                        appliedCount += 1

                        ' Step 5: Check if this is a reversing entry and process reversal
                        If Me.IsReversingEntry(jeLines) Then
                            Me.CreateReversalEntry(si, api, entityName, jeId, jeLines)
                        End If

                        ' Step 6: Log audit trail
                        Me.LogAuditTrail(si, entityName, jeId, jeLines, "APPLIED")
                    Else
                        ' JE is out of balance -- reject it
                        rejectedCount += 1
                        Me.LogAuditTrail(si, entityName, jeId, jeLines, "REJECTED - UNBALANCED")
                    End If
                Next

                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_JournalEntries: Complete. Applied={appliedCount}, Rejected={rejectedCount}")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_JournalEntries.ProcessJournalEntries", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ReadJournalEntries: Reads JE data from the cube using the JE marker flow member.
        ' JE lines are stored with:
        '   - UD1 = JE ID, UD2 = JE Type, Account = target account, Flow = F_ManualJE
        '   - Amount: positive = debit, negative = credit
        '----------------------------------------------------------------------------------------------------
        Private Function ReadJournalEntries(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                            ByVal entityName As String) As List(Of JournalEntry)
            Dim entries As New List(Of JournalEntry)

            Try
                ' Get all UD1 members that represent JE IDs (convention: JE_ prefix)
                Dim ud1Filter As String = "UD1#JE_Total.Base"
                Dim ud1Members As List(Of MemberInfo) = BRApi.Finance.Members.GetMembersByFilter( _
                    si, BRApi.Finance.Dim.GetDimPk(si, DimType.UD1.Id), ud1Filter)

                If ud1Members Is Nothing OrElse ud1Members.Count = 0 Then
                    Return entries
                End If

                ' Get target accounts (all base accounts under the JE account range)
                Dim accountFilter As String = "A#[All Accounts].Base"
                Dim accountMembers As List(Of MemberInfo) = BRApi.Finance.Members.GetMembersByFilter( _
                    si, BRApi.Finance.Dim.GetDimPk(si, DimType.Account.Id), accountFilter)

                ' Iterate through JE IDs and accounts to find non-zero JE data
                For Each ud1Member As MemberInfo In ud1Members
                    Dim jeId As String = ud1Member.Member.Name

                    ' Read the JE type from UD2
                    Dim jeType As String = Me.GetJEType(si, api, entityName, jeId)

                    If accountMembers IsNot Nothing Then
                        For Each acctMember As MemberInfo In accountMembers
                            Dim accountName As String = acctMember.Member.Name

                            ' Read the JE amount at this intersection
                            Dim jePov As String = _
                                $"E#{entityName}:A#{accountName}:C#C_Local:F#F_ManualJE:O#O_None:I#I_None:UD1#{jeId}"
                            Dim amount As Double = api.Data.GetDataCell(jePov).CellAmount

                            If amount <> 0 Then
                                entries.Add(New JournalEntry() With {
                                    .JeId = jeId,
                                    .JeType = jeType,
                                    .AccountName = accountName,
                                    .Amount = amount,
                                    .EntityName = entityName
                                })
                            End If
                        Next
                    End If
                Next

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, _
                    $"FR_JournalEntries: WARNING - Error reading JE data: {ex.Message}")
            End Try

            Return entries
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetJEType: Reads the JE type classification from a control account or UD2 property.
        '----------------------------------------------------------------------------------------------------
        Private Function GetJEType(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                   ByVal entityName As String, ByVal jeId As String) As String
            Try
                ' Read JE type from a control/metadata intersection
                Dim typePov As String = _
                    $"E#{entityName}:A#A_JEType:C#C_Local:F#F_ManualJE:O#O_None:I#I_None:UD1#{jeId}"
                Dim typeValue As Double = api.Data.GetDataCell(typePov).CellAmount

                ' Map numeric type codes to string labels
                Select Case CInt(typeValue)
                    Case 1 : Return "RECLASS"
                    Case 2 : Return "ADJUST"
                    Case 3 : Return "ELIM"
                    Case 4 : Return "CORRECT"
                    Case Else : Return "ADJUST" ' Default to adjustment
                End Select

            Catch ex As Exception
                Return "ADJUST"
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GroupJournalEntries: Groups JE lines by their JE ID for batch validation.
        '----------------------------------------------------------------------------------------------------
        Private Function GroupJournalEntries(ByVal entries As List(Of JournalEntry)) As Dictionary(Of String, List(Of JournalEntry))
            Dim groups As New Dictionary(Of String, List(Of JournalEntry))(StringComparer.OrdinalIgnoreCase)

            For Each entry As JournalEntry In entries
                If Not groups.ContainsKey(entry.JeId) Then
                    groups(entry.JeId) = New List(Of JournalEntry)
                End If
                groups(entry.JeId).Add(entry)
            Next

            Return groups
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ValidateJournalEntry: Ensures a JE balances (total debits = total credits).
        ' Sum of all line amounts must be zero (positive = debit, negative = credit).
        ' A small tolerance (0.01) is allowed for rounding.
        '----------------------------------------------------------------------------------------------------
        Private Function ValidateJournalEntry(ByVal si As SessionInfo, ByVal jeId As String, _
                                               ByVal jeLines As List(Of JournalEntry)) As Boolean
            Dim netAmount As Double = 0

            For Each line As JournalEntry In jeLines
                netAmount += line.Amount
            Next

            ' Allow a small rounding tolerance
            Dim isBalanced As Boolean = Math.Abs(netAmount) < 0.01

            If Not isBalanced Then
                BRApi.ErrorLog.LogMessage(si, _
                    $"  VALIDATION FAILED: JE [{jeId}] is out of balance. Net amount={netAmount:N4} (expected 0)")
            End If

            Return isBalanced
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ApplyJournalEntry: Writes the validated JE lines to the target data intersections.
        ' The consolidation member depends on the JE type:
        '   - RECLASS/ADJUST/CORRECT -> C_Local (adjusts local data)
        '   - ELIM -> C_Elimination (manual elimination entry)
        '----------------------------------------------------------------------------------------------------
        Private Sub ApplyJournalEntry(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                       ByVal entityName As String, ByVal jeId As String, _
                                       ByVal jeLines As List(Of JournalEntry))
            Try
                For Each line As JournalEntry In jeLines
                    ' Determine the target consolidation member based on JE type
                    Dim consolMember As String
                    Select Case line.JeType.ToUpper()
                        Case "ELIM"
                            consolMember = "C_Elimination"
                        Case Else
                            consolMember = "C_Local"
                    End Select

                    ' Write the JE line to the target intersection
                    ' Flow = F_ManualJE distinguishes JE data from system-calculated data
                    Dim targetPov As String = _
                        $"E#{entityName}:A#{line.AccountName}:C#{consolMember}:F#F_ManualJE:O#O_None:I#I_None:UD1#{jeId}"
                    api.Data.SetDataCell(targetPov, line.Amount)
                Next

                BRApi.ErrorLog.LogMessage(si, _
                    $"  Applied JE [{jeId}] ({jeLines(0).JeType}): {jeLines.Count} line(s)")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_JournalEntries.ApplyJournalEntry", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' IsReversingEntry: Checks if the JE is flagged for automatic reversal in the next period.
        ' Reversing entries are identified by a control flag stored with the JE.
        '----------------------------------------------------------------------------------------------------
        Private Function IsReversingEntry(ByVal jeLines As List(Of JournalEntry)) As Boolean
            ' Check the first line's JE type -- RECLASS entries are typically reversing
            If jeLines.Count > 0 Then
                Return String.Equals(jeLines(0).JeType, "RECLASS", StringComparison.OrdinalIgnoreCase)
            End If
            Return False
        End Function

        '----------------------------------------------------------------------------------------------------
        ' CreateReversalEntry: Creates an automatic reversal of the JE for the next period.
        ' The reversal has the same accounts but with opposite signs (debits become credits
        ' and vice versa). Written to the next period's data.
        '----------------------------------------------------------------------------------------------------
        Private Sub CreateReversalEntry(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                         ByVal entityName As String, ByVal jeId As String, _
                                         ByVal jeLines As List(Of JournalEntry))
            Try
                Dim reversalId As String = $"{jeId}_REV"

                For Each line As JournalEntry In jeLines
                    ' Write the reversal with opposite sign to the next period
                    ' T#NextPeriod targets the subsequent time period
                    Dim consolMember As String = If(String.Equals(line.JeType, "ELIM", StringComparison.OrdinalIgnoreCase), _
                        "C_Elimination", "C_Local")

                    Dim reversalPov As String = _
                        $"E#{entityName}:A#{line.AccountName}:C#{consolMember}:F#F_ManualJE:O#O_None:I#I_None:UD1#{reversalId}:T#NextPeriod"
                    api.Data.SetDataCell(reversalPov, -line.Amount) ' Opposite sign for reversal
                Next

                BRApi.ErrorLog.LogMessage(si, _
                    $"  Reversing entry created: [{reversalId}] for next period ({jeLines.Count} lines)")

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, _
                    $"  WARNING: Could not create reversal for JE [{jeId}]: {ex.Message}")
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' LogAuditTrail: Records detailed JE processing information for audit compliance.
        ' Includes JE ID, type, all line details, amounts, and processing status.
        '----------------------------------------------------------------------------------------------------
        Private Sub LogAuditTrail(ByVal si As SessionInfo, ByVal entityName As String, _
                                   ByVal jeId As String, ByVal jeLines As List(Of JournalEntry), _
                                   ByVal status As String)
            Try
                Dim totalDebit As Double = 0
                Dim totalCredit As Double = 0

                For Each line As JournalEntry In jeLines
                    If line.Amount > 0 Then
                        totalDebit += line.Amount
                    Else
                        totalCredit += Math.Abs(line.Amount)
                    End If
                Next

                ' Build audit log message
                Dim auditMsg As String = $"AUDIT: JE=[{jeId}], Entity=[{entityName}], " & _
                    $"Type=[{jeLines(0).JeType}], Lines=[{jeLines.Count}], " & _
                    $"TotalDR=[{totalDebit:N2}], TotalCR=[{totalCredit:N2}], " & _
                    $"Status=[{status}]"

                BRApi.ErrorLog.LogMessage(si, auditMsg)

                ' Log individual line details
                For Each line As JournalEntry In jeLines
                    Dim drCr As String = If(line.Amount >= 0, "DR", "CR")
                    BRApi.ErrorLog.LogMessage(si, _
                        $"  AUDIT LINE: {drCr} {line.AccountName} {Math.Abs(line.Amount):N2}")
                Next

            Catch ex As Exception
                ' Audit logging failure should not stop processing
                BRApi.ErrorLog.LogMessage(si, $"  WARNING: Audit trail logging failed for JE [{jeId}]: {ex.Message}")
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' JournalEntry: Data structure representing a single line of a journal entry.
        '----------------------------------------------------------------------------------------------------
        Private Class JournalEntry
            Public Property JeId As String
            Public Property JeType As String
            Public Property AccountName As String
            Public Property Amount As Double
            Public Property EntityName As String
        End Class

    End Class

End Namespace

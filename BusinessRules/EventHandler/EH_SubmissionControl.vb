'------------------------------------------------------------------------------------------------------------
' EH_SubmissionControl
' Event Handler Business Rule - Workflow Gate Checks
'
' Purpose:  Performs gate checks before allowing workflow submission to the next step.
'           Validates that all prerequisites are met, including data quality validation
'           passage, required account population, IC reconciliation completion (where
'           applicable), manager approval, and commentary for material variances.
'
' Gate Checks:
'   1. Data quality validations passed (no critical failures)
'   2. All required accounts populated
'   3. IC reconciliation completed (for entities with IC transactions)
'   4. Manager approval obtained (for budget/forecast submissions)
'   5. Commentary entered for material variances (>10% or >$100K)
'
' Returns approval or rejection with detailed reasons for any gate that fails.
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

Namespace OneStream.BusinessRule.EventHandler.EH_SubmissionControl

    Public Class MainClass

        '--- Material variance thresholds ---
        Private Const VARIANCE_PCT_THRESHOLD As Double = 0.10     ' 10%
        Private Const VARIANCE_AMT_THRESHOLD As Double = 100000   ' $100,000

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
                Dim workflowStep As String = args.NameValuePairs.XFGetValue("WorkflowStep", "Submit")

                If String.IsNullOrEmpty(timeName) Then
                    timeName = BRApi.Finance.Members.GetMemberName(si, DimType.Time.Id, si.WorkflowClusterPk.TimeId)
                End If
                If String.IsNullOrEmpty(entityName) Then
                    entityName = BRApi.Finance.Members.GetMemberName(si, DimType.Entity.Id, si.WorkflowClusterPk.EntityId)
                End If

                Dim userName As String = si.UserName

                BRApi.ErrorLog.LogMessage(si, "EH_SubmissionControl: Gate check for " &
                    scenarioName & "/" & timeName & "/" & entityName & " Step=" & workflowStep & " User=" & userName)

                '--- Run all gate checks and collect results ---
                Dim gateResults As New List(Of GateCheckResult)

                ' Gate 1: Data quality validation must have passed
                CheckDataQualityGate(si, scenarioName, timeName, entityName, gateResults)

                ' Gate 2: Required accounts must be populated
                CheckRequiredAccountsGate(si, scenarioName, timeName, entityName, gateResults)

                ' Gate 3: IC reconciliation (only for entities with IC activity)
                CheckICReconciliationGate(si, scenarioName, timeName, entityName, workflowStep, gateResults)

                ' Gate 4: Manager approval (for budget/forecast submissions)
                CheckManagerApprovalGate(si, scenarioName, timeName, entityName, workflowStep, gateResults)

                ' Gate 5: Commentary for material variances
                CheckVarianceCommentaryGate(si, scenarioName, timeName, entityName, gateResults)

                '--- Evaluate gate results ---
                Dim failedGates As New List(Of String)
                For Each result As GateCheckResult In gateResults
                    If Not result.Passed Then
                        failedGates.Add(result.GateName & ": " & result.Reason)
                        BRApi.ErrorLog.LogMessage(si, "  GATE FAILED: " & result.GateName & " - " & result.Reason)
                    Else
                        BRApi.ErrorLog.LogMessage(si, "  GATE PASSED: " & result.GateName)
                    End If
                Next

                '--- Block submission if any gates failed ---
                If failedGates.Count > 0 Then
                    Dim rejectionMessage As String = "Submission rejected. The following gates failed:" &
                        Environment.NewLine & String.Join(Environment.NewLine, failedGates.ToArray())
                    BRApi.ErrorLog.LogMessage(si, "EH_SubmissionControl: SUBMISSION BLOCKED - " & rejectionMessage)
                    Throw New XFException(si, "EH_SubmissionControl", rejectionMessage)
                End If

                BRApi.ErrorLog.LogMessage(si, "EH_SubmissionControl: All gates passed. Submission approved.")
                Return Nothing

            Catch ex As XFException
                Throw
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "EH_SubmissionControl", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Gate 1: Verifies that the data quality validation rule has been run and passed
        ''' without critical failures. Checks a control flag stored by EH_DataQualityValidation.
        ''' </summary>
        Private Sub CheckDataQualityGate(ByVal si As SessionInfo, ByVal scenario As String,
                                          ByVal time As String, ByVal entity As String,
                                          ByVal results As List(Of GateCheckResult))
            Try
                '--- Check for a validation flag set by EH_DataQualityValidation ---
                Dim validationFlag As String = BRApi.Finance.Data.GetSubstVarValue(
                    si, "DQV_Status_" & entity & "_" & time)

                If String.IsNullOrEmpty(validationFlag) OrElse validationFlag.ToUpper() <> "PASSED" Then
                    results.Add(New GateCheckResult("DataQualityValidation", False,
                        "Data quality validation has not been completed or has critical failures. " &
                        "Please run data quality checks before submitting."))
                Else
                    results.Add(New GateCheckResult("DataQualityValidation", True, String.Empty))
                End If

            Catch ex As Exception
                ' If we cannot verify, treat as a warning but allow submission
                results.Add(New GateCheckResult("DataQualityValidation", True,
                    "Unable to verify data quality status (allowed with warning): " & ex.Message))
            End Try
        End Sub

        ''' <summary>
        ''' Gate 2: Verifies that all mandatory accounts have non-zero values submitted.
        ''' </summary>
        Private Sub CheckRequiredAccountsGate(ByVal si As SessionInfo, ByVal scenario As String,
                                               ByVal time As String, ByVal entity As String,
                                               ByVal results As List(Of GateCheckResult))
            Try
                Dim requiredAccounts As String() = {"A#Revenue", "A#COGS", "A#OperatingExpenses"}
                Dim missingAccounts As New List(Of String)

                For Each acct As String In requiredAccounts
                    Dim value As Double = GetDataValue(si, scenario, time, entity, acct)
                    If value = 0 Then
                        missingAccounts.Add(acct.Replace("A#", ""))
                    End If
                Next

                If missingAccounts.Count > 0 Then
                    results.Add(New GateCheckResult("RequiredAccounts", False,
                        "The following required accounts have zero values: " &
                        String.Join(", ", missingAccounts.ToArray())))
                Else
                    results.Add(New GateCheckResult("RequiredAccounts", True, String.Empty))
                End If

            Catch ex As Exception
                results.Add(New GateCheckResult("RequiredAccounts", False,
                    "Error checking required accounts: " & ex.Message))
            End Try
        End Sub

        ''' <summary>
        ''' Gate 3: Checks that IC reconciliation has been completed for entities with
        ''' intercompany activity. Only enforced at the IC reconciliation workflow step.
        ''' </summary>
        Private Sub CheckICReconciliationGate(ByVal si As SessionInfo, ByVal scenario As String,
                                               ByVal time As String, ByVal entity As String,
                                               ByVal workflowStep As String,
                                               ByVal results As List(Of GateCheckResult))
            Try
                '--- Only check IC reconciliation at the appropriate workflow step ---
                If Not workflowStep.Equals("ICRecon", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not workflowStep.Equals("Submit", StringComparison.OrdinalIgnoreCase) Then
                    results.Add(New GateCheckResult("ICReconciliation", True, "Not applicable at this step."))
                    Return
                End If

                '--- Check if this entity has IC transactions ---
                Dim hasICActivity As Boolean = False
                Dim icBalance As Double = GetDataValue(si, scenario, time, entity, "A#ICReceivables")
                Dim icPayable As Double = GetDataValue(si, scenario, time, entity, "A#ICPayables")

                hasICActivity = (icBalance <> 0 OrElse icPayable <> 0)

                If Not hasICActivity Then
                    ' No IC activity - gate automatically passes
                    results.Add(New GateCheckResult("ICReconciliation", True, "No IC activity for this entity."))
                    Return
                End If

                '--- Check IC reconciliation status flag ---
                Dim icReconFlag As String = BRApi.Finance.Data.GetSubstVarValue(
                    si, "ICRecon_Status_" & entity & "_" & time)

                If String.IsNullOrEmpty(icReconFlag) OrElse icReconFlag.ToUpper() <> "RECONCILED" Then
                    results.Add(New GateCheckResult("ICReconciliation", False,
                        "Intercompany reconciliation has not been completed. " &
                        "Please reconcile IC balances with partner entities before submitting."))
                Else
                    results.Add(New GateCheckResult("ICReconciliation", True, String.Empty))
                End If

            Catch ex As Exception
                results.Add(New GateCheckResult("ICReconciliation", False,
                    "Error checking IC reconciliation: " & ex.Message))
            End Try
        End Sub

        ''' <summary>
        ''' Gate 4: Verifies that manager approval has been obtained for budget and forecast
        ''' submissions. Not required for actual close submissions.
        ''' </summary>
        Private Sub CheckManagerApprovalGate(ByVal si As SessionInfo, ByVal scenario As String,
                                              ByVal time As String, ByVal entity As String,
                                              ByVal workflowStep As String,
                                              ByVal results As List(Of GateCheckResult))
            Try
                '--- Manager approval is only required for Budget and Forecast scenarios ---
                If scenario.Equals("Actual", StringComparison.OrdinalIgnoreCase) Then
                    results.Add(New GateCheckResult("ManagerApproval", True, "Not required for Actuals."))
                    Return
                End If

                '--- Check for manager approval flag ---
                Dim approvalFlag As String = BRApi.Finance.Data.GetSubstVarValue(
                    si, "MgrApproval_" & entity & "_" & scenario & "_" & time)

                If String.IsNullOrEmpty(approvalFlag) OrElse approvalFlag.ToUpper() <> "APPROVED" Then
                    results.Add(New GateCheckResult("ManagerApproval", False,
                        "Manager approval has not been obtained for this " & scenario & " submission. " &
                        "Please obtain manager sign-off before submitting."))
                Else
                    results.Add(New GateCheckResult("ManagerApproval", True, String.Empty))
                End If

            Catch ex As Exception
                results.Add(New GateCheckResult("ManagerApproval", False,
                    "Error checking manager approval: " & ex.Message))
            End Try
        End Sub

        ''' <summary>
        ''' Gate 5: Verifies that commentary has been entered for any accounts with
        ''' material variances (exceeding 10% or $100K vs. prior period or budget).
        ''' </summary>
        Private Sub CheckVarianceCommentaryGate(ByVal si As SessionInfo, ByVal scenario As String,
                                                 ByVal time As String, ByVal entity As String,
                                                 ByVal results As List(Of GateCheckResult))
            Try
                '--- Define accounts to check for material variances ---
                Dim accountsToCheck As String() = {"A#Revenue", "A#COGS", "A#GrossProfit",
                    "A#OperatingExpenses", "A#NetIncome"}

                Dim missingCommentary As New List(Of String)

                For Each acct As String In accountsToCheck
                    Dim currentValue As Double = GetDataValue(si, scenario, time, entity, acct)
                    Dim budgetValue As Double = GetDataValue(si, "Budget", time, entity, acct)

                    '--- Calculate variance ---
                    Dim varianceAmt As Double = Math.Abs(currentValue - budgetValue)
                    Dim variancePct As Double = 0
                    If budgetValue <> 0 Then
                        variancePct = Math.Abs((currentValue - budgetValue) / budgetValue)
                    End If

                    '--- Check if variance is material ---
                    If variancePct >= VARIANCE_PCT_THRESHOLD OrElse varianceAmt >= VARIANCE_AMT_THRESHOLD Then
                        '--- Check if commentary exists ---
                        Dim commentKey As String = "Comment_" & entity & "_" & acct.Replace("A#", "") & "_" & time
                        Dim commentary As String = String.Empty
                        Try
                            commentary = BRApi.Finance.Data.GetSubstVarValue(si, commentKey)
                        Catch
                            ' Commentary variable may not exist
                        End Try

                        If String.IsNullOrEmpty(commentary) Then
                            missingCommentary.Add(acct.Replace("A#", "") &
                                " (Var: " & variancePct.ToString("P1") & " / $" & varianceAmt.ToString("N0") & ")")
                        End If
                    End If
                Next

                If missingCommentary.Count > 0 Then
                    results.Add(New GateCheckResult("VarianceCommentary", False,
                        "Commentary required for material variances on: " &
                        String.Join(", ", missingCommentary.ToArray())))
                Else
                    results.Add(New GateCheckResult("VarianceCommentary", True, String.Empty))
                End If

            Catch ex As Exception
                results.Add(New GateCheckResult("VarianceCommentary", False,
                    "Error checking variance commentary: " & ex.Message))
            End Try
        End Sub

        ''' <summary>
        ''' Reads a data value from the finance cube.
        ''' </summary>
        Private Function GetDataValue(ByVal si As SessionInfo, ByVal scenario As String,
                                       ByVal time As String, ByVal entity As String,
                                       ByVal account As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, time, entity, account)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
            End Try
            Return 0
        End Function

        ''' <summary>
        ''' Holds the result of a single gate check.
        ''' </summary>
        Private Class GateCheckResult
            Public Property GateName As String
            Public Property Passed As Boolean
            Public Property Reason As String

            Public Sub New(ByVal gateName As String, ByVal passed As Boolean, ByVal reason As String)
                Me.GateName = gateName
                Me.Passed = passed
                Me.Reason = reason
            End Sub
        End Class

    End Class

End Namespace

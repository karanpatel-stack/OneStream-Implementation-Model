'------------------------------------------------------------------------------------------------------------
' MF_ScenarioLocking
' Member Filter Business Rule - Scenario Locking After Approval
'
' Purpose:  Controls which scenarios are available for data input by checking workflow
'           approval status. Approved or published scenarios are excluded from input
'           member lists to prevent unauthorized modifications to finalized data.
'
' Locking Rules:
'   Actual scenario   -> Locked after period close (workflow status = Approved or Locked)
'   Budget scenario   -> Locked after budget approval cycle completes
'   Forecast scenario -> Locked after management sign-off
'   Admin override    -> Administrators can unlock any scenario for corrections
'
' Scope:    Member Filter
' Version:  1.0
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.MemberFilter.MF_ScenarioLocking

    Public Class MainClass

        '--- Workflow status codes that indicate a locked/finalized state ---
        Private Const STATUS_APPROVED As Integer = 3
        Private Const STATUS_LOCKED As Integer = 4
        Private Const STATUS_PUBLISHED As Integer = 5

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As MemberFilterArgs) As Object
            Try
                '--- Get the current user and check for admin override privilege ---
                Dim userName As String = si.UserName
                Dim isAdmin As Boolean = CheckAdminOverride(si, userName)

                '--- If admin, return all scenarios without filtering ---
                If isAdmin Then
                    BRApi.ErrorLog.LogMessage(si, "MF_ScenarioLocking: Admin override active for user " & userName)
                    Return "S#Root.Descendants"
                End If

                '--- Get the current time context to check workflow status ---
                Dim timeName As String = BRApi.Finance.Members.GetMemberName(si, DimType.Time.Id, si.WorkflowClusterPk.TimeId)
                Dim entityName As String = BRApi.Finance.Members.GetMemberName(si, DimType.Entity.Id, si.WorkflowClusterPk.EntityId)

                '--- Define the scenarios to evaluate ---
                Dim allScenarios As New List(Of String) From {
                    "Actual", "Budget", "Forecast", "Forecast_Q2", "Forecast_Q3", "Forecast_Q4"
                }

                '--- Build list of unlocked (accessible) scenarios ---
                Dim unlockedScenarios As New List(Of String)

                For Each scenarioName As String In allScenarios
                    Dim isLocked As Boolean = IsScenarioLocked(si, scenarioName, timeName, entityName)

                    If Not isLocked Then
                        unlockedScenarios.Add(scenarioName)
                    Else
                        BRApi.ErrorLog.LogMessage(si, "MF_ScenarioLocking: Scenario '" & scenarioName &
                            "' is locked for period " & timeName & " / entity " & entityName)
                    End If
                Next

                '--- Build the member filter expression from unlocked scenarios ---
                If unlockedScenarios.Count = 0 Then
                    BRApi.ErrorLog.LogMessage(si, "MF_ScenarioLocking: WARNING - All scenarios locked for " &
                        timeName & " / " & entityName & ". User: " & userName)
                    Return "S#NoOpenScenario"
                End If

                Dim filterParts As New List(Of String)
                For Each s As String In unlockedScenarios
                    filterParts.Add("S#" & s)
                Next

                Return String.Join(":", filterParts.ToArray())

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "MF_ScenarioLocking", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Checks the workflow status for a given scenario/time/entity combination
        ''' and determines if the scenario should be locked for data input.
        ''' </summary>
        Private Function IsScenarioLocked(ByVal si As SessionInfo, ByVal scenarioName As String,
                                           ByVal timeName As String, ByVal entityName As String) As Boolean
            Try
                '--- Query the workflow status from the OneStream workflow engine ---
                Dim wfStatus As WorkflowStatusInfo = BRApi.Workflow.Status.GetWorkflowStatus(
                    si, scenarioName, timeName, entityName)

                If wfStatus Is Nothing Then
                    ' No workflow record means the scenario/period has not been initialized
                    ' Treat as unlocked (open for input)
                    Return False
                End If

                Dim statusCode As Integer = wfStatus.StatusCode

                '--- Check if status indicates a locked/finalized state ---
                Select Case statusCode
                    Case STATUS_APPROVED, STATUS_LOCKED, STATUS_PUBLISHED
                        Return True
                    Case Else
                        Return False
                End Select

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_ScenarioLocking.IsScenarioLocked: Error checking " &
                    scenarioName & "/" & timeName & " - " & ex.Message)
                ' On error, default to unlocked to avoid blocking users unnecessarily
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Checks whether the current user has administrator privileges that allow
        ''' overriding scenario locks. Only members of the GRP_SystemAdmin or
        ''' GRP_DataAdmin groups receive this override.
        ''' </summary>
        Private Function CheckAdminOverride(ByVal si As SessionInfo, ByVal userName As String) As Boolean
            Try
                '--- System administrators can always override locks ---
                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_SystemAdmin") Then
                    Return True
                End If

                '--- Data administrators can override for correction purposes ---
                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_DataAdmin") Then
                    Return True
                End If

                Return False

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_ScenarioLocking.CheckAdminOverride: Error - " & ex.Message)
                Return False
            End Try
        End Function

    End Class

End Namespace

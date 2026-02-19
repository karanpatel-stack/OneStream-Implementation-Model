'------------------------------------------------------------------------------------------------------------
' DSF_EntityStatusIcon
' Dashboard String Function Business Rule
'
' Purpose:  Returns an HTML icon/indicator based on the current entity's workflow status.
'           Used in dashboard grids and reports to provide at-a-glance status visibility
'           for each entity in the consolidation close process.
'
' Status Mapping:
'   Not Started  -> Red X icon
'   In Progress  -> Yellow clock icon
'   Submitted    -> Blue arrow icon
'   Approved     -> Green checkmark icon
'   Locked       -> Green lock icon (fully complete)
'
' Scope:    Dashboard - String Function
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

Namespace OneStream.BusinessRule.DashboardStringFunction.DSF_EntityStatusIcon

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardStringFunctionArgs) As Object
            Try
                '--- Retrieve entity and workflow context from the dashboard POV ---
                Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", String.Empty)
                Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                Dim timeName As String = args.NameValuePairs.XFGetValue("Time", String.Empty)

                '--- If entity or time not supplied, fall back to the session workflow cluster ---
                If String.IsNullOrEmpty(entityName) Then
                    entityName = BRApi.Finance.Members.GetMemberName(si, DimType.Entity.Id, si.WorkflowClusterPk.EntityId)
                End If
                If String.IsNullOrEmpty(timeName) Then
                    timeName = BRApi.Finance.Members.GetMemberName(si, DimType.Time.Id, si.WorkflowClusterPk.TimeId)
                End If

                '--- Query the workflow status for the specified entity/scenario/time ---
                Dim workflowStatus As Integer = GetWorkflowStatus(si, scenarioName, timeName, entityName)

                '--- Map workflow status code to an HTML icon string ---
                ' Status codes follow OneStream WorkflowStatusType enumeration:
                '   0 = Not Started, 1 = In Progress, 2 = Submitted,
                '   3 = Approved, 4 = Locked/Published
                Dim iconHtml As String = BuildStatusIcon(workflowStatus)

                Return iconHtml

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "DSF_EntityStatusIcon", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Retrieves the workflow status integer for a given scenario/time/entity combination
        ''' using the OneStream workflow API.
        ''' </summary>
        Private Function GetWorkflowStatus(ByVal si As SessionInfo, ByVal scenarioName As String,
                                            ByVal timeName As String, ByVal entityName As String) As Integer
            Try
                Dim wfStatusInfo As WorkflowStatusInfo = BRApi.Workflow.Status.GetWorkflowStatus(
                    si, scenarioName, timeName, entityName)

                If wfStatusInfo IsNot Nothing Then
                    Return wfStatusInfo.StatusCode
                End If

                ' Default to Not Started if no workflow record found
                Return 0

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "DSF_EntityStatusIcon.GetWorkflowStatus: " &
                    "Error retrieving status for " & entityName & " - " & ex.Message)
                Return -1
            End Try
        End Function

        ''' <summary>
        ''' Builds an HTML snippet representing the workflow status as a colored icon.
        ''' Uses inline CSS for portability across OneStream dashboard renderers.
        ''' </summary>
        Private Function BuildStatusIcon(ByVal statusCode As Integer) As String
            Select Case statusCode
                Case 0
                    '--- Not Started: Red X ---
                    Return "<span style=""color:#DC3545; font-size:16px; font-weight:bold;"" title=""Not Started"">&#10060; Not Started</span>"

                Case 1
                    '--- In Progress: Yellow/Amber clock ---
                    Return "<span style=""color:#FFC107; font-size:16px; font-weight:bold;"" title=""In Progress"">&#9203; In Progress</span>"

                Case 2
                    '--- Submitted: Blue arrow ---
                    Return "<span style=""color:#007BFF; font-size:16px; font-weight:bold;"" title=""Submitted"">&#10145; Submitted</span>"

                Case 3
                    '--- Approved: Green checkmark ---
                    Return "<span style=""color:#28A745; font-size:16px; font-weight:bold;"" title=""Approved"">&#10004; Approved</span>"

                Case 4
                    '--- Locked/Published: Green lock ---
                    Return "<span style=""color:#28A745; font-size:16px; font-weight:bold;"" title=""Locked"">&#128274; Locked</span>"

                Case Else
                    '--- Unknown status: Gray question mark ---
                    Return "<span style=""color:#6C757D; font-size:16px;"" title=""Unknown"">&#10067; Unknown</span>"
            End Select
        End Function

    End Class

End Namespace

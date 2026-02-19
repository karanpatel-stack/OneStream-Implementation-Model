'------------------------------------------------------------------------------------------------------------
' EH_AuditTrailLogger
' Event Handler Business Rule - Comprehensive Audit Logging
'
' Purpose:  Provides comprehensive audit trail logging for all significant system events
'           including data changes, workflow actions, and system operations. Captures
'           full context (user, timestamp, entity, account, old/new values) and writes
'           to the OneStream log system for compliance and traceability.
'
' Logged Events:
'   - Data Changes:     User, timestamp, entity, account, old value, new value
'   - Workflow Actions:  Submit, approve, reject, lock, unlock
'   - System Events:     Consolidation run, FX translation, data load
'   - Session Context:   User name, machine name, session ID
'
' All entries are queryable for SOX/compliance audit trail requirements.
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

Namespace OneStream.BusinessRule.EventHandler.EH_AuditTrailLogger

    Public Class MainClass

        '--- Audit log entry separator for structured logging ---
        Private Const FIELD_SEP As String = "|"
        Private Const LOG_PREFIX As String = "AUDIT_TRAIL"

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As EventHandlerArgs) As Object
            Try
                '--- Determine the type of event being logged ---
                Dim eventType As String = DetermineEventType(args)

                '--- Capture session context ---
                Dim context As AuditContext = CaptureSessionContext(si)

                '--- Route to the appropriate logging handler ---
                Select Case eventType
                    Case "DataChange"
                        LogDataChange(si, args, context)
                    Case "WorkflowAction"
                        LogWorkflowAction(si, args, context)
                    Case "SystemEvent"
                        LogSystemEvent(si, args, context)
                    Case Else
                        LogGenericEvent(si, args, context, eventType)
                End Select

                Return Nothing

            Catch ex As Exception
                ' Audit logging failures should never block the primary operation
                ' Log the error but do not throw
                Try
                    BRApi.ErrorLog.LogMessage(si, "EH_AuditTrailLogger: INTERNAL ERROR - " & ex.Message)
                Catch
                    ' Last resort: swallow the error to protect the primary operation
                End Try
                Return Nothing
            End Try
        End Function

        ''' <summary>
        ''' Determines the event type from the event handler arguments.
        ''' Maps OneStream event handler types to audit log categories.
        ''' </summary>
        Private Function DetermineEventType(ByVal args As EventHandlerArgs) As String
            Select Case args.EventHandlerType
                Case EventHandlerType.DataChanged
                    Return "DataChange"
                Case EventHandlerType.WorkflowAction
                    Return "WorkflowAction"
                Case EventHandlerType.Consolidation,
                     EventHandlerType.Translation,
                     EventHandlerType.DataLoad
                    Return "SystemEvent"
                Case Else
                    Return args.EventHandlerType.ToString()
            End Select
        End Function

        ''' <summary>
        ''' Captures the current session context including user identity, machine,
        ''' and session details for the audit trail.
        ''' </summary>
        Private Function CaptureSessionContext(ByVal si As SessionInfo) As AuditContext
            Dim ctx As New AuditContext()

            ctx.UserName = si.UserName
            ctx.TimestampUtc = DateTime.UtcNow
            ctx.SessionId = si.SessionId.ToString()

            '--- Attempt to capture machine/environment details ---
            Try
                ctx.MachineName = Environment.MachineName
            Catch
                ctx.MachineName = "Unknown"
            End Try

            '--- Capture the current POV context ---
            Try
                ctx.ScenarioName = BRApi.Finance.Members.GetMemberName(si, DimType.Scenario.Id, si.WorkflowClusterPk.ScenarioId)
                ctx.TimeName = BRApi.Finance.Members.GetMemberName(si, DimType.Time.Id, si.WorkflowClusterPk.TimeId)
                ctx.EntityName = BRApi.Finance.Members.GetMemberName(si, DimType.Entity.Id, si.WorkflowClusterPk.EntityId)
            Catch
                ctx.ScenarioName = "Unknown"
                ctx.TimeName = "Unknown"
                ctx.EntityName = "Unknown"
            End Try

            Return ctx
        End Function

        ''' <summary>
        ''' Logs a data change event with old and new values for full traceability.
        ''' </summary>
        Private Sub LogDataChange(ByVal si As SessionInfo, ByVal args As EventHandlerArgs, ByVal context As AuditContext)
            Try
                Dim accountName As String = args.NameValuePairs.XFGetValue("Account", "Unknown")
                Dim oldValueStr As String = args.NameValuePairs.XFGetValue("OldValue", "0")
                Dim newValueStr As String = args.NameValuePairs.XFGetValue("NewValue", "0")
                Dim entityOverride As String = args.NameValuePairs.XFGetValue("Entity", context.EntityName)

                '--- Parse numeric values ---
                Dim oldValue As Double = 0
                Dim newValue As Double = 0
                Double.TryParse(oldValueStr, NumberStyles.Any, CultureInfo.InvariantCulture, oldValue)
                Double.TryParse(newValueStr, NumberStyles.Any, CultureInfo.InvariantCulture, newValue)

                Dim changeAmount As Double = newValue - oldValue

                '--- Build the structured audit log entry ---
                Dim logEntry As String = BuildLogEntry(
                    "DATA_CHANGE", context,
                    "Entity=" & entityOverride,
                    "Account=" & accountName,
                    "OldValue=" & oldValue.ToString("N2"),
                    "NewValue=" & newValue.ToString("N2"),
                    "ChangeAmount=" & changeAmount.ToString("N2"))

                WriteAuditEntry(si, logEntry)

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "EH_AuditTrailLogger.LogDataChange: Error - " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Logs workflow actions such as submit, approve, reject, lock, and unlock.
        ''' </summary>
        Private Sub LogWorkflowAction(ByVal si As SessionInfo, ByVal args As EventHandlerArgs, ByVal context As AuditContext)
            Try
                Dim actionType As String = args.NameValuePairs.XFGetValue("WorkflowAction", "Unknown")
                Dim fromStatus As String = args.NameValuePairs.XFGetValue("FromStatus", "Unknown")
                Dim toStatus As String = args.NameValuePairs.XFGetValue("ToStatus", "Unknown")
                Dim comments As String = args.NameValuePairs.XFGetValue("Comments", String.Empty)
                Dim entityOverride As String = args.NameValuePairs.XFGetValue("Entity", context.EntityName)

                '--- Build the structured audit log entry ---
                Dim logEntry As String = BuildLogEntry(
                    "WORKFLOW_ACTION", context,
                    "Entity=" & entityOverride,
                    "Action=" & actionType,
                    "FromStatus=" & fromStatus,
                    "ToStatus=" & toStatus,
                    "Comments=" & SanitizeForLog(comments))

                WriteAuditEntry(si, logEntry)

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "EH_AuditTrailLogger.LogWorkflowAction: Error - " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Logs system events such as consolidation runs, FX translations, and data loads.
        ''' </summary>
        Private Sub LogSystemEvent(ByVal si As SessionInfo, ByVal args As EventHandlerArgs, ByVal context As AuditContext)
            Try
                Dim eventSubType As String = args.EventHandlerType.ToString()
                Dim details As String = args.NameValuePairs.XFGetValue("Details", String.Empty)
                Dim entityOverride As String = args.NameValuePairs.XFGetValue("Entity", context.EntityName)
                Dim status As String = args.NameValuePairs.XFGetValue("Status", "Started")

                '--- Build the structured audit log entry ---
                Dim logEntry As String = BuildLogEntry(
                    "SYSTEM_EVENT", context,
                    "Entity=" & entityOverride,
                    "EventSubType=" & eventSubType,
                    "Status=" & status,
                    "Details=" & SanitizeForLog(details))

                WriteAuditEntry(si, logEntry)

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "EH_AuditTrailLogger.LogSystemEvent: Error - " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Logs any event type that does not match the specific handlers above.
        ''' </summary>
        Private Sub LogGenericEvent(ByVal si As SessionInfo, ByVal args As EventHandlerArgs,
                                     ByVal context As AuditContext, ByVal eventType As String)
            Try
                Dim details As String = args.NameValuePairs.XFGetValue("Details", String.Empty)

                Dim logEntry As String = BuildLogEntry(
                    "GENERIC_EVENT", context,
                    "EventType=" & eventType,
                    "Details=" & SanitizeForLog(details))

                WriteAuditEntry(si, logEntry)

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "EH_AuditTrailLogger.LogGenericEvent: Error - " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Builds a structured, pipe-delimited log entry string suitable for parsing
        ''' and querying by audit trail analysis tools.
        ''' </summary>
        Private Function BuildLogEntry(ByVal category As String, ByVal context As AuditContext,
                                        ByVal ParamArray fields() As String) As String
            Dim parts As New List(Of String)

            '--- Standard header fields ---
            parts.Add(LOG_PREFIX)
            parts.Add(category)
            parts.Add("Timestamp=" & context.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            parts.Add("User=" & context.UserName)
            parts.Add("Session=" & context.SessionId)
            parts.Add("Machine=" & context.MachineName)
            parts.Add("Scenario=" & context.ScenarioName)
            parts.Add("Time=" & context.TimeName)

            '--- Event-specific fields ---
            For Each field As String In fields
                parts.Add(field)
            Next

            Return String.Join(FIELD_SEP, parts.ToArray())
        End Function

        ''' <summary>
        ''' Writes the audit entry to the OneStream logging system.
        ''' In production, this could also write to a dedicated audit table,
        ''' external SIEM system, or compliance database.
        ''' </summary>
        Private Sub WriteAuditEntry(ByVal si As SessionInfo, ByVal logEntry As String)
            '--- Primary: write to OneStream error/message log ---
            BRApi.ErrorLog.LogMessage(si, logEntry)

            '--- Secondary: could also write to a data management cube for queryability ---
            ' Example: BRApi.Finance.Data.SetDataCell(si, auditPov, auditValue)
            ' This would store audit metadata in a dedicated audit cube/dimension
        End Sub

        ''' <summary>
        ''' Sanitizes a string for inclusion in structured log entries.
        ''' Removes or escapes characters that could break the log format.
        ''' </summary>
        Private Function SanitizeForLog(ByVal input As String) As String
            If String.IsNullOrEmpty(input) Then
                Return String.Empty
            End If

            ' Replace pipe delimiter and newlines to preserve log structure
            Dim sanitized As String = input.Replace(FIELD_SEP, ";")
            sanitized = sanitized.Replace(vbCrLf, " ")
            sanitized = sanitized.Replace(vbCr, " ")
            sanitized = sanitized.Replace(vbLf, " ")

            ' Truncate very long entries to prevent log bloat
            If sanitized.Length > 500 Then
                sanitized = sanitized.Substring(0, 497) & "..."
            End If

            Return sanitized
        End Function

        ''' <summary>
        ''' Session context data structure for audit entries.
        ''' </summary>
        Private Class AuditContext
            Public Property UserName As String = String.Empty
            Public Property TimestampUtc As DateTime = DateTime.UtcNow
            Public Property SessionId As String = String.Empty
            Public Property MachineName As String = String.Empty
            Public Property ScenarioName As String = String.Empty
            Public Property TimeName As String = String.Empty
            Public Property EntityName As String = String.Empty
        End Class

    End Class

End Namespace

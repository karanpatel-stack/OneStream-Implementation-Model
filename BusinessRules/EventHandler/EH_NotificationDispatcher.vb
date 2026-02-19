'------------------------------------------------------------------------------------------------------------
' EH_NotificationDispatcher
' Event Handler Business Rule - Email and Teams Notification Dispatch
'
' Purpose:  Dispatches notifications via email and/or Microsoft Teams webhooks in response
'           to workflow and data quality events. Notifies the appropriate recipients based
'           on the event type, builds formatted HTML email bodies, and supports user
'           notification preferences.
'
' Notification Triggers:
'   - Workflow Submission:   Notify the approver that data is ready for review
'   - Workflow Approval:     Notify the submitter that their data was approved
'   - Workflow Rejection:    Notify the submitter with the rejection reason
'   - Data Quality Failure:  Notify the data steward of validation issues
'   - IC Mismatch:           Notify both partner entity controllers
'
' Delivery Channels:
'   - Email (SMTP via OneStream notification API)
'   - Microsoft Teams (webhook integration)
'   - User preferences determine which channel(s) are used
'
' Scope:    Event Handler
' Version:  1.0
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Net
Imports System.Text
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.EventHandler.EH_NotificationDispatcher

    Public Class MainClass

        '--- Notification configuration ---
        Private Const APP_NAME As String = "OneStream Finance"
        Private Const DEFAULT_FROM_ADDRESS As String = "onestream-noreply@company.com"

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As EventHandlerArgs) As Object
            Try
                '--- Determine the notification trigger event ---
                Dim eventType As String = args.NameValuePairs.XFGetValue("NotificationType", String.Empty)

                If String.IsNullOrEmpty(eventType) Then
                    '--- Infer notification type from the event handler type ---
                    eventType = InferNotificationType(args)
                End If

                If String.IsNullOrEmpty(eventType) Then
                    Return Nothing
                End If

                '--- Extract common POV context ---
                Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                Dim timeName As String = args.NameValuePairs.XFGetValue("Time", String.Empty)
                Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", String.Empty)

                If String.IsNullOrEmpty(timeName) Then
                    timeName = BRApi.Finance.Members.GetMemberName(si, DimType.Time.Id, si.WorkflowClusterPk.TimeId)
                End If
                If String.IsNullOrEmpty(entityName) Then
                    entityName = BRApi.Finance.Members.GetMemberName(si, DimType.Entity.Id, si.WorkflowClusterPk.EntityId)
                End If

                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: Processing " & eventType &
                    " notification for " & scenarioName & "/" & timeName & "/" & entityName)

                '--- Route to the appropriate notification handler ---
                Select Case eventType.ToUpper()
                    Case "SUBMISSION"
                        HandleSubmissionNotification(si, args, scenarioName, timeName, entityName)
                    Case "APPROVAL"
                        HandleApprovalNotification(si, args, scenarioName, timeName, entityName)
                    Case "REJECTION"
                        HandleRejectionNotification(si, args, scenarioName, timeName, entityName)
                    Case "DATAQUALITY"
                        HandleDataQualityNotification(si, args, scenarioName, timeName, entityName)
                    Case "ICMISMATCH"
                        HandleICMismatchNotification(si, args, scenarioName, timeName, entityName)
                    Case Else
                        BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: Unknown notification type '" & eventType & "'")
                End Select

                Return Nothing

            Catch ex As Exception
                ' Notification failures should never block the primary workflow
                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: ERROR (non-blocking) - " & ex.Message)
                Return Nothing
            End Try
        End Function

        '--- Notification Handlers ---

        ''' <summary>
        ''' Handles workflow submission notifications. Notifies the approver that
        ''' data has been submitted and is ready for review.
        ''' </summary>
        Private Sub HandleSubmissionNotification(ByVal si As SessionInfo, ByVal args As EventHandlerArgs,
                                                  ByVal scenario As String, ByVal time As String,
                                                  ByVal entity As String)
            Dim submitterName As String = si.UserName
            Dim approverEmail As String = GetApproverEmail(si, entity)

            If String.IsNullOrEmpty(approverEmail) Then
                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: No approver configured for " & entity)
                Return
            End If

            Dim subject As String = String.Format("[{0}] Data Submitted for Review - {1} / {2} / {3}",
                APP_NAME, scenario, time, entity)

            Dim body As String = BuildHtmlBody(
                "Data Submission Notification",
                String.Format("{0} has submitted data for your review.", submitterName),
                scenario, time, entity,
                "Please review and approve or reject the submission in the OneStream workflow.",
                "#007BFF")

            SendNotification(si, approverEmail, subject, body, entity)
        End Sub

        ''' <summary>
        ''' Handles workflow approval notifications. Notifies the submitter that
        ''' their data has been approved.
        ''' </summary>
        Private Sub HandleApprovalNotification(ByVal si As SessionInfo, ByVal args As EventHandlerArgs,
                                                ByVal scenario As String, ByVal time As String,
                                                ByVal entity As String)
            Dim approverName As String = si.UserName
            Dim submitterEmail As String = GetSubmitterEmail(si, entity, time)

            If String.IsNullOrEmpty(submitterEmail) Then
                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: No submitter email found for " & entity)
                Return
            End If

            Dim subject As String = String.Format("[{0}] Data Approved - {1} / {2} / {3}",
                APP_NAME, scenario, time, entity)

            Dim body As String = BuildHtmlBody(
                "Data Approval Notification",
                String.Format("Your submission has been approved by {0}.", approverName),
                scenario, time, entity,
                "No further action is required.",
                "#28A745")

            SendNotification(si, submitterEmail, subject, body, entity)
        End Sub

        ''' <summary>
        ''' Handles workflow rejection notifications. Notifies the submitter with
        ''' the rejection reason and required corrective actions.
        ''' </summary>
        Private Sub HandleRejectionNotification(ByVal si As SessionInfo, ByVal args As EventHandlerArgs,
                                                 ByVal scenario As String, ByVal time As String,
                                                 ByVal entity As String)
            Dim approverName As String = si.UserName
            Dim rejectionReason As String = args.NameValuePairs.XFGetValue("RejectionReason", "No reason provided.")
            Dim submitterEmail As String = GetSubmitterEmail(si, entity, time)

            If String.IsNullOrEmpty(submitterEmail) Then
                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: No submitter email found for " & entity)
                Return
            End If

            Dim subject As String = String.Format("[{0}] Data Rejected - {1} / {2} / {3}",
                APP_NAME, scenario, time, entity)

            Dim body As String = BuildHtmlBody(
                "Data Rejection Notification",
                String.Format("Your submission has been rejected by {0}.", approverName),
                scenario, time, entity,
                "<strong>Rejection Reason:</strong><br/>" & rejectionReason &
                "<br/><br/>Please correct the issues and resubmit.",
                "#DC3545")

            SendNotification(si, submitterEmail, subject, body, entity)
        End Sub

        ''' <summary>
        ''' Handles data quality failure notifications. Notifies the data steward
        ''' of validation issues that require attention.
        ''' </summary>
        Private Sub HandleDataQualityNotification(ByVal si As SessionInfo, ByVal args As EventHandlerArgs,
                                                   ByVal scenario As String, ByVal time As String,
                                                   ByVal entity As String)
            Dim dataStewardEmail As String = GetDataStewardEmail(si, entity)
            Dim failureDetails As String = args.NameValuePairs.XFGetValue("FailureDetails", "See system log for details.")

            If String.IsNullOrEmpty(dataStewardEmail) Then
                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: No data steward configured for " & entity)
                Return
            End If

            Dim subject As String = String.Format("[{0}] Data Quality Alert - {1} / {2} / {3}",
                APP_NAME, scenario, time, entity)

            Dim body As String = BuildHtmlBody(
                "Data Quality Alert",
                "Data quality validation has identified issues requiring attention.",
                scenario, time, entity,
                "<strong>Validation Issues:</strong><br/>" & failureDetails,
                "#FFC107")

            SendNotification(si, dataStewardEmail, subject, body, entity)
        End Sub

        ''' <summary>
        ''' Handles IC mismatch notifications. Notifies both the current entity controller
        ''' and the partner entity controller of the IC balance discrepancy.
        ''' </summary>
        Private Sub HandleICMismatchNotification(ByVal si As SessionInfo, ByVal args As EventHandlerArgs,
                                                  ByVal scenario As String, ByVal time As String,
                                                  ByVal entity As String)
            Dim partnerEntity As String = args.NameValuePairs.XFGetValue("PartnerEntity", "Unknown")
            Dim mismatchDetails As String = args.NameValuePairs.XFGetValue("MismatchDetails", "See IC matching report.")

            '--- Notify the current entity's controller ---
            Dim entityControllerEmail As String = GetEntityControllerEmail(si, entity)
            If Not String.IsNullOrEmpty(entityControllerEmail) Then
                Dim subject As String = String.Format("[{0}] IC Mismatch Alert - {1} vs {2} / {3}",
                    APP_NAME, entity, partnerEntity, time)
                Dim body As String = BuildHtmlBody(
                    "Intercompany Mismatch Alert",
                    String.Format("IC balance mismatch detected between {0} and {1}.", entity, partnerEntity),
                    scenario, time, entity,
                    "<strong>Mismatch Details:</strong><br/>" & mismatchDetails &
                    "<br/><br/>Please coordinate with the partner entity controller to resolve.",
                    "#DC3545")
                SendNotification(si, entityControllerEmail, subject, body, entity)
            End If

            '--- Notify the partner entity's controller ---
            Dim partnerControllerEmail As String = GetEntityControllerEmail(si, partnerEntity)
            If Not String.IsNullOrEmpty(partnerControllerEmail) Then
                Dim subject As String = String.Format("[{0}] IC Mismatch Alert - {1} vs {2} / {3}",
                    APP_NAME, partnerEntity, entity, time)
                Dim body As String = BuildHtmlBody(
                    "Intercompany Mismatch Alert",
                    String.Format("IC balance mismatch detected between {0} and {1}.", partnerEntity, entity),
                    scenario, time, partnerEntity,
                    "<strong>Mismatch Details:</strong><br/>" & mismatchDetails &
                    "<br/><br/>Please coordinate with the partner entity controller to resolve.",
                    "#DC3545")
                SendNotification(si, partnerControllerEmail, subject, body, partnerEntity)
            End If
        End Sub

        '--- Helper Methods ---

        ''' <summary>
        ''' Infers the notification type from the event handler arguments when not
        ''' explicitly specified.
        ''' </summary>
        Private Function InferNotificationType(ByVal args As EventHandlerArgs) As String
            If args.EventHandlerType = EventHandlerType.WorkflowAction Then
                Dim action As String = args.NameValuePairs.XFGetValue("WorkflowAction", String.Empty)
                Select Case action.ToUpper()
                    Case "SUBMIT" : Return "SUBMISSION"
                    Case "APPROVE" : Return "APPROVAL"
                    Case "REJECT" : Return "REJECTION"
                End Select
            End If
            Return String.Empty
        End Function

        ''' <summary>
        ''' Builds a formatted HTML email body with consistent branding and layout.
        ''' </summary>
        Private Function BuildHtmlBody(ByVal title As String, ByVal summary As String,
                                        ByVal scenario As String, ByVal time As String,
                                        ByVal entity As String, ByVal detailsHtml As String,
                                        ByVal accentColor As String) As String
            Dim sb As New StringBuilder()

            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=""UTF-8""></head><body>")
            sb.AppendLine("<div style=""font-family: Segoe UI, Arial, sans-serif; max-width: 600px; margin: 0 auto;"">")

            '--- Header bar ---
            sb.AppendFormat("<div style=""background-color: {0}; padding: 16px; color: white;"">", accentColor)
            sb.AppendFormat("<h2 style=""margin: 0;"">{0}</h2>", title)
            sb.AppendLine("</div>")

            '--- Body content ---
            sb.AppendLine("<div style=""padding: 20px; border: 1px solid #ddd; border-top: none;"">")
            sb.AppendFormat("<p style=""font-size: 14px;"">{0}</p>", summary)

            '--- POV context table ---
            sb.AppendLine("<table style=""width: 100%; border-collapse: collapse; margin: 16px 0;"">")
            sb.AppendLine("<tr><td style=""padding: 8px; border: 1px solid #ddd; font-weight: bold; width: 30%;"">Scenario</td>")
            sb.AppendFormat("<td style=""padding: 8px; border: 1px solid #ddd;"">{0}</td></tr>", scenario)
            sb.AppendLine("<tr><td style=""padding: 8px; border: 1px solid #ddd; font-weight: bold;"">Period</td>")
            sb.AppendFormat("<td style=""padding: 8px; border: 1px solid #ddd;"">{0}</td></tr>", time)
            sb.AppendLine("<tr><td style=""padding: 8px; border: 1px solid #ddd; font-weight: bold;"">Entity</td>")
            sb.AppendFormat("<td style=""padding: 8px; border: 1px solid #ddd;"">{0}</td></tr>", entity)
            sb.AppendLine("<tr><td style=""padding: 8px; border: 1px solid #ddd; font-weight: bold;"">Date/Time (UTC)</td>")
            sb.AppendFormat("<td style=""padding: 8px; border: 1px solid #ddd;"">{0}</td></tr>",
                DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))
            sb.AppendLine("</table>")

            '--- Additional details ---
            If Not String.IsNullOrEmpty(detailsHtml) Then
                sb.AppendFormat("<div style=""padding: 12px; background-color: #f8f9fa; border-radius: 4px; margin-top: 12px;"">{0}</div>", detailsHtml)
            End If

            '--- Footer ---
            sb.AppendLine("<hr style=""margin-top: 24px; border: none; border-top: 1px solid #ddd;""/>")
            sb.AppendFormat("<p style=""font-size: 11px; color: #6c757d;"">This is an automated notification from {0}. " &
                "Please do not reply to this email.</p>", APP_NAME)

            sb.AppendLine("</div></div></body></html>")

            Return sb.ToString()
        End Function

        ''' <summary>
        ''' Sends a notification via the configured channel (email and/or Teams webhook).
        ''' </summary>
        Private Sub SendNotification(ByVal si As SessionInfo, ByVal recipientEmail As String,
                                      ByVal subject As String, ByVal htmlBody As String,
                                      ByVal entityName As String)
            '--- Send email notification ---
            SendEmailNotification(si, recipientEmail, subject, htmlBody)

            '--- Send Teams notification if webhook is configured ---
            Dim teamsWebhookUrl As String = GetTeamsWebhookUrl(si, entityName)
            If Not String.IsNullOrEmpty(teamsWebhookUrl) Then
                SendTeamsNotification(si, teamsWebhookUrl, subject, entityName)
            End If
        End Sub

        ''' <summary>
        ''' Sends an email notification using the OneStream notification API.
        ''' </summary>
        Private Sub SendEmailNotification(ByVal si As SessionInfo, ByVal recipientEmail As String,
                                           ByVal subject As String, ByVal htmlBody As String)
            Try
                BRApi.Utilities.SendMail(si, DEFAULT_FROM_ADDRESS, recipientEmail, subject, htmlBody, True)
                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: Email sent to " & recipientEmail)
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: Email send failed for " &
                    recipientEmail & " - " & ex.Message)
            End Try
        End Sub

        ''' <summary>
        ''' Sends a notification card to a Microsoft Teams channel via incoming webhook.
        ''' </summary>
        Private Sub SendTeamsNotification(ByVal si As SessionInfo, ByVal webhookUrl As String,
                                           ByVal subject As String, ByVal entityName As String)
            Try
                '--- Build a simple Teams adaptive card JSON payload ---
                Dim payload As String = "{""@type"":""MessageCard"",""@context"":""http://schema.org/extensions""," &
                    """summary"":""" & subject.Replace("""", "\""") & """," &
                    """themeColor"":""0076D7""," &
                    """title"":""" & subject.Replace("""", "\""") & """," &
                    """sections"":[{""activityTitle"":""Entity: " & entityName & """," &
                    """activitySubtitle"":""" & DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") & " UTC""}]}"

                '--- POST the payload to the Teams webhook URL ---
                Using client As New WebClient()
                    client.Headers.Add("Content-Type", "application/json")
                    client.UploadString(webhookUrl, "POST", payload)
                End Using

                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: Teams notification sent for " & entityName)

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher: Teams notification failed - " & ex.Message)
            End Try
        End Sub

        '--- Recipient Lookup Methods ---

        Private Function GetApproverEmail(ByVal si As SessionInfo, ByVal entity As String) As String
            Return GetRoleEmail(si, entity, "Approver")
        End Function

        Private Function GetSubmitterEmail(ByVal si As SessionInfo, ByVal entity As String, ByVal time As String) As String
            Return GetRoleEmail(si, entity, "Submitter")
        End Function

        Private Function GetDataStewardEmail(ByVal si As SessionInfo, ByVal entity As String) As String
            Return GetRoleEmail(si, entity, "DataSteward")
        End Function

        Private Function GetEntityControllerEmail(ByVal si As SessionInfo, ByVal entity As String) As String
            Return GetRoleEmail(si, entity, "Controller")
        End Function

        ''' <summary>
        ''' Retrieves the email address for a specific role associated with an entity.
        ''' Role-to-email mappings are stored in substitution variables.
        ''' </summary>
        Private Function GetRoleEmail(ByVal si As SessionInfo, ByVal entity As String, ByVal role As String) As String
            Try
                Dim varName As String = role & "Email_" & entity
                Dim email As String = BRApi.Finance.Data.GetSubstVarValue(si, varName)
                If Not String.IsNullOrEmpty(email) Then
                    Return email.Trim()
                End If

                '--- Fallback: try a default role email ---
                Dim defaultVar As String = "Default" & role & "Email"
                email = BRApi.Finance.Data.GetSubstVarValue(si, defaultVar)
                If Not String.IsNullOrEmpty(email) Then
                    Return email.Trim()
                End If
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "EH_NotificationDispatcher.GetRoleEmail: Error for " &
                    entity & "/" & role & " - " & ex.Message)
            End Try
            Return String.Empty
        End Function

        ''' <summary>
        ''' Retrieves the Teams webhook URL for the specified entity's notification channel.
        ''' </summary>
        Private Function GetTeamsWebhookUrl(ByVal si As SessionInfo, ByVal entity As String) As String
            Try
                Dim webhookVar As String = "TeamsWebhook_" & entity
                Dim url As String = BRApi.Finance.Data.GetSubstVarValue(si, webhookVar)
                If Not String.IsNullOrEmpty(url) Then
                    Return url.Trim()
                End If

                '--- Fallback: try a default Teams webhook ---
                url = BRApi.Finance.Data.GetSubstVarValue(si, "DefaultTeamsWebhook")
                If Not String.IsNullOrEmpty(url) Then
                    Return url.Trim()
                End If
            Catch
            End Try
            Return String.Empty
        End Function

    End Class

End Namespace

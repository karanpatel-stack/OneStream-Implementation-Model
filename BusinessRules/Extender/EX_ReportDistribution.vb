'------------------------------------------------------------------------------------------------------------
' EX_ReportDistribution.vb
' OneStream XF Extender Business Rule
'
' Purpose:  Automated report generation and email distribution engine. Defines report packages
'           by recipient role (Executive, Regional VP, Plant Controller, CFO), generates report
'           data via DDA calls, formats output as HTML or Excel, builds distribution lists from
'           role/entity mapping, and tracks delivery status with archival support.
'
' Parameters (pipe-delimited):
'   Scenario     - Scenario name (e.g., "Actual")
'   TimePeriod   - Reporting period (e.g., "2024M6")
'   TriggerMode  - "Schedule" (routine), "PeriodClose" (event), or "OnDemand"
'   RoleFilter   - "All" or comma-separated roles (e.g., "Executive,CFO")
'
' Usage:     Called from scheduled job, period-close workflow, or on-demand via UI.
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Threading
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.Extender.EX_ReportDistribution
    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Report package definition: a collection of reports for a specific role.
        '----------------------------------------------------------------------------------------------------
        Private Class ReportPackage
            Public Property RoleName As String
            Public Property Reports As List(Of ReportDefinition)
        End Class

        Private Class ReportDefinition
            Public Property ReportName As String
            Public Property DDABusinessRule As String      ' DDA rule to generate report data
            Public Property OutputFormat As String          ' "HTML" or "Excel"
            Public Property Description As String
        End Class

        '----------------------------------------------------------------------------------------------------
        ' Recipient: a person or group to receive a report package.
        '----------------------------------------------------------------------------------------------------
        Private Class Recipient
            Public Property Name As String
            Public Property Email As String
            Public Property RoleName As String
            Public Property EntityScope As String          ' Entity or region the recipient is responsible for
        End Class

        '----------------------------------------------------------------------------------------------------
        ' Delivery tracking record.
        '----------------------------------------------------------------------------------------------------
        Private Class DeliveryRecord
            Public Property RecipientEmail As String
            Public Property RecipientName As String
            Public Property RoleName As String
            Public Property ReportName As String
            Public Property DeliveryStatus As String       ' Sent, Delivered, Failed
            Public Property ErrorMessage As String
            Public Property SentAtUtc As DateTime
        End Class

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As ExtenderArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = ExtenderFunctionType.ExecuteServerProcess
                        Dim paramString As String = args.NameValuePairs.XFGetValue("Parameters", String.Empty)
                        Me.ExecuteReportDistribution(si, globals, api, paramString)
                        Return Nothing

                    Case Else
                        Throw New XFException(si, $"EX_ReportDistribution: Unsupported function type [{args.FunctionType}].")
                End Select
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExecuteReportDistribution
        ' Main orchestration: builds packages, resolves recipients, generates and delivers reports.
        '----------------------------------------------------------------------------------------------------
        Private Sub ExecuteReportDistribution(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal paramString As String)
            Dim distStart As DateTime = DateTime.UtcNow
            Dim deliveryLog As New List(Of DeliveryRecord)

            ' ------------------------------------------------------------------
            ' 1. Parse parameters
            ' ------------------------------------------------------------------
            Dim parameters() As String = paramString.Split("|"c)
            If parameters.Length < 4 Then
                Throw New XFException(si, "EX_ReportDistribution: Expected 4 pipe-delimited parameters (Scenario|TimePeriod|TriggerMode|RoleFilter).")
            End If

            Dim scenarioName As String = parameters(0).Trim()
            Dim timePeriod As String = parameters(1).Trim()
            Dim triggerMode As String = parameters(2).Trim()
            Dim roleFilter As String = parameters(3).Trim()

            BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: Starting. Scenario=[{scenarioName}], Period=[{timePeriod}], Trigger=[{triggerMode}], Roles=[{roleFilter}].")
            api.Progress.ReportProgress(0, "Initializing report distribution...")

            ' ------------------------------------------------------------------
            ' 2. Build report package definitions by role
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(5, "Building report package definitions...")
            Dim allPackages As List(Of ReportPackage) = BuildReportPackages()

            ' Apply role filter
            Dim packages As List(Of ReportPackage)
            If roleFilter.Equals("All", StringComparison.OrdinalIgnoreCase) Then
                packages = allPackages
            Else
                Dim allowedRoles() As String = roleFilter.Split(","c)
                Dim roleSet As New HashSet(Of String)(allowedRoles.Select(Function(r) r.Trim()), StringComparer.OrdinalIgnoreCase)
                packages = allPackages.Where(Function(p) roleSet.Contains(p.RoleName)).ToList()
            End If

            BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: {packages.Count} report package(s) to process.")

            ' ------------------------------------------------------------------
            ' 3. Resolve distribution lists from role/entity mapping
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(10, "Resolving distribution lists...")
            Dim allRecipients As List(Of Recipient) = LoadDistributionList(si)
            BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: {allRecipients.Count} total recipient(s) loaded.")

            ' ------------------------------------------------------------------
            ' 4. Process each report package
            ' ------------------------------------------------------------------
            Dim totalPackages As Integer = packages.Count
            Dim completedPackages As Integer = 0

            For Each pkg As ReportPackage In packages
                Dim pkgProgress As Integer = CInt(15 + (70.0 * completedPackages / totalPackages))
                api.Progress.ReportProgress(pkgProgress, $"Processing package [{pkg.RoleName}]...")

                ' Get recipients for this role
                Dim recipients As List(Of Recipient) = allRecipients.Where(
                    Function(r) r.RoleName.Equals(pkg.RoleName, StringComparison.OrdinalIgnoreCase)).ToList()

                If recipients.Count = 0 Then
                    BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: No recipients for role [{pkg.RoleName}]. Skipping package.")
                    completedPackages += 1
                    Continue For
                End If

                BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: [{pkg.RoleName}] has {recipients.Count} recipient(s), {pkg.Reports.Count} report(s).")

                ' Generate each report in the package
                For Each reportDef As ReportDefinition In pkg.Reports
                    Try
                        ' Step A: Generate report data by calling DDA business rule
                        api.Progress.ReportProgress(pkgProgress + 2, $"[{pkg.RoleName}] Generating [{reportDef.ReportName}]...")
                        Dim reportContent As String = GenerateReportData(si, reportDef, scenarioName, timePeriod)

                        ' Step B: Format output
                        Dim formattedOutput As String
                        Dim attachmentPath As String = Nothing

                        If reportDef.OutputFormat.Equals("Excel", StringComparison.OrdinalIgnoreCase) Then
                            attachmentPath = FormatAsExcel(si, reportDef.ReportName, reportContent, scenarioName, timePeriod)
                            formattedOutput = BuildEmailBodyForExcel(reportDef.ReportName, scenarioName, timePeriod)
                        Else
                            formattedOutput = FormatAsHTML(reportDef.ReportName, reportContent, scenarioName, timePeriod)
                        End If

                        ' Step C: Deliver to each recipient (scoped by entity if applicable)
                        For Each recipient As Recipient In recipients
                            Dim deliveryRecord As New DeliveryRecord With {
                                .RecipientEmail = recipient.Email,
                                .RecipientName = recipient.Name,
                                .RoleName = pkg.RoleName,
                                .ReportName = reportDef.ReportName,
                                .SentAtUtc = DateTime.UtcNow
                            }

                            Try
                                Dim subject As String = $"{reportDef.ReportName} - {scenarioName} {timePeriod}"
                                If Not String.IsNullOrEmpty(recipient.EntityScope) Then
                                    subject &= $" ({recipient.EntityScope})"
                                End If

                                If attachmentPath IsNot Nothing Then
                                    BRApi.Utilities.SendMailWithAttachment(si, recipient.Email, subject, formattedOutput, attachmentPath)
                                Else
                                    BRApi.Utilities.SendMail(si, recipient.Email, subject, formattedOutput)
                                End If

                                deliveryRecord.DeliveryStatus = "Sent"
                            Catch mailEx As Exception
                                deliveryRecord.DeliveryStatus = "Failed"
                                deliveryRecord.ErrorMessage = mailEx.Message
                                BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: FAILED to send [{reportDef.ReportName}] to [{recipient.Email}]: {mailEx.Message}")
                            End Try

                            deliveryLog.Add(deliveryRecord)
                        Next

                        ' Step D: Archive generated report
                        ArchiveReport(si, reportDef.ReportName, reportContent, scenarioName, timePeriod, pkg.RoleName)

                    Catch reportEx As Exception
                        BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: ERROR generating [{reportDef.ReportName}] for [{pkg.RoleName}]: {reportEx.Message}")

                        ' Record failure for all recipients of this report
                        For Each recipient As Recipient In recipients
                            deliveryLog.Add(New DeliveryRecord With {
                                .RecipientEmail = recipient.Email,
                                .RecipientName = recipient.Name,
                                .RoleName = pkg.RoleName,
                                .ReportName = reportDef.ReportName,
                                .DeliveryStatus = "Failed",
                                .ErrorMessage = $"Report generation failed: {reportEx.Message}",
                                .SentAtUtc = DateTime.UtcNow
                            })
                        Next
                    End Try
                Next

                completedPackages += 1
            Next

            ' ------------------------------------------------------------------
            ' 5. Generate distribution summary and log delivery status
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(90, "Generating distribution summary...")
            Dim summaryReport As String = GenerateDistributionSummary(deliveryLog, scenarioName, timePeriod, triggerMode, distStart)
            BRApi.ErrorLog.LogMessage(si, summaryReport)

            ' Persist delivery log to database for auditing
            PersistDeliveryLog(si, deliveryLog, scenarioName, timePeriod)

            api.Progress.ReportProgress(100, "Report distribution complete.")
            BRApi.ErrorLog.LogMessage(si, "EX_ReportDistribution: Process completed.")
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' BuildReportPackages
        ' Defines the report packages for each recipient role.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildReportPackages() As List(Of ReportPackage)
            Dim packages As New List(Of ReportPackage)

            ' Executive package: high-level summaries
            packages.Add(New ReportPackage With {
                .RoleName = "Executive",
                .Reports = New List(Of ReportDefinition) From {
                    New ReportDefinition With {.ReportName = "P&L Summary", .DDABusinessRule = "DDA_PL_Summary", .OutputFormat = "HTML", .Description = "Consolidated P&L with prior period and budget comparison"},
                    New ReportDefinition With {.ReportName = "KPI Dashboard", .DDABusinessRule = "DDA_KPI_Dashboard", .OutputFormat = "HTML", .Description = "Key performance indicators with trend analysis"},
                    New ReportDefinition With {.ReportName = "Cash Position", .DDABusinessRule = "DDA_CashPosition", .OutputFormat = "HTML", .Description = "Cash and liquidity position summary"}
                }
            })

            ' Regional VP package: regional performance detail
            packages.Add(New ReportPackage With {
                .RoleName = "RegionalVP",
                .Reports = New List(Of ReportDefinition) From {
                    New ReportDefinition With {.ReportName = "Regional P&L", .DDABusinessRule = "DDA_Regional_PL", .OutputFormat = "Excel", .Description = "Regional income statement with entity detail"},
                    New ReportDefinition With {.ReportName = "Plant Performance", .DDABusinessRule = "DDA_PlantPerformance", .OutputFormat = "Excel", .Description = "Manufacturing plant performance metrics"},
                    New ReportDefinition With {.ReportName = "Variance Analysis", .DDABusinessRule = "DDA_VarianceAnalysis", .OutputFormat = "Excel", .Description = "Actual vs. budget and prior year variance"}
                }
            })

            ' Plant Controller package: operational detail
            packages.Add(New ReportPackage With {
                .RoleName = "PlantController",
                .Reports = New List(Of ReportDefinition) From {
                    New ReportDefinition With {.ReportName = "Plant P&L", .DDABusinessRule = "DDA_Plant_PL", .OutputFormat = "Excel", .Description = "Plant-level income statement"},
                    New ReportDefinition With {.ReportName = "Budget vs Actual", .DDABusinessRule = "DDA_BvA", .OutputFormat = "Excel", .Description = "Detailed budget vs. actual comparison"},
                    New ReportDefinition With {.ReportName = "Production Metrics", .DDABusinessRule = "DDA_ProductionMetrics", .OutputFormat = "Excel", .Description = "Production volume, yield, and efficiency KPIs"}
                }
            })

            ' CFO package: consolidated and compliance
            packages.Add(New ReportPackage With {
                .RoleName = "CFO",
                .Reports = New List(Of ReportDefinition) From {
                    New ReportDefinition With {.ReportName = "Consolidated Financials", .DDABusinessRule = "DDA_ConsolidatedFS", .OutputFormat = "Excel", .Description = "Full consolidated financial statements"},
                    New ReportDefinition With {.ReportName = "IC Reconciliation", .DDABusinessRule = "DDA_IC_Recon", .OutputFormat = "Excel", .Description = "Intercompany reconciliation summary"},
                    New ReportDefinition With {.ReportName = "Close Status", .DDABusinessRule = "DDA_CloseStatus", .OutputFormat = "HTML", .Description = "Period close task status and completion summary"}
                }
            })

            Return packages
        End Function

        '----------------------------------------------------------------------------------------------------
        ' LoadDistributionList
        ' Loads recipient information from a configuration table mapping roles to email addresses.
        '----------------------------------------------------------------------------------------------------
        Private Function LoadDistributionList(ByVal si As SessionInfo) As List(Of Recipient)
            Dim recipients As New List(Of Recipient)

            Dim sql As String = "SELECT RecipientName, Email, RoleName, EntityScope " &
                                "FROM [Config].[dbo].[ReportDistributionList] WHERE IsActive = 1 ORDER BY RoleName, RecipientName"
            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)

            If dt IsNot Nothing Then
                For Each row As DataRow In dt.Rows
                    recipients.Add(New Recipient With {
                        .Name = row("RecipientName").ToString(),
                        .Email = row("Email").ToString(),
                        .RoleName = row("RoleName").ToString(),
                        .EntityScope = row("EntityScope").ToString()
                    })
                Next
            End If

            Return recipients
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GenerateReportData
        ' Calls the DDA business rule to generate report content as a string (HTML or raw data).
        '----------------------------------------------------------------------------------------------------
        Private Function GenerateReportData(ByVal si As SessionInfo, ByVal reportDef As ReportDefinition, ByVal scenario As String, ByVal period As String) As String
            Dim brParams As String = $"{scenario}|{period}"
            Dim result As Object = BRApi.BusinessRules.ExecuteBusinessRule(si, reportDef.DDABusinessRule, brParams)

            If result IsNot Nothing Then
                Return result.ToString()
            End If

            Return String.Empty
        End Function

        '----------------------------------------------------------------------------------------------------
        ' FormatAsHTML
        ' Wraps report content in a styled HTML email template.
        '----------------------------------------------------------------------------------------------------
        Private Function FormatAsHTML(ByVal reportName As String, ByVal content As String, ByVal scenario As String, ByVal period As String) As String
            Dim html As New Text.StringBuilder()

            html.AppendLine("<!DOCTYPE html>")
            html.AppendLine("<html><head>")
            html.AppendLine("<style>")
            html.AppendLine("  body { font-family: Calibri, Arial, sans-serif; font-size: 11pt; color: #333; margin: 20px; }")
            html.AppendLine("  h1 { color: #003366; border-bottom: 2px solid #003366; padding-bottom: 8px; }")
            html.AppendLine("  h2 { color: #336699; }")
            html.AppendLine("  table { border-collapse: collapse; width: 100%; margin: 10px 0; }")
            html.AppendLine("  th { background-color: #003366; color: white; padding: 8px 12px; text-align: left; }")
            html.AppendLine("  td { padding: 6px 12px; border-bottom: 1px solid #ddd; }")
            html.AppendLine("  tr:nth-child(even) { background-color: #f9f9f9; }")
            html.AppendLine("  .footer { color: #999; font-size: 9pt; margin-top: 30px; border-top: 1px solid #ddd; padding-top: 10px; }")
            html.AppendLine("</style>")
            html.AppendLine("</head><body>")
            html.AppendLine($"<h1>{reportName}</h1>")
            html.AppendLine($"<p><strong>Scenario:</strong> {scenario} &nbsp;&nbsp; <strong>Period:</strong> {period}</p>")
            html.AppendLine(content)
            html.AppendLine("<div class='footer'>")
            html.AppendLine($"  <p>Generated by OneStream Report Distribution | {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</p>")
            html.AppendLine($"  <p>This is an automated report. Please do not reply to this email.</p>")
            html.AppendLine("</div>")
            html.AppendLine("</body></html>")

            Return html.ToString()
        End Function

        '----------------------------------------------------------------------------------------------------
        ' FormatAsExcel
        ' Exports report content to an Excel file and returns the file path.
        '----------------------------------------------------------------------------------------------------
        Private Function FormatAsExcel(ByVal si As SessionInfo, ByVal reportName As String, ByVal content As String, ByVal scenario As String, ByVal period As String) As String
            ' Generate a temp file path for the Excel attachment
            Dim safeName As String = reportName.Replace(" ", "_").Replace("/", "_")
            Dim fileName As String = $"{safeName}_{scenario}_{period}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx"
            Dim filePath As String = Path.Combine(BRApi.Utilities.GetTempDirectory(si), fileName)

            ' Use the OneStream Excel export utility to create the workbook
            BRApi.Utilities.ExportToExcel(si, content, filePath)

            BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: Excel report generated at [{filePath}].")
            Return filePath
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildEmailBodyForExcel
        ' Creates a short HTML email body when the main report is attached as Excel.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildEmailBodyForExcel(ByVal reportName As String, ByVal scenario As String, ByVal period As String) As String
            Dim html As New Text.StringBuilder()
            html.AppendLine("<html><body style='font-family: Calibri, Arial, sans-serif;'>")
            html.AppendLine($"<h2 style='color: #003366;'>{reportName}</h2>")
            html.AppendLine($"<p><strong>Scenario:</strong> {scenario} &nbsp;&nbsp; <strong>Period:</strong> {period}</p>")
            html.AppendLine("<p>Please find the attached Excel report for your review.</p>")
            html.AppendLine("<p style='color: #999; font-size: 9pt;'>This is an automated report from OneStream. Please do not reply.</p>")
            html.AppendLine("</body></html>")
            Return html.ToString()
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ArchiveReport
        ' Saves a copy of the generated report content for historical reference.
        '----------------------------------------------------------------------------------------------------
        Private Sub ArchiveReport(ByVal si As SessionInfo, ByVal reportName As String, ByVal content As String, ByVal scenario As String, ByVal period As String, ByVal roleName As String)
            Try
                Dim sql As String = "INSERT INTO [Reporting].[dbo].[ReportArchive] " &
                                    "(ReportName, RoleName, Scenario, TimePeriod, Content, GeneratedAtUtc, GeneratedBy) " &
                                    "VALUES (@ReportName, @RoleName, @Scenario, @Period, @Content, @GeneratedAt, @User)"
                BRApi.Database.ExecuteActionQuery(si, sql, True)
                BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: Archived [{reportName}] for [{roleName}].")
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: WARNING - Failed to archive [{reportName}]: {ex.Message}")
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' PersistDeliveryLog
        ' Writes all delivery records to the database for tracking and auditing.
        '----------------------------------------------------------------------------------------------------
        Private Sub PersistDeliveryLog(ByVal si As SessionInfo, ByVal deliveryLog As List(Of DeliveryRecord), ByVal scenario As String, ByVal period As String)
            For Each record As DeliveryRecord In deliveryLog
                Try
                    Dim sql As String = "INSERT INTO [Reporting].[dbo].[DeliveryLog] " &
                                        "(Scenario, TimePeriod, RecipientEmail, RecipientName, RoleName, ReportName, DeliveryStatus, ErrorMessage, SentAtUtc) " &
                                        "VALUES (@Scenario, @Period, @Email, @Name, @Role, @Report, @Status, @Error, @SentAt)"
                    BRApi.Database.ExecuteActionQuery(si, sql, True)
                Catch ex As Exception
                    BRApi.ErrorLog.LogMessage(si, $"EX_ReportDistribution: WARNING - Failed to persist delivery log for [{record.RecipientEmail}]: {ex.Message}")
                End Try
            Next
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GenerateDistributionSummary
        ' Builds a summary report of all deliveries for this run.
        '----------------------------------------------------------------------------------------------------
        Private Function GenerateDistributionSummary(ByVal deliveryLog As List(Of DeliveryRecord), ByVal scenario As String, ByVal period As String, ByVal triggerMode As String, ByVal startTime As DateTime) As String
            Dim elapsed As Double = (DateTime.UtcNow - startTime).TotalSeconds

            Dim sentCount As Integer = deliveryLog.Count(Function(d) d.DeliveryStatus = "Sent")
            Dim failedCount As Integer = deliveryLog.Count(Function(d) d.DeliveryStatus = "Failed")

            Dim report As New Text.StringBuilder()
            report.AppendLine("========================================================================")
            report.AppendLine("          REPORT DISTRIBUTION SUMMARY")
            report.AppendLine("========================================================================")
            report.AppendLine($"Scenario:            {scenario}")
            report.AppendLine($"Period:              {period}")
            report.AppendLine($"Trigger Mode:        {triggerMode}")
            report.AppendLine($"Run Date (UTC):      {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}")
            report.AppendLine($"Elapsed Time (s):    {elapsed:F1}")
            report.AppendLine()
            report.AppendLine("--- Delivery Summary ---")
            report.AppendLine($"  Total Deliveries:  {deliveryLog.Count}")
            report.AppendLine($"  Sent:              {sentCount}")
            report.AppendLine($"  Failed:            {failedCount}")
            report.AppendLine()

            ' By-role breakdown
            report.AppendLine("--- By Role ---")
            Dim roleGroups = deliveryLog.GroupBy(Function(d) d.RoleName)
            For Each rg In roleGroups
                Dim roleSent As Integer = rg.Count(Function(d) d.DeliveryStatus = "Sent")
                Dim roleFailed As Integer = rg.Count(Function(d) d.DeliveryStatus = "Failed")
                report.AppendLine($"  {rg.Key.PadRight(20)} Sent: {roleSent}, Failed: {roleFailed}")
            Next
            report.AppendLine()

            ' Failed deliveries detail
            If failedCount > 0 Then
                report.AppendLine("--- Failed Deliveries ---")
                For Each failed As DeliveryRecord In deliveryLog.Where(Function(d) d.DeliveryStatus = "Failed")
                    report.AppendLine($"  [{failed.RoleName}] {failed.ReportName} -> {failed.RecipientEmail}: {failed.ErrorMessage}")
                Next
            End If

            report.AppendLine("========================================================================")
            Return report.ToString()
        End Function

    End Class
End Namespace

'------------------------------------------------------------------------------------------------------------
' EX_RollingForecastSeeder.vb
' OneStream XF Extender Business Rule
'
' Purpose:  Automates rolling forecast (RF) period seeding. Copies actuals for closed periods
'           into the RF scenario, and seeds open future periods using category-specific logic
'           (revenue growth, COGS ratio, OPEX inflation, headcount changes, CAPEX schedules).
'           Supports what-if scenarios with alternate growth assumptions.
'
' Parameters (pipe-delimited):
'   Scenario        - Target RF scenario name (e.g., "RollingForecast")
'   BaseGrowthRate  - Revenue growth assumption as decimal (e.g., "0.05" for 5%)
'   InflationFactor - OPEX inflation assumption as decimal (e.g., "0.03" for 3%)
'   WhatIfTag       - Optional what-if label (e.g., "Optimistic", "Conservative", or "" for base)
'
' Usage:     Triggered monthly after period close or on-demand for what-if analysis.
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

Namespace OneStream.BusinessRule.Extender.EX_RollingForecastSeeder
    Public Class MainClass

        Private Const RF_WINDOW_MONTHS As Integer = 18   ' Rolling forecast horizon

        '----------------------------------------------------------------------------------------------------
        ' Seeding log entry for audit trail.
        '----------------------------------------------------------------------------------------------------
        Private Class SeedingLogEntry
            Public Property Entity As String
            Public Property Account As String
            Public Property Period As String
            Public Property SeededValue As Decimal
            Public Property SourceDescription As String   ' e.g., "PY Actual * 1.05", "Approved CAPEX"
            Public Property Category As String            ' Revenue, COGS, OPEX, Headcount, CAPEX
        End Class

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As ExtenderArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = ExtenderFunctionType.ExecuteServerProcess
                        Dim paramString As String = args.NameValuePairs.XFGetValue("Parameters", String.Empty)
                        Me.ExecuteRollingForecastSeed(si, globals, api, paramString)
                        Return Nothing

                    Case Else
                        Throw New XFException(si, $"EX_RollingForecastSeeder: Unsupported function type [{args.FunctionType}].")
                End Select
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExecuteRollingForecastSeed
        ' Main orchestration: determines periods, copies actuals, seeds future periods.
        '----------------------------------------------------------------------------------------------------
        Private Sub ExecuteRollingForecastSeed(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal paramString As String)
            Dim seedStart As DateTime = DateTime.UtcNow
            Dim seedingLog As New List(Of SeedingLogEntry)

            ' ------------------------------------------------------------------
            ' 1. Parse parameters
            ' ------------------------------------------------------------------
            Dim parameters() As String = paramString.Split("|"c)
            If parameters.Length < 3 Then
                Throw New XFException(si, "EX_RollingForecastSeeder: Expected at least 3 pipe-delimited parameters (Scenario|GrowthRate|InflationFactor[|WhatIfTag]).")
            End If

            Dim rfScenario As String = parameters(0).Trim()
            Dim baseGrowthRate As Decimal = Decimal.Parse(parameters(1).Trim(), CultureInfo.InvariantCulture)
            Dim inflationFactor As Decimal = Decimal.Parse(parameters(2).Trim(), CultureInfo.InvariantCulture)
            Dim whatIfTag As String = If(parameters.Length >= 4, parameters(3).Trim(), String.Empty)

            BRApi.ErrorLog.LogMessage(si, $"EX_RollingForecastSeeder: Starting. RF Scenario=[{rfScenario}], Growth=[{baseGrowthRate:P1}], Inflation=[{inflationFactor:P1}], WhatIf=[{If(String.IsNullOrEmpty(whatIfTag), "Base", whatIfTag)}].")
            api.Progress.ReportProgress(0, "Initializing rolling forecast seeder...")

            ' ------------------------------------------------------------------
            ' 2. Determine current period and rolling forecast window
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(5, "Determining forecast window...")
            Dim currentPeriod As String = BRApi.Finance.Time.GetCurrentPeriod(si)
            Dim rfPeriods As List(Of String) = BRApi.Finance.Time.GetForwardPeriods(si, currentPeriod, RF_WINDOW_MONTHS)

            BRApi.ErrorLog.LogMessage(si, $"EX_RollingForecastSeeder: Current period = [{currentPeriod}]. RF window = {rfPeriods.Count} periods.")

            ' ------------------------------------------------------------------
            ' 3. Identify closed periods (copy actuals) vs. open periods (seed)
            ' ------------------------------------------------------------------
            Dim closedPeriods As New List(Of String)
            Dim openPeriods As New List(Of String)

            For Each period As String In rfPeriods
                Dim status As String = BRApi.Finance.Time.GetPeriodStatus(si, "Actual", period)
                If status.Equals("Closed", StringComparison.OrdinalIgnoreCase) OrElse
                   status.Equals("Locked", StringComparison.OrdinalIgnoreCase) Then
                    closedPeriods.Add(period)
                Else
                    openPeriods.Add(period)
                End If
            Next

            BRApi.ErrorLog.LogMessage(si, $"EX_RollingForecastSeeder: {closedPeriods.Count} closed period(s), {openPeriods.Count} open period(s).")

            ' ------------------------------------------------------------------
            ' 4. Get entity list
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(10, "Resolving entities...")
            Dim entities As List(Of String) = BRApi.Finance.Entity.GetBaseEntities(si)
            BRApi.ErrorLog.LogMessage(si, $"EX_RollingForecastSeeder: {entities.Count} base entities to seed.")

            Dim totalWork As Integer = entities.Count * rfPeriods.Count
            Dim completedWork As Integer = 0

            ' ------------------------------------------------------------------
            ' 5. Copy Actual data to RF scenario for closed periods
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(15, "Copying actuals for closed periods...")

            For Each period As String In closedPeriods
                For Each entity As String In entities
                    Try
                        BRApi.Finance.Data.CopyScenarioData(si, "Actual", rfScenario, period, entity)

                        seedingLog.Add(New SeedingLogEntry With {
                            .Entity = entity,
                            .Period = period,
                            .Category = "Actuals",
                            .SourceDescription = "Copied from Actual scenario",
                            .SeededValue = 0  ' Bulk copy, individual values not tracked here
                        })
                    Catch ex As Exception
                        BRApi.ErrorLog.LogMessage(si, $"EX_RollingForecastSeeder: WARNING - Failed to copy actuals for [{entity}] [{period}]: {ex.Message}")
                    End Try

                    completedWork += 1
                Next

                Dim pct As Integer = CInt(15 + (30.0 * completedWork / totalWork))
                api.Progress.ReportProgress(pct, $"Copied actuals: {period}...")
            Next

            ' ------------------------------------------------------------------
            ' 6. Seed open periods with category-specific logic
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(45, "Seeding open forecast periods...")

            For Each period As String In openPeriods
                Dim priorYearPeriod As String = GetPriorYearPeriod(si, period)

                For Each entity As String In entities
                    Try
                        ' --- Revenue: Prior Year Actual * (1 + growth rate) ---
                        Dim pyRevenue As Decimal = GetAccountBalance(si, "Actual", priorYearPeriod, entity, "Revenue")
                        Dim seededRevenue As Decimal = pyRevenue * (1D + baseGrowthRate)
                        WriteSeededValue(si, rfScenario, period, entity, "Revenue", seededRevenue)
                        seedingLog.Add(New SeedingLogEntry With {
                            .Entity = entity, .Account = "Revenue", .Period = period,
                            .SeededValue = seededRevenue, .Category = "Revenue",
                            .SourceDescription = $"PY Actual ({pyRevenue:N2}) * (1 + {baseGrowthRate:P1})"
                        })

                        ' --- COGS: Seeded Revenue * Prior Year COGS Ratio ---
                        Dim pyCOGS As Decimal = GetAccountBalance(si, "Actual", priorYearPeriod, entity, "COGS")
                        Dim cogsRatio As Decimal = If(pyRevenue <> 0, pyCOGS / pyRevenue, 0D)
                        Dim seededCOGS As Decimal = seededRevenue * cogsRatio
                        WriteSeededValue(si, rfScenario, period, entity, "COGS", seededCOGS)
                        seedingLog.Add(New SeedingLogEntry With {
                            .Entity = entity, .Account = "COGS", .Period = period,
                            .SeededValue = seededCOGS, .Category = "COGS",
                            .SourceDescription = $"Revenue ({seededRevenue:N2}) * PY COGS Ratio ({cogsRatio:P2})"
                        })

                        ' --- OPEX: Prior Year Actual * (1 + inflation factor) ---
                        Dim pyOPEX As Decimal = GetAccountBalance(si, "Actual", priorYearPeriod, entity, "OPEX")
                        Dim seededOPEX As Decimal = pyOPEX * (1D + inflationFactor)
                        WriteSeededValue(si, rfScenario, period, entity, "OPEX", seededOPEX)
                        seedingLog.Add(New SeedingLogEntry With {
                            .Entity = entity, .Account = "OPEX", .Period = period,
                            .SeededValue = seededOPEX, .Category = "OPEX",
                            .SourceDescription = $"PY Actual ({pyOPEX:N2}) * (1 + {inflationFactor:P1})"
                        })

                        ' --- Headcount: Current headcount + planned changes ---
                        Dim currentHC As Decimal = GetAccountBalance(si, "Actual", currentPeriod, entity, "Headcount")
                        Dim plannedChanges As Decimal = GetPlannedHeadcountChanges(si, entity, period)
                        Dim seededHC As Decimal = currentHC + plannedChanges
                        WriteSeededValue(si, rfScenario, period, entity, "Headcount", seededHC)
                        seedingLog.Add(New SeedingLogEntry With {
                            .Entity = entity, .Account = "Headcount", .Period = period,
                            .SeededValue = seededHC, .Category = "Headcount",
                            .SourceDescription = $"Current ({currentHC:N0}) + Planned ({plannedChanges:N0})"
                        })

                        ' --- CAPEX: From approved project schedules ---
                        Dim approvedCAPEX As Decimal = GetApprovedCAPEX(si, entity, period)
                        WriteSeededValue(si, rfScenario, period, entity, "CAPEX", approvedCAPEX)
                        seedingLog.Add(New SeedingLogEntry With {
                            .Entity = entity, .Account = "CAPEX", .Period = period,
                            .SeededValue = approvedCAPEX, .Category = "CAPEX",
                            .SourceDescription = $"Approved project schedule ({approvedCAPEX:N2})"
                        })

                    Catch ex As Exception
                        BRApi.ErrorLog.LogMessage(si, $"EX_RollingForecastSeeder: WARNING - Seeding failed for [{entity}] [{period}]: {ex.Message}")
                    End Try

                    completedWork += 1
                Next

                Dim pctOpen As Integer = CInt(45 + (40.0 * (completedWork - (closedPeriods.Count * entities.Count)) / (openPeriods.Count * entities.Count)))
                api.Progress.ReportProgress(Math.Min(pctOpen, 85), $"Seeding period: {period}...")
            Next

            ' ------------------------------------------------------------------
            ' 7. Apply what-if overrides if specified
            ' ------------------------------------------------------------------
            If Not String.IsNullOrEmpty(whatIfTag) Then
                api.Progress.ReportProgress(87, $"Applying what-if overrides [{whatIfTag}]...")
                ApplyWhatIfOverrides(si, rfScenario, whatIfTag, openPeriods, entities)
                BRApi.ErrorLog.LogMessage(si, $"EX_RollingForecastSeeder: What-if overrides applied for tag [{whatIfTag}].")
            End If

            ' ------------------------------------------------------------------
            ' 8. Generate seeding log
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(92, "Generating seeding log...")
            Dim logReport As String = GenerateSeedingLog(si, seedingLog, rfScenario, baseGrowthRate, inflationFactor, whatIfTag, seedStart)
            BRApi.ErrorLog.LogMessage(si, logReport)

            api.Progress.ReportProgress(100, "Rolling forecast seeding complete.")
            BRApi.ErrorLog.LogMessage(si, "EX_RollingForecastSeeder: Process completed.")
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GetPriorYearPeriod
        ' Returns the same month from the prior year (e.g., 2024M6 -> 2023M6).
        '----------------------------------------------------------------------------------------------------
        Private Function GetPriorYearPeriod(ByVal si As SessionInfo, ByVal period As String) As String
            Return BRApi.Finance.Time.GetPriorYearPeriod(si, period)
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetAccountBalance
        ' Reads a single account balance for the given dimension intersection.
        '----------------------------------------------------------------------------------------------------
        Private Function GetAccountBalance(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String, ByVal entity As String, ByVal account As String) As Decimal
            Dim value As Decimal = BRApi.Finance.Data.GetDataCellValue(si, "Working", scenario, period, entity, account)
            Return value
        End Function

        '----------------------------------------------------------------------------------------------------
        ' WriteSeededValue
        ' Writes a seeded value to the RF scenario in the target cube.
        '----------------------------------------------------------------------------------------------------
        Private Sub WriteSeededValue(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String, ByVal entity As String, ByVal account As String, ByVal value As Decimal)
            BRApi.Finance.Data.SetDataCellValue(si, "Working", scenario, period, entity, account, "Input", value)
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GetPlannedHeadcountChanges
        ' Reads planned headcount changes (hires/terminations) from HR planning data.
        '----------------------------------------------------------------------------------------------------
        Private Function GetPlannedHeadcountChanges(ByVal si As SessionInfo, ByVal entity As String, ByVal period As String) As Decimal
            Dim sql As String = $"SELECT ISNULL(SUM(PlannedChange), 0) FROM [Planning].[dbo].[HeadcountPlan] " &
                                $"WHERE EntityName = '{entity}' AND EffectivePeriod = '{period}' AND ApprovalStatus = 'Approved'"
            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing AndAlso dt.Rows.Count > 0 AndAlso Not IsDBNull(dt.Rows(0)(0)) Then
                Return Convert.ToDecimal(dt.Rows(0)(0))
            End If
            Return 0D
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetApprovedCAPEX
        ' Reads approved CAPEX from project schedules for the entity and period.
        '----------------------------------------------------------------------------------------------------
        Private Function GetApprovedCAPEX(ByVal si As SessionInfo, ByVal entity As String, ByVal period As String) As Decimal
            Dim sql As String = $"SELECT ISNULL(SUM(ScheduledAmount), 0) FROM [Planning].[dbo].[CAPEXSchedule] " &
                                $"WHERE EntityName = '{entity}' AND ScheduledPeriod = '{period}' AND ProjectStatus = 'Approved'"
            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing AndAlso dt.Rows.Count > 0 AndAlso Not IsDBNull(dt.Rows(0)(0)) Then
                Return Convert.ToDecimal(dt.Rows(0)(0))
            End If
            Return 0D
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ApplyWhatIfOverrides
        ' Applies alternate assumptions from a what-if configuration table.
        '----------------------------------------------------------------------------------------------------
        Private Sub ApplyWhatIfOverrides(ByVal si As SessionInfo, ByVal scenario As String, ByVal whatIfTag As String, ByVal periods As List(Of String), ByVal entities As List(Of String))
            ' Load overrides for this what-if tag
            Dim sql As String = $"SELECT EntityName, AccountName, TimePeriod, OverrideValue " &
                                $"FROM [Planning].[dbo].[WhatIfOverrides] " &
                                $"WHERE WhatIfTag = '{whatIfTag}' AND IsActive = 1"

            Dim dt As DataTable = BRApi.Database.ExecuteSql(si, sql, True)
            If dt IsNot Nothing Then
                For Each row As DataRow In dt.Rows
                    Dim entity As String = row("EntityName").ToString()
                    Dim account As String = row("AccountName").ToString()
                    Dim period As String = row("TimePeriod").ToString()
                    Dim overrideValue As Decimal = Convert.ToDecimal(row("OverrideValue"))

                    If periods.Contains(period) AndAlso entities.Contains(entity) Then
                        WriteSeededValue(si, scenario, period, entity, account, overrideValue)
                        BRApi.ErrorLog.LogMessage(si, $"EX_RollingForecastSeeder: WhatIf override [{whatIfTag}]: [{entity}] [{account}] [{period}] = {overrideValue:N2}")
                    End If
                Next
            End If
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GenerateSeedingLog
        ' Produces an audit report showing the source of each seeded data point.
        '----------------------------------------------------------------------------------------------------
        Private Function GenerateSeedingLog(ByVal si As SessionInfo, ByVal logEntries As List(Of SeedingLogEntry), ByVal scenario As String, ByVal growthRate As Decimal, ByVal inflationFactor As Decimal, ByVal whatIfTag As String, ByVal startTime As DateTime) As String
            Dim elapsed As Double = (DateTime.UtcNow - startTime).TotalSeconds

            Dim report As New Text.StringBuilder()
            report.AppendLine("========================================================================")
            report.AppendLine("          ROLLING FORECAST SEEDING LOG")
            report.AppendLine("========================================================================")
            report.AppendLine($"RF Scenario:        {scenario}")
            report.AppendLine($"Growth Rate:        {growthRate:P2}")
            report.AppendLine($"Inflation Factor:   {inflationFactor:P2}")
            report.AppendLine($"What-If Tag:        {If(String.IsNullOrEmpty(whatIfTag), "Base (none)", whatIfTag)}")
            report.AppendLine($"Run Date (UTC):     {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}")
            report.AppendLine($"Elapsed Time (s):   {elapsed:F1}")
            report.AppendLine($"Total Log Entries:  {logEntries.Count}")
            report.AppendLine()

            ' Summary by category
            report.AppendLine("--- Summary by Category ---")
            Dim categories = logEntries.GroupBy(Function(e) e.Category)
            For Each cat In categories
                report.AppendLine($"  {cat.Key.PadRight(15)} {cat.Count()} entries")
            Next
            report.AppendLine()

            ' Summary by period
            report.AppendLine("--- Summary by Period ---")
            Dim periodGroups = logEntries.GroupBy(Function(e) e.Period).OrderBy(Function(g) g.Key)
            For Each pg In periodGroups
                report.AppendLine($"  {pg.Key.PadRight(15)} {pg.Count()} entries")
            Next
            report.AppendLine()

            ' Sample detail (first 50 entries)
            report.AppendLine("--- Sample Seeding Detail (first 50) ---")
            report.AppendLine(String.Format("{0,-15} {1,-12} {2,-12} {3,15} {4}",
                "Entity", "Account", "Period", "Value", "Source"))
            report.AppendLine(New String("-"c, 100))

            Dim sampleCount As Integer = Math.Min(50, logEntries.Count)
            For i As Integer = 0 To sampleCount - 1
                Dim entry As SeedingLogEntry = logEntries(i)
                report.AppendLine(String.Format("{0,-15} {1,-12} {2,-12} {3,15:N2} {4}",
                    entry.Entity, entry.Account, entry.Period, entry.SeededValue, entry.SourceDescription))
            Next

            If logEntries.Count > 50 Then
                report.AppendLine($"  ... and {logEntries.Count - 50} more entries")
            End If

            report.AppendLine("========================================================================")
            Return report.ToString()
        End Function

    End Class
End Namespace

'------------------------------------------------------------------------------------------------------------
' EX_BatchConsolidation.vb
' OneStream XF Extender Business Rule
'
' Purpose:  Batch consolidation orchestrator that processes entities bottom-up through
'           the hierarchy. Supports parallel branch processing, currency translation,
'           intercompany elimination, and comprehensive progress/error reporting.
'
' Parameters (pipe-delimited):
'   Scenario     - Scenario name (e.g., "Actual", "Budget")
'   TimePeriods  - Comma-separated period range (e.g., "2024M1,2024M3" for Jan-Mar)
'   EntityScope  - "All", region name (e.g., "NorthAmerica"), or specific entity (e.g., "E001")
'
' Usage:     Called from a Data Management step or scheduled job.
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

Namespace OneStream.BusinessRule.Extender.EX_BatchConsolidation
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As ExtenderArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = ExtenderFunctionType.ExecuteServerProcess
                        ' Parse parameters from the args
                        Dim paramString As String = args.NameValuePairs.XFGetValue("Parameters", String.Empty)
                        Me.ExecuteBatchConsolidation(si, globals, api, paramString)
                        Return Nothing

                    Case Else
                        Throw New XFException(si, $"EX_BatchConsolidation: Unsupported function type [{args.FunctionType}].")
                End Select
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExecuteBatchConsolidation
        ' Main orchestration method: parses parameters, resolves entities, and runs consolidation.
        '----------------------------------------------------------------------------------------------------
        Private Sub ExecuteBatchConsolidation(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal paramString As String)
            Dim overallStart As DateTime = DateTime.UtcNow
            Dim processedCount As Integer = 0
            Dim successCount As Integer = 0
            Dim failCount As Integer = 0
            Dim skippedEntities As New List(Of String)
            Dim entityTimings As New Dictionary(Of String, Double)

            ' ------------------------------------------------------------------
            ' 1. Parse parameters
            ' ------------------------------------------------------------------
            Dim parameters() As String = paramString.Split("|"c)
            If parameters.Length < 3 Then
                Throw New XFException(si, "EX_BatchConsolidation: Expected 3 pipe-delimited parameters (Scenario|TimePeriods|EntityScope).")
            End If

            Dim scenarioName As String = parameters(0).Trim()
            Dim timePeriodRange As String = parameters(1).Trim()
            Dim entityScope As String = parameters(2).Trim()

            BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: Starting. Scenario=[{scenarioName}], Periods=[{timePeriodRange}], Scope=[{entityScope}]")
            api.Progress.ReportProgress(0, "Initializing batch consolidation...")

            ' ------------------------------------------------------------------
            ' 2. Resolve time periods
            ' ------------------------------------------------------------------
            Dim timePeriods As List(Of String) = ResolveTimePeriods(si, timePeriodRange)
            If timePeriods.Count = 0 Then
                Throw New XFException(si, "EX_BatchConsolidation: No valid time periods resolved from input.")
            End If

            BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: Resolved {timePeriods.Count} time period(s).")

            ' ------------------------------------------------------------------
            ' 3. Resolve entity hierarchy bottom-up
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(5, "Resolving entity hierarchy...")
            Dim entityList As List(Of String) = ResolveEntitiesBottomUp(si, entityScope)
            Dim totalEntities As Integer = entityList.Count

            If totalEntities = 0 Then
                Throw New XFException(si, $"EX_BatchConsolidation: No entities resolved for scope [{entityScope}].")
            End If

            BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: {totalEntities} entities resolved for processing (bottom-up order).")

            ' ------------------------------------------------------------------
            ' 4. Identify independent branches for parallel processing
            ' ------------------------------------------------------------------
            Dim branches As List(Of List(Of String)) = IdentifyIndependentBranches(si, entityList)
            BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: Identified {branches.Count} independent branch(es).")

            ' ------------------------------------------------------------------
            ' 5. Process each time period
            ' ------------------------------------------------------------------
            For periodIdx As Integer = 0 To timePeriods.Count - 1
                Dim currentPeriod As String = timePeriods(periodIdx)
                BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: === Processing period [{currentPeriod}] ({periodIdx + 1}/{timePeriods.Count}) ===")

                ' Process independent branches (parallel-eligible entities within each branch)
                For branchIdx As Integer = 0 To branches.Count - 1
                    Dim branch As List(Of String) = branches(branchIdx)

                    For entityIdx As Integer = 0 To branch.Count - 1
                        Dim entityName As String = branch(entityIdx)
                        Dim entityStart As DateTime = DateTime.UtcNow

                        ' Calculate overall progress percentage
                        Dim progressPct As Integer = CInt(10 + (85.0 * processedCount / (totalEntities * timePeriods.Count)))
                        api.Progress.ReportProgress(progressPct, $"Period [{currentPeriod}]: Processing entity [{entityName}] ({processedCount + 1}/{totalEntities})...")

                        Try
                            ' Step A: Run calculations for entity
                            RunEntityCalculations(si, scenarioName, currentPeriod, entityName)

                            ' Step B: Currency translation
                            RunCurrencyTranslation(si, scenarioName, currentPeriod, entityName)

                            ' Step C: Intercompany elimination (only for parent entities)
                            If IsParentEntity(si, entityName) Then
                                RunICElimination(si, scenarioName, currentPeriod, entityName)
                            End If

                            ' Step D: Consolidation roll-up
                            RunConsolidation(si, scenarioName, currentPeriod, entityName)

                            ' Record success
                            successCount += 1
                            Dim elapsed As Double = (DateTime.UtcNow - entityStart).TotalSeconds
                            entityTimings(entityName & "|" & currentPeriod) = elapsed

                            BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: Entity [{entityName}] period [{currentPeriod}] completed in {elapsed:F1}s.")

                        Catch entityEx As Exception
                            ' Log the error but continue processing other entities
                            failCount += 1
                            skippedEntities.Add($"{entityName}|{currentPeriod}")
                            BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: ERROR on entity [{entityName}] period [{currentPeriod}]: {entityEx.Message}")
                        End Try

                        processedCount += 1
                    Next
                Next
            Next

            ' ------------------------------------------------------------------
            ' 6. Generate summary report
            ' ------------------------------------------------------------------
            api.Progress.ReportProgress(95, "Generating summary report...")
            Dim totalElapsed As Double = (DateTime.UtcNow - overallStart).TotalSeconds

            Dim summary As New Text.StringBuilder()
            summary.AppendLine("=== BATCH CONSOLIDATION SUMMARY ===")
            summary.AppendLine($"Scenario:           {scenarioName}")
            summary.AppendLine($"Periods:            {timePeriodRange}")
            summary.AppendLine($"Entity Scope:       {entityScope}")
            summary.AppendLine($"Total Processed:    {processedCount}")
            summary.AppendLine($"Succeeded:          {successCount}")
            summary.AppendLine($"Failed:             {failCount}")
            summary.AppendLine($"Total Elapsed (s):  {totalElapsed:F1}")
            summary.AppendLine($"Avg per Entity (s): {If(processedCount > 0, (totalElapsed / processedCount).ToString("F2"), "N/A")}")

            If skippedEntities.Count > 0 Then
                summary.AppendLine()
                summary.AppendLine("--- Failed Entities ---")
                For Each s As String In skippedEntities
                    summary.AppendLine($"  {s}")
                Next
            End If

            ' Log the summary
            BRApi.ErrorLog.LogMessage(si, summary.ToString())
            api.Progress.ReportProgress(100, "Batch consolidation complete.")
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ResolveTimePeriods
        ' Expands a comma-separated range like "2024M1,2024M3" into individual period names.
        '----------------------------------------------------------------------------------------------------
        Private Function ResolveTimePeriods(ByVal si As SessionInfo, ByVal rangeStr As String) As List(Of String)
            Dim periods As New List(Of String)
            Dim parts() As String = rangeStr.Split(","c)

            If parts.Length = 1 Then
                ' Single period
                periods.Add(parts(0).Trim())
            ElseIf parts.Length = 2 Then
                ' Range: resolve start to end using the Time dimension
                Dim startPeriod As String = parts(0).Trim()
                Dim endPeriod As String = parts(1).Trim()

                ' Use BRApi to enumerate periods between start and end
                Dim allPeriods As List(Of String) = BRApi.Finance.Time.GetPeriodsInRange(si, startPeriod, endPeriod)
                If allPeriods IsNot Nothing AndAlso allPeriods.Count > 0 Then
                    periods.AddRange(allPeriods)
                Else
                    ' Fallback: add both endpoints
                    periods.Add(startPeriod)
                    periods.Add(endPeriod)
                End If
            Else
                ' Treat each entry as an individual period
                For Each p As String In parts
                    Dim trimmed As String = p.Trim()
                    If Not String.IsNullOrEmpty(trimmed) Then
                        periods.Add(trimmed)
                    End If
                Next
            End If

            Return periods
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ResolveEntitiesBottomUp
        ' Returns entities in bottom-up order (leaves first, root last) based on the scope.
        '----------------------------------------------------------------------------------------------------
        Private Function ResolveEntitiesBottomUp(ByVal si As SessionInfo, ByVal scope As String) As List(Of String)
            Dim entities As New List(Of String)

            If scope.Equals("All", StringComparison.OrdinalIgnoreCase) Then
                ' Get the full entity hierarchy from the root, bottom-up
                Dim rootMember As String = BRApi.Finance.Entity.GetRootEntityName(si)
                entities = BRApi.Finance.Entity.GetDescendantsBottomUp(si, rootMember, True)
            Else
                ' Try as a parent (region) first, then as a specific entity
                Dim descendants As List(Of String) = BRApi.Finance.Entity.GetDescendantsBottomUp(si, scope, True)
                If descendants IsNot Nothing AndAlso descendants.Count > 0 Then
                    entities = descendants
                Else
                    ' Single specific entity
                    entities.Add(scope)
                End If
            End If

            Return entities
        End Function

        '----------------------------------------------------------------------------------------------------
        ' IdentifyIndependentBranches
        ' Groups entities into independent branches that can be processed in parallel.
        '----------------------------------------------------------------------------------------------------
        Private Function IdentifyIndependentBranches(ByVal si As SessionInfo, ByVal entities As List(Of String)) As List(Of List(Of String))
            Dim branches As New List(Of List(Of String))

            ' Get top-level children under root to define branches
            Dim rootEntity As String = BRApi.Finance.Entity.GetRootEntityName(si)
            Dim topChildren As List(Of String) = BRApi.Finance.Entity.GetChildren(si, rootEntity)

            If topChildren IsNot Nothing AndAlso topChildren.Count > 1 Then
                For Each topChild As String In topChildren
                    Dim branchEntities As New List(Of String)
                    Dim topChildDescendants As List(Of String) = BRApi.Finance.Entity.GetDescendantsBottomUp(si, topChild, True)
                    ' Filter to only include entities that are in our processing list
                    For Each descendant As String In topChildDescendants
                        If entities.Contains(descendant) Then
                            branchEntities.Add(descendant)
                        End If
                    Next
                    If branchEntities.Count > 0 Then
                        branches.Add(branchEntities)
                    End If
                Next
                ' Add root entity as its own branch (processed last)
                If entities.Contains(rootEntity) Then
                    branches.Add(New List(Of String) From {rootEntity})
                End If
            Else
                ' Cannot split into branches; process sequentially
                branches.Add(entities)
            End If

            Return branches
        End Function

        '----------------------------------------------------------------------------------------------------
        ' RunEntityCalculations
        ' Executes calculation rules for a given entity/period combination.
        '----------------------------------------------------------------------------------------------------
        Private Sub RunEntityCalculations(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String, ByVal entity As String)
            BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: Running calculations for [{entity}] [{period}].")
            BRApi.Finance.Calculate.ExecuteCalculations(si, scenario, period, entity)
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' RunCurrencyTranslation
        ' Translates entity data from local currency to reporting currency.
        '----------------------------------------------------------------------------------------------------
        Private Sub RunCurrencyTranslation(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String, ByVal entity As String)
            BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: Running currency translation for [{entity}] [{period}].")
            BRApi.Finance.Consolidation.Translate(si, scenario, period, entity)
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' RunICElimination
        ' Performs intercompany elimination for parent entities.
        '----------------------------------------------------------------------------------------------------
        Private Sub RunICElimination(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String, ByVal entity As String)
            BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: Running IC elimination for [{entity}] [{period}].")
            BRApi.Finance.Consolidation.EliminateIntercompany(si, scenario, period, entity)
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' RunConsolidation
        ' Executes the consolidation roll-up for the entity.
        '----------------------------------------------------------------------------------------------------
        Private Sub RunConsolidation(ByVal si As SessionInfo, ByVal scenario As String, ByVal period As String, ByVal entity As String)
            BRApi.ErrorLog.LogMessage(si, $"EX_BatchConsolidation: Running consolidation for [{entity}] [{period}].")
            BRApi.Finance.Consolidation.Consolidate(si, scenario, period, entity)
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' IsParentEntity
        ' Returns True if the entity has children (i.e., is a parent/roll-up node).
        '----------------------------------------------------------------------------------------------------
        Private Function IsParentEntity(ByVal si As SessionInfo, ByVal entity As String) As Boolean
            Dim children As List(Of String) = BRApi.Finance.Entity.GetChildren(si, entity)
            Return children IsNot Nothing AndAlso children.Count > 0
        End Function

    End Class
End Namespace

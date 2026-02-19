'------------------------------------------------------------------------------------------------------------
' DDA_PlantPerformance
' Dashboard DataAdapter Business Rule
' Purpose: Plant-level operational dashboard with OEE, throughput, scrap, capacity, and cost metrics
'------------------------------------------------------------------------------------------------------------
Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.Linq
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_PlantPerformance
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("PlantPerformance")
                        dt.Columns.Add("PlantName", GetType(String))
                        dt.Columns.Add("Region", GetType(String))
                        dt.Columns.Add("OEE", GetType(Double))
                        dt.Columns.Add("Throughput", GetType(Double))
                        dt.Columns.Add("ScrapRate", GetType(Double))
                        dt.Columns.Add("CapacityUtil", GetType(Double))
                        dt.Columns.Add("CostPerUnit", GetType(Double))
                        dt.Columns.Add("TargetOEE", GetType(Double))
                        dt.Columns.Add("TargetThroughput", GetType(Double))
                        dt.Columns.Add("Rank", GetType(Integer))

                        ' Retrieve parameters from dashboard context
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim targetScenario As String = args.NameValuePairs.XFGetValue("TargetScenario", "Budget")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim parentEntity As String = args.NameValuePairs.XFGetValue("ParentEntity", "AllPlants")

                        ' Define plant entities with their region mapping
                        Dim plantDefinitions As New List(Of Tuple(Of String, String))()
                        plantDefinitions.Add(Tuple.Create("Plant_Detroit", "North America"))
                        plantDefinitions.Add(Tuple.Create("Plant_Houston", "North America"))
                        plantDefinitions.Add(Tuple.Create("Plant_Munich", "Europe"))
                        plantDefinitions.Add(Tuple.Create("Plant_Shanghai", "Asia Pacific"))
                        plantDefinitions.Add(Tuple.Create("Plant_Sao_Paulo", "Latin America"))
                        plantDefinitions.Add(Tuple.Create("Plant_Chennai", "Asia Pacific"))
                        plantDefinitions.Add(Tuple.Create("Plant_Manchester", "Europe"))
                        plantDefinitions.Add(Tuple.Create("Plant_Monterrey", "Latin America"))

                        ' Define operational metric account members
                        Dim acctOEE As String = "A#OEE_Pct"
                        Dim acctThroughput As String = "A#Throughput_Units"
                        Dim acctScrapRate As String = "A#Scrap_Rate_Pct"
                        Dim acctCapUtil As String = "A#Capacity_Utilization_Pct"
                        Dim acctCostPerUnit As String = "A#Cost_Per_Unit"

                        ' Collect performance data for ranking
                        Dim plantScores As New List(Of Tuple(Of String, Double))()

                        For Each plantDef In plantDefinitions
                            Dim plantName As String = plantDef.Item1
                            Dim region As String = plantDef.Item2

                            ' Fetch actual metrics for the plant
                            Dim oeeValue As Double = FetchMetric(si, scenarioName, timeName, plantName, acctOEE)
                            Dim throughputValue As Double = FetchMetric(si, scenarioName, timeName, plantName, acctThroughput)
                            Dim scrapRate As Double = FetchMetric(si, scenarioName, timeName, plantName, acctScrapRate)
                            Dim capUtil As Double = FetchMetric(si, scenarioName, timeName, plantName, acctCapUtil)
                            Dim costPerUnit As Double = FetchMetric(si, scenarioName, timeName, plantName, acctCostPerUnit)

                            ' Fetch target metrics for comparison
                            Dim targetOEE As Double = FetchMetric(si, targetScenario, timeName, plantName, acctOEE)
                            Dim targetThroughput As Double = FetchMetric(si, targetScenario, timeName, plantName, acctThroughput)

                            ' Calculate composite score for ranking (weighted: OEE 40%, throughput attainment 30%, inverse scrap 30%)
                            Dim throughputAttainment As Double = 0
                            If targetThroughput > 0 Then throughputAttainment = throughputValue / targetThroughput
                            Dim compositeScore As Double = (oeeValue * 0.4) + (throughputAttainment * 0.3) + ((1 - scrapRate) * 0.3)
                            plantScores.Add(Tuple.Create(plantName, compositeScore))

                            Dim row As DataRow = dt.NewRow()
                            row("PlantName") = plantName.Replace("_", " ")
                            row("Region") = region
                            row("OEE") = Math.Round(oeeValue, 4)
                            row("Throughput") = Math.Round(throughputValue, 0)
                            row("ScrapRate") = Math.Round(scrapRate, 4)
                            row("CapacityUtil") = Math.Round(capUtil, 4)
                            row("CostPerUnit") = Math.Round(costPerUnit, 2)
                            row("TargetOEE") = Math.Round(targetOEE, 4)
                            row("TargetThroughput") = Math.Round(targetThroughput, 0)
                            row("Rank") = 0 ' Placeholder, set after sorting
                            dt.Rows.Add(row)
                        Next

                        ' Sort by composite score descending and assign rank
                        Dim rankedPlants = plantScores.OrderByDescending(Function(p) p.Item2).ToList()
                        For Each row As DataRow In dt.Rows
                            Dim rawName As String = row("PlantName").ToString().Replace(" ", "_")
                            Dim rankIndex As Integer = rankedPlants.FindIndex(Function(p) p.Item1 = rawName)
                            row("Rank") = rankIndex + 1
                        Next

                        ' Sort the DataTable by Rank ascending
                        dt.DefaultView.Sort = "Rank ASC"
                        Dim sortedDt As DataTable = dt.DefaultView.ToTable()

                        Return sortedDt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        Private Function FetchMetric(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
                ' Return zero on retrieval failure
            End Try
            Return 0
        End Function

    End Class
End Namespace

'------------------------------------------------------------------------------------------------------------
' DDA_ProductionVariance
' Dashboard DataAdapter Business Rule
' Purpose: Production variance waterfall showing actual vs standard cost variances by category
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_ProductionVariance
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("ProductionVariance")
                        dt.Columns.Add("Category", GetType(String))
                        dt.Columns.Add("Value", GetType(Double))
                        dt.Columns.Add("RunningTotal", GetType(Double))
                        dt.Columns.Add("IsPositive", GetType(Boolean))
                        dt.Columns.Add("ProductGroup", GetType(String))
                        dt.Columns.Add("PlantName", GetType(String))

                        ' Retrieve dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim standardScenario As String = args.NameValuePairs.XFGetValue("StandardScenario", "Budget")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", "Corporate")
                        Dim productGroup As String = args.NameValuePairs.XFGetValue("ProductGroup", "AllProducts")
                        Dim plantFilter As String = args.NameValuePairs.XFGetValue("Plant", "AllPlants")

                        ' Define variance categories with corresponding account members
                        Dim varianceCategories As New List(Of Tuple(Of String, String))()
                        varianceCategories.Add(Tuple.Create("Standard Cost", "A#StandardCost_Total"))
                        varianceCategories.Add(Tuple.Create("Material Price Variance", "A#MaterialPriceVariance"))
                        varianceCategories.Add(Tuple.Create("Material Usage Variance", "A#MaterialUsageVariance"))
                        varianceCategories.Add(Tuple.Create("Labor Rate Variance", "A#LaborRateVariance"))
                        varianceCategories.Add(Tuple.Create("Labor Efficiency Variance", "A#LaborEfficiencyVariance"))
                        varianceCategories.Add(Tuple.Create("OH Spending Variance", "A#OHSpendingVariance"))
                        varianceCategories.Add(Tuple.Create("OH Volume Variance", "A#OHVolumeVariance"))

                        ' Build the waterfall data starting from standard cost
                        Dim runningTotal As Double = 0
                        Dim isFirst As Boolean = True

                        For Each category In varianceCategories
                            Dim categoryName As String = category.Item1
                            Dim accountMember As String = category.Item2

                            Dim value As Double = FetchVarianceAmount(si, scenarioName, timeName, entityName, accountMember)

                            If isFirst Then
                                ' First row is the standard cost baseline
                                runningTotal = value
                                Dim baseRow As DataRow = dt.NewRow()
                                baseRow("Category") = categoryName
                                baseRow("Value") = Math.Round(value, 2)
                                baseRow("RunningTotal") = Math.Round(runningTotal, 2)
                                baseRow("IsPositive") = True
                                baseRow("ProductGroup") = productGroup
                                baseRow("PlantName") = plantFilter
                                dt.Rows.Add(baseRow)
                                isFirst = False
                            Else
                                ' Subsequent rows are variance increments
                                runningTotal += value
                                Dim varianceRow As DataRow = dt.NewRow()
                                varianceRow("Category") = categoryName
                                varianceRow("Value") = Math.Round(value, 2)
                                varianceRow("RunningTotal") = Math.Round(runningTotal, 2)
                                varianceRow("IsPositive") = (value >= 0)
                                varianceRow("ProductGroup") = productGroup
                                varianceRow("PlantName") = plantFilter
                                dt.Rows.Add(varianceRow)
                            End If
                        Next

                        ' Add actual cost total row as the waterfall endpoint
                        Dim actualCost As Double = FetchVarianceAmount(si, scenarioName, timeName, entityName, "A#ActualCost_Total")
                        Dim totalRow As DataRow = dt.NewRow()
                        totalRow("Category") = "Actual Cost"
                        totalRow("Value") = Math.Round(actualCost, 2)
                        totalRow("RunningTotal") = Math.Round(actualCost, 2)
                        totalRow("IsPositive") = True
                        totalRow("ProductGroup") = productGroup
                        totalRow("PlantName") = plantFilter
                        dt.Rows.Add(totalRow)

                        ' If a specific product group is selected, add drill-down detail rows
                        If productGroup <> "AllProducts" Then
                            AppendProductDrillDown(si, dt, scenarioName, timeName, entityName, productGroup, plantFilter)
                        End If

                        Return dt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        Private Function FetchVarianceAmount(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
                ' Silently return zero on failure
            End Try
            Return 0
        End Function

        Private Sub AppendProductDrillDown(ByVal si As SessionInfo, ByVal dt As DataTable, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal productGroup As String, ByVal plantFilter As String)
            ' Add product-level detail rows for drill-down capability
            Dim detailAccounts As New List(Of String)()
            detailAccounts.Add("A#MaterialPriceVariance")
            detailAccounts.Add("A#MaterialUsageVariance")
            detailAccounts.Add("A#LaborRateVariance")
            detailAccounts.Add("A#LaborEfficiencyVariance")

            For Each acct In detailAccounts
                Dim detailPov As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#{4}:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, acct, productGroup)
                Try
                    Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, detailPov)
                    If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                        Dim detailRow As DataRow = dt.NewRow()
                        detailRow("Category") = acct.Replace("A#", "") & " (Detail)"
                        detailRow("Value") = Math.Round(dataCell.CellAmount, 2)
                        detailRow("RunningTotal") = 0
                        detailRow("IsPositive") = (dataCell.CellAmount >= 0)
                        detailRow("ProductGroup") = productGroup
                        detailRow("PlantName") = plantFilter
                        dt.Rows.Add(detailRow)
                    End If
                Catch
                    ' Skip detail rows that fail to retrieve
                End Try
            Next
        End Sub

    End Class
End Namespace

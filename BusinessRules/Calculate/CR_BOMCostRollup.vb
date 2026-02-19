'------------------------------------------------------------------------------------------------------------
' CR_BOMCostRollup
' Calculate Business Rule - Bill of Materials Cost Explosion
'
' Purpose:  Reads BOM structure (parent-child relationships with quantities), performs recursive
'           cost rollup from raw materials through sub-assemblies to finished goods, adds labor
'           and overhead per operation/routing step, calculates total manufactured cost per
'           finished good, supports standard vs actual BOM versions, and writes unit/total costs.
'
' Scope:    Finance - Calculate
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
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.Finance.CR_BOMCostRollup

    Public Class MainClass

        ''' <summary>
        ''' Represents a single component in a Bill of Materials.
        ''' </summary>
        Private Class BOMComponent
            Public ComponentId As String
            Public ParentId As String
            Public QtyPer As Double               ' Quantity of this component per 1 unit of parent
            Public IsRawMaterial As Boolean        ' True = leaf node, has a purchased cost
            Public UnitMaterialCost As Double      ' Purchased cost for raw materials
            Public LaborCostPerUnit As Double      ' Labor cost per operation at this level
            Public OverheadCostPerUnit As Double   ' Overhead per operation at this level
            Public ScrapFactor As Double           ' e.g., 1.02 = 2% scrap allowance
        End Class

        ''' <summary>
        ''' Holds the calculated rolled-up cost for a BOM item.
        ''' </summary>
        Private Class RolledUpCost
            Public MaterialCost As Double
            Public LaborCost As Double
            Public OverheadCost As Double
            Public TotalCost As Double
        End Class

        ' BOM data structures
        Private _bomComponents As New List(Of BOMComponent)
        Private _childrenMap As New Dictionary(Of String, List(Of BOMComponent))
        Private _costCache As New Dictionary(Of String, RolledUpCost)

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    BRApi.ErrorLog.LogMessage(si, "CR_BOMCostRollup: Starting BOM cost explosion.")

                    '--- Determine BOM version: Standard vs Actual ---
                    Dim scenarioName As String = api.Pov.Scenario.Name
                    Dim bomVersion As String = If(scenarioName.Contains("Budget") OrElse scenarioName.Contains("Forecast"),
                                                  "Standard", "Actual")

                    BRApi.ErrorLog.LogMessage(si, "CR_BOMCostRollup: Using BOM version = " & bomVersion)

                    '--- Define finished goods (top-level parents) ---
                    Dim finishedGoods As New List(Of String) From {
                        "FG_WidgetA", "FG_WidgetB", "FG_WidgetC"
                    }

                    '--- Build BOM structure ---
                    ' In production this would be read from a data source; here we load from cube data
                    LoadBOMStructure(si, api, bomVersion)

                    '--- Process each finished good ---
                    For Each fg As String In finishedGoods
                        ' Clear cache for each top-level rollup
                        _costCache.Clear()

                        ' Perform recursive BOM cost rollup
                        Dim rolledUp As RolledUpCost = RollUpCost(si, api, fg, bomVersion)

                        If rolledUp Is Nothing Then
                            BRApi.ErrorLog.LogMessage(si, "CR_BOMCostRollup: WARNING - No BOM data for " & fg)
                            Continue For
                        End If

                        Dim entity As String = "E#" & fg

                        '--- Read production quantity for total cost calculation ---
                        Dim productionQty As Double = ReadCell(si, api, "A#BOM_ProductionQty", entity)

                        '--- Write unit costs ---
                        WriteCell(si, api, "A#BOM_UnitMaterialCost", entity, rolledUp.MaterialCost)
                        WriteCell(si, api, "A#BOM_UnitLaborCost", entity, rolledUp.LaborCost)
                        WriteCell(si, api, "A#BOM_UnitOverheadCost", entity, rolledUp.OverheadCost)
                        WriteCell(si, api, "A#BOM_UnitTotalCost", entity, rolledUp.TotalCost)

                        '--- Write total costs (unit cost x production quantity) ---
                        WriteCell(si, api, "A#BOM_TotalMaterialCost", entity, Math.Round(rolledUp.MaterialCost * productionQty, 2))
                        WriteCell(si, api, "A#BOM_TotalLaborCost", entity, Math.Round(rolledUp.LaborCost * productionQty, 2))
                        WriteCell(si, api, "A#BOM_TotalOverheadCost", entity, Math.Round(rolledUp.OverheadCost * productionQty, 2))
                        WriteCell(si, api, "A#BOM_TotalManufCost", entity, Math.Round(rolledUp.TotalCost * productionQty, 2))

                        '--- Write BOM version flag ---
                        WriteCell(si, api, "A#BOM_VersionFlag", entity, If(bomVersion = "Standard", 1, 2))

                        BRApi.ErrorLog.LogMessage(si, "CR_BOMCostRollup: " & fg _
                            & " | UnitCost=" & rolledUp.TotalCost.ToString("N4") _
                            & " (Mat=" & rolledUp.MaterialCost.ToString("N4") _
                            & " Lab=" & rolledUp.LaborCost.ToString("N4") _
                            & " OH=" & rolledUp.OverheadCost.ToString("N4") & ")" _
                            & " | Qty=" & productionQty.ToString("N0") _
                            & " | TotalCost=" & (rolledUp.TotalCost * productionQty).ToString("N2"))
                    Next

                    BRApi.ErrorLog.LogMessage(si, "CR_BOMCostRollup: BOM cost explosion completed successfully.")
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFaborException(si, "CR_BOMCostRollup", ex.Message, ex))
            End Try
        End Function

        ''' <summary>
        ''' Loads BOM structure from the cube. Reads parent-child relationships, quantities per,
        ''' and cost data for each component.
        ''' </summary>
        Private Sub LoadBOMStructure(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, ByVal bomVersion As String)
            _bomComponents.Clear()
            _childrenMap.Clear()

            '--- Define BOM components ---
            ' FG_WidgetA -> SA_SubAssemblyX (x2) + RM_Steel (x3.5)
            ' FG_WidgetA -> SA_SubAssemblyY (x1)
            ' SA_SubAssemblyX -> RM_Aluminum (x1.2) + RM_Fasteners (x10)
            ' SA_SubAssemblyY -> RM_Copper (x0.8) + RM_Plastic (x2.0)

            ' FG_WidgetB -> SA_SubAssemblyX (x3) + RM_Steel (x5.0)
            ' FG_WidgetC -> SA_SubAssemblyY (x2) + RM_Aluminum (x4.0) + RM_Fasteners (x20)

            Dim allComponents As New List(Of BOMComponent)

            '--- Raw materials (leaf nodes) ---
            allComponents.Add(CreateComponent("RM_Steel", "", 0, True, si, api, bomVersion))
            allComponents.Add(CreateComponent("RM_Aluminum", "", 0, True, si, api, bomVersion))
            allComponents.Add(CreateComponent("RM_Copper", "", 0, True, si, api, bomVersion))
            allComponents.Add(CreateComponent("RM_Plastic", "", 0, True, si, api, bomVersion))
            allComponents.Add(CreateComponent("RM_Fasteners", "", 0, True, si, api, bomVersion))

            '--- Sub-assembly X children ---
            allComponents.Add(CreateComponent("RM_Aluminum", "SA_SubAssemblyX", 1.2, True, si, api, bomVersion))
            allComponents.Add(CreateComponent("RM_Fasteners", "SA_SubAssemblyX", 10, True, si, api, bomVersion))

            '--- Sub-assembly Y children ---
            allComponents.Add(CreateComponent("RM_Copper", "SA_SubAssemblyY", 0.8, True, si, api, bomVersion))
            allComponents.Add(CreateComponent("RM_Plastic", "SA_SubAssemblyY", 2.0, True, si, api, bomVersion))

            '--- Widget A children ---
            allComponents.Add(CreateComponent("SA_SubAssemblyX", "FG_WidgetA", 2, False, si, api, bomVersion))
            allComponents.Add(CreateComponent("SA_SubAssemblyY", "FG_WidgetA", 1, False, si, api, bomVersion))
            allComponents.Add(CreateComponent("RM_Steel", "FG_WidgetA", 3.5, True, si, api, bomVersion))

            '--- Widget B children ---
            allComponents.Add(CreateComponent("SA_SubAssemblyX", "FG_WidgetB", 3, False, si, api, bomVersion))
            allComponents.Add(CreateComponent("RM_Steel", "FG_WidgetB", 5.0, True, si, api, bomVersion))

            '--- Widget C children ---
            allComponents.Add(CreateComponent("SA_SubAssemblyY", "FG_WidgetC", 2, False, si, api, bomVersion))
            allComponents.Add(CreateComponent("RM_Aluminum", "FG_WidgetC", 4.0, True, si, api, bomVersion))
            allComponents.Add(CreateComponent("RM_Fasteners", "FG_WidgetC", 20, True, si, api, bomVersion))

            _bomComponents = allComponents

            '--- Build parent -> children lookup ---
            For Each comp As BOMComponent In _bomComponents
                If Not String.IsNullOrEmpty(comp.ParentId) Then
                    If Not _childrenMap.ContainsKey(comp.ParentId) Then
                        _childrenMap(comp.ParentId) = New List(Of BOMComponent)
                    End If
                    _childrenMap(comp.ParentId).Add(comp)
                End If
            Next
        End Sub

        ''' <summary>
        ''' Creates a BOM component, reading cost data from the cube.
        ''' </summary>
        Private Function CreateComponent(ByVal compId As String, ByVal parentId As String,
                                         ByVal qtyPer As Double, ByVal isRaw As Boolean,
                                         ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                         ByVal bomVersion As String) As BOMComponent
            Dim comp As New BOMComponent()
            comp.ComponentId = compId
            comp.ParentId = parentId
            comp.QtyPer = qtyPer
            comp.IsRawMaterial = isRaw

            Dim entity As String = "E#" & compId
            Dim versionPrefix As String = If(bomVersion = "Standard", "A#BOM_Std_", "A#BOM_Act_")

            comp.UnitMaterialCost = If(isRaw, ReadCell(si, api, versionPrefix & "MaterialCost", entity), 0)
            comp.LaborCostPerUnit = ReadCell(si, api, versionPrefix & "LaborCost", entity)
            comp.OverheadCostPerUnit = ReadCell(si, api, versionPrefix & "OverheadCost", entity)
            comp.ScrapFactor = ReadCell(si, api, "A#BOM_ScrapFactor", entity)
            If comp.ScrapFactor < 1.0 Then comp.ScrapFactor = 1.0  ' Minimum = no scrap

            Return comp
        End Function

        ''' <summary>
        ''' Recursively rolls up cost from raw materials through sub-assemblies to the specified item.
        ''' Uses memoization via _costCache to avoid redundant calculations.
        ''' </summary>
        Private Function RollUpCost(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                    ByVal itemId As String, ByVal bomVersion As String) As RolledUpCost
            ' Check cache first
            If _costCache.ContainsKey(itemId) Then
                Return _costCache(itemId)
            End If

            Dim result As New RolledUpCost()

            ' Read this item's direct labor and overhead (routing/operation cost at this level)
            Dim entity As String = "E#" & itemId
            Dim versionPrefix As String = If(bomVersion = "Standard", "A#BOM_Std_", "A#BOM_Act_")
            Dim directLabor As Double = ReadCell(si, api, versionPrefix & "LaborCost", entity)
            Dim directOverhead As Double = ReadCell(si, api, versionPrefix & "OverheadCost", entity)

            result.LaborCost = directLabor
            result.OverheadCost = directOverhead

            ' If this item has children, roll them up
            If _childrenMap.ContainsKey(itemId) Then
                For Each child As BOMComponent In _childrenMap(itemId)
                    If child.IsRawMaterial Then
                        ' Raw material: use purchased cost x quantity per x scrap factor
                        result.MaterialCost += Math.Round(child.UnitMaterialCost * child.QtyPer * child.ScrapFactor, 6)
                        result.LaborCost += Math.Round(child.LaborCostPerUnit * child.QtyPer, 6)
                        result.OverheadCost += Math.Round(child.OverheadCostPerUnit * child.QtyPer, 6)
                    Else
                        ' Sub-assembly: recursively roll up its cost
                        Dim childCost As RolledUpCost = RollUpCost(si, api, child.ComponentId, bomVersion)
                        If childCost IsNot Nothing Then
                            result.MaterialCost += Math.Round(childCost.MaterialCost * child.QtyPer * child.ScrapFactor, 6)
                            result.LaborCost += Math.Round(childCost.LaborCost * child.QtyPer, 6)
                            result.OverheadCost += Math.Round(childCost.OverheadCost * child.QtyPer, 6)
                        End If
                    End If
                Next
            End If

            result.TotalCost = Math.Round(result.MaterialCost + result.LaborCost + result.OverheadCost, 4)

            ' Cache the result
            _costCache(itemId) = result
            Return result
        End Function

        Private Function ReadCell(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                                  ByVal acct As String, ByVal entity As String) As Double
            Try
                Dim pov As String = api.Pov.Scenario.Name & ":" & api.Pov.Time.Name & ":" _
                    & entity & ":" & acct & ":F#Periodic:O#Top:I#Top:C1#Top:C2#Top:C3#Top:C4#Top"
                Dim dc As DataCell = BRApi.Finance.Data.GetDataCell(si, pov, False)
                Return dc.CellAmount
            Catch ex As Exception
                Return 0
            End Try
        End Function

        Private Sub WriteCell(ByVal si As SessionInfo, ByVal api As FinanceRulesApi,
                              ByVal acct As String, ByVal entity As String, ByVal amount As Double)
            Try
                api.Data.SetDataCell(si, acct, entity, "F#Periodic", "O#Top", "I#Top",
                                     "C1#Top", "C2#Top", "C3#Top", "C4#Top", amount, True)
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "CR_BOMCostRollup.WriteCell: Error - " & acct & " - " & ex.Message)
            End Try
        End Sub

    End Class

End Namespace

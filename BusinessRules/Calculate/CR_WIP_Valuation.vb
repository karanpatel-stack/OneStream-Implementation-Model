'------------------------------------------------------------------------------------------------------------
' CR_WIP_Valuation.vb
' OneStream XF Calculate Business Rule
'
' Purpose:  Calculates Work-in-Progress (WIP) inventory valuation. Maintains the WIP roll-forward
'           from opening balance through materials, labor, and overhead inputs, less completed goods
'           and scrap. Computes equivalent units and cost per equivalent unit for each production stage.
'           Results are written to BS inventory detail accounts.
'
' WIP Roll-Forward:
'   Opening WIP
'   + Materials Issued to Production
'   + Direct Labor Applied (hours x rate)
'   + Manufacturing Overhead Applied (hours x overhead rate)
'   - Cost of Goods Completed (to Finished Goods)
'   - Scrap / Waste (at recoverable value)
'   = Closing WIP
'
' Frequency: Monthly
' Scope:     Manufacturing entities
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

Namespace OneStream.BusinessRule.Finance.CR_WIP_Valuation

    Public Class MainClass

        ' Represents a production stage for equivalent unit calculation
        Private Class ProductionStage
            Public Property StageName As String
            Public Property PctCompleteMaterials As Double    ' % complete for materials at this stage
            Public Property PctCompleteConversion As Double   ' % complete for conversion (labor + OH)
        End Class

        '----------------------------------------------------------------------------------------------------
        ' Main entry point
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object

            Try
                If args.CalculationType <> FinanceRulesCalculationType.Calculate Then
                    Return Nothing
                End If

                Dim entityName As String = api.Entity.GetName()
                Dim scenarioName As String = api.Scenario.GetName()
                Dim timeName As String = api.Time.GetName()

                ' ---- Define Production Stages ----
                Dim stages As New List(Of ProductionStage) From {
                    New ProductionStage With {.StageName = "Stage1_RawPrep", .PctCompleteMaterials = 1.0, .PctCompleteConversion = 0.20},
                    New ProductionStage With {.StageName = "Stage2_Machining", .PctCompleteMaterials = 1.0, .PctCompleteConversion = 0.50},
                    New ProductionStage With {.StageName = "Stage3_Assembly", .PctCompleteMaterials = 1.0, .PctCompleteConversion = 0.75},
                    New ProductionStage With {.StageName = "Stage4_QC_Testing", .PctCompleteMaterials = 1.0, .PctCompleteConversion = 0.90},
                    New ProductionStage With {.StageName = "Stage5_Finishing", .PctCompleteMaterials = 1.0, .PctCompleteConversion = 0.95}
                }

                ' ---- Define product lines to process ----
                Dim productLines As New List(Of String) From {
                    "ProductLine_A", "ProductLine_B", "ProductLine_C"
                }

                ' ---- Process Each Product Line ----
                For Each productLine As String In productLines

                    ' ============================================================================================
                    ' SECTION 1: WIP Roll-Forward
                    ' ============================================================================================

                    ' Opening WIP Balance (prior period closing WIP)
                    Dim openingWIPPov As String = BuildPov(entityName, scenarioName, timeName, _
                        "WIP_OpeningBalance", productLine)
                    Dim openingWIP As Double = ReadAmount(si, openingWIPPov)

                    ' Materials Issued to Production
                    Dim materialsIssuedPov As String = BuildPov(entityName, scenarioName, timeName, _
                        "WIP_MaterialsIssued", productLine)
                    Dim materialsIssued As Double = ReadAmount(si, materialsIssuedPov)

                    ' Direct Labor: Hours x Hourly Rate
                    Dim laborHoursPov As String = BuildPov(entityName, scenarioName, timeName, _
                        "WIP_DirectLaborHours", productLine)
                    Dim laborHours As Double = ReadAmount(si, laborHoursPov)

                    Dim laborRatePov As String = BuildPov(entityName, scenarioName, timeName, _
                        "WIP_DirectLaborRate", productLine)
                    Dim laborRate As Double = ReadAmount(si, laborRatePov)
                    If laborRate <= 0 Then laborRate = 35.0  ' Default hourly rate if not specified

                    Dim directLaborApplied As Double = laborHours * laborRate

                    ' Manufacturing Overhead: Hours x Overhead Rate
                    Dim ohRatePov As String = BuildPov(entityName, scenarioName, timeName, _
                        "WIP_OverheadRate", productLine)
                    Dim overheadRate As Double = ReadAmount(si, ohRatePov)
                    If overheadRate <= 0 Then overheadRate = 22.50  ' Default OH rate

                    Dim overheadApplied As Double = laborHours * overheadRate

                    ' Total inputs for the period
                    Dim totalInputs As Double = materialsIssued + directLaborApplied + overheadApplied

                    ' Cost of Goods Completed (transferred to Finished Goods)
                    Dim cogsCompletedPov As String = BuildPov(entityName, scenarioName, timeName, _
                        "WIP_GoodsCompleted", productLine)
                    Dim goodsCompleted As Double = ReadAmount(si, cogsCompletedPov)

                    ' Scrap / Waste at recoverable value
                    Dim scrapPov As String = BuildPov(entityName, scenarioName, timeName, _
                        "WIP_Scrap", productLine)
                    Dim scrapValue As Double = ReadAmount(si, scrapPov)

                    ' Scrap recovery
                    Dim scrapRecoveryPov As String = BuildPov(entityName, scenarioName, timeName, _
                        "WIP_ScrapRecovery", productLine)
                    Dim scrapRecovery As Double = ReadAmount(si, scrapRecoveryPov)

                    ' Net scrap cost
                    Dim netScrapCost As Double = scrapValue - scrapRecovery

                    ' Total outputs for the period
                    Dim totalOutputs As Double = goodsCompleted + netScrapCost

                    ' ---- Closing WIP ----
                    Dim closingWIP As Double = openingWIP + totalInputs - totalOutputs

                    ' Ensure WIP does not go negative (data quality check)
                    If closingWIP < 0 Then
                        BRApi.ErrorLog.LogMessage(si, String.Format( _
                            "CR_WIP_Valuation WARNING: Negative closing WIP ({0:N2}) for {1} / {2} " & _
                            "in period {3}. Setting to zero.", closingWIP, entityName, productLine, timeName))
                        closingWIP = 0
                    End If

                    ' ============================================================================================
                    ' SECTION 2: Equivalent Units Calculation
                    ' ============================================================================================

                    Dim totalEquivUnitsMaterials As Double = 0
                    Dim totalEquivUnitsConversion As Double = 0

                    For Each stage As ProductionStage In stages

                        ' Read physical units at this production stage
                        Dim unitsPov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#WIP_UnitsAtStage:U1#{3}:U2#{4}", _
                            entityName, scenarioName, timeName, productLine, stage.StageName)
                        Dim unitsCell As DataCell = BRApi.Finance.Data.GetDataCell(si, unitsPov, True)
                        Dim physicalUnits As Double = unitsCell.CellAmount

                        ' Equivalent units for materials
                        Dim equivMaterials As Double = physicalUnits * stage.PctCompleteMaterials

                        ' Equivalent units for conversion costs (labor + overhead)
                        Dim equivConversion As Double = physicalUnits * stage.PctCompleteConversion

                        totalEquivUnitsMaterials += equivMaterials
                        totalEquivUnitsConversion += equivConversion

                        ' Write stage-level equivalent units
                        Dim equivMatPov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#WIP_EquivUnits_Materials:U1#{3}:U2#{4}", _
                            entityName, scenarioName, timeName, productLine, stage.StageName)
                        api.Data.SetDataCell(si, equivMatPov, equivMaterials, True)

                        Dim equivConvPov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#WIP_EquivUnits_Conversion:U1#{3}:U2#{4}", _
                            entityName, scenarioName, timeName, productLine, stage.StageName)
                        api.Data.SetDataCell(si, equivConvPov, equivConversion, True)

                    Next ' stage

                    ' ============================================================================================
                    ' SECTION 3: Cost Per Equivalent Unit
                    ' ============================================================================================

                    ' Cost per equivalent unit for materials
                    Dim costPerEquivMaterials As Double = 0
                    If totalEquivUnitsMaterials > 0 Then
                        costPerEquivMaterials = materialsIssued / totalEquivUnitsMaterials
                    End If

                    ' Cost per equivalent unit for conversion
                    Dim totalConversionCost As Double = directLaborApplied + overheadApplied
                    Dim costPerEquivConversion As Double = 0
                    If totalEquivUnitsConversion > 0 Then
                        costPerEquivConversion = totalConversionCost / totalEquivUnitsConversion
                    End If

                    ' Total cost per equivalent unit
                    Dim totalCostPerEquivUnit As Double = costPerEquivMaterials + costPerEquivConversion

                    ' ============================================================================================
                    ' SECTION 4: Write Results
                    ' ============================================================================================

                    ' WIP Roll-Forward line items
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "WIP_DirectLaborApplied", productLine, directLaborApplied)
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "WIP_OverheadApplied", productLine, overheadApplied)
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "WIP_TotalInputs", productLine, totalInputs)
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "WIP_TotalOutputs", productLine, totalOutputs)
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "WIP_NetScrapCost", productLine, netScrapCost)

                    ' Closing WIP balance (to BS)
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "BS_WIP_ClosingBalance", productLine, closingWIP)

                    ' Equivalent unit totals
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "WIP_TotalEquivUnits_Materials", productLine, totalEquivUnitsMaterials)
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "WIP_TotalEquivUnits_Conversion", productLine, totalEquivUnitsConversion)

                    ' Cost per equivalent unit
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "WIP_CostPerEquivUnit_Materials", productLine, costPerEquivMaterials)
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "WIP_CostPerEquivUnit_Conversion", productLine, costPerEquivConversion)
                    WriteResult(si, api, entityName, scenarioName, timeName, _
                        "WIP_CostPerEquivUnit_Total", productLine, totalCostPerEquivUnit)

                Next ' productLine

                ' Trigger downstream inventory calculations
                api.Data.Calculate("A#BS_WIP_ClosingBalance")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Build a standard POV string with UD1
        '----------------------------------------------------------------------------------------------------
        Private Function BuildPov(ByVal entity As String, ByVal scenario As String, _
                ByVal time As String, ByVal account As String, ByVal ud1 As String) As String

            Return String.Format("E#{0}:S#{1}:T#{2}:A#{3}:U1#{4}", entity, scenario, time, account, ud1)

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Read amount from cube
        '----------------------------------------------------------------------------------------------------
        Private Function ReadAmount(ByVal si As SessionInfo, ByVal pov As String) As Double

            Dim cell As DataCell = BRApi.Finance.Data.GetDataCell(si, pov, True)
            Return cell.CellAmount

        End Function

        '----------------------------------------------------------------------------------------------------
        ' Helper: Write result to cube with UD1
        '----------------------------------------------------------------------------------------------------
        Private Sub WriteResult(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                ByVal entity As String, ByVal scenario As String, ByVal time As String, _
                ByVal account As String, ByVal ud1 As String, ByVal amount As Double)

            Dim pov As String = BuildPov(entity, scenario, time, account, ud1)
            api.Data.SetDataCell(si, pov, amount, True)

        End Sub

    End Class

End Namespace

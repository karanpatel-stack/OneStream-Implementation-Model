'------------------------------------------------------------------------------------------------------------
' CR_AllocateCorpOverhead.vb
' OneStream XF Calculate Business Rule
'
' Purpose:  Allocates corporate overhead cost pools (IT, HR, Finance, Executive, Facilities)
'           to operating entities based on configured allocation drivers. Supports multi-level
'           allocation where shared services costs flow to regions first, then to plants.
'           Writes allocated amounts to CorporateAllocation accounts per entity.
'
' Allocation Pools & Drivers:
'   IT         -> Headcount or PC count
'   HR         -> Headcount
'   Finance    -> Revenue or transaction count
'   Executive  -> Revenue
'   Facilities -> Square footage
'
' Frequency: Monthly
' Scope:     Corporate entity (source) to all operating entities (targets)
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

Namespace OneStream.BusinessRule.Finance.CR_AllocateCorpOverhead

    Public Class MainClass

        ' Represents an allocation pool configuration
        Private Class AllocationPool
            Public Property PoolName As String
            Public Property CostAccount As String             ' Source account for pool costs
            Public Property DriverAccount As String           ' Account that holds the driver metric per entity
            Public Property AllocatedAccount As String        ' Target account to write allocated cost
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

                Dim scenarioName As String = api.Scenario.GetName()
                Dim timeName As String = api.Time.GetName()

                ' ---- Define Allocation Pools ----
                Dim pools As New List(Of AllocationPool) From {
                    New AllocationPool With {
                        .PoolName = "IT", .CostAccount = "CORP_IT_Costs",
                        .DriverAccount = "ALLOC_Driver_Headcount", .AllocatedAccount = "ALLOC_IT_Allocated"
                    },
                    New AllocationPool With {
                        .PoolName = "HR", .CostAccount = "CORP_HR_Costs",
                        .DriverAccount = "ALLOC_Driver_Headcount", .AllocatedAccount = "ALLOC_HR_Allocated"
                    },
                    New AllocationPool With {
                        .PoolName = "Finance", .CostAccount = "CORP_Finance_Costs",
                        .DriverAccount = "ALLOC_Driver_Revenue", .AllocatedAccount = "ALLOC_Finance_Allocated"
                    },
                    New AllocationPool With {
                        .PoolName = "Executive", .CostAccount = "CORP_Executive_Costs",
                        .DriverAccount = "ALLOC_Driver_Revenue", .AllocatedAccount = "ALLOC_Executive_Allocated"
                    },
                    New AllocationPool With {
                        .PoolName = "Facilities", .CostAccount = "CORP_Facilities_Costs",
                        .DriverAccount = "ALLOC_Driver_SqFootage", .AllocatedAccount = "ALLOC_Facilities_Allocated"
                    }
                }

                ' ---- Define entity hierarchy: Regions contain plants ----
                Dim regionToPlants As New Dictionary(Of String, List(Of String)) From {
                    {"Region_NorthAmerica", New List(Of String) From {"Plant_Detroit", "Plant_Chicago", "Plant_Houston"}},
                    {"Region_Europe", New List(Of String) From {"Plant_Munich", "Plant_Lyon", "Plant_Milan"}},
                    {"Region_AsiaPac", New List(Of String) From {"Plant_Shanghai", "Plant_Tokyo", "Plant_Seoul"}}
                }

                ' Corporate entity that holds overhead costs
                Const corporateEntity As String = "Corporate"

                ' Build flat list of all target entities (plants)
                Dim allPlants As New List(Of String)
                For Each kvp As KeyValuePair(Of String, List(Of String)) In regionToPlants
                    allPlants.AddRange(kvp.Value)
                Next

                ' ---- Process Each Allocation Pool ----
                For Each pool As AllocationPool In pools

                    ' Read total pool cost from corporate entity
                    Dim poolCostPov As String = String.Format( _
                        "E#{0}:S#{1}:T#{2}:A#{3}", corporateEntity, scenarioName, timeName, pool.CostAccount)
                    Dim poolCostCell As DataCell = BRApi.Finance.Data.GetDataCell(si, poolCostPov, True)
                    Dim totalPoolCost As Double = poolCostCell.CellAmount

                    If Math.Abs(totalPoolCost) < 0.01 Then Continue For

                    ' ---- Step 1: Read driver values for all target entities ----
                    Dim driverValues As New Dictionary(Of String, Double)
                    Dim totalDriver As Double = 0

                    For Each plantEntity As String In allPlants
                        Dim driverPov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#{3}", plantEntity, scenarioName, timeName, pool.DriverAccount)
                        Dim driverCell As DataCell = BRApi.Finance.Data.GetDataCell(si, driverPov, True)
                        Dim driverVal As Double = driverCell.CellAmount

                        If driverVal < 0 Then driverVal = 0  ' Negative drivers are not valid

                        driverValues(plantEntity) = driverVal
                        totalDriver += driverVal
                    Next

                    ' Guard against division by zero if no driver data exists
                    If totalDriver <= 0 Then
                        BRApi.ErrorLog.LogMessage(si, String.Format( _
                            "CR_AllocateCorpOverhead WARNING: Total driver for pool '{0}' is zero. " & _
                            "Allocation skipped for period {1}.", pool.PoolName, timeName))
                        Continue For
                    End If

                    ' ---- Step 2: Calculate and write allocation percentages and amounts ----
                    Dim allocationChecksum As Double = 0

                    For Each plantEntity As String In allPlants
                        Dim driverVal As Double = driverValues(plantEntity)
                        Dim allocPct As Double = driverVal / totalDriver
                        Dim allocAmount As Double = totalPoolCost * allocPct

                        ' Write allocation percentage for audit trail
                        Dim allocPctPov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#ALLOC_Pct_{3}", _
                            plantEntity, scenarioName, timeName, pool.PoolName)
                        api.Data.SetDataCell(si, allocPctPov, allocPct, True)

                        ' Write allocated cost amount
                        Dim allocAmtPov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#{3}", _
                            plantEntity, scenarioName, timeName, pool.AllocatedAccount)
                        api.Data.SetDataCell(si, allocAmtPov, allocAmount, True)

                        allocationChecksum += allocAmount
                    Next

                    ' ---- Step 3: Write offsetting entry on corporate entity ----
                    ' The corporate entity gets a negative (elimination) of the allocated cost
                    Dim corpOffsetPov As String = String.Format( _
                        "E#{0}:S#{1}:T#{2}:A#{3}", corporateEntity, scenarioName, timeName, pool.AllocatedAccount)
                    api.Data.SetDataCell(si, corpOffsetPov, -totalPoolCost, True)

                    ' ---- Step 4: Validate allocation balances to zero ----
                    Dim roundingDiff As Double = allocationChecksum - totalPoolCost
                    If Math.Abs(roundingDiff) > 0.01 Then
                        ' Apply rounding adjustment to the largest entity (first plant)
                        Dim adjustEntity As String = allPlants(0)
                        Dim existingPov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#{3}", adjustEntity, scenarioName, timeName, pool.AllocatedAccount)
                        Dim existingCell As DataCell = BRApi.Finance.Data.GetDataCell(si, existingPov, True)
                        Dim adjustedAmount As Double = existingCell.CellAmount - roundingDiff
                        api.Data.SetDataCell(si, existingPov, adjustedAmount, True)
                    End If

                Next ' pool

                ' ---- Multi-Level Allocation: Roll up plant allocations to regions ----
                For Each regionKvp As KeyValuePair(Of String, List(Of String)) In regionToPlants
                    Dim regionEntity As String = regionKvp.Key
                    Dim plants As List(Of String) = regionKvp.Value

                    For Each pool As AllocationPool In pools
                        Dim regionAllocTotal As Double = 0

                        For Each plantEntity As String In plants
                            Dim plantAllocPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#{3}", _
                                plantEntity, scenarioName, timeName, pool.AllocatedAccount)
                            Dim plantAllocCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, plantAllocPov, True)
                            regionAllocTotal += plantAllocCell.CellAmount
                        Next

                        ' Write regional total for reporting
                        Dim regionTotalPov As String = String.Format( _
                            "E#{0}:S#{1}:T#{2}:A#ALLOC_RegionTotal_{3}", _
                            regionEntity, scenarioName, timeName, pool.PoolName)
                        api.Data.SetDataCell(si, regionTotalPov, regionAllocTotal, True)
                    Next
                Next

                ' Trigger downstream calculations
                api.Data.Calculate("A#ALLOC_TotalAllocated")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try

        End Function

    End Class

End Namespace

'------------------------------------------------------------------------------------------------------------
' OneStream XF Finance Business Rule: FR_Consolidation
'------------------------------------------------------------------------------------------------------------
' Purpose:     Main consolidation engine for group reporting. Processes entity hierarchies to
'              aggregate financial data from child entities to parent entities, applying the
'              correct consolidation method based on ownership percentage.
'
' Consolidation Methods:
'   - Full Consolidation (>50% ownership): 100% of subsidiary data aggregated to parent
'   - Proportional Consolidation (joint ventures): ownership % applied to all accounts
'   - Equity Method (20-50% ownership): share of net income picked up (delegated to FR_EquityPickup)
'
' Process Order (mirrors standard consolidation dimension members):
'   1. C_Local        - Local currency data (input or calculated)
'   2. C_Translated   - FX-translated data (handled by FR_CurrencyTranslation)
'   3. C_Proportional - Proportional share adjustments
'   4. C_Elimination  - Intercompany elimination entries (handled by FR_IntercompanyElimination)
'   5. C_Consolidated - Final consolidated result = sum of all consolidation members
'
' Dependencies: FR_CurrencyTranslation, FR_IntercompanyElimination, FR_EquityPickup,
'               FR_MinorityInterest
'
' Author:       OneStream Administrator
' Created:      2026-02-18
' Modified:     2026-02-18
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports Microsoft.VisualBasic
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.Finance.FR_Consolidation

    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Main entry point called by the OneStream calculation engine.
        ' The api object provides access to the entire OneStream runtime context including
        ' data, metadata, dimension members, and utility functions.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object
            Try
                '--------------------------------------------------------------------------------------------
                ' The Finance Business Rule is triggered during consolidation. We only process during
                ' the Calculate event to avoid duplicate processing on other events.
                '--------------------------------------------------------------------------------------------
                If args.FinanceRulesEventType = FinanceRulesEventType.Calculate Then
                    Return Me.ProcessConsolidation(si, globals, api, args)
                End If

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_Consolidation.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessConsolidation: Orchestrates the full consolidation workflow.
        ' Iterates through the entity hierarchy and applies the appropriate consolidation method
        ' to each entity based on its ownership percentage stored in entity properties.
        '----------------------------------------------------------------------------------------------------
        Private Function ProcessConsolidation(ByVal si As SessionInfo, ByVal globals As BRGlobals, _
                                              ByVal api As FinanceRulesApi, ByVal args As FinanceRulesArgs) As Object
            Try
                ' Retrieve the current entity being processed in the consolidation
                Dim currentEntityName As String = api.Entity.GetName()
                Dim parentEntityName As String = api.Entity.GetParentName()

                ' Skip processing if this is a base-level input entity with no children
                ' Base entities only have local data; consolidation logic applies to parent entities
                If Not api.Entity.HasChildren() Then
                    ' For leaf entities, simply ensure C_Local data is available
                    Me.ProcessLocalData(si, api, args, currentEntityName)
                    Return Nothing
                End If

                ' Log the start of consolidation for this entity
                BRApi.ErrorLog.LogMessage(si, $"FR_Consolidation: Beginning consolidation for entity [{currentEntityName}]")

                '--------------------------------------------------------------------------------------------
                ' Step 1: Process each child entity and determine consolidation method
                '--------------------------------------------------------------------------------------------
                Dim childEntities As List(Of String) = Me.GetChildEntities(si, api, currentEntityName)

                For Each childEntity As String In childEntities
                    ' Retrieve ownership percentage from entity dimension properties
                    ' OneStream stores custom properties on dimension members via BRApi.Finance.Members
                    Dim ownershipPct As Double = Me.GetOwnershipPercentage(si, api, childEntity, parentEntityName)

                    ' Determine consolidation method based on ownership threshold
                    Dim consolMethod As ConsolidationMethod = Me.DetermineConsolidationMethod(ownershipPct)

                    BRApi.ErrorLog.LogMessage(si, $"  Child [{childEntity}]: Ownership={ownershipPct:P2}, Method={consolMethod.ToString()}")

                    Select Case consolMethod
                        Case ConsolidationMethod.FullConsolidation
                            ' 100% aggregation -- all child data rolls to parent
                            Me.ProcessFullConsolidation(si, api, args, childEntity, currentEntityName, ownershipPct)

                        Case ConsolidationMethod.ProportionalConsolidation
                            ' Joint venture: apply ownership % to all account values
                            Me.ProcessProportionalConsolidation(si, api, args, childEntity, currentEntityName, ownershipPct)

                        Case ConsolidationMethod.EquityMethod
                            ' Associate: only pick up share of net income (delegated to FR_EquityPickup)
                            ' Here we just flag that equity method applies; actual entries handled separately
                            Me.FlagEquityMethodEntity(si, api, args, childEntity, currentEntityName, ownershipPct)

                        Case ConsolidationMethod.NoConsolidation
                            ' Below 20% ownership -- treated as financial investment, no consolidation
                            BRApi.ErrorLog.LogMessage(si, $"  Skipping [{childEntity}]: ownership {ownershipPct:P2} below equity method threshold")
                    End Select
                Next

                '--------------------------------------------------------------------------------------------
                ' Step 2: Calculate the consolidated totals
                ' C_Consolidated = C_Local + C_Translated + C_Proportional + C_Elimination
                '--------------------------------------------------------------------------------------------
                Me.CalculateConsolidatedTotal(si, api, args, currentEntityName)

                BRApi.ErrorLog.LogMessage(si, $"FR_Consolidation: Completed consolidation for entity [{currentEntityName}]")

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_Consolidation.ProcessConsolidation", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessLocalData: Ensures local currency data exists for leaf/base entities.
        ' For input entities, C_Local is typically loaded from data input or staging.
        ' This method validates that data is present and consistent.
        '----------------------------------------------------------------------------------------------------
        Private Sub ProcessLocalData(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                     ByVal args As FinanceRulesArgs, ByVal entityName As String)
            Try
                ' Read local data using the standard data buffer pattern
                ' The POV (Point of View) is set by the calculation engine automatically
                Dim localDataExists As Boolean = api.Data.HasData("C_Local")

                If Not localDataExists Then
                    BRApi.ErrorLog.LogMessage(si, $"FR_Consolidation: WARNING - No local data found for entity [{entityName}]")
                End If

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_Consolidation.ProcessLocalData", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' GetChildEntities: Retrieves the immediate child members of the specified parent entity.
        ' Uses the OneStream dimension API to navigate the entity hierarchy.
        '----------------------------------------------------------------------------------------------------
        Private Function GetChildEntities(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                          ByVal parentEntityName As String) As List(Of String)
            Try
                Dim children As New List(Of String)

                ' Use BRApi to get child members from the Entity dimension
                ' MemberFilter syntax: Children of the specified parent entity
                Dim memberFilter As String = $"E#{parentEntityName}.Children"
                Dim memberList As List(Of MemberInfo) = BRApi.Finance.Members.GetMembersByFilter( _
                    si, BRApi.Finance.Dim.GetDimPk(si, DimType.Entity.Id), memberFilter)

                If memberList IsNot Nothing Then
                    For Each member As MemberInfo In memberList
                        children.Add(member.Member.Name)
                    Next
                End If

                Return children

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_Consolidation.GetChildEntities", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetOwnershipPercentage: Reads the ownership % for a child entity relative to its parent.
        ' Ownership is stored as a custom property (text or numeric) on the entity dimension member.
        ' Convention: Property "OwnershipPct" holds a decimal value (e.g., 1.0 = 100%, 0.6 = 60%).
        '----------------------------------------------------------------------------------------------------
        Private Function GetOwnershipPercentage(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                 ByVal childEntity As String, ByVal parentEntity As String) As Double
            Try
                ' Attempt to read ownership from entity properties
                ' OneStream stores member properties via text fields or UDn properties
                Dim ownershipStr As String = BRApi.Finance.Entity.GetPropertyValue( _
                    si, childEntity, "OwnershipPct")

                If Not String.IsNullOrEmpty(ownershipStr) Then
                    Dim ownershipPct As Double = 0
                    If Double.TryParse(ownershipStr, NumberStyles.Any, CultureInfo.InvariantCulture, ownershipPct) Then
                        Return ownershipPct
                    End If
                End If

                ' Default to 100% ownership if property not defined
                ' This assumes full consolidation when no explicit ownership is set
                Return 1.0

            Catch ex As Exception
                ' If property retrieval fails, default to 100% and log a warning
                BRApi.ErrorLog.LogMessage(si, $"FR_Consolidation: WARNING - Could not read ownership for [{childEntity}], defaulting to 100%")
                Return 1.0
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' DetermineConsolidationMethod: Maps ownership percentage to the appropriate method.
        ' Standard thresholds per IFRS 10/IAS 28:
        '   >50%  = Control        -> Full Consolidation
        '   50%   = Joint Control  -> Proportional Consolidation (or Equity per IFRS 11)
        '   20-50%= Significant Influence -> Equity Method
        '   <20%  = Financial Investment  -> No Consolidation (fair value)
        '----------------------------------------------------------------------------------------------------
        Private Function DetermineConsolidationMethod(ByVal ownershipPct As Double) As ConsolidationMethod
            If ownershipPct > 0.5 Then
                Return ConsolidationMethod.FullConsolidation
            ElseIf ownershipPct = 0.5 Then
                Return ConsolidationMethod.ProportionalConsolidation
            ElseIf ownershipPct >= 0.2 Then
                Return ConsolidationMethod.EquityMethod
            Else
                Return ConsolidationMethod.NoConsolidation
            End If
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessFullConsolidation: Aggregates 100% of child entity data to the parent.
        ' All accounts are summed from child C_Consolidated into parent C_Local before
        ' the parent's own consolidation adjustments are applied.
        '----------------------------------------------------------------------------------------------------
        Private Sub ProcessFullConsolidation(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                              ByVal args As FinanceRulesArgs, ByVal childEntity As String, _
                                              ByVal parentEntity As String, ByVal ownershipPct As Double)
            Try
                ' For full consolidation, aggregate all account data from the child's consolidated view
                ' Use api.Data.Calculate to read child data and write to parent
                '
                ' Source POV: Entity=childEntity, Consolidation=C_Consolidated
                ' Target POV: Entity=parentEntity, Consolidation=C_Local
                '
                ' The Calculate method processes all accounts in the current scenario/time context

                ' Build the source and destination data cell descriptors
                Dim sourcePovScript As String = $"E#{childEntity}:C#C_Consolidated"
                Dim destPovScript As String = $"E#{parentEntity}:C#C_Local"

                ' Use the data API to copy data from child consolidated to parent local
                ' api.Data.Calculate performs the aggregation across all accounts
                api.Data.Calculate(sourcePovScript, destPovScript, "A#[All Accounts].Base")

                ' If ownership is less than 100% but still > 50% (e.g., 80%),
                ' we still fully consolidate but must calculate Non-Controlling Interest (NCI)
                ' NCI processing is handled by FR_MinorityInterest
                If ownershipPct < 1.0 Then
                    BRApi.ErrorLog.LogMessage(si, _
                        $"  Full consol [{childEntity}] with NCI: ownership={ownershipPct:P2}, NCI%={(1.0 - ownershipPct):P2}")
                End If

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_Consolidation.ProcessFullConsolidation", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ProcessProportionalConsolidation: Applies ownership % to all child account values.
        ' Used for joint ventures (50% ownership) where the venturer consolidates its
        ' proportionate share of assets, liabilities, revenue, and expenses.
        '----------------------------------------------------------------------------------------------------
        Private Sub ProcessProportionalConsolidation(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                      ByVal args As FinanceRulesArgs, ByVal childEntity As String, _
                                                      ByVal parentEntity As String, ByVal ownershipPct As Double)
            Try
                ' Read the child entity's consolidated data
                Dim sourcePovScript As String = $"E#{childEntity}:C#C_Consolidated"

                ' Write proportional share to C_Proportional consolidation member
                Dim destPovScript As String = $"E#{parentEntity}:C#C_Proportional"

                ' Calculate proportional amounts by applying ownership percentage
                ' The factor parameter multiplies all source values by the ownership %
                api.Data.Calculate(sourcePovScript, destPovScript, "A#[All Accounts].Base", ownershipPct)

                BRApi.ErrorLog.LogMessage(si, _
                    $"  Proportional consol [{childEntity}] at {ownershipPct:P2} to [{parentEntity}]")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_Consolidation.ProcessProportionalConsolidation", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' FlagEquityMethodEntity: Marks an entity for equity method processing.
        ' The actual equity pickup entries are generated by FR_EquityPickup.
        ' This method stores a flag so downstream rules know which entities require equity treatment.
        '----------------------------------------------------------------------------------------------------
        Private Sub FlagEquityMethodEntity(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                            ByVal args As FinanceRulesArgs, ByVal childEntity As String, _
                                            ByVal parentEntity As String, ByVal ownershipPct As Double)
            Try
                ' Store equity method flag in substitution variables or custom data intersection
                ' This allows FR_EquityPickup to identify which entities need processing
                BRApi.ErrorLog.LogMessage(si, _
                    $"  Equity method flagged: [{childEntity}] at {ownershipPct:P2} for parent [{parentEntity}]")

                ' Write a marker value to a control account to signal equity method processing
                ' Account: A_EquityMethodFlag, Value: ownership percentage
                Dim flagPov As String = $"E#{parentEntity}:A#A_EquityMethodFlag:C#C_Local:F#F_None:O#O_None:I#{childEntity}"
                api.Data.SetDataCell(flagPov, ownershipPct)

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_Consolidation.FlagEquityMethodEntity", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' CalculateConsolidatedTotal: Computes the final C_Consolidated member as the sum of
        ' all consolidation sub-members. This is the total consolidated view for the entity.
        '
        ' C_Consolidated = C_Local + C_Translated + C_Proportional + C_Elimination
        '
        ' Each sub-member represents a distinct layer of the consolidation process.
        '----------------------------------------------------------------------------------------------------
        Private Sub CalculateConsolidatedTotal(ByVal si As SessionInfo, ByVal api As FinanceRulesApi, _
                                                ByVal args As FinanceRulesArgs, ByVal entityName As String)
            Try
                ' The consolidation total is typically handled automatically by OneStream's
                ' dimension aggregation if C_Consolidated is defined as a parent of the sub-members.
                ' However, we explicitly calculate it here for control and auditability.

                Dim consolMembers As String() = {"C_Local", "C_Translated", "C_Proportional", "C_Elimination"}
                Dim destPov As String = $"E#{entityName}:C#C_Consolidated"

                ' Iterate through each consolidation sub-member and aggregate to the total
                For Each consolMember As String In consolMembers
                    Dim sourcePov As String = $"E#{entityName}:C#{consolMember}"
                    api.Data.Calculate(sourcePov, destPov, "A#[All Accounts].Base")
                Next

                BRApi.ErrorLog.LogMessage(si, $"  Consolidated total calculated for [{entityName}]")

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "FR_Consolidation.CalculateConsolidatedTotal", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' Enumeration for consolidation methods to provide type-safe method selection.
        '----------------------------------------------------------------------------------------------------
        Private Enum ConsolidationMethod
            FullConsolidation = 0
            ProportionalConsolidation = 1
            EquityMethod = 2
            NoConsolidation = 3
        End Enum

    End Class

End Namespace

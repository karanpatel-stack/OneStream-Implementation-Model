'------------------------------------------------------------------------------------------------------------
' MF_CostCenterAccess
' Member Filter Business Rule - Cost Center Access Filtering
'
' Purpose:  Filters the Cost Center dimension based on organizational ownership and the
'           user's role in the organizational hierarchy. Ensures department heads see only
'           their cost centers, plant managers see all cost centers at their plant, and
'           VP Finance has full visibility across the organization.
'
' Access Hierarchy:
'   VP Finance / CFO       -> All cost centers (full hierarchy)
'   Plant Manager          -> All cost centers at their manufacturing plant
'   Department Head        -> Only cost centers owned by their department
'   Budget Analyst         -> Cost centers assigned for analysis (read-only context)
'   Default                -> Top-level summary only
'
' Cost center ownership is read from entity/member properties configured in the
' OneStream dimension metadata and security settings.
'
' Scope:    Member Filter
' Version:  1.0
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.MemberFilter.MF_CostCenterAccess

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As MemberFilterArgs) As Object
            Try
                '--- Identify the current user ---
                Dim userName As String = si.UserName
                Dim userRole As String = DetermineCostCenterRole(si, userName)

                BRApi.ErrorLog.LogMessage(si, "MF_CostCenterAccess: User=" & userName & ", Role=" & userRole)

                '--- Build the cost center filter based on the user's role ---
                Dim costCenterFilter As String = BuildCostCenterFilter(si, userRole, userName)

                Return costCenterFilter

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "MF_CostCenterAccess", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Determines the user's cost center access role based on security group membership.
        ''' Checks groups in descending order of access privilege.
        ''' </summary>
        Private Function DetermineCostCenterRole(ByVal si As SessionInfo, ByVal userName As String) As String
            Try
                '--- Check security groups in priority order ---
                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_VPFinance") Then
                    Return "VPFinance"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_CFO") Then
                    Return "VPFinance"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_Corporate") Then
                    Return "VPFinance"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_PlantManagers") Then
                    Return "PlantManager"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_DepartmentHeads") Then
                    Return "DepartmentHead"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_BudgetAnalysts") Then
                    Return "BudgetAnalyst"
                End If

                Return "Default"

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_CostCenterAccess.DetermineCostCenterRole: Error - " & ex.Message)
                Return "Default"
            End Try
        End Function

        ''' <summary>
        ''' Builds the cost center member filter expression based on the user's role
        ''' and organizational assignment.
        ''' </summary>
        Private Function BuildCostCenterFilter(ByVal si As SessionInfo, ByVal userRole As String,
                                                ByVal userName As String) As String
            Select Case userRole

                Case "VPFinance"
                    '--- VP Finance / CFO / Corporate: full access to all cost centers ---
                    Return "UD2#TotalCostCenters.Descendants"

                Case "PlantManager"
                    '--- Plant managers: all cost centers at their assigned plant ---
                    Return BuildPlantCostCenterFilter(si, userName)

                Case "DepartmentHead"
                    '--- Department heads: only their owned cost centers ---
                    Return BuildDepartmentCostCenterFilter(si, userName)

                Case "BudgetAnalyst"
                    '--- Budget analysts: assigned cost centers for analysis ---
                    Return BuildAnalystCostCenterFilter(si, userName)

                Case Else
                    '--- Default: top-level summary only ---
                    Return "UD2#TotalCostCenters"

            End Select
        End Function

        ''' <summary>
        ''' Builds a filter for plant managers showing all cost centers at their plant.
        ''' Reads the plant assignment from user properties, then retrieves the cost center
        ''' hierarchy under that plant's organizational node.
        ''' </summary>
        Private Function BuildPlantCostCenterFilter(ByVal si As SessionInfo, ByVal userName As String) As String
            Try
                Dim plantName As String = GetUserProperty(si, userName, "UserAssignment_Plant")

                If Not String.IsNullOrEmpty(plantName) Then
                    '--- The cost center hierarchy mirrors the plant structure ---
                    ' Convention: Cost center parent node = "CC_" & plantName
                    ' e.g., Plant "US01" -> Cost center parent "CC_US01"
                    Dim ccParent As String = "CC_" & plantName
                    Return "UD2#" & ccParent & ".Descendants"
                End If

                BRApi.ErrorLog.LogMessage(si, "MF_CostCenterAccess: No plant assignment for " & userName)
                Return "UD2#TotalCostCenters"

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_CostCenterAccess.BuildPlantCostCenterFilter: Error - " & ex.Message)
                Return "UD2#TotalCostCenters"
            End Try
        End Function

        ''' <summary>
        ''' Builds a filter for department heads showing only their owned cost centers.
        ''' Reads the department assignment from user properties and retrieves the cost
        ''' center list from the department-to-cost-center mapping.
        ''' </summary>
        Private Function BuildDepartmentCostCenterFilter(ByVal si As SessionInfo, ByVal userName As String) As String
            Try
                Dim departmentName As String = GetUserProperty(si, userName, "UserAssignment_Department")

                If Not String.IsNullOrEmpty(departmentName) Then
                    '--- Read cost center list for this department from substitution variables ---
                    ' Convention: SubstVar "DeptCostCenters_Engineering" = "CC1001;CC1002;CC1003"
                    Dim mappingVar As String = "DeptCostCenters_" & departmentName
                    Dim ccList As String = String.Empty

                    Try
                        ccList = BRApi.Finance.Data.GetSubstVarValue(si, mappingVar)
                    Catch
                        ' Substitution variable may not exist
                    End Try

                    If Not String.IsNullOrEmpty(ccList) Then
                        Dim costCenters() As String = ccList.Split(";"c)
                        Dim filterParts As New List(Of String)
                        For Each cc As String In costCenters
                            Dim trimmed As String = cc.Trim()
                            If Not String.IsNullOrEmpty(trimmed) Then
                                filterParts.Add("UD2#" & trimmed)
                            End If
                        Next
                        If filterParts.Count > 0 Then
                            Return String.Join(":", filterParts.ToArray())
                        End If
                    End If

                    '--- Fallback: use department name as cost center parent node ---
                    Return "UD2#CC_" & departmentName & ".Descendants"
                End If

                BRApi.ErrorLog.LogMessage(si, "MF_CostCenterAccess: No department assignment for " & userName)
                Return "UD2#TotalCostCenters"

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_CostCenterAccess.BuildDepartmentCostCenterFilter: Error - " & ex.Message)
                Return "UD2#TotalCostCenters"
            End Try
        End Function

        ''' <summary>
        ''' Builds a filter for budget analysts based on their assigned cost center list.
        ''' Analysts may be assigned across departments for cross-functional analysis.
        ''' </summary>
        Private Function BuildAnalystCostCenterFilter(ByVal si As SessionInfo, ByVal userName As String) As String
            Try
                '--- Read analyst's assigned cost centers from user properties ---
                Dim assignedCCs As String = GetUserProperty(si, userName, "UserAssignment_CostCenters")

                If Not String.IsNullOrEmpty(assignedCCs) Then
                    Dim costCenters() As String = assignedCCs.Split(";"c)
                    Dim filterParts As New List(Of String)
                    For Each cc As String In costCenters
                        Dim trimmed As String = cc.Trim()
                        If Not String.IsNullOrEmpty(trimmed) Then
                            filterParts.Add("UD2#" & trimmed & ".Descendants")
                        End If
                    Next
                    If filterParts.Count > 0 Then
                        Return String.Join(":", filterParts.ToArray())
                    End If
                End If

                '--- Fallback: top-level summary ---
                Return "UD2#TotalCostCenters"

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_CostCenterAccess.BuildAnalystCostCenterFilter: Error - " & ex.Message)
                Return "UD2#TotalCostCenters"
            End Try
        End Function

        ''' <summary>
        ''' Helper to read a user property with error handling.
        ''' </summary>
        Private Function GetUserProperty(ByVal si As SessionInfo, ByVal userName As String,
                                          ByVal propertyName As String) As String
            Try
                Dim value As String = BRApi.Security.Admin.GetUserProperty(si, userName, propertyName)
                If Not String.IsNullOrEmpty(value) Then
                    Return value.Trim()
                End If
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_CostCenterAccess.GetUserProperty: " &
                    "Error reading '" & propertyName & "' for " & userName & " - " & ex.Message)
            End Try
            Return String.Empty
        End Function

    End Class

End Namespace

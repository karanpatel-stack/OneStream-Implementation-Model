'------------------------------------------------------------------------------------------------------------
' MF_EntitySecurity
' Member Filter Business Rule - Dynamic Entity Access Filtering
'
' Purpose:  Dynamically filters the Entity dimension member list based on the current user's
'           security role and organizational assignment. Ensures users only see and interact
'           with entities they are authorized to access.
'
' Security Hierarchy:
'   Corporate User     -> All entities (full hierarchy)
'   Executive Team     -> Consolidated entities + all regional parents
'   Regional Manager   -> Their region's plant entities only
'   Plant Controller   -> Their assigned plant entity only
'   Read-Only Analyst  -> Same as their role level, but enforced read-only elsewhere
'
' Role Detection:  Uses BRApi.Security to read user group memberships and map them
'                  to entity access lists.
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

Namespace OneStream.BusinessRule.MemberFilter.MF_EntitySecurity

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As MemberFilterArgs) As Object
            Try
                '--- Identify the current user and their security context ---
                Dim userName As String = si.UserName
                Dim userRole As String = DetermineUserRole(si, userName)

                BRApi.ErrorLog.LogMessage(si, "MF_EntitySecurity: User=" & userName & ", Role=" & userRole)

                '--- Build the member filter expression based on the user's role ---
                Dim memberFilter As String = BuildEntityFilter(si, userRole, userName)

                '--- Return the filter expression to restrict the entity member list ---
                ' OneStream uses member filter expressions to control which members appear
                ' in dropdowns, grids, and input forms.
                Return memberFilter

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "MF_EntitySecurity", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Determines the user's role by checking their group memberships against
        ''' known security groups. Returns the highest-privilege matching role.
        ''' </summary>
        Private Function DetermineUserRole(ByVal si As SessionInfo, ByVal userName As String) As String
            Try
                '--- Check group memberships in order of highest to lowest privilege ---
                ' OneStream security groups are configured in the application's security settings

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_Corporate") Then
                    Return "Corporate"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_ExecutiveTeam") Then
                    Return "Executive"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_RegionalManagers") Then
                    Return "RegionalManager"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_PlantControllers") Then
                    Return "PlantController"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_Analysts") Then
                    Return "Analyst"
                End If

                '--- Default: minimal access (no entities) ---
                BRApi.ErrorLog.LogMessage(si, "MF_EntitySecurity: WARNING - User '" & userName &
                    "' not in any recognized security group. Returning empty filter.")
                Return "None"

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_EntitySecurity.DetermineUserRole: Error - " & ex.Message)
                Return "None"
            End Try
        End Function

        ''' <summary>
        ''' Builds the OneStream member filter expression for the Entity dimension
        ''' based on the user's role and, where applicable, their specific assignment.
        ''' </summary>
        Private Function BuildEntityFilter(ByVal si As SessionInfo, ByVal userRole As String,
                                            ByVal userName As String) As String
            Select Case userRole

                Case "Corporate"
                    '--- Corporate users: full access to all entities ---
                    Return "E#Root.Descendants"

                Case "Executive"
                    '--- Executives: see consolidated entities and all region-level parents ---
                    ' This includes the top consolidated entity plus all regional nodes,
                    ' but not necessarily individual plant detail (though they may drill down).
                    Return "E#Corporate_Consolidated.Base:E#Americas.Descendants:E#EMEA.Descendants:E#APAC.Descendants"

                Case "RegionalManager"
                    '--- Regional managers: see only their assigned region's entities ---
                    Dim regionName As String = GetUserAssignment(si, userName, "Region")
                    If Not String.IsNullOrEmpty(regionName) Then
                        Return "E#" & regionName & ".Descendants"
                    Else
                        BRApi.ErrorLog.LogMessage(si, "MF_EntitySecurity: No region assignment found for " & userName)
                        Return "E#NoAccess"
                    End If

                Case "PlantController"
                    '--- Plant controllers: see only their assigned plant entity ---
                    Dim plantName As String = GetUserAssignment(si, userName, "Plant")
                    If Not String.IsNullOrEmpty(plantName) Then
                        Return "E#" & plantName
                    Else
                        BRApi.ErrorLog.LogMessage(si, "MF_EntitySecurity: No plant assignment found for " & userName)
                        Return "E#NoAccess"
                    End If

                Case "Analyst"
                    '--- Analysts: same entity access as their organizational level ---
                    ' Analysts inherit access from their department assignment
                    Dim deptEntity As String = GetUserAssignment(si, userName, "Department")
                    If Not String.IsNullOrEmpty(deptEntity) Then
                        Return "E#" & deptEntity & ".Descendants"
                    Else
                        Return "E#Corporate_Consolidated"
                    End If

                Case Else
                    '--- No recognized role: return a dummy member that effectively blocks access ---
                    Return "E#NoAccess"

            End Select
        End Function

        ''' <summary>
        ''' Retrieves the user's organizational assignment (region, plant, or department)
        ''' from user properties or a mapping table. In production, this would typically
        ''' read from a custom security mapping stored in application settings or a
        ''' dashboard data management cube.
        ''' </summary>
        Private Function GetUserAssignment(ByVal si As SessionInfo, ByVal userName As String,
                                            ByVal assignmentType As String) As String
            Try
                '--- Read the assignment from user properties ---
                ' OneStream stores user-level custom properties that can be configured
                ' in the security administration module
                Dim propertyName As String = "UserAssignment_" & assignmentType
                Dim assignmentValue As String = BRApi.Security.Admin.GetUserProperty(si, userName, propertyName)

                If Not String.IsNullOrEmpty(assignmentValue) Then
                    Return assignmentValue.Trim()
                End If

                '--- Fallback: attempt to read from application substitution variables ---
                Dim substVarName As String = "User_" & userName.Replace(" ", "_") & "_" & assignmentType
                assignmentValue = BRApi.Finance.Data.GetSubstVarValue(si, substVarName)

                If Not String.IsNullOrEmpty(assignmentValue) Then
                    Return assignmentValue.Trim()
                End If

                Return String.Empty

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_EntitySecurity.GetUserAssignment: Error for " &
                    userName & "/" & assignmentType & " - " & ex.Message)
                Return String.Empty
            End Try
        End Function

    End Class

End Namespace

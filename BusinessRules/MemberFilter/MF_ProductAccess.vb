'------------------------------------------------------------------------------------------------------------
' MF_ProductAccess
' Member Filter Business Rule - Product Dimension Access Filtering
'
' Purpose:  Filters the Product dimension member list based on the current user's product
'           line assignment. Ensures product managers see only their product families,
'           plant managers see products manufactured at their facility, and corporate
'           users have full product visibility.
'
' Access Model:
'   Corporate User     -> All products (full hierarchy)
'   Product Manager    -> Their assigned product family and descendants
'   Plant Manager      -> Products manufactured at their assigned plant
'   Sales Manager      -> Products in their sales territory/channel
'   Default            -> Top-level summary only (no detail access)
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

Namespace OneStream.BusinessRule.MemberFilter.MF_ProductAccess

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As MemberFilterArgs) As Object
            Try
                '--- Identify the current user and their role ---
                Dim userName As String = si.UserName
                Dim userRole As String = DetermineProductRole(si, userName)

                BRApi.ErrorLog.LogMessage(si, "MF_ProductAccess: User=" & userName & ", ProductRole=" & userRole)

                '--- Build the product filter based on role ---
                Dim productFilter As String = BuildProductFilter(si, userRole, userName)

                Return productFilter

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "MF_ProductAccess", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Determines the user's product access role based on security group membership.
        ''' </summary>
        Private Function DetermineProductRole(ByVal si As SessionInfo, ByVal userName As String) As String
            Try
                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_Corporate") Then
                    Return "Corporate"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_ProductManagers") Then
                    Return "ProductManager"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_PlantManagers") Then
                    Return "PlantManager"
                End If

                If BRApi.Security.Admin.IsUserInGroup(si, userName, "GRP_SalesManagers") Then
                    Return "SalesManager"
                End If

                Return "Default"

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_ProductAccess.DetermineProductRole: Error - " & ex.Message)
                Return "Default"
            End Try
        End Function

        ''' <summary>
        ''' Builds the product dimension member filter expression based on the user's role
        ''' and their specific product/plant assignment.
        ''' </summary>
        Private Function BuildProductFilter(ByVal si As SessionInfo, ByVal userRole As String,
                                             ByVal userName As String) As String
            Select Case userRole

                Case "Corporate"
                    '--- Corporate: full access to all products ---
                    Return "UD1#TotalProducts.Descendants"

                Case "ProductManager"
                    '--- Product managers: their assigned product family and all children ---
                    Dim productFamily As String = GetUserProductAssignment(si, userName)
                    If Not String.IsNullOrEmpty(productFamily) Then
                        ' Include the family node plus all leaf products underneath
                        Return "UD1#" & productFamily & ".Descendants"
                    Else
                        BRApi.ErrorLog.LogMessage(si, "MF_ProductAccess: No product family assignment for " & userName)
                        Return "UD1#TotalProducts"
                    End If

                Case "PlantManager"
                    '--- Plant managers: products manufactured at their plant ---
                    Dim plantName As String = GetUserPlantAssignment(si, userName)
                    If Not String.IsNullOrEmpty(plantName) Then
                        Return BuildPlantProductFilter(si, plantName)
                    Else
                        BRApi.ErrorLog.LogMessage(si, "MF_ProductAccess: No plant assignment for " & userName)
                        Return "UD1#TotalProducts"
                    End If

                Case "SalesManager"
                    '--- Sales managers: products in their sales channel ---
                    Dim salesChannel As String = GetUserSalesChannel(si, userName)
                    If Not String.IsNullOrEmpty(salesChannel) Then
                        Return "UD1#" & salesChannel & ".Descendants"
                    Else
                        Return "UD1#TotalProducts"
                    End If

                Case Else
                    '--- Default: top-level summary only ---
                    Return "UD1#TotalProducts"

            End Select
        End Function

        ''' <summary>
        ''' Retrieves the user's product family assignment from user properties.
        ''' Product managers are assigned to one or more product families (e.g., "PF_Electronics",
        ''' "PF_Industrial"). Multiple assignments are separated by semicolons.
        ''' </summary>
        Private Function GetUserProductAssignment(ByVal si As SessionInfo, ByVal userName As String) As String
            Try
                Dim assignment As String = BRApi.Security.Admin.GetUserProperty(si, userName, "UserAssignment_ProductFamily")
                If Not String.IsNullOrEmpty(assignment) Then
                    '--- Handle multiple product family assignments ---
                    If assignment.Contains(";") Then
                        Dim families() As String = assignment.Split(";"c)
                        Dim filterParts As New List(Of String)
                        For Each family As String In families
                            Dim trimmed As String = family.Trim()
                            If Not String.IsNullOrEmpty(trimmed) Then
                                filterParts.Add("UD1#" & trimmed & ".Descendants")
                            End If
                        Next
                        Return String.Join(":", filterParts.ToArray())
                    End If
                    Return assignment.Trim()
                End If
                Return String.Empty
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_ProductAccess.GetUserProductAssignment: Error - " & ex.Message)
                Return String.Empty
            End Try
        End Function

        ''' <summary>
        ''' Retrieves the user's plant assignment for plant manager role.
        ''' </summary>
        Private Function GetUserPlantAssignment(ByVal si As SessionInfo, ByVal userName As String) As String
            Try
                Return BRApi.Security.Admin.GetUserProperty(si, userName, "UserAssignment_Plant")
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_ProductAccess.GetUserPlantAssignment: Error - " & ex.Message)
                Return String.Empty
            End Try
        End Function

        ''' <summary>
        ''' Builds a product filter for a specific manufacturing plant by reading
        ''' the plant-to-product mapping from application settings.
        ''' </summary>
        Private Function BuildPlantProductFilter(ByVal si As SessionInfo, ByVal plantName As String) As String
            Try
                '--- Read the plant-to-product mapping from substitution variables ---
                ' Convention: SubstVar "PlantProducts_US01" = "PRD_Widget;PRD_Gadget;PRD_Assembly"
                Dim mappingVar As String = "PlantProducts_" & plantName
                Dim productList As String = BRApi.Finance.Data.GetSubstVarValue(si, mappingVar)

                If Not String.IsNullOrEmpty(productList) Then
                    Dim products() As String = productList.Split(";"c)
                    Dim filterParts As New List(Of String)
                    For Each product As String In products
                        Dim trimmed As String = product.Trim()
                        If Not String.IsNullOrEmpty(trimmed) Then
                            filterParts.Add("UD1#" & trimmed & ".Descendants")
                        End If
                    Next
                    If filterParts.Count > 0 Then
                        Return String.Join(":", filterParts.ToArray())
                    End If
                End If

                '--- Fallback: show all products if mapping not defined ---
                BRApi.ErrorLog.LogMessage(si, "MF_ProductAccess: No plant-product mapping found for " & plantName)
                Return "UD1#TotalProducts"

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_ProductAccess.BuildPlantProductFilter: Error - " & ex.Message)
                Return "UD1#TotalProducts"
            End Try
        End Function

        ''' <summary>
        ''' Retrieves the user's sales channel assignment.
        ''' </summary>
        Private Function GetUserSalesChannel(ByVal si As SessionInfo, ByVal userName As String) As String
            Try
                Return BRApi.Security.Admin.GetUserProperty(si, userName, "UserAssignment_SalesChannel")
            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, "MF_ProductAccess.GetUserSalesChannel: Error - " & ex.Message)
                Return String.Empty
            End Try
        End Function

    End Class

End Namespace

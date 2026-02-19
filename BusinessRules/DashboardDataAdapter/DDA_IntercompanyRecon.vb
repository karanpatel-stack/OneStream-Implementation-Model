'------------------------------------------------------------------------------------------------------------
' DDA_IntercompanyRecon
' Dashboard DataAdapter Business Rule
' Purpose: Intercompany matching and exception reporting for IC balance reconciliation
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_IntercompanyRecon
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("IntercompanyRecon")
                        dt.Columns.Add("Entity1", GetType(String))
                        dt.Columns.Add("Entity2", GetType(String))
                        dt.Columns.Add("Entity1Amount", GetType(Double))
                        dt.Columns.Add("Entity2Amount", GetType(Double))
                        dt.Columns.Add("Difference", GetType(Double))
                        dt.Columns.Add("AbsDifference", GetType(Double))
                        dt.Columns.Add("Status", GetType(String))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim toleranceAmount As Double = Double.Parse(args.NameValuePairs.XFGetValue("Tolerance", "100"))
                        Dim matchType As String = args.NameValuePairs.XFGetValue("MatchType", "ARAP") ' ARAP or RevenueCOGS

                        ' Define the entity list for IC matching
                        Dim entities As New List(Of String)()
                        entities.Add("Entity_US01")
                        entities.Add("Entity_US02")
                        entities.Add("Entity_UK01")
                        entities.Add("Entity_DE01")
                        entities.Add("Entity_CN01")
                        entities.Add("Entity_BR01")
                        entities.Add("Entity_IN01")
                        entities.Add("Entity_MX01")

                        ' Determine which account pair to match based on match type
                        Dim entity1Account As String
                        Dim entity2Account As String
                        If matchType = "RevenueCOGS" Then
                            entity1Account = "A#IC_Revenue"
                            entity2Account = "A#IC_COGS"
                        Else
                            entity1Account = "A#IC_AR"
                            entity2Account = "A#IC_AP"
                        End If

                        ' Iterate through all unique entity pairs
                        For i As Integer = 0 To entities.Count - 2
                            For j As Integer = i + 1 To entities.Count - 1
                                Dim ent1 As String = entities(i)
                                Dim ent2 As String = entities(j)

                                ' Entity1's balance booked against Entity2 (IC partner)
                                Dim entity1Amount As Double = FetchICBalance(si, scenarioName, timeName, ent1, entity1Account, ent2)
                                ' Entity2's balance booked against Entity1 (IC partner)
                                Dim entity2Amount As Double = FetchICBalance(si, scenarioName, timeName, ent2, entity2Account, ent1)

                                ' Only include pairs where at least one side has a balance
                                If entity1Amount = 0 AndAlso entity2Amount = 0 Then Continue For

                                ' IC amounts should net to zero; AR should equal AP (opposite signs)
                                Dim difference As Double = entity1Amount + entity2Amount
                                Dim absDifference As Double = Math.Abs(difference)

                                ' Determine matching status based on tolerance
                                Dim status As String
                                If absDifference = 0 Then
                                    status = "Matched"
                                ElseIf absDifference <= toleranceAmount Then
                                    status = "Tolerance"
                                Else
                                    status = "Unmatched"
                                End If

                                Dim row As DataRow = dt.NewRow()
                                row("Entity1") = ent1
                                row("Entity2") = ent2
                                row("Entity1Amount") = Math.Round(entity1Amount, 2)
                                row("Entity2Amount") = Math.Round(entity2Amount, 2)
                                row("Difference") = Math.Round(difference, 2)
                                row("AbsDifference") = Math.Round(absDifference, 2)
                                row("Status") = status
                                dt.Rows.Add(row)
                            Next
                        Next

                        ' Sort by absolute difference descending so largest mismatches appear first
                        dt.DefaultView.Sort = "AbsDifference DESC"
                        Dim sortedDt As DataTable = dt.DefaultView.ToTable()

                        Return sortedDt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        Private Function FetchICBalance(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String, ByVal icPartner As String) As Double
            Try
                ' Build POV with specific IC partner dimension member
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#{4}:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account, icPartner)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
                ' Return zero on failure
            End Try
            Return 0
        End Function

    End Class
End Namespace

'------------------------------------------------------------------------------------------------------------
' DDA_AcctReconStatus
' Dashboard DataAdapter Business Rule
' Purpose: Account reconciliation status tracker with preparation, review, approval stages, and aging
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

Namespace OneStream.BusinessRule.DashboardDataAdapter.DDA_AcctReconStatus
    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardDataAdapterArgs) As Object
            Try
                Select Case args.FunctionType
                    Case Is = DashboardDataAdapterFunctionType.GetDataTable
                        Dim dt As New DataTable("AcctReconStatus")
                        dt.Columns.Add("AccountName", GetType(String))
                        dt.Columns.Add("Entity", GetType(String))
                        dt.Columns.Add("Balance", GetType(Double))
                        dt.Columns.Add("ReconStatus", GetType(String))
                        dt.Columns.Add("PreparedBy", GetType(String))
                        dt.Columns.Add("ReviewedBy", GetType(String))
                        dt.Columns.Add("PrepDate", GetType(String))
                        dt.Columns.Add("ReviewDate", GetType(String))
                        dt.Columns.Add("DaysSincePrep", GetType(Integer))
                        dt.Columns.Add("HasExceptions", GetType(Boolean))

                        ' Dashboard parameters
                        Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                        Dim timeName As String = args.NameValuePairs.XFGetValue("Time", si.WorkflowClusterPk.TimeName)
                        Dim entityFilter As String = args.NameValuePairs.XFGetValue("Entity", "AllEntities")
                        Dim statusFilter As String = args.NameValuePairs.XFGetValue("StatusFilter", "All")

                        ' Define accounts that require reconciliation
                        Dim reconAccounts As New List(Of String)()
                        reconAccounts.Add("Cash & Equivalents")
                        reconAccounts.Add("Accounts Receivable")
                        reconAccounts.Add("Inventory")
                        reconAccounts.Add("Prepaid Expenses")
                        reconAccounts.Add("Fixed Assets")
                        reconAccounts.Add("Goodwill & Intangibles")
                        reconAccounts.Add("Accounts Payable")
                        reconAccounts.Add("Accrued Liabilities")
                        reconAccounts.Add("Current Debt")
                        reconAccounts.Add("Long-Term Debt")
                        reconAccounts.Add("Deferred Revenue")
                        reconAccounts.Add("Intercompany Receivable")
                        reconAccounts.Add("Intercompany Payable")
                        reconAccounts.Add("Retained Earnings")

                        ' Define entities
                        Dim entities As New List(Of String)()
                        entities.Add("Entity_US01")
                        entities.Add("Entity_US02")
                        entities.Add("Entity_UK01")
                        entities.Add("Entity_DE01")
                        entities.Add("Entity_CN01")
                        entities.Add("Entity_BR01")

                        ' Map display account names to system account members
                        Dim accountMemberMap As New Dictionary(Of String, String)()
                        accountMemberMap.Add("Cash & Equivalents", "A#CashAndEquivalents")
                        accountMemberMap.Add("Accounts Receivable", "A#AccountsReceivable")
                        accountMemberMap.Add("Inventory", "A#Inventory")
                        accountMemberMap.Add("Prepaid Expenses", "A#PrepaidExpenses")
                        accountMemberMap.Add("Fixed Assets", "A#PPE_Net")
                        accountMemberMap.Add("Goodwill & Intangibles", "A#GoodwillIntangibles")
                        accountMemberMap.Add("Accounts Payable", "A#AccountsPayable")
                        accountMemberMap.Add("Accrued Liabilities", "A#AccruedLiabilities")
                        accountMemberMap.Add("Current Debt", "A#CurrentDebt")
                        accountMemberMap.Add("Long-Term Debt", "A#LongTermDebt")
                        accountMemberMap.Add("Deferred Revenue", "A#DeferredRevenue")
                        accountMemberMap.Add("Intercompany Receivable", "A#IC_AR")
                        accountMemberMap.Add("Intercompany Payable", "A#IC_AP")
                        accountMemberMap.Add("Retained Earnings", "A#RetainedEarnings")

                        Dim today As DateTime = DateTime.Now

                        For Each entityName In entities
                            ' Apply entity filter
                            If entityFilter <> "AllEntities" AndAlso entityName <> entityFilter Then Continue For

                            For Each acctName In reconAccounts
                                Dim accountMember As String = accountMemberMap(acctName)

                                ' Fetch the account balance
                                Dim balance As Double = FetchBalance(si, scenarioName, timeName, entityName, accountMember)

                                ' Fetch reconciliation status metadata
                                Dim reconStatusCode As Integer = CInt(FetchReconMetric(si, scenarioName, timeName, entityName, accountMember, "Recon_Status"))
                                Dim reconStatus As String = DecodeReconStatus(reconStatusCode)

                                ' Apply status filter
                                If statusFilter <> "All" AndAlso reconStatus <> statusFilter Then Continue For

                                ' Fetch preparer and reviewer information
                                Dim preparerCode As Integer = CInt(FetchReconMetric(si, scenarioName, timeName, entityName, accountMember, "Recon_PreparedBy"))
                                Dim reviewerCode As Integer = CInt(FetchReconMetric(si, scenarioName, timeName, entityName, accountMember, "Recon_ReviewedBy"))
                                Dim preparedBy As String = If(preparerCode > 0, "User_" & preparerCode.ToString(), String.Empty)
                                Dim reviewedBy As String = If(reviewerCode > 0, "User_" & reviewerCode.ToString(), String.Empty)

                                ' Fetch date stamps (stored as numeric YYYYMMDD)
                                Dim prepDateNum As Integer = CInt(FetchReconMetric(si, scenarioName, timeName, entityName, accountMember, "Recon_PrepDate"))
                                Dim reviewDateNum As Integer = CInt(FetchReconMetric(si, scenarioName, timeName, entityName, accountMember, "Recon_ReviewDate"))
                                Dim prepDate As String = FormatDateFromNumeric(prepDateNum)
                                Dim reviewDate As String = FormatDateFromNumeric(reviewDateNum)

                                ' Calculate days since preparation
                                Dim daysSincePrep As Integer = 0
                                If prepDateNum > 0 Then
                                    Try
                                        Dim prepDateTime As New DateTime(prepDateNum \ 10000, (prepDateNum Mod 10000) \ 100, prepDateNum Mod 100)
                                        daysSincePrep = CInt((today - prepDateTime).TotalDays)
                                    Catch
                                        daysSincePrep = 0
                                    End Try
                                End If

                                ' Check for exceptions
                                Dim exceptionFlag As Double = FetchReconMetric(si, scenarioName, timeName, entityName, accountMember, "Recon_HasException")
                                Dim hasExceptions As Boolean = (exceptionFlag > 0)

                                Dim row As DataRow = dt.NewRow()
                                row("AccountName") = acctName
                                row("Entity") = entityName.Replace("Entity_", "")
                                row("Balance") = Math.Round(balance, 2)
                                row("ReconStatus") = reconStatus
                                row("PreparedBy") = preparedBy
                                row("ReviewedBy") = reviewedBy
                                row("PrepDate") = prepDate
                                row("ReviewDate") = reviewDate
                                row("DaysSincePrep") = daysSincePrep
                                row("HasExceptions") = hasExceptions
                                dt.Rows.Add(row)
                            Next
                        Next

                        ' Sort by status priority: Not Started > Prepared > Reviewed > Approved
                        dt.DefaultView.Sort = "ReconStatus ASC, DaysSincePrep DESC"
                        Dim sortedDt As DataTable = dt.DefaultView.ToTable()

                        Return sortedDt

                    Case Else
                        Return Nothing
                End Select

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try
        End Function

        Private Function DecodeReconStatus(ByVal statusCode As Integer) As String
            Select Case statusCode
                Case 0 : Return "Not Started"
                Case 1 : Return "Prepared"
                Case 2 : Return "Reviewed"
                Case 3 : Return "Approved"
                Case Else : Return "Not Started"
            End Select
        End Function

        Private Function FormatDateFromNumeric(ByVal dateNum As Integer) As String
            If dateNum <= 0 Then Return String.Empty
            Try
                Dim year As Integer = dateNum \ 10000
                Dim month As Integer = (dateNum Mod 10000) \ 100
                Dim day As Integer = dateNum Mod 100
                Return New DateTime(year, month, day).ToString("yyyy-MM-dd")
            Catch
                Return String.Empty
            End Try
        End Function

        Private Function FetchBalance(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String) As Double
            Try
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#[None]:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
            End Try
            Return 0
        End Function

        Private Function FetchReconMetric(ByVal si As SessionInfo, ByVal scenario As String, ByVal timeName As String, ByVal entity As String, ByVal account As String, ByVal reconField As String) As Double
            Try
                ' Recon metadata stored in UD dimensions: U1 = recon field type, account = the balance sheet account
                Dim povString As String = String.Format(
                    "S#{0}:T#{1}:E#{2}:{3}:V#Periodic:F#EndBal:O#Forms:IC#[ICP None]:U1#{4}:U2#[None]:U3#[None]:U4#[None]:U5#[None]:U6#[None]:U7#[None]:U8#[None]",
                    scenario, timeName, entity, account, reconField)
                Dim dataCell As DataCell = BRApi.Finance.Data.GetDataCell(si, povString)
                If dataCell IsNot Nothing AndAlso dataCell.CellStatus <> CellStatus.NoData Then
                    Return dataCell.CellAmount
                End If
            Catch
            End Try
            Return 0
        End Function

    End Class
End Namespace

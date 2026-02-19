'------------------------------------------------------------------------------------------------------------
' OneStream XF Connector Business Rule: CN_NetSuite_GL
'------------------------------------------------------------------------------------------------------------
' Purpose:     Extracts General Ledger data from NetSuite via SuiteAnalytics Connect (ODBC).
'              Queries TRANSACTION_LINES joined with TRANSACTIONS, ACCOUNTS, and SUBSIDIARIES
'              to extract full chart of accounts detail. Handles NetSuite multi-book accounting,
'              maps subsidiaries to OneStream entities, accounts to OneStream account dimension,
'              and loads transformed data to staging.
'
' Source:      NetSuite SuiteAnalytics Connect (ODBC2 driver)
'              - TRANSACTION_LINES (journal detail)
'              - TRANSACTIONS (transaction headers)
'              - ACCOUNTS (chart of accounts)
'              - SUBSIDIARIES (legal entity / subsidiary structure)
'              - DEPARTMENTS, CLASSES, LOCATIONS (classification segments)
'
' Author:      OneStream Administrator
' Created:     2026-02-18
' Modified:    2026-02-18
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Data.Odbc
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

Namespace OneStream.BusinessRule.Connector.CN_NetSuite_GL

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As ConnectorArgs) As Object
            Try
                Select Case args.ActionType
                    Case Is = ConnectorActionTypes.UI
                        Return Me.SetupUI(si, args)

                    Case Is = ConnectorActionTypes.Initialize
                        Me.ValidateConnection(si, args)

                    Case Is = ConnectorActionTypes.GetData
                        Dim rowsLoaded As Long = Me.ExtractNetSuiteGL(si, args)
                        BRApi.ErrorLog.LogMessage(si, $"CN_NetSuite_GL: Extraction complete. Rows loaded: {rowsLoaded}")

                    Case Is = ConnectorActionTypes.Finalize
                        BRApi.ErrorLog.LogMessage(si, "CN_NetSuite_GL: Finalize phase completed.")
                End Select

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_NetSuite_GL.Main", ex.Message))
            End Try
        End Function

        Private Function SetupUI(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Object
            Try
                Dim paramList As New List(Of DashboardDataSetParam)

                paramList.Add(New DashboardDataSetParam("ConnectionString", _
                    "NetSuite SuiteAnalytics ODBC Connection String", _
                    "DSN=NetSuite;UID=ONESTREAM_INT;PWD=;", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("SubsidiaryFilter", _
                    "NetSuite Subsidiary IDs (comma-separated, blank=all)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("PeriodStart", _
                    "Period Start Date (YYYY-MM-DD)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("PeriodEnd", _
                    "Period End Date (YYYY-MM-DD)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("AccountingBook", _
                    "Accounting Book ID (blank=primary)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ExcludeEliminations", _
                    "Exclude Elimination Subsidiaries (True/False)", _
                    "True", _
                    DashboardDataSetParamTypes.Text))

                Return paramList

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_NetSuite_GL.SetupUI", ex.Message))
            End Try
        End Function

        Private Sub ValidateConnection(ByVal si As SessionInfo, ByVal args As ConnectorArgs)
            Try
                Dim connString As String = args.GetParameterValue("ConnectionString")
                If String.IsNullOrWhiteSpace(connString) Then
                    Throw New XFException(si, "CN_NetSuite_GL", "Connection string is required.")
                End If

                Using conn As New OdbcConnection(connString)
                    conn.ConnectionTimeout = 60  ' NetSuite ODBC can be slow to connect
                    conn.Open()
                    BRApi.ErrorLog.LogMessage(si, "CN_NetSuite_GL: NetSuite SuiteAnalytics connection test successful.")
                    conn.Close()
                End Using

            Catch ex As OdbcException
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_NetSuite_GL.ValidateConnection", _
                    $"NetSuite connection failed: {ex.Message}"))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ExtractNetSuiteGL: Queries NetSuite transaction lines with account and subsidiary detail,
        ' applies multi-book logic, maps to OneStream dimensions, and loads to staging.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractNetSuiteGL(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim rowCount As Long = 0
            Dim errorCount As Long = 0
            Dim skipCount As Long = 0

            Dim connString As String = args.GetParameterValue("ConnectionString")
            Dim subsidiaryFilter As String = args.GetParameterValue("SubsidiaryFilter")
            Dim periodStart As String = args.GetParameterValue("PeriodStart")
            Dim periodEnd As String = args.GetParameterValue("PeriodEnd")
            Dim accountingBook As String = args.GetParameterValue("AccountingBook")
            Dim excludeEliminations As Boolean = Boolean.Parse(If(args.GetParameterValue("ExcludeEliminations"), "True"))

            ' Load mappings
            Dim subsidiaryMap As Dictionary(Of String, String) = Me.BuildSubsidiaryMapping(si)
            Dim accountMap As Dictionary(Of String, String) = Me.BuildNetSuiteAccountMapping(si)

            Try
                Using conn As New OdbcConnection(connString)
                    conn.Open()

                    Dim sql As String = Me.BuildNetSuiteQuery(subsidiaryFilter, periodStart, periodEnd, _
                        accountingBook, excludeEliminations)

                    BRApi.ErrorLog.LogMessage(si, "CN_NetSuite_GL: Executing NetSuite GL query...")

                    Using cmd As New OdbcCommand(sql, conn)
                        cmd.CommandTimeout = 900  ' 15-min timeout; SuiteAnalytics queries can be slow

                        Using reader As OdbcDataReader = cmd.ExecuteReader()
                            Dim dt As DataTable = args.GetDataTable()

                            While reader.Read()
                                Try
                                    ' Extract NetSuite fields
                                    Dim subsidiaryId As String = reader("SUBSIDIARY_ID").ToString().Trim()
                                    Dim subsidiaryName As String = reader("SUBSIDIARY_NAME").ToString().Trim()
                                    Dim accountNumber As String = reader("ACCOUNT_NUMBER").ToString().Trim()
                                    Dim accountName As String = reader("ACCOUNT_NAME").ToString().Trim()
                                    Dim accountType As String = reader("ACCOUNT_TYPE").ToString().Trim()
                                    Dim department As String = reader("DEPARTMENT_NAME").ToString().Trim()
                                    Dim className As String = reader("CLASS_NAME").ToString().Trim()
                                    Dim location As String = reader("LOCATION_NAME").ToString().Trim()
                                    Dim transactionDate As DateTime = Convert.ToDateTime(reader("TRANSACTION_DATE"))
                                    Dim amount As Decimal = Convert.ToDecimal(If(reader("AMOUNT") Is DBNull.Value, 0, reader("AMOUNT")))
                                    Dim netAmount As Decimal = Convert.ToDecimal(If(reader("NET_AMOUNT") Is DBNull.Value, 0, reader("NET_AMOUNT")))
                                    Dim currency As String = reader("CURRENCY_NAME").ToString().Trim()
                                    Dim transactionId As String = reader("TRANSACTION_ID").ToString().Trim()
                                    Dim transactionType As String = reader("TRANSACTION_TYPE").ToString().Trim()
                                    Dim memo As String = reader("MEMO").ToString().Trim()

                                    ' Map NetSuite subsidiary to OneStream entity
                                    Dim osEntity As String = ""
                                    If Not subsidiaryMap.TryGetValue(subsidiaryId, osEntity) Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"CN_NetSuite_GL: WARNING - Unmapped subsidiary [{subsidiaryId}:{subsidiaryName}]. Skipping.")
                                        skipCount += 1
                                        Continue While
                                    End If

                                    ' Map NetSuite account to OneStream account
                                    Dim osAccount As String = ""
                                    If Not accountMap.TryGetValue(accountNumber, osAccount) Then
                                        ' Fallback: use account type prefix with number
                                        osAccount = Me.DeriveAccountFromType(accountNumber, accountType)
                                    End If

                                    ' Map transaction date to OneStream time period
                                    Dim osTimePeriod As String = $"{transactionDate.Year}M{transactionDate.Month}"

                                    ' Map department to UD1
                                    Dim osDepartment As String = If(String.IsNullOrWhiteSpace(department), _
                                        "UD1_None", $"UD1_{department.Replace(" ", "_")}")

                                    ' Map class to UD2 (typically product line in NetSuite)
                                    Dim osClass As String = If(String.IsNullOrWhiteSpace(className), _
                                        "UD2_None", $"UD2_{className.Replace(" ", "_")}")

                                    ' Map location to UD3
                                    Dim osLocation As String = If(String.IsNullOrWhiteSpace(location), _
                                        "UD3_None", $"UD3_{location.Replace(" ", "_")}")

                                    ' NetSuite stores amounts with sign convention:
                                    ' Debits are positive for debit-normal accounts, credits are negative
                                    ' We use the net_amount which already has proper sign
                                    Dim signedAmount As Decimal = netAmount

                                    ' Add row to staging
                                    Dim newRow As DataRow = dt.NewRow()
                                    newRow("Entity") = osEntity
                                    newRow("Account") = osAccount
                                    newRow("Time") = osTimePeriod
                                    newRow("Scenario") = "Actual"
                                    newRow("Flow") = "F_None"
                                    newRow("Origin") = "O_None"
                                    newRow("IC") = "I_None"
                                    newRow("UD1") = osDepartment
                                    newRow("UD2") = osClass
                                    newRow("UD3") = osLocation
                                    newRow("UD4") = "UD4_None"
                                    newRow("Amount") = signedAmount
                                    newRow("Currency") = currency
                                    newRow("Description") = $"NS TxnID:{transactionId} Type:{transactionType} {memo}"
                                    dt.Rows.Add(newRow)

                                    rowCount += 1

                                    If rowCount Mod 10000 = 0 Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"CN_NetSuite_GL: Progress - {rowCount} rows processed...")
                                    End If

                                Catch exRow As Exception
                                    errorCount += 1
                                    BRApi.ErrorLog.LogMessage(si, $"CN_NetSuite_GL: Row error: {exRow.Message}")
                                    If errorCount > 1000 Then
                                        Throw New XFException(si, "CN_NetSuite_GL", "Error threshold exceeded.")
                                    End If
                                End Try
                            End While
                        End Using
                    End Using

                    conn.Close()
                End Using

                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_NetSuite_GL: Summary - Loaded:{rowCount}, Skipped:{skipCount}, Errors:{errorCount}")

                Return rowCount

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_NetSuite_GL.ExtractNetSuiteGL", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildNetSuiteQuery: Constructs the SuiteAnalytics SQL query joining transaction lines
        ' with accounts, subsidiaries, and classification dimensions.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildNetSuiteQuery(ByVal subsidiaryFilter As String, ByVal periodStart As String, _
                                             ByVal periodEnd As String, ByVal accountingBook As String, _
                                             ByVal excludeEliminations As Boolean) As String
            Dim sql As String = _
                "SELECT " & _
                "  TL.SUBSIDIARY_ID, S.NAME AS SUBSIDIARY_NAME, " & _
                "  A.ACCOUNT_NUMBER, A.NAME AS ACCOUNT_NAME, A.TYPE_NAME AS ACCOUNT_TYPE, " & _
                "  D.NAME AS DEPARTMENT_NAME, CL.NAME AS CLASS_NAME, L.NAME AS LOCATION_NAME, " & _
                "  T.TRANSACTION_DATE, TL.AMOUNT, TL.NET_AMOUNT, " & _
                "  CUR.NAME AS CURRENCY_NAME, " & _
                "  T.TRANSACTION_ID, T.TRANSACTION_TYPE, TL.MEMO " & _
                "FROM TRANSACTION_LINES TL " & _
                "INNER JOIN TRANSACTIONS T ON TL.TRANSACTION_ID = T.TRANSACTION_ID " & _
                "INNER JOIN ACCOUNTS A ON TL.ACCOUNT_ID = A.ACCOUNT_ID " & _
                "INNER JOIN SUBSIDIARIES S ON TL.SUBSIDIARY_ID = S.SUBSIDIARY_ID " & _
                "LEFT JOIN DEPARTMENTS D ON TL.DEPARTMENT_ID = D.DEPARTMENT_ID " & _
                "LEFT JOIN CLASSES CL ON TL.CLASS_ID = CL.CLASS_ID " & _
                "LEFT JOIN LOCATIONS L ON TL.LOCATION_ID = L.LOCATION_ID " & _
                "LEFT JOIN CURRENCIES CUR ON T.CURRENCY_ID = CUR.CURRENCY_ID " & _
                "WHERE T.TRANSACTION_TYPE NOT IN ('Opportunity', 'Estimate') "

            ' Apply accounting book filter for multi-book support
            If Not String.IsNullOrWhiteSpace(accountingBook) Then
                sql &= $" AND TL.ACCOUNTING_BOOK_ID = {accountingBook}"
            End If

            ' Apply subsidiary filter
            If Not String.IsNullOrWhiteSpace(subsidiaryFilter) Then
                Dim subs As String() = subsidiaryFilter.Split(","c)
                Dim inClause As String = String.Join(",", subs.Select(Function(s) s.Trim()))
                sql &= $" AND TL.SUBSIDIARY_ID IN ({inClause})"
            End If

            ' Apply date filters
            If Not String.IsNullOrWhiteSpace(periodStart) Then
                sql &= $" AND T.TRANSACTION_DATE >= TO_DATE('{periodStart}', 'YYYY-MM-DD')"
            End If
            If Not String.IsNullOrWhiteSpace(periodEnd) Then
                sql &= $" AND T.TRANSACTION_DATE <= TO_DATE('{periodEnd}', 'YYYY-MM-DD')"
            End If

            ' Exclude elimination subsidiaries if requested
            If excludeEliminations Then
                sql &= " AND S.IS_ELIMINATION = 'No'"
            End If

            sql &= " ORDER BY TL.SUBSIDIARY_ID, T.TRANSACTION_DATE, T.TRANSACTION_ID"

            Return sql
        End Function

        '----------------------------------------------------------------------------------------------------
        ' DeriveAccountFromType: When no explicit mapping exists, derives a OneStream account member
        ' from the NetSuite account type and number.
        '----------------------------------------------------------------------------------------------------
        Private Function DeriveAccountFromType(ByVal accountNumber As String, ByVal accountType As String) As String
            Select Case accountType.ToLower()
                Case "bank", "other current asset", "fixed asset", "other asset"
                    Return $"A_BS_{accountNumber}"
                Case "accounts receivable"
                    Return "A_BS_AR"
                Case "accounts payable"
                    Return "A_BS_AP"
                Case "other current liability", "long term liability", "credit card"
                    Return $"A_BS_Liab_{accountNumber}"
                Case "equity"
                    Return $"A_BS_Equity_{accountNumber}"
                Case "income"
                    Return $"A_Revenue_{accountNumber}"
                Case "cost of goods sold"
                    Return $"A_COGS_{accountNumber}"
                Case "expense"
                    Return $"A_OpEx_{accountNumber}"
                Case "other income"
                    Return $"A_OtherIncome_{accountNumber}"
                Case "other expense"
                    Return $"A_OtherExpense_{accountNumber}"
                Case Else
                    Return $"A_Other_{accountNumber}"
            End Select
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildSubsidiaryMapping: Maps NetSuite subsidiary IDs to OneStream entity members.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildSubsidiaryMapping(ByVal si As SessionInfo) As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            map.Add("1", "E_USCorp")
            map.Add("2", "E_USWest")
            map.Add("3", "E_USEast")
            map.Add("4", "E_UKCorp")
            map.Add("5", "E_DECorp")
            map.Add("6", "E_FRParis")
            map.Add("7", "E_JPTokyo")
            map.Add("8", "E_AUSydney")
            Return map
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildNetSuiteAccountMapping: Maps NetSuite account numbers to OneStream account members.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildNetSuiteAccountMapping(ByVal si As SessionInfo) As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            map.Add("1000", "A_BS_Cash")
            map.Add("1100", "A_BS_AR")
            map.Add("1200", "A_BS_Inventory")
            map.Add("1300", "A_BS_Prepaid")
            map.Add("1500", "A_BS_FixedAssets")
            map.Add("1550", "A_BS_AccumDepr")
            map.Add("2000", "A_BS_AP")
            map.Add("2100", "A_BS_AccruedLiab")
            map.Add("2500", "A_BS_LongTermDebt")
            map.Add("3000", "A_BS_RetainedEarnings")
            map.Add("4000", "A_Revenue_Product")
            map.Add("4100", "A_Revenue_Service")
            map.Add("4200", "A_Revenue_Other")
            map.Add("5000", "A_COGS_Material")
            map.Add("5100", "A_COGS_Labor")
            map.Add("5200", "A_COGS_Overhead")
            map.Add("6000", "A_SGA_Salaries")
            map.Add("6100", "A_SGA_Benefits")
            map.Add("6200", "A_SGA_Rent")
            map.Add("6300", "A_SGA_Depreciation")
            map.Add("6400", "A_SGA_Travel")
            map.Add("6500", "A_SGA_Professional")
            map.Add("6600", "A_SGA_Marketing")
            Return map
        End Function

    End Class

End Namespace

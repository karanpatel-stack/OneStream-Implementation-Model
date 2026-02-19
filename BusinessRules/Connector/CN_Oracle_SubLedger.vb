'------------------------------------------------------------------------------------------------------------
' OneStream XF Connector Business Rule: CN_Oracle_SubLedger
'------------------------------------------------------------------------------------------------------------
' Purpose:     Extracts Accounts Payable and Accounts Receivable sub-ledger detail from Oracle EBS.
'              Queries AP_INVOICES_ALL for payables and AR_PAYMENT_SCHEDULES_ALL for receivables,
'              calculates aging buckets (Current, 30, 60, 90, 120+ days), and loads to OneStream
'              staging for reconciliation and working capital analysis.
'
' Source:      Oracle EBS R12
'              - AP_INVOICES_ALL, AP_PAYMENT_SCHEDULES_ALL (Accounts Payable)
'              - AR_PAYMENT_SCHEDULES_ALL, HZ_CUST_ACCOUNTS (Accounts Receivable)
' Target:      OneStream staging for working capital reporting cube
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

Namespace OneStream.BusinessRule.Connector.CN_Oracle_SubLedger

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
                        Dim apRows As Long = Me.ExtractAPData(si, args)
                        Dim arRows As Long = Me.ExtractARData(si, args)
                        BRApi.ErrorLog.LogMessage(si, _
                            $"CN_Oracle_SubLedger: Extraction complete. AP rows: {apRows}, AR rows: {arRows}")

                    Case Is = ConnectorActionTypes.Finalize
                        BRApi.ErrorLog.LogMessage(si, "CN_Oracle_SubLedger: Finalize phase completed.")
                End Select

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Oracle_SubLedger.Main", ex.Message))
            End Try
        End Function

        Private Function SetupUI(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Object
            Try
                Dim paramList As New List(Of DashboardDataSetParam)

                paramList.Add(New DashboardDataSetParam("ConnectionString", _
                    "Oracle DB Connection String (ODBC)", _
                    "DSN=OracleEBS;UID=ONESTREAM_SVC;PWD=;", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("OrgId", _
                    "Oracle Operating Unit Org ID", _
                    "101", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("AsOfDate", _
                    "Aging As-Of Date (YYYY-MM-DD, blank=today)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ExtractAP", _
                    "Extract AP Sub-Ledger (True/False)", _
                    "True", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ExtractAR", _
                    "Extract AR Sub-Ledger (True/False)", _
                    "True", _
                    DashboardDataSetParamTypes.Text))

                Return paramList

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Oracle_SubLedger.SetupUI", ex.Message))
            End Try
        End Function

        Private Sub ValidateConnection(ByVal si As SessionInfo, ByVal args As ConnectorArgs)
            Try
                Dim connString As String = args.GetParameterValue("ConnectionString")
                Using conn As New OdbcConnection(connString)
                    conn.ConnectionTimeout = 30
                    conn.Open()
                    BRApi.ErrorLog.LogMessage(si, "CN_Oracle_SubLedger: Connection test successful.")
                    conn.Close()
                End Using
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Oracle_SubLedger.ValidateConnection", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ExtractAPData: Extracts AP invoice and payment data, calculates aging, loads to staging.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractAPData(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim extractAP As Boolean = Boolean.Parse(If(args.GetParameterValue("ExtractAP"), "True"))
            If Not extractAP Then Return 0

            Dim rowCount As Long = 0
            Dim connString As String = args.GetParameterValue("ConnectionString")
            Dim orgId As String = If(args.GetParameterValue("OrgId"), "101")
            Dim asOfDate As DateTime = Me.GetAsOfDate(args.GetParameterValue("AsOfDate"))

            Try
                Using conn As New OdbcConnection(connString)
                    conn.Open()

                    Dim sql As String = _
                        "SELECT " & _
                        "  I.INVOICE_ID, I.INVOICE_NUM, I.VENDOR_ID, V.VENDOR_NAME, " & _
                        "  I.INVOICE_AMOUNT, I.AMOUNT_PAID, " & _
                        "  (I.INVOICE_AMOUNT - NVL(I.AMOUNT_PAID, 0)) AS AMOUNT_REMAINING, " & _
                        "  I.INVOICE_DATE, PS.DUE_DATE, I.PAYMENT_STATUS_FLAG, " & _
                        "  I.INVOICE_CURRENCY_CODE, I.ORG_ID " & _
                        "FROM AP_INVOICES_ALL I " & _
                        "INNER JOIN AP_SUPPLIERS V ON I.VENDOR_ID = V.VENDOR_ID " & _
                        "LEFT JOIN AP_PAYMENT_SCHEDULES_ALL PS ON I.INVOICE_ID = PS.INVOICE_ID " & _
                        $"WHERE I.ORG_ID = {orgId} " & _
                        "  AND I.PAYMENT_STATUS_FLAG <> 'Y' " & _
                        "  AND (I.INVOICE_AMOUNT - NVL(I.AMOUNT_PAID, 0)) <> 0 " & _
                        "ORDER BY I.VENDOR_ID, I.INVOICE_DATE"

                    BRApi.ErrorLog.LogMessage(si, "CN_Oracle_SubLedger: Extracting AP sub-ledger data...")

                    Using cmd As New OdbcCommand(sql, conn)
                        cmd.CommandTimeout = 300
                        Using reader As OdbcDataReader = cmd.ExecuteReader()
                            Dim dt As DataTable = args.GetDataTable()

                            While reader.Read()
                                Try
                                    Dim invoiceNum As String = reader("INVOICE_NUM").ToString().Trim()
                                    Dim vendorName As String = reader("VENDOR_NAME").ToString().Trim()
                                    Dim amountRemaining As Decimal = Convert.ToDecimal(reader("AMOUNT_REMAINING"))
                                    Dim dueDate As DateTime = Convert.ToDateTime(reader("DUE_DATE"))
                                    Dim currency As String = reader("INVOICE_CURRENCY_CODE").ToString().Trim()

                                    ' Calculate aging bucket based on as-of date
                                    Dim daysPastDue As Integer = CInt((asOfDate - dueDate).TotalDays)
                                    Dim agingBucket As String = Me.DetermineAgingBucket(daysPastDue)

                                    ' Map org to OneStream entity
                                    Dim osEntity As String = Me.MapOrgToEntity(orgId)

                                    ' Build the staging row for AP aging
                                    Dim newRow As DataRow = dt.NewRow()
                                    newRow("Entity") = osEntity
                                    newRow("Account") = $"A_AP_{agingBucket}"
                                    newRow("Time") = $"{asOfDate.Year}M{asOfDate.Month}"
                                    newRow("Scenario") = "Actual"
                                    newRow("Flow") = "F_None"
                                    newRow("Origin") = "O_None"
                                    newRow("IC") = "I_None"
                                    newRow("UD1") = "UD1_AP"
                                    newRow("UD2") = "UD2_None"
                                    newRow("UD3") = "UD3_None"
                                    newRow("UD4") = "UD4_None"
                                    newRow("Amount") = amountRemaining
                                    newRow("Currency") = currency
                                    newRow("Description") = $"AP Inv:{invoiceNum} Vendor:{vendorName} Due:{dueDate:yyyy-MM-dd} Days:{daysPastDue}"
                                    dt.Rows.Add(newRow)

                                    rowCount += 1

                                Catch exRow As Exception
                                    BRApi.ErrorLog.LogMessage(si, $"CN_Oracle_SubLedger: AP row error: {exRow.Message}")
                                End Try
                            End While
                        End Using
                    End Using

                    conn.Close()
                End Using

                BRApi.ErrorLog.LogMessage(si, $"CN_Oracle_SubLedger: AP rows loaded: {rowCount}")
                Return rowCount

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Oracle_SubLedger.ExtractAPData", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExtractARData: Extracts AR invoice and receipt data, calculates aging, loads to staging.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractARData(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim extractAR As Boolean = Boolean.Parse(If(args.GetParameterValue("ExtractAR"), "True"))
            If Not extractAR Then Return 0

            Dim rowCount As Long = 0
            Dim connString As String = args.GetParameterValue("ConnectionString")
            Dim orgId As String = If(args.GetParameterValue("OrgId"), "101")
            Dim asOfDate As DateTime = Me.GetAsOfDate(args.GetParameterValue("AsOfDate"))

            Try
                Using conn As New OdbcConnection(connString)
                    conn.Open()

                    Dim sql As String = _
                        "SELECT " & _
                        "  PS.PAYMENT_SCHEDULE_ID, PS.TRX_NUMBER, " & _
                        "  CA.ACCOUNT_NUMBER AS CUSTOMER_NUMBER, " & _
                        "  P.PARTY_NAME AS CUSTOMER_NAME, " & _
                        "  PS.AMOUNT_DUE_ORIGINAL, PS.AMOUNT_DUE_REMAINING, " & _
                        "  PS.TRX_DATE, PS.DUE_DATE, PS.STATUS, " & _
                        "  PS.INVOICE_CURRENCY_CODE, PS.ORG_ID " & _
                        "FROM AR_PAYMENT_SCHEDULES_ALL PS " & _
                        "INNER JOIN HZ_CUST_ACCOUNTS CA ON PS.CUSTOMER_ID = CA.CUST_ACCOUNT_ID " & _
                        "INNER JOIN HZ_PARTIES P ON CA.PARTY_ID = P.PARTY_ID " & _
                        $"WHERE PS.ORG_ID = {orgId} " & _
                        "  AND PS.STATUS = 'OP' " & _
                        "  AND PS.AMOUNT_DUE_REMAINING <> 0 " & _
                        "ORDER BY CA.ACCOUNT_NUMBER, PS.TRX_DATE"

                    BRApi.ErrorLog.LogMessage(si, "CN_Oracle_SubLedger: Extracting AR sub-ledger data...")

                    Using cmd As New OdbcCommand(sql, conn)
                        cmd.CommandTimeout = 300
                        Using reader As OdbcDataReader = cmd.ExecuteReader()
                            Dim dt As DataTable = args.GetDataTable()

                            While reader.Read()
                                Try
                                    Dim trxNumber As String = reader("TRX_NUMBER").ToString().Trim()
                                    Dim customerName As String = reader("CUSTOMER_NAME").ToString().Trim()
                                    Dim amountRemaining As Decimal = Convert.ToDecimal(reader("AMOUNT_DUE_REMAINING"))
                                    Dim dueDate As DateTime = Convert.ToDateTime(reader("DUE_DATE"))
                                    Dim currency As String = reader("INVOICE_CURRENCY_CODE").ToString().Trim()

                                    ' Calculate aging bucket
                                    Dim daysPastDue As Integer = CInt((asOfDate - dueDate).TotalDays)
                                    Dim agingBucket As String = Me.DetermineAgingBucket(daysPastDue)

                                    Dim osEntity As String = Me.MapOrgToEntity(orgId)

                                    Dim newRow As DataRow = dt.NewRow()
                                    newRow("Entity") = osEntity
                                    newRow("Account") = $"A_AR_{agingBucket}"
                                    newRow("Time") = $"{asOfDate.Year}M{asOfDate.Month}"
                                    newRow("Scenario") = "Actual"
                                    newRow("Flow") = "F_None"
                                    newRow("Origin") = "O_None"
                                    newRow("IC") = "I_None"
                                    newRow("UD1") = "UD1_AR"
                                    newRow("UD2") = "UD2_None"
                                    newRow("UD3") = "UD3_None"
                                    newRow("UD4") = "UD4_None"
                                    newRow("Amount") = amountRemaining
                                    newRow("Currency") = currency
                                    newRow("Description") = $"AR Trx:{trxNumber} Customer:{customerName} Due:{dueDate:yyyy-MM-dd} Days:{daysPastDue}"
                                    dt.Rows.Add(newRow)

                                    rowCount += 1

                                Catch exRow As Exception
                                    BRApi.ErrorLog.LogMessage(si, $"CN_Oracle_SubLedger: AR row error: {exRow.Message}")
                                End Try
                            End While
                        End Using
                    End Using

                    conn.Close()
                End Using

                BRApi.ErrorLog.LogMessage(si, $"CN_Oracle_SubLedger: AR rows loaded: {rowCount}")
                Return rowCount

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Oracle_SubLedger.ExtractARData", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' DetermineAgingBucket: Classifies days past due into standard aging buckets.
        '----------------------------------------------------------------------------------------------------
        Private Function DetermineAgingBucket(ByVal daysPastDue As Integer) As String
            If daysPastDue <= 0 Then
                Return "Current"
            ElseIf daysPastDue <= 30 Then
                Return "Past30"
            ElseIf daysPastDue <= 60 Then
                Return "Past60"
            ElseIf daysPastDue <= 90 Then
                Return "Past90"
            Else
                Return "Past120Plus"
            End If
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetAsOfDate: Parses the as-of date parameter or defaults to today.
        '----------------------------------------------------------------------------------------------------
        Private Function GetAsOfDate(ByVal dateParam As String) As DateTime
            If Not String.IsNullOrWhiteSpace(dateParam) Then
                Dim parsed As DateTime
                If DateTime.TryParse(dateParam, CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then
                    Return parsed
                End If
            End If
            Return DateTime.Today
        End Function

        '----------------------------------------------------------------------------------------------------
        ' MapOrgToEntity: Maps Oracle operating unit org ID to OneStream entity member.
        '----------------------------------------------------------------------------------------------------
        Private Function MapOrgToEntity(ByVal orgId As String) As String
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            map.Add("101", "E_USCorp")
            map.Add("102", "E_USWest")
            map.Add("201", "E_UKCorp")
            map.Add("301", "E_DECorp")
            map.Add("401", "E_FRParis")

            Dim result As String = ""
            If map.TryGetValue(orgId, result) Then
                Return result
            End If
            Return $"E_Org_{orgId}"
        End Function

    End Class

End Namespace

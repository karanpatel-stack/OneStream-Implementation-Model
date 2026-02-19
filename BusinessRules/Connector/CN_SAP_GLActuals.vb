'------------------------------------------------------------------------------------------------------------
' OneStream XF Connector Business Rule: CN_SAP_GLActuals
'------------------------------------------------------------------------------------------------------------
' Purpose:     Extracts General Ledger actuals from SAP HANA/BW via ODBC/ADO.NET connection.
'              Queries the Universal Journal (ACDOCA) or classic BSEG/BKPF tables for GL postings,
'              maps SAP company codes, GL accounts, and cost centers to OneStream dimension members,
'              and loads transformed data to the staging area.
'
' Source:      SAP S/4HANA or ECC -- ACDOCA (Universal Journal), BSEG/BKPF (fallback)
' Target:      OneStream staging table via BRApi.Finance.Connector
'
' Key Fields:  Company Code (BUKRS), GL Account (HKONT/RACCT), Cost Center (KOSTL),
'              Profit Center (PRCTR), Document Date (BLDAT), Posting Date (BUDAT),
'              Amount in LC (DMBTR/HSL), Amount in DC (WRBTR/WSL), Currency (WAERS),
'              Document Number (BELNR)
'
' Features:    - Incremental load support via posting date range parameters
'              - SAP fiscal year variant handling (V3, K4, etc.)
'              - Debit/credit indicator (SHKZG) conversion to signed amounts
'              - Configurable company code to OneStream entity mapping
'              - Connection testing with timeout
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

Namespace OneStream.BusinessRule.Connector.CN_SAP_GLActuals

    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Main entry point invoked by the Data Management / Stage engine.
        ' The Connector BR is triggered during the workflow step that loads source data into staging.
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As ConnectorArgs) As Object
            Try
                Dim rowsLoaded As Long = 0

                Select Case args.ActionType
                    Case Is = ConnectorActionTypes.UI
                        ' Return list of parameter prompts for the Data Management UI
                        Return Me.SetupUI(si, args)

                    Case Is = ConnectorActionTypes.Initialize
                        ' Initialization phase -- validate parameters and test connection
                        Me.ValidateParameters(si, args)
                        BRApi.ErrorLog.LogMessage(si, "CN_SAP_GLActuals: Initialization completed successfully.")

                    Case Is = ConnectorActionTypes.GetData
                        ' Main data extraction phase
                        rowsLoaded = Me.ExtractAndLoadData(si, args)
                        BRApi.ErrorLog.LogMessage(si, $"CN_SAP_GLActuals: Data extraction completed. Total rows loaded: {rowsLoaded}")

                    Case Is = ConnectorActionTypes.Finalize
                        ' Post-load cleanup
                        BRApi.ErrorLog.LogMessage(si, "CN_SAP_GLActuals: Finalize phase completed.")
                End Select

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_GLActuals.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' SetupUI: Defines parameter prompts displayed in the Data Management workspace when the
        ' user configures this connector. Parameters include connection string, date range, and
        ' company code filter.
        '----------------------------------------------------------------------------------------------------
        Private Function SetupUI(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Object
            Try
                Dim paramList As New List(Of DashboardDataSetParam)

                paramList.Add(New DashboardDataSetParam("ConnectionString", _
                    "SAP HANA ODBC Connection String", _
                    "DSN=SAPHANA;UID=ONESTREAM_SVC;PWD=;", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("StartDate", _
                    "Posting Date Start (YYYYMMDD)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("EndDate", _
                    "Posting Date End (YYYYMMDD)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("CompanyCodeFilter", _
                    "SAP Company Codes (comma-separated, blank=all)", _
                    "1000,2000,3000", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("UseACDOCA", _
                    "Use ACDOCA Universal Journal (True/False)", _
                    "True", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("FiscalYearVariant", _
                    "SAP Fiscal Year Variant (e.g. K4, V3)", _
                    "K4", _
                    DashboardDataSetParamTypes.Text))

                Return paramList

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_GLActuals.SetupUI", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ValidateParameters: Ensures required parameters are present and tests the SAP connection.
        '----------------------------------------------------------------------------------------------------
        Private Sub ValidateParameters(ByVal si As SessionInfo, ByVal args As ConnectorArgs)
            Try
                Dim connString As String = args.GetParameterValue("ConnectionString")
                If String.IsNullOrWhiteSpace(connString) Then
                    Throw New XFException(si, "CN_SAP_GLActuals", "Connection string parameter is required.")
                End If

                ' Test the connection with a 30-second timeout
                Using conn As New OdbcConnection(connString)
                    conn.ConnectionTimeout = 30
                    conn.Open()
                    BRApi.ErrorLog.LogMessage(si, "CN_SAP_GLActuals: SAP HANA connection test successful.")
                    conn.Close()
                End Using

            Catch ex As OdbcException
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_GLActuals.ValidateParameters", _
                    $"SAP HANA connection failed: {ex.Message}"))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ExtractAndLoadData: Connects to SAP HANA, executes the GL extraction query, transforms
        ' each row (mapping dimensions, converting D/C indicators), and loads to staging.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractAndLoadData(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim rowCount As Long = 0
            Dim errorCount As Long = 0
            Dim skipCount As Long = 0

            ' Retrieve parameters
            Dim connString As String = args.GetParameterValue("ConnectionString")
            Dim startDate As String = args.GetParameterValue("StartDate")
            Dim endDate As String = args.GetParameterValue("EndDate")
            Dim companyCodeFilter As String = args.GetParameterValue("CompanyCodeFilter")
            Dim useACDOCA As Boolean = Boolean.Parse(If(args.GetParameterValue("UseACDOCA"), "True"))
            Dim fiscalYearVariant As String = args.GetParameterValue("FiscalYearVariant")

            ' Build company code to OneStream entity mapping
            Dim entityMap As Dictionary(Of String, String) = Me.BuildCompanyCodeMapping(si)

            ' Build GL account mapping
            Dim accountMap As Dictionary(Of String, String) = Me.BuildGLAccountMapping(si)

            Try
                Using conn As New OdbcConnection(connString)
                    conn.Open()

                    ' Build the SQL query based on whether ACDOCA or BSEG/BKPF is used
                    Dim sql As String = If(useACDOCA, _
                        Me.BuildACDOCAQuery(startDate, endDate, companyCodeFilter), _
                        Me.BuildBSEGQuery(startDate, endDate, companyCodeFilter))

                    BRApi.ErrorLog.LogMessage(si, $"CN_SAP_GLActuals: Executing query against SAP HANA...")

                    Using cmd As New OdbcCommand(sql, conn)
                        cmd.CommandTimeout = 600  ' 10-minute timeout for large extracts

                        ' Add date parameters
                        If Not String.IsNullOrWhiteSpace(startDate) Then
                            cmd.Parameters.AddWithValue("@StartDate", startDate)
                        End If
                        If Not String.IsNullOrWhiteSpace(endDate) Then
                            cmd.Parameters.AddWithValue("@EndDate", endDate)
                        End If

                        Using reader As OdbcDataReader = cmd.ExecuteReader()
                            ' Get the staging data table from the connector args
                            Dim dt As DataTable = args.GetDataTable()

                            While reader.Read()
                                Try
                                    ' Extract raw SAP fields
                                    Dim sapCompanyCode As String = reader("BUKRS").ToString().Trim()
                                    Dim sapGLAccount As String = reader("RACCT").ToString().Trim()
                                    Dim sapCostCenter As String = reader("KOSTL").ToString().Trim()
                                    Dim sapProfitCenter As String = reader("PRCTR").ToString().Trim()
                                    Dim postingDate As String = reader("BUDAT").ToString().Trim()
                                    Dim documentDate As String = reader("BLDAT").ToString().Trim()
                                    Dim amountLC As Decimal = Convert.ToDecimal(reader("HSL"))
                                    Dim amountDC As Decimal = Convert.ToDecimal(reader("WSL"))
                                    Dim currency As String = reader("WAERS").ToString().Trim()
                                    Dim docNumber As String = reader("BELNR").ToString().Trim()
                                    Dim drcrIndicator As String = reader("DRCRK").ToString().Trim()

                                    ' Convert debit/credit indicator to signed amount
                                    ' SAP Convention: S = Debit (positive), H = Credit (negative in OneStream)
                                    Dim signedAmountLC As Decimal = Me.ApplyDebitCreditSign(amountLC, drcrIndicator)
                                    Dim signedAmountDC As Decimal = Me.ApplyDebitCreditSign(amountDC, drcrIndicator)

                                    ' Map SAP company code to OneStream entity
                                    Dim osEntity As String = ""
                                    If Not entityMap.TryGetValue(sapCompanyCode, osEntity) Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"CN_SAP_GLActuals: WARNING - Unmapped company code [{sapCompanyCode}], doc [{docNumber}]. Skipping.")
                                        skipCount += 1
                                        Continue While
                                    End If

                                    ' Map SAP GL account to OneStream account
                                    Dim osAccount As String = ""
                                    If Not accountMap.TryGetValue(sapGLAccount, osAccount) Then
                                        ' Attempt fallback: strip leading zeros and retry
                                        Dim strippedAccount As String = sapGLAccount.TrimStart("0"c)
                                        If Not accountMap.TryGetValue(strippedAccount, osAccount) Then
                                            BRApi.ErrorLog.LogMessage(si, _
                                                $"CN_SAP_GLActuals: WARNING - Unmapped GL account [{sapGLAccount}], doc [{docNumber}]. Skipping.")
                                            skipCount += 1
                                            Continue While
                                        End If
                                    End If

                                    ' Map posting date to OneStream time period
                                    Dim osTimePeriod As String = Me.MapToOneStreamPeriod(postingDate, fiscalYearVariant)

                                    ' Map cost center to UD1 dimension (custom dimension 1)
                                    Dim osCostCenter As String = Me.MapCostCenter(sapCostCenter)

                                    ' Map profit center to UD2 dimension (custom dimension 2)
                                    Dim osProfitCenter As String = Me.MapProfitCenter(sapProfitCenter)

                                    ' Add row to staging data table
                                    Dim newRow As DataRow = dt.NewRow()
                                    newRow("Entity") = osEntity
                                    newRow("Account") = osAccount
                                    newRow("Time") = osTimePeriod
                                    newRow("Scenario") = "Actual"
                                    newRow("Flow") = "F_None"
                                    newRow("Origin") = "O_None"
                                    newRow("IC") = "I_None"
                                    newRow("UD1") = osCostCenter
                                    newRow("UD2") = osProfitCenter
                                    newRow("UD3") = "UD3_None"
                                    newRow("UD4") = "UD4_None"
                                    newRow("Amount") = signedAmountLC
                                    newRow("AmountReporting") = signedAmountDC
                                    newRow("Currency") = currency
                                    newRow("Description") = $"SAP Doc:{docNumber} Date:{documentDate}"
                                    dt.Rows.Add(newRow)

                                    rowCount += 1

                                    ' Log progress every 10,000 rows
                                    If rowCount Mod 10000 = 0 Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"CN_SAP_GLActuals: Progress - {rowCount} rows processed...")
                                    End If

                                Catch exRow As Exception
                                    errorCount += 1
                                    BRApi.ErrorLog.LogMessage(si, _
                                        $"CN_SAP_GLActuals: ERROR on row {rowCount + errorCount}: {exRow.Message}")
                                    If errorCount > 1000 Then
                                        Throw New XFException(si, "CN_SAP_GLActuals", _
                                            $"Error threshold exceeded ({errorCount} errors). Aborting extraction.")
                                    End If
                                End Try
                            End While
                        End Using
                    End Using

                    conn.Close()
                End Using

                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_SAP_GLActuals: Extraction summary - Loaded:{rowCount}, Skipped:{skipCount}, Errors:{errorCount}")

                Return rowCount

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_GLActuals.ExtractAndLoadData", _
                    $"Extraction failed after {rowCount} rows: {ex.Message}"))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildACDOCAQuery: Constructs the SQL query against SAP ACDOCA (Universal Journal).
        ' ACDOCA is the single source of truth in S/4HANA, containing all FI/CO postings.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildACDOCAQuery(ByVal startDate As String, ByVal endDate As String, _
                                           ByVal companyCodeFilter As String) As String
            Dim sql As String = "SELECT " & _
                "BUKRS, RACCT, KOSTL, PRCTR, BUDAT, BLDAT, " & _
                "HSL, WSL, WAERS, BELNR, DRCRK " & _
                "FROM ACDOCA " & _
                "WHERE RLDNR = '0L' " ' Leading ledger only

            ' Apply date filters for incremental loads
            If Not String.IsNullOrWhiteSpace(startDate) Then
                sql &= $" AND BUDAT >= '{startDate}'"
            End If
            If Not String.IsNullOrWhiteSpace(endDate) Then
                sql &= $" AND BUDAT <= '{endDate}'"
            End If

            ' Apply company code filter if specified
            If Not String.IsNullOrWhiteSpace(companyCodeFilter) Then
                Dim codes As String() = companyCodeFilter.Split(","c)
                Dim inClause As String = String.Join(",", codes.Select(Function(c) $"'{c.Trim()}'"))
                sql &= $" AND BUKRS IN ({inClause})"
            End If

            sql &= " ORDER BY BUKRS, BUDAT, BELNR"

            Return sql
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildBSEGQuery: Constructs the SQL query against classic SAP BSEG/BKPF tables.
        ' Used as fallback when ACDOCA is not available (older ECC systems).
        '----------------------------------------------------------------------------------------------------
        Private Function BuildBSEGQuery(ByVal startDate As String, ByVal endDate As String, _
                                         ByVal companyCodeFilter As String) As String
            Dim sql As String = "SELECT " & _
                "B.BUKRS, B.HKONT AS RACCT, B.KOSTL, B.PRCTR, " & _
                "H.BUDAT, H.BLDAT, " & _
                "B.DMBTR AS HSL, B.WRBTR AS WSL, H.WAERS, " & _
                "H.BELNR, B.SHKZG AS DRCRK " & _
                "FROM BSEG B " & _
                "INNER JOIN BKPF H ON B.BUKRS = H.BUKRS AND B.BELNR = H.BELNR AND B.GJAHR = H.GJAHR "

            Dim hasWhere As Boolean = False

            If Not String.IsNullOrWhiteSpace(startDate) Then
                sql &= " WHERE H.BUDAT >= '" & startDate & "'"
                hasWhere = True
            End If
            If Not String.IsNullOrWhiteSpace(endDate) Then
                sql &= If(hasWhere, " AND", " WHERE") & " H.BUDAT <= '" & endDate & "'"
                hasWhere = True
            End If
            If Not String.IsNullOrWhiteSpace(companyCodeFilter) Then
                Dim codes As String() = companyCodeFilter.Split(","c)
                Dim inClause As String = String.Join(",", codes.Select(Function(c) $"'{c.Trim()}'"))
                sql &= If(hasWhere, " AND", " WHERE") & $" B.BUKRS IN ({inClause})"
            End If

            sql &= " ORDER BY B.BUKRS, H.BUDAT, H.BELNR"

            Return sql
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ApplyDebitCreditSign: Converts SAP debit/credit indicators to signed amounts.
        ' SAP stores amounts as absolute values with a separate D/C indicator.
        ' S (Soll) = Debit = Positive in OneStream for asset/expense accounts
        ' H (Haben) = Credit = Negative in OneStream for asset/expense accounts
        '----------------------------------------------------------------------------------------------------
        Private Function ApplyDebitCreditSign(ByVal amount As Decimal, ByVal drcrIndicator As String) As Decimal
            If drcrIndicator.Equals("H", StringComparison.OrdinalIgnoreCase) Then
                Return -Math.Abs(amount)
            Else
                Return Math.Abs(amount)
            End If
        End Function

        '----------------------------------------------------------------------------------------------------
        ' MapToOneStreamPeriod: Converts an SAP posting date (YYYYMMDD) to a OneStream time member.
        ' Handles SAP fiscal year variants where fiscal year may differ from calendar year.
        ' For variant K4 (April fiscal year start), posting in Jan 2026 maps to FY2025 Period 10.
        ' Standard calendar variant maps to 2026M1, 2026M2, etc.
        '----------------------------------------------------------------------------------------------------
        Private Function MapToOneStreamPeriod(ByVal postingDate As String, ByVal fiscalYearVariant As String) As String
            Try
                Dim dt As DateTime = DateTime.ParseExact(postingDate, "yyyyMMdd", CultureInfo.InvariantCulture)
                Dim calYear As Integer = dt.Year
                Dim calMonth As Integer = dt.Month

                Select Case fiscalYearVariant.ToUpper()
                    Case "K4"
                        ' April fiscal year start: Apr=P1, May=P2, ..., Mar=P12
                        Dim fiscalYear As Integer = If(calMonth >= 4, calYear, calYear - 1)
                        Dim fiscalPeriod As Integer = If(calMonth >= 4, calMonth - 3, calMonth + 9)
                        Return $"{fiscalYear}M{fiscalPeriod}"

                    Case "V3"
                        ' October fiscal year start: Oct=P1, Nov=P2, ..., Sep=P12
                        Dim fiscalYear As Integer = If(calMonth >= 10, calYear + 1, calYear)
                        Dim fiscalPeriod As Integer = If(calMonth >= 10, calMonth - 9, calMonth + 3)
                        Return $"{fiscalYear}M{fiscalPeriod}"

                    Case Else
                        ' Standard calendar year: Jan=M1, Feb=M2, ..., Dec=M12
                        Return $"{calYear}M{calMonth}"
                End Select

            Catch ex As Exception
                ' Default to current period if date parsing fails
                Return $"{DateTime.Now.Year}M{DateTime.Now.Month}"
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildCompanyCodeMapping: Constructs the mapping from SAP company codes to OneStream
        ' entity dimension members. In production, this would read from a mapping table or
        ' substitution variables.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildCompanyCodeMapping(ByVal si As SessionInfo) As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            ' Mapping configuration -- in production, read from BRApi.Finance.Data or a lookup table
            map.Add("1000", "E_USCorp")
            map.Add("1100", "E_USWest")
            map.Add("1200", "E_USEast")
            map.Add("2000", "E_UKCorp")
            map.Add("2100", "E_UKNorth")
            map.Add("3000", "E_DECorp")
            map.Add("3100", "E_DEMunich")
            map.Add("4000", "E_JPTokyo")
            map.Add("5000", "E_CNShanghai")

            Return map
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildGLAccountMapping: Constructs the mapping from SAP GL account numbers to OneStream
        ' account dimension members.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildGLAccountMapping(ByVal si As SessionInfo) As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            ' Revenue accounts
            map.Add("400000", "A_Revenue_Product")
            map.Add("410000", "A_Revenue_Service")
            map.Add("420000", "A_Revenue_Other")

            ' COGS accounts
            map.Add("500000", "A_COGS_Material")
            map.Add("510000", "A_COGS_Labor")
            map.Add("520000", "A_COGS_Overhead")

            ' Operating expense accounts
            map.Add("600000", "A_SGA_Salaries")
            map.Add("610000", "A_SGA_Benefits")
            map.Add("620000", "A_SGA_Travel")
            map.Add("630000", "A_SGA_Rent")
            map.Add("640000", "A_SGA_Depreciation")
            map.Add("650000", "A_SGA_Professional")

            ' Balance sheet accounts
            map.Add("100000", "A_BS_Cash")
            map.Add("110000", "A_BS_AR")
            map.Add("120000", "A_BS_Inventory")
            map.Add("200000", "A_BS_AP")
            map.Add("210000", "A_BS_AccruedLiab")
            map.Add("300000", "A_BS_RetainedEarnings")

            Return map
        End Function

        '----------------------------------------------------------------------------------------------------
        ' MapCostCenter: Maps SAP cost center to OneStream UD1 dimension member.
        '----------------------------------------------------------------------------------------------------
        Private Function MapCostCenter(ByVal sapCostCenter As String) As String
            If String.IsNullOrWhiteSpace(sapCostCenter) Then Return "UD1_None"
            ' Convention: prefix with UD1_ and keep the SAP cost center number
            Return $"UD1_{sapCostCenter.TrimStart("0"c)}"
        End Function

        '----------------------------------------------------------------------------------------------------
        ' MapProfitCenter: Maps SAP profit center to OneStream UD2 dimension member.
        '----------------------------------------------------------------------------------------------------
        Private Function MapProfitCenter(ByVal sapProfitCenter As String) As String
            If String.IsNullOrWhiteSpace(sapProfitCenter) Then Return "UD2_None"
            Return $"UD2_{sapProfitCenter.TrimStart("0"c)}"
        End Function

    End Class

End Namespace

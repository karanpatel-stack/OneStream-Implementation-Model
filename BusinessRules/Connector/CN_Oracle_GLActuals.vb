'------------------------------------------------------------------------------------------------------------
' OneStream XF Connector Business Rule: CN_Oracle_GLActuals
'------------------------------------------------------------------------------------------------------------
' Purpose:     Extracts General Ledger actuals from Oracle E-Business Suite (EBS) or Oracle Cloud
'              Financials. Queries GL_JE_HEADERS, GL_JE_LINES, and GL_CODE_COMBINATIONS for
'              journal entry detail, maps Oracle multi-segment chart of accounts to OneStream
'              dimension members, and loads to staging.
'
' Source:      Oracle EBS R12 or Oracle Cloud
'              - GL_JE_HEADERS (journal header), GL_JE_LINES (journal lines)
'              - GL_CODE_COMBINATIONS (account segments), GL_PERIODS (period mapping)
'              - GL_LEDGERS (ledger configuration)
'
' Segment Mapping Convention:
'   Segment1 = Company       -> OneStream Entity
'   Segment2 = Account       -> OneStream Account
'   Segment3 = Department    -> OneStream UD1 (Cost Center)
'   Segment4 = Product       -> OneStream UD2
'   Segment5 = Intercompany  -> OneStream IC
'   Segment6 = Future/Spare  -> OneStream UD3
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

Namespace OneStream.BusinessRule.Connector.CN_Oracle_GLActuals

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
                        Dim rowsLoaded As Long = Me.ExtractGLData(si, args)
                        BRApi.ErrorLog.LogMessage(si, $"CN_Oracle_GLActuals: Extraction complete. Rows loaded: {rowsLoaded}")

                    Case Is = ConnectorActionTypes.Finalize
                        BRApi.ErrorLog.LogMessage(si, "CN_Oracle_GLActuals: Finalize phase completed.")
                End Select

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Oracle_GLActuals.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' SetupUI: Defines parameters for the Data Management workspace.
        '----------------------------------------------------------------------------------------------------
        Private Function SetupUI(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Object
            Try
                Dim paramList As New List(Of DashboardDataSetParam)

                paramList.Add(New DashboardDataSetParam("ConnectionString", _
                    "Oracle DB Connection String (ODBC)", _
                    "DSN=OracleEBS;UID=ONESTREAM_SVC;PWD=;", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("LedgerId", _
                    "Oracle Ledger ID", _
                    "1", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("PeriodName", _
                    "Oracle GL Period Name (e.g. JAN-26)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("JournalSource", _
                    "Journal Source Filter (blank=all)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("JournalCategory", _
                    "Journal Category Filter (blank=all)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("PostedOnly", _
                    "Only Posted Journals (True/False)", _
                    "True", _
                    DashboardDataSetParamTypes.Text))

                Return paramList

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Oracle_GLActuals.SetupUI", ex.Message))
            End Try
        End Function

        Private Sub ValidateConnection(ByVal si As SessionInfo, ByVal args As ConnectorArgs)
            Try
                Dim connString As String = args.GetParameterValue("ConnectionString")
                If String.IsNullOrWhiteSpace(connString) Then
                    Throw New XFException(si, "CN_Oracle_GLActuals", "Connection string is required.")
                End If

                Using conn As New OdbcConnection(connString)
                    conn.ConnectionTimeout = 30
                    conn.Open()
                    BRApi.ErrorLog.LogMessage(si, "CN_Oracle_GLActuals: Oracle connection test successful.")
                    conn.Close()
                End Using

            Catch ex As OdbcException
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Oracle_GLActuals.ValidateConnection", _
                    $"Oracle connection failed: {ex.Message}"))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ExtractGLData: Connects to Oracle EBS, queries GL journal data, transforms Oracle segment
        ' values to OneStream dimension members, and loads to the staging table.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractGLData(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim rowCount As Long = 0
            Dim errorCount As Long = 0
            Dim skipCount As Long = 0

            Dim connString As String = args.GetParameterValue("ConnectionString")
            Dim ledgerId As String = If(args.GetParameterValue("LedgerId"), "1")
            Dim periodName As String = args.GetParameterValue("PeriodName")
            Dim journalSource As String = args.GetParameterValue("JournalSource")
            Dim journalCategory As String = args.GetParameterValue("JournalCategory")
            Dim postedOnly As Boolean = Boolean.Parse(If(args.GetParameterValue("PostedOnly"), "True"))

            ' Load dimension mappings
            Dim companyMap As Dictionary(Of String, String) = Me.BuildCompanySegmentMapping(si)
            Dim accountMap As Dictionary(Of String, String) = Me.BuildAccountSegmentMapping(si)

            Try
                Using conn As New OdbcConnection(connString)
                    conn.Open()

                    Dim sql As String = Me.BuildOracleGLQuery(ledgerId, periodName, journalSource, journalCategory, postedOnly)

                    BRApi.ErrorLog.LogMessage(si, $"CN_Oracle_GLActuals: Executing Oracle GL query for period [{periodName}]...")

                    Using cmd As New OdbcCommand(sql, conn)
                        cmd.CommandTimeout = 600

                        Using reader As OdbcDataReader = cmd.ExecuteReader()
                            Dim dt As DataTable = args.GetDataTable()

                            While reader.Read()
                                Try
                                    ' Extract Oracle segment values from GL_CODE_COMBINATIONS
                                    Dim segment1_Company As String = reader("SEGMENT1").ToString().Trim()
                                    Dim segment2_Account As String = reader("SEGMENT2").ToString().Trim()
                                    Dim segment3_Dept As String = reader("SEGMENT3").ToString().Trim()
                                    Dim segment4_Product As String = reader("SEGMENT4").ToString().Trim()
                                    Dim segment5_IC As String = reader("SEGMENT5").ToString().Trim()
                                    Dim segment6_Future As String = reader("SEGMENT6").ToString().Trim()

                                    ' Extract journal amounts
                                    Dim enteredDr As Decimal = Convert.ToDecimal(If(reader("ENTERED_DR") Is DBNull.Value, 0, reader("ENTERED_DR")))
                                    Dim enteredCr As Decimal = Convert.ToDecimal(If(reader("ENTERED_CR") Is DBNull.Value, 0, reader("ENTERED_CR")))
                                    Dim accountedDr As Decimal = Convert.ToDecimal(If(reader("ACCOUNTED_DR") Is DBNull.Value, 0, reader("ACCOUNTED_DR")))
                                    Dim accountedCr As Decimal = Convert.ToDecimal(If(reader("ACCOUNTED_CR") Is DBNull.Value, 0, reader("ACCOUNTED_CR")))
                                    Dim currencyCode As String = reader("CURRENCY_CODE").ToString().Trim()
                                    Dim oraclePeriod As String = reader("PERIOD_NAME").ToString().Trim()
                                    Dim jeHeaderId As String = reader("JE_HEADER_ID").ToString().Trim()
                                    Dim jeName As String = reader("NAME").ToString().Trim()

                                    ' Calculate net amount (Debit positive, Credit negative for asset/expense)
                                    Dim enteredAmount As Decimal = enteredDr - enteredCr
                                    Dim accountedAmount As Decimal = accountedDr - accountedCr

                                    ' Map Oracle company segment to OneStream entity
                                    Dim osEntity As String = ""
                                    If Not companyMap.TryGetValue(segment1_Company, osEntity) Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"CN_Oracle_GLActuals: WARNING - Unmapped company segment [{segment1_Company}]. Skipping.")
                                        skipCount += 1
                                        Continue While
                                    End If

                                    ' Map Oracle account segment to OneStream account
                                    Dim osAccount As String = ""
                                    If Not accountMap.TryGetValue(segment2_Account, osAccount) Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"CN_Oracle_GLActuals: WARNING - Unmapped account segment [{segment2_Account}]. Skipping.")
                                        skipCount += 1
                                        Continue While
                                    End If

                                    ' Map Oracle period name to OneStream time member
                                    Dim osTimePeriod As String = Me.MapOraclePeriodToOneStream(oraclePeriod)

                                    ' Map department segment to UD1
                                    Dim osDepartment As String = If(String.IsNullOrWhiteSpace(segment3_Dept), _
                                        "UD1_None", $"UD1_Dept_{segment3_Dept}")

                                    ' Map product segment to UD2
                                    Dim osProduct As String = If(String.IsNullOrWhiteSpace(segment4_Product), _
                                        "UD2_None", $"UD2_Prod_{segment4_Product}")

                                    ' Map intercompany segment
                                    Dim osIC As String = If(String.IsNullOrWhiteSpace(segment5_IC) OrElse segment5_IC = "000", _
                                        "I_None", $"I_{segment5_IC}")

                                    ' Add row to staging
                                    Dim newRow As DataRow = dt.NewRow()
                                    newRow("Entity") = osEntity
                                    newRow("Account") = osAccount
                                    newRow("Time") = osTimePeriod
                                    newRow("Scenario") = "Actual"
                                    newRow("Flow") = "F_None"
                                    newRow("Origin") = "O_None"
                                    newRow("IC") = osIC
                                    newRow("UD1") = osDepartment
                                    newRow("UD2") = osProduct
                                    newRow("UD3") = If(String.IsNullOrWhiteSpace(segment6_Future), "UD3_None", $"UD3_{segment6_Future}")
                                    newRow("UD4") = "UD4_None"
                                    newRow("Amount") = accountedAmount
                                    newRow("AmountReporting") = enteredAmount
                                    newRow("Currency") = currencyCode
                                    newRow("Description") = $"JE:{jeHeaderId} {jeName}"
                                    dt.Rows.Add(newRow)

                                    rowCount += 1

                                    If rowCount Mod 10000 = 0 Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"CN_Oracle_GLActuals: Progress - {rowCount} rows processed...")
                                    End If

                                Catch exRow As Exception
                                    errorCount += 1
                                    BRApi.ErrorLog.LogMessage(si, _
                                        $"CN_Oracle_GLActuals: Row error: {exRow.Message}")
                                    If errorCount > 1000 Then
                                        Throw New XFException(si, "CN_Oracle_GLActuals", "Error threshold exceeded.")
                                    End If
                                End Try
                            End While
                        End Using
                    End Using

                    conn.Close()
                End Using

                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_Oracle_GLActuals: Summary - Loaded:{rowCount}, Skipped:{skipCount}, Errors:{errorCount}")

                Return rowCount

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Oracle_GLActuals.ExtractGLData", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildOracleGLQuery: Constructs the Oracle GL journal extraction query joining headers,
        ' lines, and code combinations to get full segment detail.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildOracleGLQuery(ByVal ledgerId As String, ByVal periodName As String, _
                                             ByVal journalSource As String, ByVal journalCategory As String, _
                                             ByVal postedOnly As Boolean) As String
            Dim sql As String = _
                "SELECT " & _
                "  H.JE_HEADER_ID, H.NAME, H.PERIOD_NAME, H.CURRENCY_CODE, " & _
                "  L.ENTERED_DR, L.ENTERED_CR, L.ACCOUNTED_DR, L.ACCOUNTED_CR, " & _
                "  CC.SEGMENT1, CC.SEGMENT2, CC.SEGMENT3, CC.SEGMENT4, CC.SEGMENT5, CC.SEGMENT6 " & _
                "FROM GL_JE_HEADERS H " & _
                "INNER JOIN GL_JE_LINES L ON H.JE_HEADER_ID = L.JE_HEADER_ID " & _
                "INNER JOIN GL_CODE_COMBINATIONS CC ON L.CODE_COMBINATION_ID = CC.CODE_COMBINATION_ID " & _
                $"WHERE H.LEDGER_ID = {ledgerId} "

            If postedOnly Then
                sql &= " AND H.STATUS = 'P'"
            End If

            If Not String.IsNullOrWhiteSpace(periodName) Then
                sql &= $" AND H.PERIOD_NAME = '{periodName}'"
            End If

            If Not String.IsNullOrWhiteSpace(journalSource) Then
                sql &= $" AND H.JE_SOURCE = '{journalSource}'"
            End If

            If Not String.IsNullOrWhiteSpace(journalCategory) Then
                sql &= $" AND H.JE_CATEGORY = '{journalCategory}'"
            End If

            sql &= " ORDER BY H.JE_HEADER_ID, L.JE_LINE_NUM"

            Return sql
        End Function

        '----------------------------------------------------------------------------------------------------
        ' MapOraclePeriodToOneStream: Converts Oracle period names (e.g. "JAN-26") to OneStream
        ' time members (e.g. "2026M1").
        '----------------------------------------------------------------------------------------------------
        Private Function MapOraclePeriodToOneStream(ByVal oraclePeriod As String) As String
            Try
                If String.IsNullOrWhiteSpace(oraclePeriod) Then
                    Return $"{DateTime.Now.Year}M{DateTime.Now.Month}"
                End If

                ' Oracle period format: MON-YY (e.g. JAN-26, FEB-26)
                Dim parts As String() = oraclePeriod.Split("-"c)
                If parts.Length <> 2 Then Return oraclePeriod

                Dim monthStr As String = parts(0).Trim().ToUpper()
                Dim yearStr As String = parts(1).Trim()

                ' Parse the 2-digit year
                Dim year As Integer = 2000 + Integer.Parse(yearStr)

                ' Map month abbreviation to number
                Dim monthMap As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase) From {
                    {"JAN", 1}, {"FEB", 2}, {"MAR", 3}, {"APR", 4}, {"MAY", 5}, {"JUN", 6},
                    {"JUL", 7}, {"AUG", 8}, {"SEP", 9}, {"OCT", 10}, {"NOV", 11}, {"DEC", 12}
                }

                Dim month As Integer = 0
                If monthMap.TryGetValue(monthStr, month) Then
                    Return $"{year}M{month}"
                End If

                ' Handle Oracle adjustment periods (ADJ period = period 13)
                If monthStr.StartsWith("ADJ") Then
                    Return $"{year}M12"  ' Map adjustment period to December
                End If

                Return oraclePeriod

            Catch ex As Exception
                Return $"{DateTime.Now.Year}M{DateTime.Now.Month}"
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildCompanySegmentMapping: Maps Oracle Segment1 (company) values to OneStream entities.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildCompanySegmentMapping(ByVal si As SessionInfo) As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            map.Add("01", "E_USCorp")
            map.Add("02", "E_USWest")
            map.Add("03", "E_USEast")
            map.Add("10", "E_UKCorp")
            map.Add("11", "E_UKLondon")
            map.Add("20", "E_DECorp")
            map.Add("30", "E_FRParis")
            map.Add("40", "E_JPTokyo")
            Return map
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildAccountSegmentMapping: Maps Oracle Segment2 (natural account) values to OneStream accounts.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildAccountSegmentMapping(ByVal si As SessionInfo) As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            ' Assets
            map.Add("1010", "A_BS_Cash")
            map.Add("1110", "A_BS_AR")
            map.Add("1210", "A_BS_Inventory")
            map.Add("1310", "A_BS_Prepaid")
            map.Add("1510", "A_BS_FixedAssets")
            map.Add("1520", "A_BS_AccumDepr")
            ' Liabilities
            map.Add("2010", "A_BS_AP")
            map.Add("2110", "A_BS_AccruedLiab")
            map.Add("2210", "A_BS_LongTermDebt")
            ' Equity
            map.Add("3010", "A_BS_CommonStock")
            map.Add("3110", "A_BS_RetainedEarnings")
            ' Revenue
            map.Add("4010", "A_Revenue_Product")
            map.Add("4020", "A_Revenue_Service")
            map.Add("4030", "A_Revenue_Other")
            ' Expenses
            map.Add("5010", "A_COGS_Material")
            map.Add("5020", "A_COGS_Labor")
            map.Add("5030", "A_COGS_Overhead")
            map.Add("6010", "A_SGA_Salaries")
            map.Add("6020", "A_SGA_Benefits")
            map.Add("6030", "A_SGA_Rent")
            map.Add("6040", "A_SGA_Depreciation")
            map.Add("6050", "A_SGA_Travel")
            map.Add("6060", "A_SGA_Professional")
            Return map
        End Function

    End Class

End Namespace

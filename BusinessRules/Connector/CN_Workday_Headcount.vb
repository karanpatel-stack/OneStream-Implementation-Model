'------------------------------------------------------------------------------------------------------------
' OneStream XF Connector Business Rule: CN_Workday_Headcount
'------------------------------------------------------------------------------------------------------------
' Purpose:     Extracts headcount and compensation data from Workday HCM via Report as a Service
'              (RaaS) REST API. Parses JSON response containing worker records with position,
'              compensation, and organizational data. Calculates FTE by month handling mid-month
'              hires and terminations, maps Workday organizations to OneStream entities and cost
'              centers, and loads to the HR cube staging area.
'
' Source:      Workday RaaS (Report as a Service) REST API endpoint
'              Custom Report: "OneStream_Headcount_Export"
' Target:      OneStream staging for HR/Workforce planning cube
'
' Data Points: Worker ID, Employee Type, FTE, Position, Cost Center, Supervisory Org,
'              Base Pay, Bonus Target %, Benefits Cost, Hire Date, Termination Date, Currency
'
' Author:      OneStream Administrator
' Created:     2026-02-18
' Modified:    2026-02-18
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Text
Imports Microsoft.VisualBasic
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.Connector.CN_Workday_Headcount

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As ConnectorArgs) As Object
            Try
                Select Case args.ActionType
                    Case Is = ConnectorActionTypes.UI
                        Return Me.SetupUI(si, args)

                    Case Is = ConnectorActionTypes.Initialize
                        Me.ValidateApiEndpoint(si, args)

                    Case Is = ConnectorActionTypes.GetData
                        Dim rowsLoaded As Long = Me.ExtractHeadcountData(si, args)
                        BRApi.ErrorLog.LogMessage(si, $"CN_Workday_Headcount: Extraction complete. Rows loaded: {rowsLoaded}")

                    Case Is = ConnectorActionTypes.Finalize
                        BRApi.ErrorLog.LogMessage(si, "CN_Workday_Headcount: Finalize phase completed.")
                End Select

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Workday_Headcount.Main", ex.Message))
            End Try
        End Function

        Private Function SetupUI(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Object
            Try
                Dim paramList As New List(Of DashboardDataSetParam)

                paramList.Add(New DashboardDataSetParam("RaaSEndpoint", _
                    "Workday RaaS Report URL", _
                    "https://wd5-services1.workday.com/ccx/service/customreport2/tenant/OneStream_Headcount_Export?format=json", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("Username", _
                    "Workday Integration System User", _
                    "ISU_OneStream@tenant", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("Password", _
                    "Workday Integration System Password", _
                    "", _
                    DashboardDataSetParamTypes.Password))

                paramList.Add(New DashboardDataSetParam("EffectiveDate", _
                    "Effective Date (YYYY-MM-DD, blank=today)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("TargetPeriod", _
                    "Target OneStream Period (e.g. 2026M1)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("IncludeTerminated", _
                    "Include Terminated Workers (True/False)", _
                    "False", _
                    DashboardDataSetParamTypes.Text))

                Return paramList

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Workday_Headcount.SetupUI", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ValidateApiEndpoint: Tests the Workday RaaS API endpoint with provided credentials.
        '----------------------------------------------------------------------------------------------------
        Private Sub ValidateApiEndpoint(ByVal si As SessionInfo, ByVal args As ConnectorArgs)
            Try
                Dim raasUrl As String = args.GetParameterValue("RaaSEndpoint")
                Dim username As String = args.GetParameterValue("Username")
                Dim password As String = args.GetParameterValue("Password")

                If String.IsNullOrWhiteSpace(raasUrl) OrElse String.IsNullOrWhiteSpace(username) Then
                    Throw New XFException(si, "CN_Workday_Headcount", "RaaS endpoint URL and username are required.")
                End If

                ' Test connectivity with a HEAD request
                Dim request As HttpWebRequest = CType(WebRequest.Create(raasUrl), HttpWebRequest)
                request.Method = "HEAD"
                request.Timeout = 30000
                request.Credentials = New NetworkCredential(username, password)

                Using response As HttpWebResponse = CType(request.GetResponse(), HttpWebResponse)
                    If response.StatusCode = HttpStatusCode.OK Then
                        BRApi.ErrorLog.LogMessage(si, "CN_Workday_Headcount: Workday RaaS API connectivity test successful.")
                    Else
                        Throw New XFException(si, "CN_Workday_Headcount", $"API returned status: {response.StatusCode}")
                    End If
                End Using

            Catch ex As WebException
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Workday_Headcount.ValidateApiEndpoint", _
                    $"Workday API connection failed: {ex.Message}"))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ExtractHeadcountData: Calls the Workday RaaS API, parses the JSON response, maps worker
        ' data to OneStream dimensions, calculates FTE by month, and loads to staging.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractHeadcountData(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim rowCount As Long = 0
            Dim errorCount As Long = 0

            Dim raasUrl As String = args.GetParameterValue("RaaSEndpoint")
            Dim username As String = args.GetParameterValue("Username")
            Dim password As String = args.GetParameterValue("Password")
            Dim effectiveDateStr As String = args.GetParameterValue("EffectiveDate")
            Dim targetPeriod As String = args.GetParameterValue("TargetPeriod")
            Dim includeTerminated As Boolean = Boolean.Parse(If(args.GetParameterValue("IncludeTerminated"), "False"))

            ' Determine the effective date and derive the target period if not explicitly set
            Dim effectiveDate As DateTime = If(Not String.IsNullOrWhiteSpace(effectiveDateStr), _
                DateTime.Parse(effectiveDateStr, CultureInfo.InvariantCulture), DateTime.Today)

            If String.IsNullOrWhiteSpace(targetPeriod) Then
                targetPeriod = $"{effectiveDate.Year}M{effectiveDate.Month}"
            End If

            ' Calculate the first and last day of the target month for FTE proration
            Dim targetYear As Integer = effectiveDate.Year
            Dim targetMonth As Integer = effectiveDate.Month
            Dim monthStart As DateTime = New DateTime(targetYear, targetMonth, 1)
            Dim monthEnd As DateTime = monthStart.AddMonths(1).AddDays(-1)
            Dim daysInMonth As Integer = DateTime.DaysInMonth(targetYear, targetMonth)

            ' Build org-to-entity mapping
            Dim orgEntityMap As Dictionary(Of String, String) = Me.BuildOrgMapping(si)

            Try
                ' Append effective date to RaaS URL if not already present
                Dim fullUrl As String = raasUrl
                If Not String.IsNullOrWhiteSpace(effectiveDateStr) Then
                    Dim separator As String = If(fullUrl.Contains("?"), "&", "?")
                    fullUrl &= $"{separator}Effective_as_of={effectiveDateStr}"
                End If

                ' Call Workday RaaS API
                BRApi.ErrorLog.LogMessage(si, $"CN_Workday_Headcount: Calling Workday RaaS API...")

                Dim request As HttpWebRequest = CType(WebRequest.Create(fullUrl), HttpWebRequest)
                request.Method = "GET"
                request.Timeout = 120000  ' 2-minute timeout
                request.Credentials = New NetworkCredential(username, password)
                request.Accept = "application/json"

                Dim jsonResponse As String = ""
                Using response As HttpWebResponse = CType(request.GetResponse(), HttpWebResponse)
                    Using stream As StreamReader = New StreamReader(response.GetResponseStream(), Encoding.UTF8)
                        jsonResponse = stream.ReadToEnd()
                    End Using
                End Using

                BRApi.ErrorLog.LogMessage(si, $"CN_Workday_Headcount: API response received. Size: {jsonResponse.Length} chars")

                ' Parse the JSON response into a DataTable
                ' Workday RaaS JSON format: {"Report_Entry": [{field1:val1, ...}, ...]}
                Dim workerRecords As DataTable = Me.ParseWorkdayJson(si, jsonResponse)

                If workerRecords Is Nothing OrElse workerRecords.Rows.Count = 0 Then
                    BRApi.ErrorLog.LogMessage(si, "CN_Workday_Headcount: WARNING - No worker records returned from API.")
                    Return 0
                End If

                BRApi.ErrorLog.LogMessage(si, $"CN_Workday_Headcount: Parsed {workerRecords.Rows.Count} worker records.")

                Dim dt As DataTable = args.GetDataTable()

                For Each workerRow As DataRow In workerRecords.Rows
                    Try
                        Dim workerId As String = workerRow("Worker_ID").ToString().Trim()
                        Dim employeeType As String = workerRow("Employee_Type").ToString().Trim()
                        Dim fte As Decimal = Convert.ToDecimal(If(workerRow("FTE") Is DBNull.Value, 1.0, workerRow("FTE")))
                        Dim position As String = workerRow("Position").ToString().Trim()
                        Dim costCenter As String = workerRow("Cost_Center").ToString().Trim()
                        Dim supOrg As String = workerRow("Supervisory_Org").ToString().Trim()
                        Dim basePay As Decimal = Convert.ToDecimal(If(workerRow("Base_Pay") Is DBNull.Value, 0, workerRow("Base_Pay")))
                        Dim bonusTargetPct As Decimal = Convert.ToDecimal(If(workerRow("Bonus_Target_Pct") Is DBNull.Value, 0, workerRow("Bonus_Target_Pct")))
                        Dim benefitsCost As Decimal = Convert.ToDecimal(If(workerRow("Benefits_Cost") Is DBNull.Value, 0, workerRow("Benefits_Cost")))
                        Dim hireDateStr As String = workerRow("Hire_Date").ToString().Trim()
                        Dim termDateStr As String = workerRow("Termination_Date").ToString().Trim()
                        Dim currency As String = workerRow("Currency").ToString().Trim()

                        ' Parse dates
                        Dim hireDate As DateTime = If(Not String.IsNullOrWhiteSpace(hireDateStr), _
                            DateTime.Parse(hireDateStr, CultureInfo.InvariantCulture), DateTime.MinValue)
                        Dim termDate As DateTime = If(Not String.IsNullOrWhiteSpace(termDateStr), _
                            DateTime.Parse(termDateStr, CultureInfo.InvariantCulture), DateTime.MaxValue)

                        ' Skip terminated workers if not requested
                        If Not includeTerminated AndAlso termDate < monthStart Then
                            Continue For
                        End If

                        ' Calculate prorated FTE for mid-month hires/terminations
                        Dim proratedFTE As Decimal = Me.CalculateProratedFTE(fte, hireDate, termDate, _
                            monthStart, monthEnd, daysInMonth)

                        ' Skip workers with zero prorated FTE (not active during this month)
                        If proratedFTE = 0 Then Continue For

                        ' Map Workday supervisory org / cost center to OneStream entity
                        Dim osEntity As String = ""
                        If Not orgEntityMap.TryGetValue(costCenter, osEntity) Then
                            osEntity = $"E_CC_{costCenter.Replace(" ", "_")}"
                        End If

                        ' Map cost center to UD1
                        Dim osCostCenter As String = $"UD1_CC_{costCenter.Replace(" ", "_")}"

                        ' Calculate monthly compensation amounts
                        Dim monthlyBasePay As Decimal = Math.Round((basePay / 12) * proratedFTE, 2)
                        Dim monthlyBonusAccrual As Decimal = Math.Round(((basePay * bonusTargetPct / 100) / 12) * proratedFTE, 2)
                        Dim monthlyBenefits As Decimal = Math.Round((benefitsCost / 12) * proratedFTE, 2)
                        Dim monthlyTotalComp As Decimal = monthlyBasePay + monthlyBonusAccrual + monthlyBenefits

                        ' Load headcount FTE
                        Me.AddStagingRow(dt, osEntity, "A_HC_FTE", targetPeriod, osCostCenter, proratedFTE, _
                            currency, $"Worker:{workerId} Type:{employeeType} Pos:{position}")

                        ' Load compensation components
                        Me.AddStagingRow(dt, osEntity, "A_HC_BasePay", targetPeriod, osCostCenter, monthlyBasePay, _
                            currency, $"Worker:{workerId} AnnualBase:{basePay:N0}")

                        Me.AddStagingRow(dt, osEntity, "A_HC_BonusAccrual", targetPeriod, osCostCenter, monthlyBonusAccrual, _
                            currency, $"Worker:{workerId} BonusTgt:{bonusTargetPct:F1}%")

                        Me.AddStagingRow(dt, osEntity, "A_HC_Benefits", targetPeriod, osCostCenter, monthlyBenefits, _
                            currency, $"Worker:{workerId}")

                        Me.AddStagingRow(dt, osEntity, "A_HC_TotalComp", targetPeriod, osCostCenter, monthlyTotalComp, _
                            currency, $"Worker:{workerId} TotalMonthly:{monthlyTotalComp:N2}")

                        ' Load headcount as statistical (count of 1 prorated)
                        Me.AddStagingRow(dt, osEntity, "A_HC_Headcount", targetPeriod, osCostCenter, _
                            If(proratedFTE > 0, 1D, 0D), currency, $"Worker:{workerId}")

                        rowCount += 1

                        If rowCount Mod 1000 = 0 Then
                            BRApi.ErrorLog.LogMessage(si, $"CN_Workday_Headcount: Progress - {rowCount} workers processed...")
                        End If

                    Catch exRow As Exception
                        errorCount += 1
                        BRApi.ErrorLog.LogMessage(si, $"CN_Workday_Headcount: Worker record error: {exRow.Message}")
                        If errorCount > 200 Then
                            Throw New XFException(si, "CN_Workday_Headcount", "Error threshold exceeded.")
                        End If
                    End Try
                Next

                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_Workday_Headcount: Summary - Workers:{rowCount}, Errors:{errorCount}")

                Return rowCount

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Workday_Headcount.ExtractHeadcountData", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' CalculateProratedFTE: Prorates FTE for workers who are hired or terminated mid-month.
        ' Returns the fraction of the month that the worker was active, multiplied by their FTE.
        '----------------------------------------------------------------------------------------------------
        Private Function CalculateProratedFTE(ByVal fte As Decimal, ByVal hireDate As DateTime, _
                                               ByVal termDate As DateTime, ByVal monthStart As DateTime, _
                                               ByVal monthEnd As DateTime, ByVal daysInMonth As Integer) As Decimal
            ' Worker not yet hired or terminated before the month
            If hireDate > monthEnd OrElse termDate < monthStart Then
                Return 0D
            End If

            ' Determine the active start and end within the month
            Dim activeStart As DateTime = If(hireDate > monthStart, hireDate, monthStart)
            Dim activeEnd As DateTime = If(termDate < monthEnd, termDate, monthEnd)

            ' Calculate days active in the month
            Dim daysActive As Integer = CInt((activeEnd - activeStart).TotalDays) + 1
            If daysActive <= 0 Then Return 0D

            ' If active the full month, return full FTE
            If daysActive >= daysInMonth Then
                Return fte
            End If

            ' Prorate: FTE * (days active / days in month)
            Return Math.Round(fte * (CDec(daysActive) / CDec(daysInMonth)), 4)
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ParseWorkdayJson: Parses the Workday RaaS JSON response into a DataTable.
        ' Workday RaaS returns JSON in the format: {"Report_Entry": [{...}, {...}, ...]}
        '----------------------------------------------------------------------------------------------------
        Private Function ParseWorkdayJson(ByVal si As SessionInfo, ByVal jsonResponse As String) As DataTable
            Try
                Dim dt As New DataTable()
                dt.Columns.Add("Worker_ID", GetType(String))
                dt.Columns.Add("Employee_Type", GetType(String))
                dt.Columns.Add("FTE", GetType(Decimal))
                dt.Columns.Add("Position", GetType(String))
                dt.Columns.Add("Cost_Center", GetType(String))
                dt.Columns.Add("Supervisory_Org", GetType(String))
                dt.Columns.Add("Base_Pay", GetType(Decimal))
                dt.Columns.Add("Bonus_Target_Pct", GetType(Decimal))
                dt.Columns.Add("Benefits_Cost", GetType(Decimal))
                dt.Columns.Add("Hire_Date", GetType(String))
                dt.Columns.Add("Termination_Date", GetType(String))
                dt.Columns.Add("Currency", GetType(String))

                ' Simple JSON parsing without external libraries
                ' Extract the array of Report_Entry objects
                Dim entryStart As Integer = jsonResponse.IndexOf("[")
                Dim entryEnd As Integer = jsonResponse.LastIndexOf("]")
                If entryStart < 0 OrElse entryEnd < 0 Then Return dt

                Dim entriesJson As String = jsonResponse.Substring(entryStart + 1, entryEnd - entryStart - 1)

                ' Split into individual JSON objects (simple approach for well-formed responses)
                Dim objectDepth As Integer = 0
                Dim objectStart As Integer = -1
                Dim objects As New List(Of String)

                For i As Integer = 0 To entriesJson.Length - 1
                    If entriesJson(i) = "{"c Then
                        If objectDepth = 0 Then objectStart = i
                        objectDepth += 1
                    ElseIf entriesJson(i) = "}"c Then
                        objectDepth -= 1
                        If objectDepth = 0 AndAlso objectStart >= 0 Then
                            objects.Add(entriesJson.Substring(objectStart, i - objectStart + 1))
                            objectStart = -1
                        End If
                    End If
                Next

                ' Parse each worker object
                For Each obj As String In objects
                    Try
                        Dim newRow As DataRow = dt.NewRow()
                        newRow("Worker_ID") = Me.ExtractJsonValue(obj, "Worker_ID")
                        newRow("Employee_Type") = Me.ExtractJsonValue(obj, "Employee_Type")
                        newRow("FTE") = Decimal.Parse(If(Me.ExtractJsonValue(obj, "FTE"), "1.0"), CultureInfo.InvariantCulture)
                        newRow("Position") = Me.ExtractJsonValue(obj, "Position")
                        newRow("Cost_Center") = Me.ExtractJsonValue(obj, "Cost_Center")
                        newRow("Supervisory_Org") = Me.ExtractJsonValue(obj, "Supervisory_Org")
                        newRow("Base_Pay") = Decimal.Parse(If(Me.ExtractJsonValue(obj, "Base_Pay"), "0"), CultureInfo.InvariantCulture)
                        newRow("Bonus_Target_Pct") = Decimal.Parse(If(Me.ExtractJsonValue(obj, "Bonus_Target_Pct"), "0"), CultureInfo.InvariantCulture)
                        newRow("Benefits_Cost") = Decimal.Parse(If(Me.ExtractJsonValue(obj, "Benefits_Cost"), "0"), CultureInfo.InvariantCulture)
                        newRow("Hire_Date") = Me.ExtractJsonValue(obj, "Hire_Date")
                        newRow("Termination_Date") = Me.ExtractJsonValue(obj, "Termination_Date")
                        newRow("Currency") = If(Me.ExtractJsonValue(obj, "Currency"), "USD")
                        dt.Rows.Add(newRow)
                    Catch
                        ' Skip malformed records
                    End Try
                Next

                Return dt

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, $"CN_Workday_Headcount: JSON parsing error: {ex.Message}")
                Return Nothing
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExtractJsonValue: Extracts a simple string value for a given key from a JSON object string.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractJsonValue(ByVal json As String, ByVal key As String) As String
            Dim searchKey As String = $"""{key}"""
            Dim keyIdx As Integer = json.IndexOf(searchKey, StringComparison.OrdinalIgnoreCase)
            If keyIdx < 0 Then Return ""

            Dim colonIdx As Integer = json.IndexOf(":"c, keyIdx + searchKey.Length)
            If colonIdx < 0 Then Return ""

            Dim valueStart As Integer = colonIdx + 1
            ' Skip whitespace
            While valueStart < json.Length AndAlso Char.IsWhiteSpace(json(valueStart))
                valueStart += 1
            End While

            If valueStart >= json.Length Then Return ""

            If json(valueStart) = """"c Then
                ' String value
                Dim valueEnd As Integer = json.IndexOf(""""c, valueStart + 1)
                If valueEnd < 0 Then Return ""
                Return json.Substring(valueStart + 1, valueEnd - valueStart - 1)
            Else
                ' Numeric or null value
                Dim valueEnd As Integer = valueStart
                While valueEnd < json.Length AndAlso json(valueEnd) <> ","c AndAlso json(valueEnd) <> "}"c
                    valueEnd += 1
                End While
                Dim rawValue As String = json.Substring(valueStart, valueEnd - valueStart).Trim()
                If rawValue.Equals("null", StringComparison.OrdinalIgnoreCase) Then Return ""
                Return rawValue
            End If
        End Function

        '----------------------------------------------------------------------------------------------------
        ' AddStagingRow: Adds a standardized row to the staging DataTable.
        '----------------------------------------------------------------------------------------------------
        Private Sub AddStagingRow(ByVal dt As DataTable, ByVal entity As String, ByVal account As String, _
                                   ByVal timePeriod As String, ByVal costCenter As String, _
                                   ByVal amount As Decimal, ByVal currency As String, ByVal description As String)
            Dim newRow As DataRow = dt.NewRow()
            newRow("Entity") = entity
            newRow("Account") = account
            newRow("Time") = timePeriod
            newRow("Scenario") = "Actual"
            newRow("Flow") = "F_None"
            newRow("Origin") = "O_None"
            newRow("IC") = "I_None"
            newRow("UD1") = costCenter
            newRow("UD2") = "UD2_None"
            newRow("UD3") = "UD3_None"
            newRow("UD4") = "UD4_None"
            newRow("Amount") = amount
            newRow("Currency") = currency
            newRow("Description") = description
            dt.Rows.Add(newRow)
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' BuildOrgMapping: Maps Workday cost centers / supervisory orgs to OneStream entity members.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildOrgMapping(ByVal si As SessionInfo) As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            map.Add("CC1000", "E_USCorp")
            map.Add("CC1100", "E_USWest")
            map.Add("CC1200", "E_USEast")
            map.Add("CC2000", "E_UKCorp")
            map.Add("CC3000", "E_DECorp")
            map.Add("CC4000", "E_FRParis")
            map.Add("CC5000", "E_JPTokyo")
            map.Add("CC6000", "E_CNShanghai")
            Return map
        End Function

    End Class

End Namespace

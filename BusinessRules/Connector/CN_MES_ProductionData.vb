'------------------------------------------------------------------------------------------------------------
' OneStream XF Connector Business Rule: CN_MES_ProductionData
'------------------------------------------------------------------------------------------------------------
' Purpose:     Extracts production volume and efficiency data from a Manufacturing Execution System
'              (MES). Connects to the MES database or REST API to retrieve production line statistics
'              including units produced, scrap, machine hours, downtime, and quality defects.
'              Calculates OEE (Overall Equipment Effectiveness) components at extraction time
'              and loads statistical accounts to OneStream staging.
'
' Source:      MES System (database or REST API)
'              Tables: PRODUCTION_LOG, DOWNTIME_EVENTS, QUALITY_INSPECTIONS
' Target:      OneStream staging for statistical accounts in manufacturing cube
'
' OEE Calculation:
'   Availability = (Planned Production Time - Downtime) / Planned Production Time
'   Performance  = (Ideal Cycle Time x Total Count) / Run Time
'   Quality      = Good Count / Total Count
'   OEE          = Availability x Performance x Quality
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

Namespace OneStream.BusinessRule.Connector.CN_MES_ProductionData

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
                        Dim rowsLoaded As Long = Me.ExtractProductionData(si, args)
                        BRApi.ErrorLog.LogMessage(si, $"CN_MES_ProductionData: Extraction complete. Rows loaded: {rowsLoaded}")

                    Case Is = ConnectorActionTypes.Finalize
                        BRApi.ErrorLog.LogMessage(si, "CN_MES_ProductionData: Finalize phase completed.")
                End Select

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_MES_ProductionData.Main", ex.Message))
            End Try
        End Function

        Private Function SetupUI(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Object
            Try
                Dim paramList As New List(Of DashboardDataSetParam)

                paramList.Add(New DashboardDataSetParam("ConnectionString", _
                    "MES Database ODBC Connection String", _
                    "DSN=MES_Production;UID=onestream_read;PWD=;", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ProductionDateFrom", _
                    "Production Date From (YYYY-MM-DD)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ProductionDateTo", _
                    "Production Date To (YYYY-MM-DD)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ProductionLineFilter", _
                    "Production Line IDs (comma-separated, blank=all)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("DataMode", _
                    "Data Mode: BATCH (daily summary) or REALTIME (shift-level)", _
                    "BATCH", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("IdealCycleTimeSeconds", _
                    "Ideal Cycle Time in Seconds (for OEE Performance calc)", _
                    "30", _
                    DashboardDataSetParamTypes.Text))

                Return paramList

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_MES_ProductionData.SetupUI", ex.Message))
            End Try
        End Function

        Private Sub ValidateConnection(ByVal si As SessionInfo, ByVal args As ConnectorArgs)
            Try
                Dim connString As String = args.GetParameterValue("ConnectionString")
                Using conn As New OdbcConnection(connString)
                    conn.ConnectionTimeout = 30
                    conn.Open()
                    BRApi.ErrorLog.LogMessage(si, "CN_MES_ProductionData: MES database connection test successful.")
                    conn.Close()
                End Using
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_MES_ProductionData.ValidateConnection", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ExtractProductionData: Queries the MES database for production log entries, calculates
        ' OEE components, and loads to OneStream staging.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractProductionData(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim rowCount As Long = 0
            Dim errorCount As Long = 0

            Dim connString As String = args.GetParameterValue("ConnectionString")
            Dim dateFrom As String = args.GetParameterValue("ProductionDateFrom")
            Dim dateTo As String = args.GetParameterValue("ProductionDateTo")
            Dim lineFilter As String = args.GetParameterValue("ProductionLineFilter")
            Dim dataMode As String = If(args.GetParameterValue("DataMode"), "BATCH").ToUpper()
            Dim idealCycleTimeSec As Double = Double.Parse(If(args.GetParameterValue("IdealCycleTimeSeconds"), "30"), _
                CultureInfo.InvariantCulture)

            ' Build production line to entity mapping
            Dim lineEntityMap As Dictionary(Of String, String) = Me.BuildProductionLineMapping()

            Try
                Using conn As New OdbcConnection(connString)
                    conn.Open()

                    Dim sql As String = Me.BuildProductionQuery(dateFrom, dateTo, lineFilter, dataMode)

                    BRApi.ErrorLog.LogMessage(si, $"CN_MES_ProductionData: Executing MES query (mode={dataMode})...")

                    Using cmd As New OdbcCommand(sql, conn)
                        cmd.CommandTimeout = 300
                        Using reader As OdbcDataReader = cmd.ExecuteReader()
                            Dim dt As DataTable = args.GetDataTable()

                            While reader.Read()
                                Try
                                    Dim lineId As String = reader("PRODUCTION_LINE_ID").ToString().Trim()
                                    Dim lineName As String = reader("PRODUCTION_LINE_NAME").ToString().Trim()
                                    Dim productCode As String = reader("PRODUCT_CODE").ToString().Trim()
                                    Dim productionDate As DateTime = Convert.ToDateTime(reader("PRODUCTION_DATE"))
                                    Dim unitsProduced As Decimal = Convert.ToDecimal(reader("UNITS_PRODUCED"))
                                    Dim unitsScrapped As Decimal = Convert.ToDecimal(reader("UNITS_SCRAPPED"))
                                    Dim machineHours As Decimal = Convert.ToDecimal(reader("MACHINE_HOURS"))
                                    Dim downtimeHours As Decimal = Convert.ToDecimal(reader("DOWNTIME_HOURS"))
                                    Dim qualityDefects As Decimal = Convert.ToDecimal(reader("QUALITY_DEFECTS"))
                                    Dim plannedHours As Decimal = Convert.ToDecimal(reader("PLANNED_HOURS"))

                                    ' Map production line to OneStream entity
                                    Dim osEntity As String = ""
                                    If Not lineEntityMap.TryGetValue(lineId, osEntity) Then
                                        osEntity = $"E_Line_{lineId}"
                                    End If

                                    ' Map product to OneStream UD1
                                    Dim osProduct As String = $"UD1_Prod_{productCode}"

                                    ' Determine OneStream time period
                                    Dim osPeriod As String = $"{productionDate.Year}M{productionDate.Month}"

                                    ' Calculate OEE components
                                    Dim goodUnits As Decimal = unitsProduced - unitsScrapped
                                    Dim runTimeHours As Decimal = machineHours - downtimeHours

                                    ' Availability = (Planned - Downtime) / Planned
                                    Dim availability As Decimal = If(plannedHours > 0, _
                                        (plannedHours - downtimeHours) / plannedHours, 0)

                                    ' Performance = (Ideal Cycle Time * Total Count) / Run Time
                                    ' Convert ideal cycle time from seconds to hours
                                    Dim idealCycleTimeHrs As Decimal = CDec(idealCycleTimeSec) / 3600D
                                    Dim performance As Decimal = If(runTimeHours > 0, _
                                        (idealCycleTimeHrs * unitsProduced) / runTimeHours, 0)
                                    ' Cap performance at 100% (can exceed due to ideal cycle time mismatch)
                                    performance = Math.Min(performance, 1D)

                                    ' Quality = Good Count / Total Count
                                    Dim quality As Decimal = If(unitsProduced > 0, _
                                        goodUnits / unitsProduced, 0)

                                    ' OEE = Availability x Performance x Quality
                                    Dim oee As Decimal = availability * performance * quality

                                    ' Load production volume statistics
                                    Dim descBase As String = $"Line:{lineName} Product:{productCode}"

                                    Me.AddStagingRow(dt, osEntity, "A_STAT_UnitsProduced", osPeriod, osProduct, unitsProduced, descBase)
                                    Me.AddStagingRow(dt, osEntity, "A_STAT_UnitsScrapped", osPeriod, osProduct, unitsScrapped, descBase)
                                    Me.AddStagingRow(dt, osEntity, "A_STAT_GoodUnits", osPeriod, osProduct, goodUnits, descBase)
                                    Me.AddStagingRow(dt, osEntity, "A_STAT_MachineHours", osPeriod, osProduct, machineHours, descBase)
                                    Me.AddStagingRow(dt, osEntity, "A_STAT_DowntimeHours", osPeriod, osProduct, downtimeHours, descBase)
                                    Me.AddStagingRow(dt, osEntity, "A_STAT_QualityDefects", osPeriod, osProduct, qualityDefects, descBase)

                                    ' Load OEE components (stored as percentages 0-100)
                                    Me.AddStagingRow(dt, osEntity, "A_STAT_OEE_Availability", osPeriod, osProduct, _
                                        Math.Round(availability * 100, 2), $"{descBase} Avail:{availability:P1}")
                                    Me.AddStagingRow(dt, osEntity, "A_STAT_OEE_Performance", osPeriod, osProduct, _
                                        Math.Round(performance * 100, 2), $"{descBase} Perf:{performance:P1}")
                                    Me.AddStagingRow(dt, osEntity, "A_STAT_OEE_Quality", osPeriod, osProduct, _
                                        Math.Round(quality * 100, 2), $"{descBase} Qual:{quality:P1}")
                                    Me.AddStagingRow(dt, osEntity, "A_STAT_OEE_Total", osPeriod, osProduct, _
                                        Math.Round(oee * 100, 2), $"{descBase} OEE:{oee:P1}")

                                    rowCount += 1

                                    If rowCount Mod 5000 = 0 Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"CN_MES_ProductionData: Progress - {rowCount} line-product-day records processed...")
                                    End If

                                Catch exRow As Exception
                                    errorCount += 1
                                    BRApi.ErrorLog.LogMessage(si, $"CN_MES_ProductionData: Row error: {exRow.Message}")
                                    If errorCount > 500 Then
                                        Throw New XFException(si, "CN_MES_ProductionData", "Error threshold exceeded.")
                                    End If
                                End Try
                            End While
                        End Using
                    End Using

                    conn.Close()
                End Using

                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_MES_ProductionData: Summary - Records:{rowCount}, Errors:{errorCount}")

                Return rowCount

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_MES_ProductionData.ExtractProductionData", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildProductionQuery: Constructs the SQL against the MES database based on data mode.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildProductionQuery(ByVal dateFrom As String, ByVal dateTo As String, _
                                               ByVal lineFilter As String, ByVal dataMode As String) As String
            Dim grouping As String = If(dataMode = "REALTIME", _
                "PL.PRODUCTION_LINE_ID, PL.PRODUCTION_LINE_NAME, P.PRODUCT_CODE, CAST(L.PRODUCTION_TIMESTAMP AS DATE), L.SHIFT_ID", _
                "PL.PRODUCTION_LINE_ID, PL.PRODUCTION_LINE_NAME, P.PRODUCT_CODE, L.PRODUCTION_DATE")

            Dim dateColumn As String = If(dataMode = "REALTIME", _
                "CAST(L.PRODUCTION_TIMESTAMP AS DATE) AS PRODUCTION_DATE", _
                "L.PRODUCTION_DATE")

            Dim sql As String = _
                "SELECT " & _
                "  PL.PRODUCTION_LINE_ID, PL.PRODUCTION_LINE_NAME, " & _
                "  P.PRODUCT_CODE, " & _
                $"  {dateColumn}, " & _
                "  SUM(L.UNITS_PRODUCED) AS UNITS_PRODUCED, " & _
                "  SUM(L.UNITS_SCRAPPED) AS UNITS_SCRAPPED, " & _
                "  SUM(L.MACHINE_HOURS) AS MACHINE_HOURS, " & _
                "  SUM(COALESCE(D.DOWNTIME_HOURS, 0)) AS DOWNTIME_HOURS, " & _
                "  SUM(COALESCE(Q.DEFECT_COUNT, 0)) AS QUALITY_DEFECTS, " & _
                "  SUM(L.PLANNED_HOURS) AS PLANNED_HOURS " & _
                "FROM PRODUCTION_LOG L " & _
                "INNER JOIN PRODUCTION_LINES PL ON L.PRODUCTION_LINE_ID = PL.PRODUCTION_LINE_ID " & _
                "INNER JOIN PRODUCTS P ON L.PRODUCT_ID = P.PRODUCT_ID " & _
                "LEFT JOIN ( " & _
                "  SELECT PRODUCTION_LINE_ID, PRODUCTION_DATE, SUM(DURATION_HOURS) AS DOWNTIME_HOURS " & _
                "  FROM DOWNTIME_EVENTS GROUP BY PRODUCTION_LINE_ID, PRODUCTION_DATE " & _
                ") D ON L.PRODUCTION_LINE_ID = D.PRODUCTION_LINE_ID AND L.PRODUCTION_DATE = D.PRODUCTION_DATE " & _
                "LEFT JOIN ( " & _
                "  SELECT PRODUCTION_LINE_ID, INSPECTION_DATE, SUM(DEFECT_COUNT) AS DEFECT_COUNT " & _
                "  FROM QUALITY_INSPECTIONS GROUP BY PRODUCTION_LINE_ID, INSPECTION_DATE " & _
                ") Q ON L.PRODUCTION_LINE_ID = Q.PRODUCTION_LINE_ID AND L.PRODUCTION_DATE = Q.INSPECTION_DATE " & _
                "WHERE 1=1 "

            If Not String.IsNullOrWhiteSpace(dateFrom) Then
                sql &= $" AND L.PRODUCTION_DATE >= '{dateFrom}'"
            End If
            If Not String.IsNullOrWhiteSpace(dateTo) Then
                sql &= $" AND L.PRODUCTION_DATE <= '{dateTo}'"
            End If

            If Not String.IsNullOrWhiteSpace(lineFilter) Then
                Dim lines As String() = lineFilter.Split(","c)
                Dim inClause As String = String.Join(",", lines.Select(Function(l) $"'{l.Trim()}'"))
                sql &= $" AND PL.PRODUCTION_LINE_ID IN ({inClause})"
            End If

            sql &= $" GROUP BY {grouping} ORDER BY PL.PRODUCTION_LINE_ID, L.PRODUCTION_DATE"

            Return sql
        End Function

        Private Sub AddStagingRow(ByVal dt As DataTable, ByVal entity As String, ByVal account As String, _
                                   ByVal timePeriod As String, ByVal product As String, _
                                   ByVal amount As Decimal, ByVal description As String)
            Dim newRow As DataRow = dt.NewRow()
            newRow("Entity") = entity
            newRow("Account") = account
            newRow("Time") = timePeriod
            newRow("Scenario") = "Actual"
            newRow("Flow") = "F_None"
            newRow("Origin") = "O_None"
            newRow("IC") = "I_None"
            newRow("UD1") = product
            newRow("UD2") = "UD2_None"
            newRow("UD3") = "UD3_None"
            newRow("UD4") = "UD4_None"
            newRow("Amount") = amount
            newRow("Description") = description
            dt.Rows.Add(newRow)
        End Sub

        Private Function BuildProductionLineMapping() As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            map.Add("PL001", "E_PlantUSA_Assembly")
            map.Add("PL002", "E_PlantUSA_Machining")
            map.Add("PL003", "E_PlantUSA_Finishing")
            map.Add("PL004", "E_PlantDE_Assembly")
            map.Add("PL005", "E_PlantDE_Machining")
            map.Add("PL006", "E_PlantCN_Assembly")
            Return map
        End Function

    End Class

End Namespace

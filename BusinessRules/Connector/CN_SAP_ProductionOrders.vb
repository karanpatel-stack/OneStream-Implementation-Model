'------------------------------------------------------------------------------------------------------------
' OneStream XF Connector Business Rule: CN_SAP_ProductionOrders
'------------------------------------------------------------------------------------------------------------
' Purpose:     Extracts production order data from SAP PP module (AFKO/AFPO tables) for manufacturing
'              performance analysis. Retrieves planned vs actual quantities, dates, and costs at the
'              production order level, calculates variances, and loads to OneStream staging.
'
' Source:      SAP S/4HANA or ECC -- AFKO (Production Order Header), AFPO (Production Order Items)
' Target:      OneStream staging table for production statistics and cost variance analysis
'
' Key Fields:  Order Number (AUFNR), Material (MATNR), Plant (WERKS), Planned Qty (GAMNG),
'              Actual Qty (IGMNG), Planned Start/End (GSTRP/GLTRP), Actual Start/End (GSTRI/GLTRI),
'              System Status (OBJNR -> JEST/TJ02T), Planned Cost, Actual Cost
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

Namespace OneStream.BusinessRule.Connector.CN_SAP_ProductionOrders

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
                        Dim rowsLoaded As Long = Me.ExtractProductionOrders(si, args)
                        BRApi.ErrorLog.LogMessage(si, $"CN_SAP_ProductionOrders: Completed. Rows loaded: {rowsLoaded}")

                    Case Is = ConnectorActionTypes.Finalize
                        BRApi.ErrorLog.LogMessage(si, "CN_SAP_ProductionOrders: Finalize phase completed.")
                End Select

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_ProductionOrders.Main", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' SetupUI: Defines parameters for Data Management workspace configuration.
        '----------------------------------------------------------------------------------------------------
        Private Function SetupUI(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Object
            Try
                Dim paramList As New List(Of DashboardDataSetParam)

                paramList.Add(New DashboardDataSetParam("ConnectionString", _
                    "SAP HANA ODBC Connection String", _
                    "DSN=SAPHANA;UID=ONESTREAM_SVC;PWD=;", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("PlantFilter", _
                    "SAP Plant Codes (comma-separated, blank=all)", _
                    "1000,2000", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("OrderDateFrom", _
                    "Order Creation Date From (YYYYMMDD)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("OrderDateTo", _
                    "Order Creation Date To (YYYYMMDD)", _
                    "", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("IncludeClosedOrders", _
                    "Include Closed/Completed Orders (True/False)", _
                    "True", _
                    DashboardDataSetParamTypes.Text))

                Return paramList

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_ProductionOrders.SetupUI", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ValidateConnection: Tests the ODBC connection to SAP HANA.
        '----------------------------------------------------------------------------------------------------
        Private Sub ValidateConnection(ByVal si As SessionInfo, ByVal args As ConnectorArgs)
            Try
                Dim connString As String = args.GetParameterValue("ConnectionString")
                If String.IsNullOrWhiteSpace(connString) Then
                    Throw New XFException(si, "CN_SAP_ProductionOrders", "Connection string is required.")
                End If

                Using conn As New OdbcConnection(connString)
                    conn.ConnectionTimeout = 30
                    conn.Open()
                    BRApi.ErrorLog.LogMessage(si, "CN_SAP_ProductionOrders: Connection test successful.")
                    conn.Close()
                End Using

            Catch ex As OdbcException
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_ProductionOrders.ValidateConnection", _
                    $"Connection failed: {ex.Message}"))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ExtractProductionOrders: Queries AFKO/AFPO for production order data, maps to OneStream
        ' dimensions, calculates variances, and loads to staging.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractProductionOrders(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim rowCount As Long = 0
            Dim errorCount As Long = 0

            Dim connString As String = args.GetParameterValue("ConnectionString")
            Dim plantFilter As String = args.GetParameterValue("PlantFilter")
            Dim dateFrom As String = args.GetParameterValue("OrderDateFrom")
            Dim dateTo As String = args.GetParameterValue("OrderDateTo")
            Dim includeClosedStr As String = If(args.GetParameterValue("IncludeClosedOrders"), "True")
            Dim includeClosed As Boolean = Boolean.Parse(includeClosedStr)

            ' Build plant-to-entity mapping
            Dim plantEntityMap As Dictionary(Of String, String) = Me.BuildPlantMapping()

            ' Build material-to-product mapping
            Dim materialProductMap As Dictionary(Of String, String) = Me.BuildMaterialMapping()

            Try
                Using conn As New OdbcConnection(connString)
                    conn.Open()

                    Dim sql As String = Me.BuildProductionOrderQuery(plantFilter, dateFrom, dateTo, includeClosed)

                    BRApi.ErrorLog.LogMessage(si, "CN_SAP_ProductionOrders: Executing production order query...")

                    Using cmd As New OdbcCommand(sql, conn)
                        cmd.CommandTimeout = 300

                        Using reader As OdbcDataReader = cmd.ExecuteReader()
                            Dim dt As DataTable = args.GetDataTable()

                            While reader.Read()
                                Try
                                    Dim orderNumber As String = reader("AUFNR").ToString().Trim()
                                    Dim material As String = reader("MATNR").ToString().Trim()
                                    Dim plant As String = reader("WERKS").ToString().Trim()
                                    Dim plannedQty As Decimal = Convert.ToDecimal(reader("GAMNG"))
                                    Dim actualQty As Decimal = Convert.ToDecimal(reader("IGMNG"))
                                    Dim plannedStart As String = reader("GSTRP").ToString().Trim()
                                    Dim actualStart As String = reader("GSTRI").ToString().Trim()
                                    Dim plannedCost As Decimal = Convert.ToDecimal(reader("PLANNED_COST"))
                                    Dim actualCost As Decimal = Convert.ToDecimal(reader("ACTUAL_COST"))
                                    Dim orderStatus As String = reader("STATUS_TEXT").ToString().Trim()

                                    ' Map plant to OneStream entity
                                    Dim osEntity As String = ""
                                    If Not plantEntityMap.TryGetValue(plant, osEntity) Then
                                        osEntity = $"E_Plant_{plant}"
                                    End If

                                    ' Map material to OneStream product dimension
                                    Dim osProduct As String = ""
                                    If Not materialProductMap.TryGetValue(material.TrimStart("0"c), osProduct) Then
                                        osProduct = $"P_{material.TrimStart("0"c)}"
                                    End If

                                    ' Determine OneStream time period from the actual start or planned start
                                    Dim refDate As String = If(Not String.IsNullOrWhiteSpace(actualStart), actualStart, plannedStart)
                                    Dim osPeriod As String = Me.ParseSAPDateToPeriod(refDate)

                                    ' Calculate variances
                                    Dim qtyVariance As Decimal = actualQty - plannedQty
                                    Dim costVariance As Decimal = actualCost - plannedCost
                                    Dim qtyVariancePct As Decimal = If(plannedQty <> 0, (qtyVariance / plannedQty) * 100, 0)
                                    Dim costVariancePct As Decimal = If(plannedCost <> 0, (costVariance / plannedCost) * 100, 0)

                                    ' Load planned quantity row
                                    Me.AddStagingRow(dt, osEntity, "A_ProdOrder_PlannedQty", osPeriod, _
                                        osProduct, plannedQty, $"Order:{orderNumber} Mat:{material}")

                                    ' Load actual quantity row
                                    Me.AddStagingRow(dt, osEntity, "A_ProdOrder_ActualQty", osPeriod, _
                                        osProduct, actualQty, $"Order:{orderNumber} Mat:{material}")

                                    ' Load quantity variance
                                    Me.AddStagingRow(dt, osEntity, "A_ProdOrder_QtyVariance", osPeriod, _
                                        osProduct, qtyVariance, $"Order:{orderNumber} Var%:{qtyVariancePct:F1}")

                                    ' Load planned cost
                                    Me.AddStagingRow(dt, osEntity, "A_ProdOrder_PlannedCost", osPeriod, _
                                        osProduct, plannedCost, $"Order:{orderNumber}")

                                    ' Load actual cost
                                    Me.AddStagingRow(dt, osEntity, "A_ProdOrder_ActualCost", osPeriod, _
                                        osProduct, actualCost, $"Order:{orderNumber}")

                                    ' Load cost variance
                                    Me.AddStagingRow(dt, osEntity, "A_ProdOrder_CostVariance", osPeriod, _
                                        osProduct, costVariance, $"Order:{orderNumber} Var%:{costVariancePct:F1}")

                                    rowCount += 1

                                    If rowCount Mod 5000 = 0 Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"CN_SAP_ProductionOrders: Progress - {rowCount} orders processed...")
                                    End If

                                Catch exRow As Exception
                                    errorCount += 1
                                    BRApi.ErrorLog.LogMessage(si, _
                                        $"CN_SAP_ProductionOrders: Row error: {exRow.Message}")
                                    If errorCount > 500 Then
                                        Throw New XFException(si, "CN_SAP_ProductionOrders", _
                                            "Error threshold exceeded. Aborting.")
                                    End If
                                End Try
                            End While
                        End Using
                    End Using

                    conn.Close()
                End Using

                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_SAP_ProductionOrders: Summary - Orders:{rowCount}, Errors:{errorCount}")

                Return rowCount

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_ProductionOrders.ExtractProductionOrders", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildProductionOrderQuery: Constructs the SQL for AFKO/AFPO production order extraction.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildProductionOrderQuery(ByVal plantFilter As String, ByVal dateFrom As String, _
                                                    ByVal dateTo As String, ByVal includeClosed As Boolean) As String
            Dim sql As String = _
                "SELECT H.AUFNR, I.MATNR, I.WERKS, " & _
                "H.GAMNG, H.IGMNG, " & _
                "H.GSTRP, H.GLTRP, H.GSTRI, H.GLTRI, " & _
                "COALESCE(C.PLANNED_COST, 0) AS PLANNED_COST, " & _
                "COALESCE(C.ACTUAL_COST, 0) AS ACTUAL_COST, " & _
                "COALESCE(S.STATUS_TEXT, 'Unknown') AS STATUS_TEXT " & _
                "FROM AFKO H " & _
                "INNER JOIN AFPO I ON H.AUFNR = I.AUFNR " & _
                "LEFT JOIN ( " & _
                "  SELECT AUFNR, SUM(WRT_PLAN) AS PLANNED_COST, SUM(WRT_IST) AS ACTUAL_COST " & _
                "  FROM COEP WHERE VERSN = '000' GROUP BY AUFNR " & _
                ") C ON H.AUFNR = C.AUFNR " & _
                "LEFT JOIN ( " & _
                "  SELECT OBJNR, STRING_AGG(TXT04, ',') AS STATUS_TEXT " & _
                "  FROM JEST J INNER JOIN TJ02T T ON J.STAT = T.ISTAT " & _
                "  WHERE J.INACT = '' AND T.SPRAS = 'E' GROUP BY OBJNR " & _
                ") S ON CONCAT('OR', H.AUFNR) = S.OBJNR " & _
                "WHERE 1=1 "

            If Not String.IsNullOrWhiteSpace(plantFilter) Then
                Dim plants As String() = plantFilter.Split(","c)
                Dim inClause As String = String.Join(",", plants.Select(Function(p) $"'{p.Trim()}'"))
                sql &= $" AND I.WERKS IN ({inClause})"
            End If

            If Not String.IsNullOrWhiteSpace(dateFrom) Then
                sql &= $" AND H.GSTRP >= '{dateFrom}'"
            End If
            If Not String.IsNullOrWhiteSpace(dateTo) Then
                sql &= $" AND H.GSTRP <= '{dateTo}'"
            End If

            If Not includeClosed Then
                sql &= " AND S.STATUS_TEXT NOT LIKE '%CLSD%' AND S.STATUS_TEXT NOT LIKE '%DLT%'"
            End If

            sql &= " ORDER BY H.AUFNR"

            Return sql
        End Function

        '----------------------------------------------------------------------------------------------------
        ' AddStagingRow: Adds a single row to the staging DataTable with standard dimension mapping.
        '----------------------------------------------------------------------------------------------------
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

        '----------------------------------------------------------------------------------------------------
        ' ParseSAPDateToPeriod: Converts SAP date string (YYYYMMDD) to OneStream period (YYYYMn).
        '----------------------------------------------------------------------------------------------------
        Private Function ParseSAPDateToPeriod(ByVal sapDate As String) As String
            Try
                If String.IsNullOrWhiteSpace(sapDate) OrElse sapDate.Length < 8 Then
                    Return $"{DateTime.Now.Year}M{DateTime.Now.Month}"
                End If
                Dim dt As DateTime = DateTime.ParseExact(sapDate.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture)
                Return $"{dt.Year}M{dt.Month}"
            Catch
                Return $"{DateTime.Now.Year}M{DateTime.Now.Month}"
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildPlantMapping: Maps SAP plant codes to OneStream entity members.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildPlantMapping() As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            map.Add("1000", "E_PlantUSA_Main")
            map.Add("1100", "E_PlantUSA_West")
            map.Add("2000", "E_PlantDE_Main")
            map.Add("2100", "E_PlantDE_South")
            map.Add("3000", "E_PlantCN_Shanghai")
            Return map
        End Function

        '----------------------------------------------------------------------------------------------------
        ' BuildMaterialMapping: Maps SAP material numbers to OneStream product dimension members.
        '----------------------------------------------------------------------------------------------------
        Private Function BuildMaterialMapping() As Dictionary(Of String, String)
            Dim map As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            map.Add("FG1000", "P_FinishedGood_A")
            map.Add("FG2000", "P_FinishedGood_B")
            map.Add("FG3000", "P_FinishedGood_C")
            map.Add("SFG100", "P_SemiFinished_A")
            map.Add("SFG200", "P_SemiFinished_B")
            Return map
        End Function

    End Class

End Namespace

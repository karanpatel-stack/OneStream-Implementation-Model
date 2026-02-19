'------------------------------------------------------------------------------------------------------------
' OneStream XF Connector Business Rule: CN_SAP_MaterialMaster
'------------------------------------------------------------------------------------------------------------
' Purpose:     Extracts material master data and Bill of Materials (BOM) structures from SAP MM/PP
'              modules. Queries MARA/MARC for material attributes and STKO/STPO for BOM headers
'              and items, builds a BOM hierarchy for cost rollup purposes, and loads the data to
'              OneStream staging for product dimension mapping and BOM cost calculations.
'
' Source:      SAP S/4HANA or ECC
'              - MARA (General Material Data), MARC (Plant-Level Material Data)
'              - STKO (BOM Header), STPO (BOM Items/Components)
'              - MBEW (Material Valuation / Standard Price)
' Target:      OneStream staging table for product dimension and cost structure
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

Namespace OneStream.BusinessRule.Connector.CN_SAP_MaterialMaster

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As ConnectorArgs) As Object
            Try
                Select Case args.ActionType
                    Case Is = ConnectorActionTypes.UI
                        Return Me.SetupUI(si, args)

                    Case Is = ConnectorActionTypes.Initialize
                        Me.TestConnection(si, args)

                    Case Is = ConnectorActionTypes.GetData
                        Dim totalRows As Long = 0
                        ' Phase 1: Extract material master attributes
                        totalRows += Me.ExtractMaterialMaster(si, args)
                        ' Phase 2: Extract BOM structures
                        totalRows += Me.ExtractBOMStructures(si, args)
                        BRApi.ErrorLog.LogMessage(si, $"CN_SAP_MaterialMaster: Total rows loaded: {totalRows}")

                    Case Is = ConnectorActionTypes.Finalize
                        BRApi.ErrorLog.LogMessage(si, "CN_SAP_MaterialMaster: Finalize completed.")
                End Select

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_MaterialMaster.Main", ex.Message))
            End Try
        End Function

        Private Function SetupUI(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Object
            Try
                Dim paramList As New List(Of DashboardDataSetParam)

                paramList.Add(New DashboardDataSetParam("ConnectionString", _
                    "SAP HANA ODBC Connection String", _
                    "DSN=SAPHANA;UID=ONESTREAM_SVC;PWD=;", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("PlantFilter", _
                    "Plant Codes (comma-separated)", _
                    "1000,2000", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("MaterialTypeFilter", _
                    "Material Types (FERT=Finished, HALB=SemiFinished, ROH=Raw)", _
                    "FERT,HALB,ROH", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("BOMUsage", _
                    "BOM Usage (1=Production, 2=Engineering, 5=Costing)", _
                    "1", _
                    DashboardDataSetParamTypes.Text))

                Return paramList

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_MaterialMaster.SetupUI", ex.Message))
            End Try
        End Function

        Private Sub TestConnection(ByVal si As SessionInfo, ByVal args As ConnectorArgs)
            Try
                Dim connString As String = args.GetParameterValue("ConnectionString")
                Using conn As New OdbcConnection(connString)
                    conn.ConnectionTimeout = 30
                    conn.Open()
                    BRApi.ErrorLog.LogMessage(si, "CN_SAP_MaterialMaster: Connection test successful.")
                    conn.Close()
                End Using
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_MaterialMaster.TestConnection", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ExtractMaterialMaster: Queries MARA/MARC/MBEW for material attributes and standard costs.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractMaterialMaster(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim rowCount As Long = 0
            Dim connString As String = args.GetParameterValue("ConnectionString")
            Dim plantFilter As String = args.GetParameterValue("PlantFilter")
            Dim materialTypeFilter As String = args.GetParameterValue("MaterialTypeFilter")

            Try
                Using conn As New OdbcConnection(connString)
                    conn.Open()

                    ' Build SQL for material master with valuation data
                    Dim sql As String = _
                        "SELECT M.MATNR, M.MTART, M.MATKL, M.MEINS, " & _
                        "T.MAKTX, " & _
                        "C.WERKS, C.MMSTA, C.PRCTR, " & _
                        "V.STPRS, V.PEINH, V.VPRSV, V.VERPR " & _
                        "FROM MARA M " & _
                        "INNER JOIN MAKT T ON M.MATNR = T.MATNR AND T.SPRAS = 'E' " & _
                        "INNER JOIN MARC C ON M.MATNR = C.MATNR " & _
                        "LEFT JOIN MBEW V ON M.MATNR = V.MATNR AND C.WERKS = V.BWKEY " & _
                        "WHERE M.LVORM = '' "  ' Exclude materials flagged for deletion

                    ' Apply material type filter
                    If Not String.IsNullOrWhiteSpace(materialTypeFilter) Then
                        Dim types As String() = materialTypeFilter.Split(","c)
                        Dim inClause As String = String.Join(",", types.Select(Function(t) $"'{t.Trim()}'"))
                        sql &= $" AND M.MTART IN ({inClause})"
                    End If

                    ' Apply plant filter
                    If Not String.IsNullOrWhiteSpace(plantFilter) Then
                        Dim plants As String() = plantFilter.Split(","c)
                        Dim inClause As String = String.Join(",", plants.Select(Function(p) $"'{p.Trim()}'"))
                        sql &= $" AND C.WERKS IN ({inClause})"
                    End If

                    sql &= " ORDER BY M.MATNR, C.WERKS"

                    BRApi.ErrorLog.LogMessage(si, "CN_SAP_MaterialMaster: Extracting material master data...")

                    Using cmd As New OdbcCommand(sql, conn)
                        cmd.CommandTimeout = 300
                        Using reader As OdbcDataReader = cmd.ExecuteReader()
                            Dim dt As DataTable = args.GetDataTable()

                            While reader.Read()
                                Try
                                    Dim materialNum As String = reader("MATNR").ToString().Trim().TrimStart("0"c)
                                    Dim materialType As String = reader("MTART").ToString().Trim()
                                    Dim materialGroup As String = reader("MATKL").ToString().Trim()
                                    Dim baseUOM As String = reader("MEINS").ToString().Trim()
                                    Dim description As String = reader("MAKTX").ToString().Trim()
                                    Dim plant As String = reader("WERKS").ToString().Trim()
                                    Dim profitCenter As String = reader("PRCTR").ToString().Trim()
                                    Dim standardPrice As Decimal = Convert.ToDecimal(If(reader("STPRS") Is DBNull.Value, 0, reader("STPRS")))
                                    Dim priceUnit As Decimal = Convert.ToDecimal(If(reader("PEINH") Is DBNull.Value, 1, reader("PEINH")))
                                    Dim priceControl As String = reader("VPRSV").ToString().Trim()

                                    ' Calculate unit standard price
                                    Dim unitPrice As Decimal = If(priceUnit > 0, standardPrice / priceUnit, standardPrice)

                                    ' Map to OneStream product dimension
                                    Dim osProduct As String = Me.MapMaterialToProduct(materialNum, materialType)

                                    ' Map plant to entity
                                    Dim osEntity As String = $"E_Plant_{plant}"

                                    ' Load standard cost to staging
                                    Dim newRow As DataRow = dt.NewRow()
                                    newRow("Entity") = osEntity
                                    newRow("Account") = "A_StdCost_Material"
                                    newRow("Time") = "CurrentPeriod"
                                    newRow("Scenario") = "Actual"
                                    newRow("Flow") = "F_None"
                                    newRow("Origin") = "O_None"
                                    newRow("IC") = "I_None"
                                    newRow("UD1") = osProduct
                                    newRow("UD2") = $"UD2_{profitCenter.TrimStart("0"c)}"
                                    newRow("UD3") = $"UD3_MG_{materialGroup}"
                                    newRow("UD4") = "UD4_None"
                                    newRow("Amount") = unitPrice
                                    newRow("Description") = $"{description} ({materialType}/{baseUOM}) PrCtrl:{priceControl}"
                                    dt.Rows.Add(newRow)

                                    rowCount += 1

                                Catch exRow As Exception
                                    BRApi.ErrorLog.LogMessage(si, $"CN_SAP_MaterialMaster: Row error: {exRow.Message}")
                                End Try
                            End While
                        End Using
                    End Using

                    conn.Close()
                End Using

                BRApi.ErrorLog.LogMessage(si, $"CN_SAP_MaterialMaster: Material master rows loaded: {rowCount}")
                Return rowCount

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_MaterialMaster.ExtractMaterialMaster", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ExtractBOMStructures: Queries STKO/STPO for Bill of Materials and builds component hierarchy.
        '----------------------------------------------------------------------------------------------------
        Private Function ExtractBOMStructures(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim rowCount As Long = 0
            Dim connString As String = args.GetParameterValue("ConnectionString")
            Dim plantFilter As String = args.GetParameterValue("PlantFilter")
            Dim bomUsage As String = If(args.GetParameterValue("BOMUsage"), "1")

            Try
                Using conn As New OdbcConnection(connString)
                    conn.Open()

                    Dim sql As String = _
                        "SELECT H.STLNR, H.STLAL, H.BMENG, H.BMEIN, " & _
                        "I.POSNR, I.IDNRK, I.MENGE, I.MEINS, I.POSTP, " & _
                        "M.MATNR AS PARENT_MATNR, MC.WERKS " & _
                        "FROM STKO H " & _
                        "INNER JOIN STPO I ON H.STLNR = I.STLNR AND H.STLAL = I.STLAL " & _
                        "INNER JOIN MAST MA ON H.STLNR = MA.STLNR AND H.STLAL = MA.STLAL " & _
                        "INNER JOIN MARA M ON MA.MATNR = M.MATNR " & _
                        "INNER JOIN MARC MC ON M.MATNR = MC.MATNR " & _
                        $"WHERE H.STLTY = 'M' AND MA.STLAN = '{bomUsage}' "

                    If Not String.IsNullOrWhiteSpace(plantFilter) Then
                        Dim plants As String() = plantFilter.Split(","c)
                        Dim inClause As String = String.Join(",", plants.Select(Function(p) $"'{p.Trim()}'"))
                        sql &= $" AND MC.WERKS IN ({inClause})"
                    End If

                    sql &= " ORDER BY M.MATNR, I.POSNR"

                    BRApi.ErrorLog.LogMessage(si, "CN_SAP_MaterialMaster: Extracting BOM structures...")

                    Using cmd As New OdbcCommand(sql, conn)
                        cmd.CommandTimeout = 300
                        Using reader As OdbcDataReader = cmd.ExecuteReader()
                            Dim dt As DataTable = args.GetDataTable()

                            While reader.Read()
                                Try
                                    Dim parentMaterial As String = reader("PARENT_MATNR").ToString().Trim().TrimStart("0"c)
                                    Dim componentMaterial As String = reader("IDNRK").ToString().Trim().TrimStart("0"c)
                                    Dim componentQty As Decimal = Convert.ToDecimal(reader("MENGE"))
                                    Dim baseQty As Decimal = Convert.ToDecimal(reader("BMENG"))
                                    Dim plant As String = reader("WERKS").ToString().Trim()
                                    Dim itemCategory As String = reader("POSTP").ToString().Trim()

                                    ' Calculate quantity per unit of parent (component qty / base qty)
                                    Dim qtyPerUnit As Decimal = If(baseQty > 0, componentQty / baseQty, componentQty)

                                    Dim osParentProduct As String = $"P_{parentMaterial}"
                                    Dim osComponentProduct As String = $"P_{componentMaterial}"
                                    Dim osEntity As String = $"E_Plant_{plant}"

                                    ' Load BOM component quantity per unit to staging
                                    Dim newRow As DataRow = dt.NewRow()
                                    newRow("Entity") = osEntity
                                    newRow("Account") = "A_BOM_QtyPerUnit"
                                    newRow("Time") = "CurrentPeriod"
                                    newRow("Scenario") = "Actual"
                                    newRow("Flow") = "F_None"
                                    newRow("Origin") = "O_None"
                                    newRow("IC") = "I_None"
                                    newRow("UD1") = osParentProduct
                                    newRow("UD2") = osComponentProduct
                                    newRow("UD3") = $"UD3_ItemCat_{itemCategory}"
                                    newRow("UD4") = "UD4_None"
                                    newRow("Amount") = qtyPerUnit
                                    newRow("Description") = $"BOM: {parentMaterial} -> {componentMaterial} Qty/Unit:{qtyPerUnit:F4}"
                                    dt.Rows.Add(newRow)

                                    rowCount += 1

                                Catch exRow As Exception
                                    BRApi.ErrorLog.LogMessage(si, $"CN_SAP_MaterialMaster: BOM row error: {exRow.Message}")
                                End Try
                            End While
                        End Using
                    End Using

                    conn.Close()
                End Using

                BRApi.ErrorLog.LogMessage(si, $"CN_SAP_MaterialMaster: BOM rows loaded: {rowCount}")
                Return rowCount

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_SAP_MaterialMaster.ExtractBOMStructures", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' MapMaterialToProduct: Maps SAP material numbers to OneStream product dimension members
        ' based on material type classification.
        '----------------------------------------------------------------------------------------------------
        Private Function MapMaterialToProduct(ByVal materialNum As String, ByVal materialType As String) As String
            Select Case materialType.ToUpper()
                Case "FERT"
                    Return $"P_FG_{materialNum}"
                Case "HALB"
                    Return $"P_SFG_{materialNum}"
                Case "ROH"
                    Return $"P_RM_{materialNum}"
                Case "HIBE"
                    Return $"P_MRO_{materialNum}"
                Case Else
                    Return $"P_{materialNum}"
            End Select
        End Function

    End Class

End Namespace

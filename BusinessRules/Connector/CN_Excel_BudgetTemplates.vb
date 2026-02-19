'------------------------------------------------------------------------------------------------------------
' OneStream XF Connector Business Rule: CN_Excel_BudgetTemplates
'------------------------------------------------------------------------------------------------------------
' Purpose:     Ingests budget data from Excel template workbooks stored in a designated folder.
'              Parses multi-tab workbook structures where each tab represents an entity or department,
'              reads named ranges or structured tables containing budget line items, validates
'              dimension member names against OneStream metadata, and loads validated data to staging.
'
' Supported Template Formats:
'   - Revenue Budget     (tabs: one per entity, rows: product x month, columns: Jan-Dec amounts)
'   - OPEX Budget        (tabs: one per department, rows: expense account x month)
'   - Headcount Budget   (tabs: one per cost center, rows: position x month FTE/cost)
'   - CAPEX Budget       (tabs: one per entity, rows: project x month spend)
'
' Folder Convention:
'   \\fileserver\onestream\budget_templates\{Year}\{Scenario}\*.xlsx
'
' Author:      OneStream Administrator
' Created:     2026-02-18
' Modified:    2026-02-18
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Data.Common
Imports System.Data.OleDb
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

Namespace OneStream.BusinessRule.Connector.CN_Excel_BudgetTemplates

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As ConnectorArgs) As Object
            Try
                Select Case args.ActionType
                    Case Is = ConnectorActionTypes.UI
                        Return Me.SetupUI(si, args)

                    Case Is = ConnectorActionTypes.Initialize
                        Me.ValidateFolderAndFiles(si, args)

                    Case Is = ConnectorActionTypes.GetData
                        Dim rowsLoaded As Long = Me.ProcessBudgetTemplates(si, args)
                        BRApi.ErrorLog.LogMessage(si, $"CN_Excel_BudgetTemplates: Processing complete. Rows loaded: {rowsLoaded}")

                    Case Is = ConnectorActionTypes.Finalize
                        BRApi.ErrorLog.LogMessage(si, "CN_Excel_BudgetTemplates: Finalize phase completed.")
                End Select

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Excel_BudgetTemplates.Main", ex.Message))
            End Try
        End Function

        Private Function SetupUI(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Object
            Try
                Dim paramList As New List(Of DashboardDataSetParam)

                paramList.Add(New DashboardDataSetParam("FolderPath", _
                    "Budget Template Folder Path (UNC or local)", _
                    "\\fileserver\onestream\budget_templates\2026\Budget\", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("TemplateType", _
                    "Template Type (Revenue, OPEX, Headcount, CAPEX)", _
                    "OPEX", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("BudgetYear", _
                    "Budget Year (e.g. 2026)", _
                    "2026", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ScenarioName", _
                    "OneStream Scenario Name", _
                    "Budget", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ExpectedVersion", _
                    "Expected Template Version (e.g. 3.0)", _
                    "3.0", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("FilePattern", _
                    "File Name Pattern (e.g. *_Budget_*.xlsx)", _
                    "*.xlsx", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ValidateMembers", _
                    "Validate Dimension Members (True/False)", _
                    "True", _
                    DashboardDataSetParamTypes.Text))

                Return paramList

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Excel_BudgetTemplates.SetupUI", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ValidateFolderAndFiles: Ensures the template folder exists and contains processable files.
        '----------------------------------------------------------------------------------------------------
        Private Sub ValidateFolderAndFiles(ByVal si As SessionInfo, ByVal args As ConnectorArgs)
            Try
                Dim folderPath As String = args.GetParameterValue("FolderPath")
                Dim filePattern As String = If(args.GetParameterValue("FilePattern"), "*.xlsx")

                If Not Directory.Exists(folderPath) Then
                    Throw New XFException(si, "CN_Excel_BudgetTemplates", _
                        $"Template folder does not exist: {folderPath}")
                End If

                Dim files As String() = Directory.GetFiles(folderPath, filePattern)
                If files.Length = 0 Then
                    Throw New XFException(si, "CN_Excel_BudgetTemplates", _
                        $"No files matching pattern '{filePattern}' found in: {folderPath}")
                End If

                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_Excel_BudgetTemplates: Found {files.Length} template file(s) in {folderPath}")

            Catch ex As XFException
                Throw
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Excel_BudgetTemplates.ValidateFolderAndFiles", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ProcessBudgetTemplates: Iterates through all Excel files in the template folder, reads
        ' each workbook and tab, validates and transforms data, and loads to staging.
        '----------------------------------------------------------------------------------------------------
        Private Function ProcessBudgetTemplates(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim totalRowsLoaded As Long = 0
            Dim totalErrors As Long = 0
            Dim filesProcessed As Integer = 0
            Dim filesRejected As Integer = 0

            Dim folderPath As String = args.GetParameterValue("FolderPath")
            Dim templateType As String = If(args.GetParameterValue("TemplateType"), "OPEX").ToUpper()
            Dim budgetYear As String = If(args.GetParameterValue("BudgetYear"), DateTime.Now.Year.ToString())
            Dim scenarioName As String = If(args.GetParameterValue("ScenarioName"), "Budget")
            Dim expectedVersion As String = If(args.GetParameterValue("ExpectedVersion"), "3.0")
            Dim filePattern As String = If(args.GetParameterValue("FilePattern"), "*.xlsx")
            Dim validateMembers As Boolean = Boolean.Parse(If(args.GetParameterValue("ValidateMembers"), "True"))

            Dim dt As DataTable = args.GetDataTable()

            ' Get the list of valid dimension members for validation
            Dim validEntities As HashSet(Of String) = Nothing
            Dim validAccounts As HashSet(Of String) = Nothing
            If validateMembers Then
                validEntities = Me.LoadValidMembers(si, "Entity")
                validAccounts = Me.LoadValidMembers(si, "Account")
            End If

            Try
                Dim files As String() = Directory.GetFiles(folderPath, filePattern)

                For Each filePath As String In files
                    Dim fileName As String = Path.GetFileName(filePath)
                    BRApi.ErrorLog.LogMessage(si, $"CN_Excel_BudgetTemplates: Processing file: {fileName}")

                    Try
                        ' Build OleDb connection string for Excel
                        Dim excelConnStr As String = _
                            $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={filePath};" & _
                            "Extended Properties=""Excel 12.0 Xml;HDR=YES;IMEX=1;"""

                        Using excelConn As New OleDbConnection(excelConnStr)
                            excelConn.Open()

                            ' Validate template version from the Config tab
                            If Not Me.ValidateTemplateVersion(si, excelConn, expectedVersion, fileName) Then
                                BRApi.ErrorLog.LogMessage(si, _
                                    $"CN_Excel_BudgetTemplates: REJECTED - {fileName} - Version mismatch (expected {expectedVersion})")
                                filesRejected += 1
                                Continue For
                            End If

                            ' Get the list of worksheet names (each tab is an entity/department)
                            Dim schemaTable As DataTable = excelConn.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, Nothing)
                            If schemaTable Is Nothing Then Continue For

                            For Each schemaRow As DataRow In schemaTable.Rows
                                Dim sheetName As String = schemaRow("TABLE_NAME").ToString()

                                ' Skip system sheets and the Config tab
                                If sheetName.StartsWith("_") OrElse sheetName.Contains("Config") _
                                    OrElse sheetName.Contains("Instructions") Then
                                    Continue For
                                End If

                                ' Clean sheet name (remove trailing $)
                                Dim cleanSheetName As String = sheetName.Replace("$", "").Replace("'", "")

                                BRApi.ErrorLog.LogMessage(si, $"  Processing tab: {cleanSheetName}")

                                Try
                                    ' Read the worksheet data
                                    Dim sql As String = $"SELECT * FROM [{sheetName}]"
                                    Dim sheetData As New DataTable()

                                    Using adapter As New OleDbDataAdapter(sql, excelConn)
                                        adapter.Fill(sheetData)
                                    End Using

                                    ' Process rows based on template type
                                    Dim tabRows As Long = 0
                                    Dim tabErrors As Long = 0

                                    Select Case templateType
                                        Case "REVENUE"
                                            Me.ProcessRevenueTab(si, dt, sheetData, cleanSheetName, budgetYear, _
                                                scenarioName, validateMembers, validEntities, validAccounts, tabRows, tabErrors)
                                        Case "OPEX"
                                            Me.ProcessOPEXTab(si, dt, sheetData, cleanSheetName, budgetYear, _
                                                scenarioName, validateMembers, validEntities, validAccounts, tabRows, tabErrors)
                                        Case "HEADCOUNT"
                                            Me.ProcessHeadcountTab(si, dt, sheetData, cleanSheetName, budgetYear, _
                                                scenarioName, validateMembers, validEntities, validAccounts, tabRows, tabErrors)
                                        Case "CAPEX"
                                            Me.ProcessCAPEXTab(si, dt, sheetData, cleanSheetName, budgetYear, _
                                                scenarioName, validateMembers, validEntities, validAccounts, tabRows, tabErrors)
                                        Case Else
                                            BRApi.ErrorLog.LogMessage(si, $"CN_Excel_BudgetTemplates: Unknown template type: {templateType}")
                                    End Select

                                    totalRowsLoaded += tabRows
                                    totalErrors += tabErrors

                                    BRApi.ErrorLog.LogMessage(si, _
                                        $"  Tab [{cleanSheetName}]: Loaded={tabRows}, Errors={tabErrors}")

                                Catch exTab As Exception
                                    BRApi.ErrorLog.LogMessage(si, _
                                        $"  Tab [{cleanSheetName}] ERROR: {exTab.Message}")
                                    totalErrors += 1
                                End Try
                            Next

                            excelConn.Close()
                        End Using

                        filesProcessed += 1

                    Catch exFile As Exception
                        BRApi.ErrorLog.LogMessage(si, _
                            $"CN_Excel_BudgetTemplates: FILE ERROR - {fileName}: {exFile.Message}")
                        filesRejected += 1
                    End Try
                Next

                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_Excel_BudgetTemplates: Summary - Files processed:{filesProcessed}, " & _
                    $"Files rejected:{filesRejected}, Rows loaded:{totalRowsLoaded}, Errors:{totalErrors}")

                Return totalRowsLoaded

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_Excel_BudgetTemplates.ProcessBudgetTemplates", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ValidateTemplateVersion: Reads the Config tab of the workbook to verify the template version.
        '----------------------------------------------------------------------------------------------------
        Private Function ValidateTemplateVersion(ByVal si As SessionInfo, ByVal conn As OleDbConnection, _
                                                  ByVal expectedVersion As String, ByVal fileName As String) As Boolean
            Try
                Dim sql As String = "SELECT * FROM [Config$]"
                Dim configData As New DataTable()
                Using adapter As New OleDbDataAdapter(sql, conn)
                    adapter.Fill(configData)
                End Using

                ' Look for a row with "Version" in the first column
                For Each row As DataRow In configData.Rows
                    If row(0).ToString().Trim().Equals("Version", StringComparison.OrdinalIgnoreCase) Then
                        Dim version As String = row(1).ToString().Trim()
                        Return version.Equals(expectedVersion, StringComparison.OrdinalIgnoreCase)
                    End If
                Next

                ' If no Config tab or no Version row, log a warning but allow processing
                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_Excel_BudgetTemplates: WARNING - No version info found in {fileName}. Proceeding.")
                Return True

            Catch
                ' Config tab may not exist; proceed with a warning
                Return True
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ProcessOPEXTab: Processes an OPEX (operating expense) budget tab.
        ' Expected structure: Column A = Account, Columns B-M = Jan through Dec amounts.
        '----------------------------------------------------------------------------------------------------
        Private Sub ProcessOPEXTab(ByVal si As SessionInfo, ByVal dt As DataTable, _
                                    ByVal sheetData As DataTable, ByVal entityName As String, _
                                    ByVal budgetYear As String, ByVal scenarioName As String, _
                                    ByVal validateMembers As Boolean, _
                                    ByVal validEntities As HashSet(Of String), _
                                    ByVal validAccounts As HashSet(Of String), _
                                    ByRef rowsLoaded As Long, ByRef errors As Long)
            ' Validate entity
            If validateMembers AndAlso validEntities IsNot Nothing AndAlso Not validEntities.Contains(entityName) Then
                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_Excel_BudgetTemplates: VALIDATION ERROR - Entity [{entityName}] not found in metadata.")
                errors += 1
                Return
            End If

            For Each row As DataRow In sheetData.Rows
                Try
                    Dim accountName As String = row(0).ToString().Trim()
                    If String.IsNullOrWhiteSpace(accountName) Then Continue For

                    ' Validate account
                    If validateMembers AndAlso validAccounts IsNot Nothing AndAlso Not validAccounts.Contains(accountName) Then
                        BRApi.ErrorLog.LogMessage(si, _
                            $"CN_Excel_BudgetTemplates: VALIDATION ERROR - Account [{accountName}] not found. Tab: {entityName}")
                        errors += 1
                        Continue For
                    End If

                    ' Process months (columns 1-12)
                    For month As Integer = 1 To 12
                        If month >= sheetData.Columns.Count Then Exit For

                        Dim cellValue As Object = row(month)
                        If cellValue Is DBNull.Value OrElse cellValue Is Nothing Then Continue For

                        Dim amount As Decimal = 0
                        If Not Decimal.TryParse(cellValue.ToString(), NumberStyles.Any, _
                            CultureInfo.InvariantCulture, amount) Then
                            Continue For
                        End If

                        ' Skip zero amounts
                        If amount = 0 Then Continue For

                        Dim newRow As DataRow = dt.NewRow()
                        newRow("Entity") = entityName
                        newRow("Account") = accountName
                        newRow("Time") = $"{budgetYear}M{month}"
                        newRow("Scenario") = scenarioName
                        newRow("Flow") = "F_None"
                        newRow("Origin") = "O_None"
                        newRow("IC") = "I_None"
                        newRow("UD1") = "UD1_None"
                        newRow("UD2") = "UD2_None"
                        newRow("UD3") = "UD3_None"
                        newRow("UD4") = "UD4_None"
                        newRow("Amount") = amount
                        newRow("Description") = $"OPEX Budget {budgetYear} M{month}"
                        dt.Rows.Add(newRow)

                        rowsLoaded += 1
                    Next

                Catch exRow As Exception
                    errors += 1
                    BRApi.ErrorLog.LogMessage(si, $"CN_Excel_BudgetTemplates: OPEX row error: {exRow.Message}")
                End Try
            Next
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ProcessRevenueTab: Revenue budget tab with product x month matrix.
        '----------------------------------------------------------------------------------------------------
        Private Sub ProcessRevenueTab(ByVal si As SessionInfo, ByVal dt As DataTable, _
                                       ByVal sheetData As DataTable, ByVal entityName As String, _
                                       ByVal budgetYear As String, ByVal scenarioName As String, _
                                       ByVal validateMembers As Boolean, _
                                       ByVal validEntities As HashSet(Of String), _
                                       ByVal validAccounts As HashSet(Of String), _
                                       ByRef rowsLoaded As Long, ByRef errors As Long)
            If validateMembers AndAlso validEntities IsNot Nothing AndAlso Not validEntities.Contains(entityName) Then
                errors += 1 : Return
            End If

            For Each row As DataRow In sheetData.Rows
                Try
                    Dim productName As String = row(0).ToString().Trim()
                    If String.IsNullOrWhiteSpace(productName) Then Continue For

                    For month As Integer = 1 To 12
                        If month >= sheetData.Columns.Count Then Exit For
                        Dim cellValue As Object = row(month)
                        If cellValue Is DBNull.Value OrElse cellValue Is Nothing Then Continue For
                        Dim amount As Decimal = 0
                        If Not Decimal.TryParse(cellValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, amount) Then Continue For
                        If amount = 0 Then Continue For

                        Dim newRow As DataRow = dt.NewRow()
                        newRow("Entity") = entityName
                        newRow("Account") = "A_Revenue_Product"
                        newRow("Time") = $"{budgetYear}M{month}"
                        newRow("Scenario") = scenarioName
                        newRow("Flow") = "F_None"
                        newRow("Origin") = "O_None"
                        newRow("IC") = "I_None"
                        newRow("UD1") = $"UD1_{productName}"
                        newRow("UD2") = "UD2_None"
                        newRow("UD3") = "UD3_None"
                        newRow("UD4") = "UD4_None"
                        newRow("Amount") = amount
                        newRow("Description") = $"Revenue Budget {budgetYear} M{month} Product:{productName}"
                        dt.Rows.Add(newRow)
                        rowsLoaded += 1
                    Next
                Catch exRow As Exception
                    errors += 1
                End Try
            Next
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ProcessHeadcountTab: Headcount budget tab with position x month FTE and cost.
        '----------------------------------------------------------------------------------------------------
        Private Sub ProcessHeadcountTab(ByVal si As SessionInfo, ByVal dt As DataTable, _
                                         ByVal sheetData As DataTable, ByVal entityName As String, _
                                         ByVal budgetYear As String, ByVal scenarioName As String, _
                                         ByVal validateMembers As Boolean, _
                                         ByVal validEntities As HashSet(Of String), _
                                         ByVal validAccounts As HashSet(Of String), _
                                         ByRef rowsLoaded As Long, ByRef errors As Long)
            If validateMembers AndAlso validEntities IsNot Nothing AndAlso Not validEntities.Contains(entityName) Then
                errors += 1 : Return
            End If

            ' Expected: Col0=Position, Col1=Account(FTE/Cost), Col2-13=Jan-Dec
            For Each row As DataRow In sheetData.Rows
                Try
                    Dim positionName As String = row(0).ToString().Trim()
                    Dim accountType As String = If(sheetData.Columns.Count > 1, row(1).ToString().Trim(), "FTE")
                    If String.IsNullOrWhiteSpace(positionName) Then Continue For

                    Dim osAccount As String = If(accountType.Contains("FTE"), "A_HC_FTE_Budget", "A_HC_Cost_Budget")
                    Dim startCol As Integer = 2

                    For month As Integer = 1 To 12
                        Dim colIdx As Integer = startCol + month - 1
                        If colIdx >= sheetData.Columns.Count Then Exit For
                        Dim cellValue As Object = row(colIdx)
                        If cellValue Is DBNull.Value OrElse cellValue Is Nothing Then Continue For
                        Dim amount As Decimal = 0
                        If Not Decimal.TryParse(cellValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, amount) Then Continue For
                        If amount = 0 Then Continue For

                        Dim newRow As DataRow = dt.NewRow()
                        newRow("Entity") = entityName
                        newRow("Account") = osAccount
                        newRow("Time") = $"{budgetYear}M{month}"
                        newRow("Scenario") = scenarioName
                        newRow("Flow") = "F_None"
                        newRow("Origin") = "O_None"
                        newRow("IC") = "I_None"
                        newRow("UD1") = $"UD1_{positionName.Replace(" ", "_")}"
                        newRow("UD2") = "UD2_None"
                        newRow("UD3") = "UD3_None"
                        newRow("UD4") = "UD4_None"
                        newRow("Amount") = amount
                        newRow("Description") = $"HC Budget {budgetYear} M{month} Pos:{positionName}"
                        dt.Rows.Add(newRow)
                        rowsLoaded += 1
                    Next
                Catch exRow As Exception
                    errors += 1
                End Try
            Next
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ProcessCAPEXTab: CAPEX budget tab with project x month spend.
        '----------------------------------------------------------------------------------------------------
        Private Sub ProcessCAPEXTab(ByVal si As SessionInfo, ByVal dt As DataTable, _
                                     ByVal sheetData As DataTable, ByVal entityName As String, _
                                     ByVal budgetYear As String, ByVal scenarioName As String, _
                                     ByVal validateMembers As Boolean, _
                                     ByVal validEntities As HashSet(Of String), _
                                     ByVal validAccounts As HashSet(Of String), _
                                     ByRef rowsLoaded As Long, ByRef errors As Long)
            If validateMembers AndAlso validEntities IsNot Nothing AndAlso Not validEntities.Contains(entityName) Then
                errors += 1 : Return
            End If

            ' Expected: Col0=Project ID, Col1=Asset Category, Col2-13=Jan-Dec spend
            For Each row As DataRow In sheetData.Rows
                Try
                    Dim projectId As String = row(0).ToString().Trim()
                    Dim assetCategory As String = If(sheetData.Columns.Count > 1, row(1).ToString().Trim(), "General")
                    If String.IsNullOrWhiteSpace(projectId) Then Continue For

                    Dim startCol As Integer = 2
                    For month As Integer = 1 To 12
                        Dim colIdx As Integer = startCol + month - 1
                        If colIdx >= sheetData.Columns.Count Then Exit For
                        Dim cellValue As Object = row(colIdx)
                        If cellValue Is DBNull.Value OrElse cellValue Is Nothing Then Continue For
                        Dim amount As Decimal = 0
                        If Not Decimal.TryParse(cellValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, amount) Then Continue For
                        If amount = 0 Then Continue For

                        Dim newRow As DataRow = dt.NewRow()
                        newRow("Entity") = entityName
                        newRow("Account") = $"A_CAPEX_{assetCategory.Replace(" ", "_")}"
                        newRow("Time") = $"{budgetYear}M{month}"
                        newRow("Scenario") = scenarioName
                        newRow("Flow") = "F_None"
                        newRow("Origin") = "O_None"
                        newRow("IC") = "I_None"
                        newRow("UD1") = $"UD1_Proj_{projectId}"
                        newRow("UD2") = "UD2_None"
                        newRow("UD3") = "UD3_None"
                        newRow("UD4") = "UD4_None"
                        newRow("Amount") = amount
                        newRow("Description") = $"CAPEX Budget {budgetYear} M{month} Proj:{projectId}"
                        dt.Rows.Add(newRow)
                        rowsLoaded += 1
                    Next
                Catch exRow As Exception
                    errors += 1
                End Try
            Next
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' LoadValidMembers: Loads the set of valid dimension member names for validation.
        '----------------------------------------------------------------------------------------------------
        Private Function LoadValidMembers(ByVal si As SessionInfo, ByVal dimensionName As String) As HashSet(Of String)
            Try
                Dim members As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

                ' In production, this would use BRApi.Finance.Members to retrieve all base members
                ' For now, return Nothing to skip validation when member list cannot be loaded
                Dim dimType As DimType = Nothing
                Select Case dimensionName.ToLower()
                    Case "entity" : dimType = DimType.Entity
                    Case "account" : dimType = DimType.Account
                    Case Else : Return Nothing
                End Select

                Dim memberList As List(Of MemberInfo) = BRApi.Finance.Members.GetMembersByFilter( _
                    si, BRApi.Finance.Dim.GetDimPk(si, dimType.Id), "Base")

                If memberList IsNot Nothing Then
                    For Each member As MemberInfo In memberList
                        members.Add(member.Member.Name)
                    Next
                End If

                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_Excel_BudgetTemplates: Loaded {members.Count} valid {dimensionName} members for validation.")
                Return members

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_Excel_BudgetTemplates: WARNING - Could not load {dimensionName} members for validation: {ex.Message}")
                Return Nothing
            End Try
        End Function

    End Class

End Namespace

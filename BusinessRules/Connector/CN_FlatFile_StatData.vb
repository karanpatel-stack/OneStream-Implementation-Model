'------------------------------------------------------------------------------------------------------------
' OneStream XF Connector Business Rule: CN_FlatFile_StatData
'------------------------------------------------------------------------------------------------------------
' Purpose:     Loads statistical data from CSV/flat files into OneStream staging. Reads CSV files
'              from a configured directory, parses with configurable delimiter, header row, and
'              encoding. Provides flexible column-to-dimension mapping, data type validation,
'              dimension member validation against metadata, duplicate detection, and a full
'              audit trail. Processed files are moved to an archive folder upon completion.
'
' Supported File Formats:
'   - CSV (comma, semicolon, tab, pipe delimited)
'   - Fixed-width (future enhancement)
'   - Configurable encoding (UTF-8, ASCII, UTF-16, etc.)
'
' Column Mapping:
'   Defined via the ColumnMapping parameter as a pipe-separated list:
'   "Entity=0|Account=1|Time=2|Amount=5|Description=6"
'   where the number is the zero-based column index in the CSV.
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
Imports System.Text
Imports Microsoft.VisualBasic
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.Connector.CN_FlatFile_StatData

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, _
                             ByVal args As ConnectorArgs) As Object
            Try
                Select Case args.ActionType
                    Case Is = ConnectorActionTypes.UI
                        Return Me.SetupUI(si, args)

                    Case Is = ConnectorActionTypes.Initialize
                        Me.ValidateConfiguration(si, args)

                    Case Is = ConnectorActionTypes.GetData
                        Dim rowsLoaded As Long = Me.ProcessFlatFiles(si, args)
                        BRApi.ErrorLog.LogMessage(si, $"CN_FlatFile_StatData: Processing complete. Total rows loaded: {rowsLoaded}")

                    Case Is = ConnectorActionTypes.Finalize
                        BRApi.ErrorLog.LogMessage(si, "CN_FlatFile_StatData: Finalize phase completed.")
                End Select

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_FlatFile_StatData.Main", ex.Message))
            End Try
        End Function

        Private Function SetupUI(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Object
            Try
                Dim paramList As New List(Of DashboardDataSetParam)

                paramList.Add(New DashboardDataSetParam("SourceFolder", _
                    "Source CSV Folder Path", _
                    "\\fileserver\onestream\stat_data\incoming\", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ArchiveFolder", _
                    "Archive Folder for Processed Files", _
                    "\\fileserver\onestream\stat_data\archive\", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("FilePattern", _
                    "File Pattern (e.g. *.csv, StatData_*.txt)", _
                    "*.csv", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("Delimiter", _
                    "Column Delimiter (comma, semicolon, tab, pipe)", _
                    "comma", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("HasHeaderRow", _
                    "First Row is Header (True/False)", _
                    "True", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("Encoding", _
                    "File Encoding (UTF-8, ASCII, UTF-16)", _
                    "UTF-8", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ColumnMapping", _
                    "Column Mapping (e.g. Entity=0|Account=1|Time=2|Amount=5|Description=6)", _
                    "Entity=0|Account=1|Time=2|UD1=3|UD2=4|Amount=5|Description=6", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("DefaultScenario", _
                    "Default Scenario (when not in file)", _
                    "Actual", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("ValidateMembers", _
                    "Validate Dimension Members (True/False)", _
                    "True", _
                    DashboardDataSetParamTypes.Text))

                paramList.Add(New DashboardDataSetParam("DuplicateHandling", _
                    "Duplicate Handling (REJECT, SUM, LAST)", _
                    "SUM", _
                    DashboardDataSetParamTypes.Text))

                Return paramList

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_FlatFile_StatData.SetupUI", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ValidateConfiguration: Validates source folder, archive folder, and column mapping.
        '----------------------------------------------------------------------------------------------------
        Private Sub ValidateConfiguration(ByVal si As SessionInfo, ByVal args As ConnectorArgs)
            Try
                Dim sourceFolder As String = args.GetParameterValue("SourceFolder")
                Dim archiveFolder As String = args.GetParameterValue("ArchiveFolder")
                Dim columnMapping As String = args.GetParameterValue("ColumnMapping")

                If Not Directory.Exists(sourceFolder) Then
                    Throw New XFException(si, "CN_FlatFile_StatData", $"Source folder does not exist: {sourceFolder}")
                End If

                ' Create archive folder if it does not exist
                If Not Directory.Exists(archiveFolder) Then
                    Directory.CreateDirectory(archiveFolder)
                    BRApi.ErrorLog.LogMessage(si, $"CN_FlatFile_StatData: Created archive folder: {archiveFolder}")
                End If

                ' Validate column mapping format
                If String.IsNullOrWhiteSpace(columnMapping) Then
                    Throw New XFException(si, "CN_FlatFile_StatData", "Column mapping parameter is required.")
                End If

                Dim mappings As Dictionary(Of String, Integer) = Me.ParseColumnMapping(columnMapping)
                If Not mappings.ContainsKey("Entity") OrElse Not mappings.ContainsKey("Account") _
                    OrElse Not mappings.ContainsKey("Amount") Then
                    Throw New XFException(si, "CN_FlatFile_StatData", _
                        "Column mapping must include at minimum: Entity, Account, Amount")
                End If

                BRApi.ErrorLog.LogMessage(si, "CN_FlatFile_StatData: Configuration validated successfully.")

            Catch ex As XFException
                Throw
            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_FlatFile_StatData.ValidateConfiguration", ex.Message))
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' ProcessFlatFiles: Iterates through all matching CSV files, parses, validates, deduplicates,
        ' and loads data to staging. Moves processed files to archive.
        '----------------------------------------------------------------------------------------------------
        Private Function ProcessFlatFiles(ByVal si As SessionInfo, ByVal args As ConnectorArgs) As Long
            Dim totalRowsLoaded As Long = 0
            Dim totalErrors As Long = 0
            Dim filesProcessed As Integer = 0

            Dim sourceFolder As String = args.GetParameterValue("SourceFolder")
            Dim archiveFolder As String = args.GetParameterValue("ArchiveFolder")
            Dim filePattern As String = If(args.GetParameterValue("FilePattern"), "*.csv")
            Dim delimiterName As String = If(args.GetParameterValue("Delimiter"), "comma").ToLower()
            Dim hasHeaderRow As Boolean = Boolean.Parse(If(args.GetParameterValue("HasHeaderRow"), "True"))
            Dim encodingName As String = If(args.GetParameterValue("Encoding"), "UTF-8")
            Dim columnMappingStr As String = args.GetParameterValue("ColumnMapping")
            Dim defaultScenario As String = If(args.GetParameterValue("DefaultScenario"), "Actual")
            Dim validateMembers As Boolean = Boolean.Parse(If(args.GetParameterValue("ValidateMembers"), "True"))
            Dim duplicateHandling As String = If(args.GetParameterValue("DuplicateHandling"), "SUM").ToUpper()

            ' Parse configuration
            Dim delimiter As Char = Me.GetDelimiterChar(delimiterName)
            Dim encoding As Encoding = Me.GetEncoding(encodingName)
            Dim columnMapping As Dictionary(Of String, Integer) = Me.ParseColumnMapping(columnMappingStr)

            ' Load valid members for validation
            Dim validEntities As HashSet(Of String) = Nothing
            Dim validAccounts As HashSet(Of String) = Nothing
            If validateMembers Then
                validEntities = Me.LoadValidDimensionMembers(si, "Entity")
                validAccounts = Me.LoadValidDimensionMembers(si, "Account")
            End If

            ' Duplicate tracking
            Dim seenKeys As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

            Dim dt As DataTable = args.GetDataTable()

            Try
                Dim files As String() = Directory.GetFiles(sourceFolder, filePattern)
                BRApi.ErrorLog.LogMessage(si, $"CN_FlatFile_StatData: Found {files.Length} file(s) matching '{filePattern}'")

                For Each filePath As String In files
                    Dim fileName As String = Path.GetFileName(filePath)
                    BRApi.ErrorLog.LogMessage(si, $"CN_FlatFile_StatData: Processing file: {fileName}")

                    Dim fileRows As Long = 0
                    Dim fileErrors As Long = 0
                    Dim fileSkipped As Long = 0
                    Dim fileDuplicates As Long = 0
                    Dim lineNumber As Integer = 0

                    Try
                        Using reader As New StreamReader(filePath, encoding)
                            Dim line As String = reader.ReadLine()
                            lineNumber += 1

                            ' Skip header row if configured
                            If hasHeaderRow AndAlso line IsNot Nothing Then
                                BRApi.ErrorLog.LogMessage(si, $"  Header: {line}")
                                line = reader.ReadLine()
                                lineNumber += 1
                            End If

                            While line IsNot Nothing
                                Try
                                    ' Skip blank lines
                                    If String.IsNullOrWhiteSpace(line) Then
                                        line = reader.ReadLine()
                                        lineNumber += 1
                                        Continue While
                                    End If

                                    ' Parse the CSV line
                                    Dim fields As String() = Me.ParseCSVLine(line, delimiter)

                                    ' Extract mapped fields with defaults
                                    Dim entity As String = Me.GetMappedField(fields, columnMapping, "Entity", "")
                                    Dim account As String = Me.GetMappedField(fields, columnMapping, "Account", "")
                                    Dim timePeriod As String = Me.GetMappedField(fields, columnMapping, "Time", "")
                                    Dim scenario As String = Me.GetMappedField(fields, columnMapping, "Scenario", defaultScenario)
                                    Dim ud1 As String = Me.GetMappedField(fields, columnMapping, "UD1", "UD1_None")
                                    Dim ud2 As String = Me.GetMappedField(fields, columnMapping, "UD2", "UD2_None")
                                    Dim ud3 As String = Me.GetMappedField(fields, columnMapping, "UD3", "UD3_None")
                                    Dim ud4 As String = Me.GetMappedField(fields, columnMapping, "UD4", "UD4_None")
                                    Dim amountStr As String = Me.GetMappedField(fields, columnMapping, "Amount", "")
                                    Dim description As String = Me.GetMappedField(fields, columnMapping, "Description", "")
                                    Dim currency As String = Me.GetMappedField(fields, columnMapping, "Currency", "USD")

                                    ' Validate required fields
                                    If String.IsNullOrWhiteSpace(entity) OrElse String.IsNullOrWhiteSpace(account) Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"  Line {lineNumber}: REJECTED - Missing required Entity or Account field.")
                                        fileErrors += 1
                                        line = reader.ReadLine()
                                        lineNumber += 1
                                        Continue While
                                    End If

                                    ' Validate amount is numeric
                                    Dim amount As Decimal = 0
                                    If Not Decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, amount) Then
                                        BRApi.ErrorLog.LogMessage(si, _
                                            $"  Line {lineNumber}: REJECTED - Invalid amount value '{amountStr}'.")
                                        fileErrors += 1
                                        line = reader.ReadLine()
                                        lineNumber += 1
                                        Continue While
                                    End If

                                    ' Validate dimension members
                                    If validateMembers Then
                                        If validEntities IsNot Nothing AndAlso Not validEntities.Contains(entity) Then
                                            BRApi.ErrorLog.LogMessage(si, _
                                                $"  Line {lineNumber}: REJECTED - Invalid entity '{entity}'.")
                                            fileErrors += 1
                                            line = reader.ReadLine()
                                            lineNumber += 1
                                            Continue While
                                        End If
                                        If validAccounts IsNot Nothing AndAlso Not validAccounts.Contains(account) Then
                                            BRApi.ErrorLog.LogMessage(si, _
                                                $"  Line {lineNumber}: REJECTED - Invalid account '{account}'.")
                                            fileErrors += 1
                                            line = reader.ReadLine()
                                            lineNumber += 1
                                            Continue While
                                        End If
                                    End If

                                    ' Handle missing time period (default to current period)
                                    If String.IsNullOrWhiteSpace(timePeriod) Then
                                        timePeriod = $"{DateTime.Now.Year}M{DateTime.Now.Month}"
                                    End If

                                    ' Duplicate detection
                                    Dim duplicateKey As String = $"{entity}|{account}|{timePeriod}|{scenario}|{ud1}|{ud2}"
                                    If seenKeys.ContainsKey(duplicateKey) Then
                                        fileDuplicates += 1
                                        Select Case duplicateHandling
                                            Case "REJECT"
                                                BRApi.ErrorLog.LogMessage(si, _
                                                    $"  Line {lineNumber}: DUPLICATE REJECTED - Key: {duplicateKey}")
                                                fileSkipped += 1
                                                line = reader.ReadLine()
                                                lineNumber += 1
                                                Continue While

                                            Case "SUM"
                                                ' Find and update the existing row by adding the amount
                                                Dim existingRowIdx As Integer = seenKeys(duplicateKey)
                                                Dim existingAmount As Decimal = Convert.ToDecimal(dt.Rows(existingRowIdx)("Amount"))
                                                dt.Rows(existingRowIdx)("Amount") = existingAmount + amount
                                                dt.Rows(existingRowIdx)("Description") = $"(Summed) {description}"
                                                line = reader.ReadLine()
                                                lineNumber += 1
                                                Continue While

                                            Case "LAST"
                                                ' Replace the existing row's amount
                                                Dim existingIdx As Integer = seenKeys(duplicateKey)
                                                dt.Rows(existingIdx)("Amount") = amount
                                                dt.Rows(existingIdx)("Description") = $"(Replaced) {description}"
                                                line = reader.ReadLine()
                                                lineNumber += 1
                                                Continue While
                                        End Select
                                    End If

                                    ' Add to staging
                                    Dim newRow As DataRow = dt.NewRow()
                                    newRow("Entity") = entity
                                    newRow("Account") = account
                                    newRow("Time") = timePeriod
                                    newRow("Scenario") = scenario
                                    newRow("Flow") = "F_None"
                                    newRow("Origin") = "O_None"
                                    newRow("IC") = "I_None"
                                    newRow("UD1") = ud1
                                    newRow("UD2") = ud2
                                    newRow("UD3") = ud3
                                    newRow("UD4") = ud4
                                    newRow("Amount") = amount
                                    newRow("Currency") = currency
                                    newRow("Description") = $"File:{fileName} Line:{lineNumber} {description}"
                                    dt.Rows.Add(newRow)

                                    ' Track for duplicate detection (store the row index)
                                    seenKeys(duplicateKey) = dt.Rows.Count - 1

                                    fileRows += 1

                                Catch exLine As Exception
                                    fileErrors += 1
                                    BRApi.ErrorLog.LogMessage(si, _
                                        $"  Line {lineNumber}: ERROR - {exLine.Message}")
                                End Try

                                line = reader.ReadLine()
                                lineNumber += 1
                            End While
                        End Using

                        BRApi.ErrorLog.LogMessage(si, _
                            $"CN_FlatFile_StatData: File [{fileName}] - Loaded:{fileRows}, " & _
                            $"Errors:{fileErrors}, Skipped:{fileSkipped}, Duplicates:{fileDuplicates}")

                        ' Move processed file to archive
                        Me.ArchiveFile(si, filePath, archiveFolder, fileName)

                        totalRowsLoaded += fileRows
                        totalErrors += fileErrors
                        filesProcessed += 1

                    Catch exFile As Exception
                        BRApi.ErrorLog.LogMessage(si, _
                            $"CN_FlatFile_StatData: FILE ERROR - {fileName}: {exFile.Message}")
                        totalErrors += 1
                    End Try
                Next

                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_FlatFile_StatData: Grand total - Files:{filesProcessed}, Rows:{totalRowsLoaded}, Errors:{totalErrors}")

                Return totalRowsLoaded

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "CN_FlatFile_StatData.ProcessFlatFiles", ex.Message))
            End Try
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ParseColumnMapping: Parses the pipe-separated column mapping string into a dictionary.
        ' Format: "Entity=0|Account=1|Time=2|Amount=5"
        '----------------------------------------------------------------------------------------------------
        Private Function ParseColumnMapping(ByVal mappingStr As String) As Dictionary(Of String, Integer)
            Dim map As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            If String.IsNullOrWhiteSpace(mappingStr) Then Return map

            Dim pairs As String() = mappingStr.Split("|"c)
            For Each pair As String In pairs
                Dim parts As String() = pair.Split("="c)
                If parts.Length = 2 Then
                    Dim fieldName As String = parts(0).Trim()
                    Dim colIndex As Integer = 0
                    If Integer.TryParse(parts(1).Trim(), colIndex) Then
                        map(fieldName) = colIndex
                    End If
                End If
            Next
            Return map
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetMappedField: Retrieves the value of a mapped column from the parsed fields array.
        '----------------------------------------------------------------------------------------------------
        Private Function GetMappedField(ByVal fields As String(), ByVal mapping As Dictionary(Of String, Integer), _
                                         ByVal fieldName As String, ByVal defaultValue As String) As String
            Dim colIndex As Integer = -1
            If mapping.TryGetValue(fieldName, colIndex) Then
                If colIndex >= 0 AndAlso colIndex < fields.Length Then
                    Dim value As String = fields(colIndex).Trim()
                    If Not String.IsNullOrWhiteSpace(value) Then Return value
                End If
            End If
            Return defaultValue
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ParseCSVLine: Parses a single CSV line handling quoted fields that may contain the delimiter.
        '----------------------------------------------------------------------------------------------------
        Private Function ParseCSVLine(ByVal line As String, ByVal delimiter As Char) As String()
            Dim fields As New List(Of String)
            Dim current As New StringBuilder()
            Dim inQuotes As Boolean = False

            For i As Integer = 0 To line.Length - 1
                Dim c As Char = line(i)

                If c = """"c Then
                    If inQuotes AndAlso i + 1 < line.Length AndAlso line(i + 1) = """"c Then
                        ' Escaped quote inside quoted field
                        current.Append(""""c)
                        i += 1
                    Else
                        inQuotes = Not inQuotes
                    End If
                ElseIf c = delimiter AndAlso Not inQuotes Then
                    fields.Add(current.ToString())
                    current.Clear()
                Else
                    current.Append(c)
                End If
            Next

            fields.Add(current.ToString())
            Return fields.ToArray()
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetDelimiterChar: Converts delimiter name to its character representation.
        '----------------------------------------------------------------------------------------------------
        Private Function GetDelimiterChar(ByVal delimiterName As String) As Char
            Select Case delimiterName.ToLower()
                Case "comma" : Return ","c
                Case "semicolon" : Return ";"c
                Case "tab" : Return vbTab(0)
                Case "pipe" : Return "|"c
                Case Else : Return ","c
            End Select
        End Function

        '----------------------------------------------------------------------------------------------------
        ' GetEncoding: Resolves encoding name to System.Text.Encoding object.
        '----------------------------------------------------------------------------------------------------
        Private Function GetEncoding(ByVal encodingName As String) As Encoding
            Select Case encodingName.ToUpper()
                Case "UTF-8" : Return Encoding.UTF8
                Case "ASCII" : Return Encoding.ASCII
                Case "UTF-16" : Return Encoding.Unicode
                Case "UTF-32" : Return Encoding.UTF32
                Case Else : Return Encoding.UTF8
            End Select
        End Function

        '----------------------------------------------------------------------------------------------------
        ' ArchiveFile: Moves a processed file to the archive folder with a timestamp suffix.
        '----------------------------------------------------------------------------------------------------
        Private Sub ArchiveFile(ByVal si As SessionInfo, ByVal sourcePath As String, _
                                 ByVal archiveFolder As String, ByVal fileName As String)
            Try
                Dim timestamp As String = DateTime.Now.ToString("yyyyMMdd_HHmmss")
                Dim archiveName As String = Path.GetFileNameWithoutExtension(fileName) & _
                    $"_{timestamp}" & Path.GetExtension(fileName)
                Dim archivePath As String = Path.Combine(archiveFolder, archiveName)

                File.Move(sourcePath, archivePath)
                BRApi.ErrorLog.LogMessage(si, $"CN_FlatFile_StatData: Archived file to: {archivePath}")

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_FlatFile_StatData: WARNING - Could not archive file {fileName}: {ex.Message}")
            End Try
        End Sub

        '----------------------------------------------------------------------------------------------------
        ' LoadValidDimensionMembers: Loads valid member names for a given dimension.
        '----------------------------------------------------------------------------------------------------
        Private Function LoadValidDimensionMembers(ByVal si As SessionInfo, ByVal dimensionName As String) As HashSet(Of String)
            Try
                Dim members As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
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
                    $"CN_FlatFile_StatData: Loaded {members.Count} valid {dimensionName} members.")
                Return members

            Catch ex As Exception
                BRApi.ErrorLog.LogMessage(si, _
                    $"CN_FlatFile_StatData: WARNING - Could not load {dimensionName} members: {ex.Message}")
                Return Nothing
            End Try
        End Function

    End Class

End Namespace

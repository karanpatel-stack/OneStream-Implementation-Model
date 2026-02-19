'------------------------------------------------------------------------------------------------------------
' DSF_DynamicLabels
' Dashboard String Function Business Rule
'
' Purpose:  Generates context-aware labels for dashboard elements based on the current
'           Point of View (POV). Builds descriptive labels incorporating scenario, time
'           period, and entity context so dashboard titles and headers dynamically reflect
'           what the user is viewing.
'
' Examples:
'   "Budget vs Actual - January 2025 - Plant US01 Detroit"
'   "Forecast Q3 2025 - EMEA Region"
'   "Consolidated Actual - FY 2025"
'
' Multi-Language Support: English (default), German, French, Japanese
'
' Scope:    Dashboard - String Function
' Version:  1.0
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Globalization
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Finance.Engine
Imports OneStream.Finance.Database

Namespace OneStream.BusinessRule.DashboardStringFunction.DSF_DynamicLabels

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardStringFunctionArgs) As Object
            Try
                '--- Read parameters from the dashboard component ---
                Dim labelType As String = args.NameValuePairs.XFGetValue("LabelType", "Title")
                Dim scenarioName As String = args.NameValuePairs.XFGetValue("Scenario", "Actual")
                Dim compareScenario As String = args.NameValuePairs.XFGetValue("CompareScenario", String.Empty)
                Dim timeName As String = args.NameValuePairs.XFGetValue("Time", String.Empty)
                Dim entityName As String = args.NameValuePairs.XFGetValue("Entity", String.Empty)
                Dim language As String = args.NameValuePairs.XFGetValue("Language", "EN")

                '--- Fall back to session POV if parameters not supplied ---
                If String.IsNullOrEmpty(timeName) Then
                    timeName = BRApi.Finance.Members.GetMemberName(si, DimType.Time.Id, si.WorkflowClusterPk.TimeId)
                End If
                If String.IsNullOrEmpty(entityName) Then
                    entityName = BRApi.Finance.Members.GetMemberName(si, DimType.Entity.Id, si.WorkflowClusterPk.EntityId)
                End If

                '--- Build the label components ---
                Dim scenarioLabel As String = BuildScenarioLabel(scenarioName, compareScenario, language)
                Dim timeLabel As String = BuildTimeLabel(timeName, language)
                Dim entityLabel As String = BuildEntityLabel(si, entityName, language)

                '--- Assemble the final label based on label type ---
                Dim result As String = AssembleLabel(labelType, scenarioLabel, timeLabel, entityLabel, language)

                Return result

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "DSF_DynamicLabels", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Builds the scenario portion of the label, including comparison scenarios if applicable.
        ''' </summary>
        Private Function BuildScenarioLabel(ByVal scenarioName As String, ByVal compareScenario As String,
                                             ByVal language As String) As String
            Dim scenarioDisplay As String = TranslateScenario(scenarioName, language)

            If Not String.IsNullOrEmpty(compareScenario) Then
                Dim compareDisplay As String = TranslateScenario(compareScenario, language)
                Dim vsWord As String = GetVsWord(language)
                Return scenarioDisplay & " " & vsWord & " " & compareDisplay
            End If

            Return scenarioDisplay
        End Function

        ''' <summary>
        ''' Builds the time period label with appropriate granularity (month, quarter, year).
        ''' Parses OneStream time member names like "2025M1", "2025Q1", "2025" and converts
        ''' them to human-readable labels.
        ''' </summary>
        Private Function BuildTimeLabel(ByVal timeName As String, ByVal language As String) As String
            Try
                '--- Detect time granularity from the member name pattern ---
                If timeName.Contains("M") Then
                    '--- Monthly: e.g., "2025M1" -> "January 2025" ---
                    Dim parts() As String = timeName.Split("M"c)
                    Dim year As Integer = Integer.Parse(parts(0))
                    Dim month As Integer = Integer.Parse(parts(1))
                    Return GetMonthName(month, language) & " " & year.ToString()

                ElseIf timeName.Contains("Q") Then
                    '--- Quarterly: e.g., "2025Q1" -> "Q1 2025" ---
                    Dim parts() As String = timeName.Split("Q"c)
                    Dim year As Integer = Integer.Parse(parts(0))
                    Dim quarter As String = "Q" & parts(1)
                    Return quarter & " " & year.ToString()

                ElseIf timeName.Length = 4 Then
                    '--- Annual: e.g., "2025" -> "FY 2025" ---
                    Dim fyPrefix As String = GetFYPrefix(language)
                    Return fyPrefix & " " & timeName

                Else
                    '--- Unknown format: return as-is ---
                    Return timeName
                End If

            Catch ex As Exception
                Return timeName
            End Try
        End Function

        ''' <summary>
        ''' Builds the entity label, resolving the entity description from metadata.
        ''' </summary>
        Private Function BuildEntityLabel(ByVal si As SessionInfo, ByVal entityName As String,
                                           ByVal language As String) As String
            Try
                '--- Retrieve the entity description for a more readable label ---
                Dim entityDescription As String = BRApi.Finance.Members.GetMemberDescription(
                    si, DimType.Entity.Id, entityName)

                If Not String.IsNullOrEmpty(entityDescription) AndAlso entityDescription <> entityName Then
                    Return entityName & " " & entityDescription
                End If

                Return entityName

            Catch ex As Exception
                Return entityName
            End Try
        End Function

        ''' <summary>
        ''' Assembles the final label string from its components based on the requested label type.
        ''' </summary>
        Private Function AssembleLabel(ByVal labelType As String, ByVal scenarioLabel As String,
                                        ByVal timeLabel As String, ByVal entityLabel As String,
                                        ByVal language As String) As String
            Select Case labelType.ToUpper()
                Case "TITLE"
                    '--- Full title: "Budget vs Actual - January 2025 - Plant US01 Detroit" ---
                    Return scenarioLabel & " - " & timeLabel & " - " & entityLabel

                Case "SCENARIO"
                    Return scenarioLabel

                Case "TIME"
                    Return timeLabel

                Case "ENTITY"
                    Return entityLabel

                Case "SCENARIOTIME"
                    Return scenarioLabel & " - " & timeLabel

                Case "TIMEENTITY"
                    Return timeLabel & " - " & entityLabel

                Case Else
                    Return scenarioLabel & " - " & timeLabel & " - " & entityLabel
            End Select
        End Function

        ''' <summary>
        ''' Returns the localized month name for the given month number.
        ''' </summary>
        Private Function GetMonthName(ByVal month As Integer, ByVal language As String) As String
            Dim monthNames As Dictionary(Of String, String())
            monthNames = New Dictionary(Of String, String())(StringComparer.OrdinalIgnoreCase)

            monthNames("EN") = New String() {"January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"}
            monthNames("DE") = New String() {"Januar", "Februar", "Marz", "April", "Mai", "Juni",
                "Juli", "August", "September", "Oktober", "November", "Dezember"}
            monthNames("FR") = New String() {"Janvier", "Fevrier", "Mars", "Avril", "Mai", "Juin",
                "Juillet", "Aout", "Septembre", "Octobre", "Novembre", "Decembre"}
            monthNames("JA") = New String() {"1月", "2月", "3月", "4月", "5月", "6月",
                "7月", "8月", "9月", "10月", "11月", "12月"}

            Dim lang As String = If(monthNames.ContainsKey(language), language, "EN")
            If month >= 1 AndAlso month <= 12 Then
                Return monthNames(lang)(month - 1)
            End If
            Return month.ToString()
        End Function

        ''' <summary>
        ''' Returns the localized "vs" conjunction for scenario comparison labels.
        ''' </summary>
        Private Function GetVsWord(ByVal language As String) As String
            Select Case language.ToUpper()
                Case "DE" : Return "vs."
                Case "FR" : Return "vs"
                Case "JA" : Return "対"
                Case Else : Return "vs"
            End Select
        End Function

        ''' <summary>
        ''' Returns the localized fiscal year prefix.
        ''' </summary>
        Private Function GetFYPrefix(ByVal language As String) As String
            Select Case language.ToUpper()
                Case "DE" : Return "GJ"
                Case "FR" : Return "AF"
                Case "JA" : Return "会計年度"
                Case Else : Return "FY"
            End Select
        End Function

        ''' <summary>
        ''' Translates standard scenario names to the specified language.
        ''' </summary>
        Private Function TranslateScenario(ByVal scenarioName As String, ByVal language As String) As String
            Select Case language.ToUpper()
                Case "DE"
                    Select Case scenarioName.ToUpper()
                        Case "ACTUAL" : Return "Ist"
                        Case "BUDGET" : Return "Plan"
                        Case "FORECAST" : Return "Prognose"
                        Case Else : Return scenarioName
                    End Select
                Case "FR"
                    Select Case scenarioName.ToUpper()
                        Case "ACTUAL" : Return "Reel"
                        Case "BUDGET" : Return "Budget"
                        Case "FORECAST" : Return "Prevision"
                        Case Else : Return scenarioName
                    End Select
                Case "JA"
                    Select Case scenarioName.ToUpper()
                        Case "ACTUAL" : Return "実績"
                        Case "BUDGET" : Return "予算"
                        Case "FORECAST" : Return "見通し"
                        Case Else : Return scenarioName
                    End Select
                Case Else
                    Return scenarioName
            End Select
        End Function

    End Class

End Namespace

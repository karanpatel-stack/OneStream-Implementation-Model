'------------------------------------------------------------------------------------------------------------
' DSF_KPIThresholds
' Dashboard String Function Business Rule
'
' Purpose:  Generates traffic light indicator icons for Key Performance Indicators (KPIs).
'           Reads threshold definitions (green/yellow/red ranges) per KPI and compares the
'           actual value against those thresholds to return a colored circle indicator.
'
' Threshold Logic:
'   Green  = On target (within acceptable range)
'   Yellow = Warning (approaching critical threshold)
'   Red    = Critical (outside acceptable range, requires action)
'
' Supports both "higher-is-better" KPIs (e.g., revenue, margin) and
' "lower-is-better" KPIs (e.g., defect rate, cycle time, cost per unit).
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

Namespace OneStream.BusinessRule.DashboardStringFunction.DSF_KPIThresholds

    Public Class MainClass

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardStringFunctionArgs) As Object
            Try
                '--- Read KPI parameters from the dashboard component ---
                Dim kpiName As String = args.NameValuePairs.XFGetValue("KPIName", String.Empty)
                Dim actualValueStr As String = args.NameValuePairs.XFGetValue("ActualValue", "0")
                Dim displayMode As String = args.NameValuePairs.XFGetValue("DisplayMode", "IconAndText")

                '--- Parse the actual KPI value ---
                Dim actualValue As Double = 0
                Double.TryParse(actualValueStr, NumberStyles.Any, CultureInfo.InvariantCulture, actualValue)

                '--- Retrieve threshold definitions for this KPI ---
                Dim thresholds As KPIThresholdDef = GetKPIThresholds(si, kpiName)

                '--- Evaluate the actual value against thresholds ---
                Dim alertLevel As String = EvaluateThreshold(actualValue, thresholds)

                '--- Build the HTML indicator based on display mode ---
                Dim result As String = BuildIndicator(alertLevel, actualValue, kpiName, displayMode)

                Return result

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "DSF_KPIThresholds", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Retrieves threshold definitions for the specified KPI. In production, these would
        ''' typically be stored in a dashboard data management cube, application settings,
        ''' or a configuration table. Here we define standard thresholds per KPI name.
        ''' </summary>
        Private Function GetKPIThresholds(ByVal si As SessionInfo, ByVal kpiName As String) As KPIThresholdDef
            Dim thresholds As New KPIThresholdDef()

            '--- Default threshold definitions per KPI ---
            ' Convention: GreenMin/GreenMax define the "on target" band
            '             YellowMin/YellowMax define the "warning" band
            '             Anything outside yellow is "critical" (red)

            Select Case kpiName.ToUpper()
                Case "REVENUE_ATTAINMENT"
                    ' Higher is better: green >= 95%, yellow 85-95%, red < 85%
                    thresholds.HigherIsBetter = True
                    thresholds.GreenThreshold = 0.95
                    thresholds.YellowThreshold = 0.85

                Case "GROSS_MARGIN"
                    ' Higher is better: green >= 35%, yellow 28-35%, red < 28%
                    thresholds.HigherIsBetter = True
                    thresholds.GreenThreshold = 0.35
                    thresholds.YellowThreshold = 0.28

                Case "EBITDA_MARGIN"
                    ' Higher is better: green >= 20%, yellow 15-20%, red < 15%
                    thresholds.HigherIsBetter = True
                    thresholds.GreenThreshold = 0.20
                    thresholds.YellowThreshold = 0.15

                Case "DSO"
                    ' Lower is better (Days Sales Outstanding): green <= 30, yellow 30-45, red > 45
                    thresholds.HigherIsBetter = False
                    thresholds.GreenThreshold = 30.0
                    thresholds.YellowThreshold = 45.0

                Case "DEFECT_RATE"
                    ' Lower is better: green <= 2%, yellow 2-5%, red > 5%
                    thresholds.HigherIsBetter = False
                    thresholds.GreenThreshold = 0.02
                    thresholds.YellowThreshold = 0.05

                Case "EMPLOYEE_TURNOVER"
                    ' Lower is better: green <= 10%, yellow 10-18%, red > 18%
                    thresholds.HigherIsBetter = False
                    thresholds.GreenThreshold = 0.10
                    thresholds.YellowThreshold = 0.18

                Case "COST_PER_UNIT"
                    ' Lower is better: green <= target, yellow within 10%, red > 10% over
                    thresholds.HigherIsBetter = False
                    thresholds.GreenThreshold = 100.0
                    thresholds.YellowThreshold = 110.0

                Case "BUDGET_VARIANCE"
                    ' Closer to zero is better: green <= 5%, yellow 5-10%, red > 10%
                    thresholds.HigherIsBetter = True
                    thresholds.GreenThreshold = 0.95
                    thresholds.YellowThreshold = 0.90

                Case Else
                    ' Default: higher is better, generic thresholds
                    thresholds.HigherIsBetter = True
                    thresholds.GreenThreshold = 0.90
                    thresholds.YellowThreshold = 0.75
                    BRApi.ErrorLog.LogMessage(si, "DSF_KPIThresholds: No threshold definition found for KPI '" &
                        kpiName & "', using defaults.")
            End Select

            Return thresholds
        End Function

        ''' <summary>
        ''' Evaluates the actual value against the KPI thresholds and returns the alert level.
        ''' </summary>
        Private Function EvaluateThreshold(ByVal actualValue As Double, ByVal thresholds As KPIThresholdDef) As String
            If thresholds.HigherIsBetter Then
                '--- Higher is better: green if above green threshold, red if below yellow ---
                If actualValue >= thresholds.GreenThreshold Then
                    Return "Green"
                ElseIf actualValue >= thresholds.YellowThreshold Then
                    Return "Yellow"
                Else
                    Return "Red"
                End If
            Else
                '--- Lower is better: green if below green threshold, red if above yellow ---
                If actualValue <= thresholds.GreenThreshold Then
                    Return "Green"
                ElseIf actualValue <= thresholds.YellowThreshold Then
                    Return "Yellow"
                Else
                    Return "Red"
                End If
            End If
        End Function

        ''' <summary>
        ''' Builds the HTML indicator based on the alert level and display mode.
        ''' Supports three display modes: IconOnly, TextOnly, IconAndText.
        ''' </summary>
        Private Function BuildIndicator(ByVal alertLevel As String, ByVal actualValue As Double,
                                         ByVal kpiName As String, ByVal displayMode As String) As String
            Dim color As String
            Dim label As String
            Dim circleHtml As String

            Select Case alertLevel
                Case "Green"
                    color = "#28A745"
                    label = "On Target"
                Case "Yellow"
                    color = "#FFC107"
                    label = "Warning"
                Case "Red"
                    color = "#DC3545"
                    label = "Critical"
                Case Else
                    color = "#6C757D"
                    label = "Unknown"
            End Select

            '--- Build the colored circle indicator ---
            circleHtml = String.Format(
                "<span style=""display:inline-block; width:14px; height:14px; border-radius:50%; background-color:{0}; " &
                "vertical-align:middle; margin-right:6px;""></span>", color)

            '--- Return based on display mode ---
            Select Case displayMode.ToUpper()
                Case "ICONONLY"
                    Return circleHtml
                Case "TEXTONLY"
                    Return String.Format("<span style=""color:{0}; font-weight:bold;"">{1}</span>", color, label)
                Case Else
                    ' IconAndText (default)
                    Return String.Format("{0}<span style=""color:{1}; font-weight:bold;"">{2}</span>",
                        circleHtml, color, label)
            End Select
        End Function

        ''' <summary>
        ''' Data structure holding threshold definitions for a single KPI.
        ''' </summary>
        Private Class KPIThresholdDef
            Public Property HigherIsBetter As Boolean = True
            Public Property GreenThreshold As Double = 0.90
            Public Property YellowThreshold As Double = 0.75
        End Class

    End Class

End Namespace

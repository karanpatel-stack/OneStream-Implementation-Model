'------------------------------------------------------------------------------------------------------------
' DSF_VarianceFormatting
' Dashboard String Function Business Rule
'
' Purpose:  Formats variance values with conditional colors and directional arrows for
'           dashboard display. Applies intelligent formatting based on account type
'           (currency vs. percentage) and magnitude (K, M, B suffixes).
'
' Formatting Rules:
'   Positive favorable variance  -> Green text with up arrow
'   Negative unfavorable variance -> Red text with down arrow
'   Within tolerance threshold   -> Gray text (neutral)
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

Namespace OneStream.BusinessRule.DashboardStringFunction.DSF_VarianceFormatting

    Public Class MainClass

        '--- Tolerance threshold: variances within this range are considered neutral ---
        Private Const DEFAULT_TOLERANCE_PCT As Double = 0.01       ' 1% for percentage accounts
        Private Const DEFAULT_TOLERANCE_AMT As Double = 1000.0     ' $1,000 for currency accounts

        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As Object, ByVal args As DashboardStringFunctionArgs) As Object
            Try
                '--- Read parameters from the dashboard component ---
                Dim varianceValueStr As String = args.NameValuePairs.XFGetValue("VarianceValue", "0")
                Dim accountType As String = args.NameValuePairs.XFGetValue("AccountType", "Currency")
                Dim favorableDirection As String = args.NameValuePairs.XFGetValue("FavorableDirection", "Positive")
                Dim tolerancePctStr As String = args.NameValuePairs.XFGetValue("TolerancePct", DEFAULT_TOLERANCE_PCT.ToString())
                Dim toleranceAmtStr As String = args.NameValuePairs.XFGetValue("ToleranceAmt", DEFAULT_TOLERANCE_AMT.ToString())

                '--- Parse the variance value ---
                Dim varianceValue As Double = 0
                Double.TryParse(varianceValueStr, NumberStyles.Any, CultureInfo.InvariantCulture, varianceValue)

                Dim tolerancePct As Double = DEFAULT_TOLERANCE_PCT
                Double.TryParse(tolerancePctStr, NumberStyles.Any, CultureInfo.InvariantCulture, tolerancePct)

                Dim toleranceAmt As Double = DEFAULT_TOLERANCE_AMT
                Double.TryParse(toleranceAmtStr, NumberStyles.Any, CultureInfo.InvariantCulture, toleranceAmt)

                '--- Determine if the variance is favorable, unfavorable, or within tolerance ---
                Dim sentiment As String = DetermineVarianceSentiment(
                    varianceValue, accountType, favorableDirection, tolerancePct, toleranceAmt)

                '--- Format the value based on account type ---
                Dim formattedValue As String = FormatValue(varianceValue, accountType)

                '--- Build the HTML output with color and arrow ---
                Dim htmlOutput As String = BuildFormattedHtml(formattedValue, varianceValue, sentiment)

                Return htmlOutput

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, "DSF_VarianceFormatting", ex.Message))
            End Try
        End Function

        ''' <summary>
        ''' Determines whether a variance is favorable, unfavorable, or within tolerance.
        ''' For revenue-type accounts, positive variance is favorable.
        ''' For expense-type accounts, negative variance (below budget) is favorable.
        ''' </summary>
        Private Function DetermineVarianceSentiment(ByVal varianceValue As Double, ByVal accountType As String,
                                                     ByVal favorableDirection As String,
                                                     ByVal tolerancePct As Double, ByVal toleranceAmt As Double) As String
            '--- Check if the variance falls within the neutral tolerance band ---
            Dim isWithinTolerance As Boolean = False

            If accountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase) Then
                isWithinTolerance = (Math.Abs(varianceValue) <= tolerancePct)
            Else
                isWithinTolerance = (Math.Abs(varianceValue) <= toleranceAmt)
            End If

            If isWithinTolerance Then
                Return "Neutral"
            End If

            '--- Determine favorability based on direction convention ---
            Dim isFavorable As Boolean
            If favorableDirection.Equals("Positive", StringComparison.OrdinalIgnoreCase) Then
                ' Revenue-type: positive variance (actual > budget) is favorable
                isFavorable = (varianceValue > 0)
            Else
                ' Expense-type: negative variance (actual < budget) is favorable
                isFavorable = (varianceValue < 0)
            End If

            If isFavorable Then
                Return "Favorable"
            Else
                Return "Unfavorable"
            End If
        End Function

        ''' <summary>
        ''' Formats the numeric value with appropriate notation based on account type.
        ''' Currency values get magnitude suffixes (K, M, B) and dollar signs.
        ''' Percentage values get formatted with basis points or percentage notation.
        ''' </summary>
        Private Function FormatValue(ByVal value As Double, ByVal accountType As String) As String
            If accountType.Equals("Percentage", StringComparison.OrdinalIgnoreCase) Then
                '--- Format as percentage with 1 decimal place ---
                Return (value * 100).ToString("N1") & "%"
            Else
                '--- Format as currency with magnitude suffix ---
                Dim absValue As Double = Math.Abs(value)
                Dim sign As String = If(value < 0, "-", "")

                If absValue >= 1000000000 Then
                    Return sign & "$" & (absValue / 1000000000).ToString("N1") & "B"
                ElseIf absValue >= 1000000 Then
                    Return sign & "$" & (absValue / 1000000).ToString("N1") & "M"
                ElseIf absValue >= 1000 Then
                    Return sign & "$" & (absValue / 1000).ToString("N1") & "K"
                Else
                    Return sign & "$" & absValue.ToString("N0")
                End If
            End If
        End Function

        ''' <summary>
        ''' Builds the final HTML string with colored text and directional arrow indicator.
        ''' </summary>
        Private Function BuildFormattedHtml(ByVal formattedValue As String, ByVal rawValue As Double,
                                             ByVal sentiment As String) As String
            Dim color As String
            Dim arrow As String

            Select Case sentiment
                Case "Favorable"
                    color = "#28A745"     ' Green
                    arrow = " &#9650;"    ' Up triangle
                Case "Unfavorable"
                    color = "#DC3545"     ' Red
                    arrow = " &#9660;"    ' Down triangle
                Case Else
                    color = "#6C757D"     ' Gray
                    arrow = " &#9644;"    ' Horizontal bar (neutral)
            End Select

            '--- Ensure the arrow direction matches the value sign, not just favorability ---
            If rawValue < 0 AndAlso sentiment = "Favorable" Then
                arrow = " &#9660;"   ' Down arrow even though favorable (expense reduction)
            ElseIf rawValue > 0 AndAlso sentiment = "Unfavorable" Then
                arrow = " &#9650;"   ' Up arrow even though unfavorable (expense increase)
            End If

            Return String.Format(
                "<span style=""color:{0}; font-weight:bold;"">{1}{2}</span>",
                color, formattedValue, arrow)
        End Function

    End Class

End Namespace

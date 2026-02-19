'------------------------------------------------------------------------------------------------------------
' CR_HeadcountPlanning.vb
' OneStream XF Calculate Business Rule
'
' Purpose:  FTE-driven compensation and burden calculation. Reads headcount by entity, cost center,
'           and position type, then computes base salary, benefits, employer taxes, bonus accrual,
'           stock compensation, and total compensation. Handles partial-year hires, terminations,
'           and inter-entity transfers. Results are written to HR cube accounts.
'
' Frequency: Monthly
' Scope:     All entities with active headcount
'------------------------------------------------------------------------------------------------------------

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports OneStream.Shared.Common
Imports OneStream.Shared.Wcf
Imports OneStream.Shared.Engine
Imports OneStream.Shared.Database
Imports OneStream.Stage.Engine
Imports OneStream.Stage.Database

Namespace OneStream.BusinessRule.Finance.CR_HeadcountPlanning

    Public Class MainClass

        '----------------------------------------------------------------------------------------------------
        ' Main entry point for the Calculate business rule
        '----------------------------------------------------------------------------------------------------
        Public Function Main(ByVal si As SessionInfo, ByVal globals As BRGlobals, ByVal api As FinanceRulesApi, _
                             ByVal args As FinanceRulesArgs) As Object

            Try
                ' Only execute during Calculate phase
                If args.CalculationType = FinanceRulesCalculationType.Calculate Then

                    Dim entityName As String = api.Entity.GetName()
                    Dim scenarioName As String = api.Scenario.GetName()
                    Dim timeName As String = api.Time.GetName()

                    ' ---- Configuration: Grade-Level Base Salaries ----
                    ' These would typically be stored in a dashboard data source or cube;
                    ' hardcoded here for clarity.
                    Dim baseSalaryByGrade As New Dictionary(Of String, Double) From {
                        {"Grade_01", 45000.0},
                        {"Grade_02", 55000.0},
                        {"Grade_03", 68000.0},
                        {"Grade_04", 82000.0},
                        {"Grade_05", 100000.0},
                        {"Grade_06", 125000.0},
                        {"Grade_07", 155000.0},
                        {"Grade_08", 195000.0},
                        {"Grade_09", 240000.0},
                        {"Grade_10", 310000.0}
                    }

                    ' ---- Configuration: Benefits Rates ----
                    Const healthBenefitPct As Double = 0.08         ' 8% of base salary
                    Const dentalBenefitAnnual As Double = 1200.0    ' Flat annual per FTE
                    Const visionBenefitAnnual As Double = 480.0     ' Flat annual per FTE
                    Const lifeInsurancePct As Double = 0.005        ' 0.5% of base salary

                    ' ---- Configuration: Employer Tax Rates ----
                    Const socialSecurityRate As Double = 0.062      ' 6.2% up to wage base
                    Const socialSecurityWageBase As Double = 160200.0
                    Const medicareRate As Double = 0.0145           ' 1.45% no cap
                    Const futaRate As Double = 0.006                ' 0.6% on first $7,000
                    Const futaWageBase As Double = 7000.0
                    Const sutaRate As Double = 0.027                ' 2.7% state avg, on first $10,000
                    Const sutaWageBase As Double = 10000.0

                    ' ---- Configuration: Bonus and Stock Comp ----
                    Dim bonusTargetByGrade As New Dictionary(Of String, Double) From {
                        {"Grade_01", 0.05}, {"Grade_02", 0.05}, {"Grade_03", 0.08},
                        {"Grade_04", 0.10}, {"Grade_05", 0.15}, {"Grade_06", 0.20},
                        {"Grade_07", 0.25}, {"Grade_08", 0.30}, {"Grade_09", 0.35},
                        {"Grade_10", 0.40}
                    }
                    Const bonusAchievementFactor As Double = 1.0    ' 100% target achievement default
                    Const stockVestingYears As Integer = 4          ' 4-year vesting schedule

                    ' ---- Position Types to Process ----
                    Dim positionTypes As New List(Of String) From {
                        "FullTime_Salaried", "FullTime_Hourly", "PartTime", "Contractor", "Executive"
                    }

                    ' ---- Grade Levels ----
                    Dim gradeLevels As New List(Of String) From {
                        "Grade_01", "Grade_02", "Grade_03", "Grade_04", "Grade_05",
                        "Grade_06", "Grade_07", "Grade_08", "Grade_09", "Grade_10"
                    }

                    ' ---- Process each position type and grade ----
                    For Each posType As String In positionTypes
                        For Each grade As String In gradeLevels

                            ' Read headcount (FTE) for this entity / cost center / position / grade
                            Dim ftePov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_Headcount_FTE:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)

                            Dim fteCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, ftePov, True)
                            Dim fteCount As Double = fteCell.CellAmount

                            ' Skip if no headcount in this bucket
                            If fteCount <= 0 Then Continue For

                            ' ---- Read Start Date Proration Factor ----
                            ' Stored as decimal 0..1 representing fraction of year active
                            Dim prorationPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_ProrationFactor:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            Dim prorationCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, prorationPov, True)
                            Dim prorationFactor As Double = prorationCell.CellAmount
                            If prorationFactor <= 0 OrElse prorationFactor > 1 Then
                                prorationFactor = 1.0  ' Default to full year if not set
                            End If

                            ' ---- Read Termination Adjustment ----
                            ' Fraction of FTEs terminated mid-period (reduces effective headcount)
                            Dim termPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_TerminationAdj:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            Dim termCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, termPov, True)
                            Dim terminationAdj As Double = termCell.CellAmount

                            ' ---- Read Transfer-In / Transfer-Out Counts ----
                            Dim xferInPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_TransferIn:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            Dim xferOutPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_TransferOut:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            Dim xferInCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, xferInPov, True)
                            Dim xferOutCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, xferOutPov, True)

                            ' Effective FTE count after adjustments
                            Dim effectiveFTE As Double = (fteCount + xferInCell.CellAmount _
                                - xferOutCell.CellAmount - terminationAdj) * prorationFactor

                            If effectiveFTE <= 0 Then Continue For

                            ' ---- Base Salary Calculation ----
                            Dim annualSalaryPerFTE As Double = 0
                            If baseSalaryByGrade.ContainsKey(grade) Then
                                annualSalaryPerFTE = baseSalaryByGrade(grade)
                            End If
                            ' Monthly base salary for this bucket
                            Dim monthlyBaseSalary As Double = (annualSalaryPerFTE / 12.0) * effectiveFTE

                            ' ---- Benefits Calculation ----
                            Dim monthlyHealthBenefit As Double = (annualSalaryPerFTE * healthBenefitPct / 12.0) * effectiveFTE
                            Dim monthlyDentalBenefit As Double = (dentalBenefitAnnual / 12.0) * effectiveFTE
                            Dim monthlyVisionBenefit As Double = (visionBenefitAnnual / 12.0) * effectiveFTE
                            Dim monthlyLifeInsurance As Double = (annualSalaryPerFTE * lifeInsurancePct / 12.0) * effectiveFTE
                            Dim monthlyTotalBenefits As Double = monthlyHealthBenefit + monthlyDentalBenefit _
                                + monthlyVisionBenefit + monthlyLifeInsurance

                            ' ---- Employer Tax Calculation ----
                            ' Social Security: 6.2% on salary up to wage base
                            Dim ssBase As Double = Math.Min(annualSalaryPerFTE, socialSecurityWageBase)
                            Dim monthlySocialSecurity As Double = (ssBase * socialSecurityRate / 12.0) * effectiveFTE

                            ' Medicare: 1.45% on all wages
                            Dim monthlyMedicare As Double = (annualSalaryPerFTE * medicareRate / 12.0) * effectiveFTE

                            ' FUTA: 0.6% on first $7,000
                            Dim futaBase As Double = Math.Min(annualSalaryPerFTE, futaWageBase)
                            Dim monthlyFUTA As Double = (futaBase * futaRate / 12.0) * effectiveFTE

                            ' SUTA: 2.7% on first $10,000
                            Dim sutaBase As Double = Math.Min(annualSalaryPerFTE, sutaWageBase)
                            Dim monthlySUTA As Double = (sutaBase * sutaRate / 12.0) * effectiveFTE

                            Dim monthlyTotalTaxes As Double = monthlySocialSecurity + monthlyMedicare _
                                + monthlyFUTA + monthlySUTA

                            ' ---- Burden Rate ----
                            Dim burdenRate As Double = 0
                            If monthlyBaseSalary > 0 Then
                                burdenRate = (monthlyTotalBenefits + monthlyTotalTaxes) / monthlyBaseSalary
                            End If

                            ' ---- Bonus Accrual ----
                            Dim bonusTargetPct As Double = 0
                            If bonusTargetByGrade.ContainsKey(grade) Then
                                bonusTargetPct = bonusTargetByGrade(grade)
                            End If
                            Dim monthlyBonusAccrual As Double = (annualSalaryPerFTE * bonusTargetPct _
                                * bonusAchievementFactor / 12.0) * effectiveFTE

                            ' ---- Stock Compensation ----
                            ' Read grant value from cube, amortize over vesting period
                            Dim stockGrantPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_StockGrantValue:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            Dim stockCell As DataCell = BRApi.Finance.Data.GetDataCell( _
                                si, stockGrantPov, True)
                            Dim monthlyStockComp As Double = 0
                            If stockCell.CellAmount > 0 AndAlso stockVestingYears > 0 Then
                                monthlyStockComp = (stockCell.CellAmount / (stockVestingYears * 12.0))
                            End If

                            ' ---- Total Compensation ----
                            Dim monthlyTotalComp As Double = monthlyBaseSalary + monthlyTotalBenefits _
                                + monthlyTotalTaxes + monthlyBonusAccrual + monthlyStockComp

                            ' ---- Write Results to HR Cube Accounts ----
                            Dim basePov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_BaseSalary:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            api.Data.SetDataCell(si, basePov, monthlyBaseSalary, True)

                            Dim benefitsPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_TotalBenefits:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            api.Data.SetDataCell(si, benefitsPov, monthlyTotalBenefits, True)

                            Dim taxesPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_EmployerTaxes:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            api.Data.SetDataCell(si, taxesPov, monthlyTotalTaxes, True)

                            Dim burdenPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_BurdenRate:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            api.Data.SetDataCell(si, burdenPov, burdenRate, True)

                            Dim bonusPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_BonusAccrual:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            api.Data.SetDataCell(si, bonusPov, monthlyBonusAccrual, True)

                            Dim stockPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_StockComp:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            api.Data.SetDataCell(si, stockPov, monthlyStockComp, True)

                            Dim totalCompPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_TotalCompensation:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            api.Data.SetDataCell(si, totalCompPov, monthlyTotalComp, True)

                            Dim effectiveFTEPov As String = String.Format( _
                                "E#{0}:S#{1}:T#{2}:A#HR_EffectiveFTE:U1#{3}:U2#{4}", _
                                entityName, scenarioName, timeName, posType, grade)
                            api.Data.SetDataCell(si, effectiveFTEPov, effectiveFTE, True)

                        Next ' grade
                    Next ' posType

                    ' ---- Trigger downstream calculations ----
                    api.Data.Calculate("A#HR_TotalCompensation")

                End If ' CalculationType check

                Return Nothing

            Catch ex As Exception
                Throw ErrorHandler.LogWrite(si, New XFException(si, ex))
            End Try

        End Function

    End Class

End Namespace

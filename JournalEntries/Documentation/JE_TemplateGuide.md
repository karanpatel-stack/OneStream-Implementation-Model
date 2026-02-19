# Journal Entry Template Guide

## Overview

This guide covers the use of pre-built journal entry templates for monthly, quarterly, and annual close activities. Templates standardize the JE creation process, reduce errors, and ensure consistent GL account usage across all plants.

All templates integrate with the `FR_JournalEntries.vb` business rule, which handles validation, posting, auto-reversal, and audit trail.

## Template Inventory

### Monthly Accruals (ADJUST Type)

| Template | File | Debit | Credit | Reversing |
|----------|------|-------|--------|-----------|
| Payroll Accrual | `JET_Accrued_Payroll.csv` | SGA_Salaries | ACCR_Payroll | Yes |
| Utility Accrual | `JET_Accrued_Utilities.csv` | MOH_Utilities | ACCR_Utilities | Yes |
| Interest Accrual | `JET_Accrued_Interest.csv` | INT_LongTermDebt | ACCR_Interest | Yes |
| Warranty Reserve | `JET_Warranty_Reserve.csv` | MOH_Other | ACCR_Warranty | No |
| Bonus Accrual | `JET_Bonus_Accrual.csv` | SGA_Benefits | ACCR_Other | No |

### Manufacturing Adjustments (ADJUST Type)

| Template | File | Debit | Credit | Reversing |
|----------|------|-------|--------|-----------|
| Inventory Reserve | `JET_Inventory_Reserve.csv` | UsageVariance | INV_Reserve | No |
| Standard Cost Reval | `JET_Standard_Cost_Reval.csv` | PriceVariance | INV_RawMaterials / INV_FinishedGoods | No |
| WIP Adjustment | `JET_WIP_Adjustment.csv` | EfficiencyVariance | INV_WIP | No |
| Overhead Absorption | `JET_Overhead_Absorption.csv` | SpendingVariance | VolumeVariance | No |

### Reclassifications (RECLASS Type, Auto-Reversing)

| Template | File | Debit | Credit | Reversing |
|----------|------|-------|--------|-----------|
| AR/AP Netting | `JET_Reclass_AR_Netting.csv` | AP_Trade | AR_Trade | Yes |
| Debt Reclassification | `JET_Reclass_Debt.csv` | LTD_TermLoan | CurrentPortionLTD | Yes |
| Prepaid Amortization | `JET_Reclass_Prepaid.csv` | MOH_Insurance / SGA_Rent | Prepaid_Insurance / Prepaid_Rent | Yes |

### Intercompany Eliminations (ELIM Type)

| Template | File | Debit | Credit | Reversing |
|----------|------|-------|--------|-----------|
| IC Revenue/COGS | `JET_IC_Revenue_COGS.csv` | REV_IC_TransferSales | RawMaterials | No |
| IC Dividend | `JET_IC_Dividend.csv` | OIE_InvestmentIncome | RE_CurrentYear | No |
| IC Loan Interest | `JET_IC_Loan_Interest.csv` | OIE_InvestmentIncome / AP_Intercompany | INT_LongTermDebt / AR_Intercompany | No |

## JE ID Naming Convention

```
JE_{Type}_{Entity}_{Period}_{Sequence}
```

| Component | Format | Example |
|-----------|--------|---------|
| Type | ADJUST, RECLASS, ELIM, CORRECT | ADJUST |
| Entity | Entity dimension member code | Plant_US01_Detroit |
| Period | YYYYMnn | 2026M01 |
| Sequence | 3-digit zero-padded number | 001 |

**Full example:** `JE_ADJUST_Plant_US01_Detroit_2026M01_001`

For multi-line JEs (e.g., standard cost reval with raw materials and finished goods lines), append a letter suffix: `_007a`, `_007b`.

## How to Use Templates

### Step 1: Select the Template
Choose the appropriate CSV template from `JournalEntries/Templates/`. Each template contains pre-filled rows for all 13 plant entities.

### Step 2: Replace the Period Placeholder
Replace `{Period}` in the JE_ID column with the actual period code (e.g., `2026M01` for January 2026).

### Step 3: Enter Amounts
Replace `0` in the Amount column with the actual dollar amount for each entity. Remove rows for entities that do not require this JE in the current period.

### Step 4: Review and Validate
Before submitting:
- Verify each JE balances (debit amount = credit amount per JE_ID)
- Confirm GL account codes are correct for the specific transaction
- Check that the entity code matches the posting entity
- Add supporting notes in the Notes column

### Step 5: Submit for Approval
Submit the completed CSV through the OneStream workflow. The approval matrix routes to the appropriate approvers based on JE type and dollar amount.

### Step 6: Processing
`FR_JournalEntries.vb` processes submitted JEs:
1. Reads and groups JE lines by JE_ID
2. Validates that each JE balances (tolerance: $0.01)
3. Routes to consolidation member: RECLASS/ADJUST/CORRECT to `C_Local`, ELIM to `C_Elimination`
4. Writes to the Finance cube via Flow = `F_ManualJE`
5. For reversing entries, creates automatic reversal in the next period with ID suffix `_REV`
6. Logs full audit trail

## Approval Workflow

Approval routing is defined in `Config/JE_ApprovalMatrix.xml`:

| Amount Range (USD) | Required Approvals |
|--------------------|-------------------|
| < $50,000 | Plant Controller |
| $50,000 - $249,999 | Plant Controller + Accounting Manager |
| $250,000 - $999,999 | + Regional Controller |
| >= $1,000,000 | + CFO |

### Special Rules
- **CORRECT type:** Requires supporting documentation attachment
- **Cross-region eliminations (Elim_CrossRegion):** Requires sign-off from both sending and receiving regional controllers
- **Year-end (Period 12):** All JEs above $100K require Regional Controller approval

### Escalation
If an approver does not respond within 48 hours, the system auto-escalates to the next approver in sequence. Maximum 2 escalations before routing to CFO.

## Reversing Entry Logic

Templates flagged with `Reversing=Y` are automatically reversed by `FR_JournalEntries.vb`:

- The reversal posts on the first day of the **next period**
- All debit/credit signs are flipped (positive becomes negative)
- Reversal JE_ID format: `{Original_JE_ID}_REV`
- RECLASS type entries are always treated as reversing per the business rule

**Which templates reverse:**
- All monthly accruals (Payroll, Utilities, Interest) — reverse when actuals post
- All reclassifications (AR/AP Netting, Debt, Prepaid) — reverse and re-post each period
- Warranty, Bonus, Inventory, Manufacturing adjustments — do NOT reverse (cumulative)
- Eliminations — do NOT reverse (re-calculated each period)

## Common Scenarios

### Scenario 1: Standard Monthly Close
Use these templates in order during the monthly close cycle:

1. **Day 2-3 (Local Adjustments):** Post all ADJUST templates — Payroll, Utilities, Interest, Warranty, Bonus accruals
2. **Day 2-3 (Manufacturing):** Post manufacturing adjustments — Inventory Reserve, Std Cost Reval, WIP, Overhead Absorption
3. **Day 3-4 (Reclassifications):** Post RECLASS templates — AR/AP Netting, Debt, Prepaid Amortization
4. **Day 5-6 (Eliminations):** Post ELIM templates — IC Revenue/COGS, Dividends, Loan Interest

### Scenario 2: Quarterly Close (Additional Steps)
In addition to monthly close templates:
- Post IC Dividend elimination (`JET_IC_Dividend.csv`) — quarterly frequency
- Review and adjust Bonus Accrual amounts based on quarterly performance targets
- Perform quarterly inventory obsolescence deep-dive and adjust reserve

### Scenario 3: Year-End Close (Additional Steps)
- Enhanced approval thresholds apply (all JEs > $100K need Regional Controller)
- Full inventory reserve review and adjustment
- Bonus accrual true-up to actual payout amounts
- Final IC elimination sweep for all regions

## File Structure

```
JournalEntries/
├── Templates/                    15 CSV template files
│   ├── JET_Accrued_Payroll.csv
│   ├── JET_Accrued_Utilities.csv
│   ├── JET_Accrued_Interest.csv
│   ├── JET_Warranty_Reserve.csv
│   ├── JET_Bonus_Accrual.csv
│   ├── JET_Inventory_Reserve.csv
│   ├── JET_Standard_Cost_Reval.csv
│   ├── JET_WIP_Adjustment.csv
│   ├── JET_Overhead_Absorption.csv
│   ├── JET_Reclass_AR_Netting.csv
│   ├── JET_Reclass_Debt.csv
│   ├── JET_Reclass_Prepaid.csv
│   ├── JET_IC_Revenue_COGS.csv
│   ├── JET_IC_Dividend.csv
│   └── JET_IC_Loan_Interest.csv
├── Config/                       Configuration files
│   ├── JE_TemplateConfig.xml     Template metadata and account mappings
│   └── JE_ApprovalMatrix.xml     Approval routing thresholds
├── SampleData/                   Example populated JE batches
│   └── SampleJE_MonthlyClose.csv Complete January 2026 close for Detroit plant
└── Documentation/                User documentation
    └── JE_TemplateGuide.md       This guide
```

## Account Code Reference

All account codes used in templates are validated against the dimension hierarchy:

- **P&L accounts:** Defined in `Dimensions/Account/Account_PL.csv`
- **Balance Sheet accounts:** Defined in `Dimensions/Account/Account_BS.csv`
- **Entity codes:** Defined in `Dimensions/Entity/Entity_Hierarchy.csv`

### Key Account Mappings

| Category | Debit Account | Credit Account |
|----------|--------------|----------------|
| Payroll | SGA_Salaries (SGA Salaries and Wages) | ACCR_Payroll (Accrued Payroll) |
| Utilities | MOH_Utilities (Factory Utilities) | ACCR_Utilities (Accrued Utilities) |
| Interest | INT_LongTermDebt (Interest on Long-Term Debt) | ACCR_Interest (Accrued Interest) |
| Warranty | MOH_Other (Other Manufacturing Overhead) | ACCR_Warranty (Warranty Reserve) |
| Bonus | SGA_Benefits (SGA Employee Benefits) | ACCR_Other (Other Accrued Liabilities) |
| Inventory | UsageVariance (Material Usage Variance) | INV_Reserve (Inventory Obsolescence Reserve) |
| Std Cost | PriceVariance (Material Price Variance) | INV_RawMaterials / INV_FinishedGoods |
| WIP | EfficiencyVariance (Labor Efficiency Variance) | INV_WIP (Work in Process Inventory) |
| Overhead | SpendingVariance (Overhead Spending Variance) | VolumeVariance (Production Volume Variance) |

## Troubleshooting

| Issue | Cause | Resolution |
|-------|-------|------------|
| JE rejected as unbalanced | Debit and credit amounts differ | Verify Amount column is identical for paired debit/credit lines |
| Invalid account code error | Typo in Account_Debit or Account_Credit | Cross-reference against Account_PL.csv and Account_BS.csv |
| Invalid entity code error | Entity not in hierarchy | Verify against Entity_Hierarchy.csv; only plant-level entities for ADJUST/RECLASS |
| Reversal not created | Reversing flag set to N | Set Reversing column to Y; RECLASS entries auto-reverse regardless |
| Approval routing failure | Amount exceeds configured thresholds | Check JE_ApprovalMatrix.xml for current threshold configuration |
| Duplicate JE_ID error | Same JE_ID used in multiple rows | Ensure unique JE_ID per entry; use letter suffixes (a, b, c) for multi-line JEs |

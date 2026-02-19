# User Guide: Monthly Close Process

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Audience:** Finance Managers, Plant Controllers, Accounting Staff
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Overview](#1-overview)
2. [Day-by-Day Timeline](#2-day-by-day-timeline)
3. [Step 1: Data Load and Validation](#3-step-1-data-load-and-validation)
4. [Step 2: Local Adjustments](#4-step-2-local-adjustments)
5. [Step 3: Intercompany Reconciliation](#5-step-3-intercompany-reconciliation)
6. [Step 4: Entity Submission](#6-step-4-entity-submission)
7. [Step 5: Consolidation](#7-step-5-consolidation)
8. [Step 6: Review and Approval](#8-step-6-review-and-approval)
9. [Step 7: Reporting and Distribution](#9-step-7-reporting-and-distribution)
10. [Role Responsibilities](#10-role-responsibilities)
11. [Common Issues and Troubleshooting](#11-common-issues-and-troubleshooting)

---

## 1. Overview

### 1.1 Purpose

This guide describes the monthly financial close process in OneStream XF. The close process transforms raw financial data from source systems into fully consolidated, multi-currency financial statements through a structured seven-step workflow.

### 1.2 The Seven-Step Close Process

| Step | Name | Duration | Key Activities |
|------|------|----------|---------------|
| 1 | Data Load and Validation | BD1 (auto) | Automated data extraction, transformation, loading, and validation |
| 2 | Local Adjustments | BD1-BD2 | Manual journal entries, reclassifications, accruals |
| 3 | IC Reconciliation | BD2 | Intercompany balance matching and dispute resolution |
| 4 | Entity Submission | BD2-BD3 | Plant controllers certify entity data and submit for review |
| 5 | Consolidation | BD3 | Currency translation, IC elimination, ownership consolidation |
| 6 | Review and Approval | BD3-BD4 | Regional and corporate review, top-side adjustments |
| 7 | Reporting and Distribution | BD4-BD5 | Financial statement generation, board package, distribution |

**BD = Business Day after month-end**

### 1.3 Prerequisites

Before starting the close process, ensure:
- All source system periods are closed (SAP, Oracle, NetSuite)
- Exchange rates for the period are loaded
- Prior period is fully closed and locked
- All users have current access credentials

---

## 2. Day-by-Day Timeline

### Standard Monthly Close Calendar

```
Month-End
    |
    v
BD1 (Business Day 1)
    [02:00-06:00] Automated data loads from all source systems
    [06:00-08:00] Data validation and error review
    [08:00-17:00] Local adjustments entry (journal entries, accruals)
    [17:00]       IC reconciliation data available
    |
BD2
    [08:00-12:00] IC reconciliation and dispute resolution
    [12:00-17:00] Entity certification and submission
    [17:00]       Submission deadline for all entities
    |
BD3
    [08:00-10:00] Consolidation processing
    [10:00-12:00] Regional manager review
    [12:00-17:00] Corporate review and top-side adjustments
    |
BD4
    [08:00-12:00] Final approval and period lock
    [12:00-17:00] Report generation and distribution
    |
BD5
    [08:00-12:00] Board package review
    [12:00]       Close process complete
```

### Month-End Exceptions

| Month | Special Considerations | Extended Deadline |
|-------|----------------------|-------------------|
| March (Q1) | Quarterly reporting; additional IFRS disclosures | BD6 |
| June (Q2) | Half-year reporting; interim audit support | BD7 |
| September (Q3) | Quarterly reporting; forecast reconciliation | BD6 |
| December (FY) | Year-end audit; annual report support; tax provisions | BD10 |

---

## 3. Step 1: Data Load and Validation

### 3.1 Automated Data Load (No User Action Required)

The automated data load runs overnight on BD1:

| Time | Process | Source | Status Check |
|------|---------|--------|-------------|
| 02:00 | MES production data | 5 plant MES systems | Dashboard: DB_ADMIN_DataOps |
| 02:30 | SAP GL actuals | SAP HANA | Dashboard: DB_ADMIN_DataOps |
| 03:00 | Oracle GL actuals | Oracle EBS | Dashboard: DB_ADMIN_DataOps |
| 03:30 | NetSuite GL actuals | NetSuite Cloud | Dashboard: DB_ADMIN_DataOps |
| 04:00 | Transform and validate | All sources | Dashboard: DB_ADMIN_DataOps |
| 04:30 | Load to Finance cube | Staging | Dashboard: DB_ADMIN_DataOps |
| 05:00 | Post-load calculations | Finance cube | Dashboard: DB_ADMIN_DataOps |
| 05:30 | Dashboard refresh | Cache | Dashboard: DB_ADMIN_DataOps |

### 3.2 Checking Load Status

1. Log in to OneStream
2. Navigate to **Workspace: WS_Finance_Close**
3. Open **Dashboard: DB_ADMIN_DataOps** (or use the Close Status widget on your home dashboard)
4. Review the **Data Load Status** panel:
   - **Green checkmark:** Load completed successfully
   - **Amber warning:** Load completed with warnings (review details)
   - **Red X:** Load failed (contact data operations team)

### 3.3 Reviewing Validation Results

After data loads complete, review validation results:

1. Navigate to **Dashboard: DB_FIN_DataValidation**
2. Select your entity from the POV selector
3. Review the following validation checks:

| Validation | Expected Result | Action if Failed |
|-----------|----------------|-----------------|
| Trial Balance | Balanced (green) | Contact data operations -- do not proceed |
| Completeness | All expected accounts populated | Review missing accounts; manual entry if needed |
| Threshold Check | Variances within acceptable range | Investigate significant variances; document if valid |
| Prior Period Roll | Opening = Prior closing | Contact data operations if break found |

### 3.4 Handling Data Load Errors

If the data load fails or has errors:

1. **Do not attempt to manually fix cube data** -- wait for the data operations team
2. Check your email for automated error notifications
3. If no notification received, contact: **data-operations@company.com**
4. The data operations team will diagnose, fix, and re-run the load
5. You will receive a confirmation email when the re-load is complete

---

## 4. Step 2: Local Adjustments

### 4.1 When Are Adjustments Needed?

Common reasons for local adjustments:
- Accrual entries not yet in the ERP (e.g., bonus accruals, revenue accruals)
- Reclassification entries (correct account mispostings in the source system)
- Provisions (warranty, bad debt, restructuring)
- Prepaids and deferrals
- Intercompany allocations not processed in ERP

### 4.2 Entering a Journal Entry

1. Navigate to **Workspace: WS_Finance_Close**
2. Open **CubeView: CV_FIN_DataEntry_GLJournal**
3. Set your POV:
   - **Entity:** Select your entity (e.g., Plant_US01_Detroit)
   - **Scenario:** Actual
   - **Time:** Current period (e.g., Feb_2026)
   - **Flow:** F_ManualAdj (for adjustments) or F_Accrual (for accruals)
   - **Consolidation:** CON_Adjusted
4. Enter debit and credit amounts:
   - Debits: Enter as **positive** numbers
   - Credits: Enter as **negative** numbers
   - **The journal entry MUST balance (debits = credits)**
5. Add a journal description in the comments column
6. Click **Save**

### 4.3 Journal Entry Validation

After saving, the system automatically validates:
- **Balance Check:** Total debits must equal total credits
- **Account Validity:** Accounts must allow manual entry
- **Period Check:** Period must be open for adjustments
- **Amount Reasonableness:** Amounts exceeding $500,000 trigger a warning (proceed if valid)

If validation fails, you will see a red error message. Correct the entry and save again.

### 4.4 Reviewing Your Adjustments

To review all adjustments entered for your entity:

1. Open **CubeView: CV_FIN_Inquiry_JournalSummary**
2. Select your entity and period
3. The view shows all manual adjustments with:
   - Entry date and user
   - Account and amount
   - Journal description
   - Approval status

---

## 5. Step 3: Intercompany Reconciliation

### 5.1 Accessing the IC Reconciliation Dashboard

1. Navigate to **Dashboard: DB_FIN_ICReconciliation**
2. Select your entity from the POV selector
3. The dashboard displays:
   - **IC Balances Summary:** Your entity's IC receivables and payables by partner
   - **Match Status:** Matched (green), Within tolerance (amber), Unmatched (red)
   - **Variance Detail:** Differences between your balances and partner balances

### 5.2 Reviewing IC Balances

For each IC partner relationship:

| Column | Description |
|--------|-------------|
| IC Partner | The counterparty entity |
| Your IC Balance | Your recorded IC receivable/payable |
| Partner IC Balance | Their recorded IC payable/receivable |
| Variance | Difference between the two |
| Status | Matched, Within Tolerance, Unmatched |

### 5.3 Resolving IC Discrepancies

**If the variance is within tolerance ($1,000):**
- No action required -- the system will auto-eliminate

**If the variance exceeds tolerance:**
1. Click on the unmatched line to see transaction detail
2. Contact the IC partner to identify the discrepancy
3. Common causes:
   - Timing differences (one side booked, other not yet)
   - Different exchange rates applied
   - Missing transactions
   - Account mapping differences
4. One or both parties must enter a correcting journal entry
5. After corrections, refresh the IC dashboard to verify the match

### 5.4 IC Reconciliation Deadline

All IC balances must be matched or documented by **BD2 at 12:00 PM**. Unresolved IC discrepancies will:
- Be flagged in the consolidation exception report
- Require written explanation from both parties
- May delay the entity submission deadline

---

## 6. Step 4: Entity Submission

### 6.1 Pre-Submission Checklist

Before submitting your entity, verify:

- [ ] Data load completed successfully (Step 1)
- [ ] All journal entries entered and saved (Step 2)
- [ ] IC balances reconciled or documented (Step 3)
- [ ] Trial balance is balanced
- [ ] All required accounts have data
- [ ] Revenue and expense amounts are reasonable
- [ ] Balance sheet balances reconcile to subledger

### 6.2 Running the Submission Validation

1. Navigate to **Dashboard: DB_FIN_EntitySubmission**
2. Select your entity and period
3. Click **Run Validation**
4. Review validation results:

| Check | Result | Action |
|-------|--------|--------|
| Trial Balance | Must pass | Fix imbalances before submitting |
| IC Match | Must pass or documented | Document any unresolved IC items |
| Account Completeness | Must pass | Enter data for missing required accounts |
| Threshold Checks | Warnings OK | Document large variances in comments |

### 6.3 Submitting Your Entity

1. Once all validations pass (or warnings are documented):
2. Enter a submission comment (required):
   - Example: "Feb 2026 close complete. All validations passed. Revenue includes $200K accrual for Project Alpha."
3. Click **Submit for Review**
4. Your entity status will change to **Submitted**
5. You will receive an email confirmation

### 6.4 Submission Deadline

- **Standard deadline:** BD2 at 17:00 local time
- **Late submissions:** Contact your regional finance manager for approval
- **After deadline:** Entity will be flagged as late in the Close Status dashboard

---

## 7. Step 5: Consolidation

### 7.1 Consolidation Process (Finance Manager Role)

After all entities are submitted, the Finance Manager initiates consolidation:

1. Navigate to **Dashboard: DB_FIN_CloseStatus**
2. Verify all entities show **Submitted** status
3. Navigate to **Workspace: WS_Finance_Close**
4. Open the **Consolidation** task
5. Select consolidation scope:
   - **Full Consolidation:** All entities (standard month-end)
   - **Regional:** Single region only (for partial updates)
   - **Single Entity:** One entity reconsolidation (for corrections)
6. Click **Execute Consolidation**

### 7.2 Consolidation Processing Steps

The consolidation runs the following steps automatically:

| Step | Process | Duration | What Happens |
|------|---------|----------|-------------|
| 1 | Pre-calculation | ~2 min | Account derivations, local calculations |
| 2 | Currency Translation | ~5 min | Local currency to USD, EUR, GBP |
| 3 | IC Elimination | ~3 min | Match and eliminate IC balances |
| 4 | Ownership Consolidation | ~3 min | Apply ownership %, NCI calculation |
| 5 | Cash Flow Generation | ~2 min | Indirect cash flow statement |
| 6 | Validation | ~2 min | Post-consolidation integrity checks |

**Total estimated time: 15-20 minutes**

### 7.3 Reviewing Consolidation Results

After consolidation completes:
1. Review the **Consolidation Log** for any warnings or errors
2. Check **DB_FIN_ConsolidationSummary** dashboard:
   - Consolidated income statement
   - Consolidated balance sheet
   - IC elimination summary
   - CTA summary
3. Verify consolidation balances are reasonable

---

## 8. Step 6: Review and Approval

### 8.1 Regional Review (Regional Finance Manager)

1. Navigate to **Dashboard: DB_FIN_RegionalReview**
2. Select your region (NA, EU, AP, SA)
3. Review:
   - Regional consolidated P&L with variance commentary
   - Regional balance sheet
   - IC elimination summary for the region
   - Entity-level detail with drill-down capability
4. If adjustments are needed:
   - Enter top-side journal entries at the regional level
   - Re-run regional consolidation if material changes made
5. Click **Approve Region** when satisfied

### 8.2 Corporate Review (Corporate Controller / CFO)

1. Navigate to **Dashboard: DB_EXEC_Summary**
2. Review global consolidated financial statements
3. Review variance analysis (budget vs. actual, prior year)
4. Enter any corporate-level adjustments (tax provisions, M&A adjustments)
5. If reconsolidation needed, trigger from **WS_Finance_Close**
6. Click **Approve Consolidation** for final sign-off

### 8.3 Top-Side Adjustments

Corporate-level adjustments are entered at the Total_Company or regional level:

1. Open **CubeView: CV_FIN_DataEntry_TopSideAdj**
2. Set POV to the appropriate entity level (Total_Company or region)
3. Enter adjustments (debit/credit must balance)
4. Add detailed comments explaining the adjustment
5. Save and re-consolidate to reflect changes

---

## 9. Step 7: Reporting and Distribution

### 9.1 Standard Report Package

After final approval, the following reports are automatically generated:

| Report | Format | Distribution | Audience |
|--------|--------|-------------|----------|
| Consolidated Income Statement | PDF, Excel | Email + Portal | Executive team, Board |
| Consolidated Balance Sheet | PDF, Excel | Email + Portal | Executive team, Board |
| Consolidated Cash Flow Statement | PDF, Excel | Email + Portal | Executive team, Board |
| Regional P&L Summary | PDF | Email | Regional managers |
| Entity P&L Detail | PDF | Email | Plant controllers |
| Variance Commentary Report | Word | Email | CFO, VP Finance |
| IC Elimination Report | Excel | Portal | Consolidation team |
| Close Process Summary | PDF | Email | All finance users |

### 9.2 Accessing Reports

1. Navigate to **Workspace: WS_Finance_Close**
2. Open **Dashboard: DB_FIN_ReportLibrary**
3. Select the period and report type
4. Click **Generate** for on-demand generation, or **Download** for pre-generated reports

### 9.3 Period Lock

After reporting is complete and approved:

1. The Corporate Controller initiates **Period Lock** from the Close task manager
2. Period lock prevents any further data entry or adjustments
3. The locked period status is displayed on the Close Status dashboard
4. **Emergency unlock** requires System Admin approval and is fully audited

---

## 10. Role Responsibilities

| Role | Step 1 | Step 2 | Step 3 | Step 4 | Step 5 | Step 6 | Step 7 |
|------|--------|--------|--------|--------|--------|--------|--------|
| Data Operations | Execute, monitor, fix | -- | -- | -- | -- | -- | -- |
| Plant Controller | Review, validate | Enter adjustments | Reconcile IC | Submit entity | -- | -- | Review reports |
| Regional Finance Mgr | -- | Review adjustments | Review IC summary | -- | Monitor | Approve region | Distribute reports |
| Corporate Controller | -- | -- | -- | -- | Execute consolidation | Approve final | Lock period |
| CFO | -- | -- | -- | -- | -- | Final review | Board package |
| Internal Audit | -- | -- | -- | -- | -- | Review audit trail | Archive |

---

## 11. Common Issues and Troubleshooting

### 11.1 Data Load Issues

| Issue | Symptom | Resolution |
|-------|---------|------------|
| Load did not run | No data for current period | Check DB_ADMIN_DataOps; contact data-operations@company.com |
| Partial load | Some entities missing data | Check load log for failed entities; re-run for specific entities |
| Duplicate data | Amounts appear doubled | Do NOT enter corrections; contact data ops for clear and reload |
| Wrong period | Data loaded to wrong month | Contact data ops; they will clear incorrect period and reload |

### 11.2 Journal Entry Issues

| Issue | Symptom | Resolution |
|-------|---------|------------|
| Journal won't save | "Validation failed" error | Check that debits = credits; verify account allows manual entry |
| Journal not visible | Entry saved but not in reports | Verify POV matches (Flow = F_ManualAdj, Consolidation = CON_Adjusted) |
| Need to reverse a journal | Error in prior entry | Enter a reversing entry with opposite signs; add comment referencing original |
| Period is locked | Cannot enter adjustments | Contact Corporate Controller for emergency unlock (requires justification) |

### 11.3 IC Reconciliation Issues

| Issue | Symptom | Resolution |
|-------|---------|------------|
| Large IC variance | Red status on IC dashboard | Contact IC partner; compare transaction lists; identify missing items |
| Partner hasn't loaded data | Partner shows $0 | Contact partner's controller; they may need to submit adjustment |
| Currency mismatch | Variance due to FX | Verify both sides use same transaction date for rate determination |
| Disputed amount | Cannot agree with partner | Escalate to regional finance manager; document the dispute |

### 11.4 Consolidation Issues

| Issue | Symptom | Resolution |
|-------|---------|------------|
| Consolidation failed | Error in consolidation log | Review log details; fix underlying data issue; re-run |
| CTA imbalance | CTA does not balance | Check exchange rates; verify all BS accounts have correct rate types |
| IC not eliminated | IC balances remain after consolidation | Check IC matching status; ensure both sides submitted and match |
| Ownership incorrect | NCI calculation wrong | Verify ownership table percentages; contact system admin |

### 11.5 Reporting Issues

| Issue | Symptom | Resolution |
|-------|---------|------------|
| Report shows old data | Period data not current | Verify consolidation completed; refresh dashboard cache |
| Missing entities in report | Entity not in consolidated view | Check entity submitted and consolidation re-run after submission |
| Export fails | Excel/PDF generation error | Try refreshing the page; if persistent, contact application admin |
| Variance explanations wrong | Commentary does not match numbers | Verify the commentary period matches the data period |

### 11.6 Emergency Contacts

| Issue Type | Contact | Method | Response Time |
|-----------|---------|--------|--------------|
| Data load failures | Data Operations Team | data-operations@company.com | 30 minutes |
| Application errors | Application Support | app-support@company.com | 1 hour |
| Access issues | IT Help Desk | helpdesk@company.com | 2 hours |
| Process questions | Finance Manager (your region) | Direct contact | Same day |
| Emergency unlock | Corporate Controller | Direct contact + written request | 4 hours |

---

*End of Document*

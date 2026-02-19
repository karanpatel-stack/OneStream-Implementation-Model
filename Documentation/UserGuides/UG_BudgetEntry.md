# User Guide: Budget Data Entry

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Audience:** Plant Controllers, Budget Analysts, Department Managers, FP&A Team
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Budget Cycle Overview](#1-budget-cycle-overview)
2. [Accessing Budget Input Forms](#2-accessing-budget-input-forms)
3. [Revenue Planning Entry](#3-revenue-planning-entry)
4. [OPEX Planning Entry](#4-opex-planning-entry)
5. [Headcount Planning Entry](#5-headcount-planning-entry)
6. [CAPEX Planning Entry](#6-capex-planning-entry)
7. [Using Drivers and Templates](#7-using-drivers-and-templates)
8. [Submitting for Approval](#8-submitting-for-approval)
9. [Revision Process](#9-revision-process)

---

## 1. Budget Cycle Overview

### 1.1 Annual Budget Timeline

The annual budget cycle runs from September through December for the upcoming fiscal year:

| Phase | Dates | Activities | Owner |
|-------|-------|-----------|-------|
| **Kickoff** | Sep 1-15 | Corporate targets distributed; budget templates opened | CFO, FP&A |
| **Bottom-Up Build** | Sep 15 - Oct 31 | Plant-level revenue, OPEX, headcount, CAPEX entry | Plant Controllers, Dept Managers |
| **First Review** | Nov 1-15 | Regional review and feedback; V1 submission | Regional Finance Managers |
| **Revision** | Nov 15-30 | Adjustments based on feedback; V2 submission | Plant Controllers |
| **Final Approval** | Dec 1-15 | Corporate review and board approval | CFO, Board |
| **Lock** | Dec 15 | Approved budget locked; no further changes | FP&A Admin |

### 1.2 Budget Scenarios

| Scenario | Purpose | Who Edits | Status |
|----------|---------|-----------|--------|
| Budget_Working | Active input area for current work | Plant Controllers, Dept Managers | Open during budget cycle |
| Budget_V1 | First submission snapshot | System (auto-snapshot) | Locked after submission |
| Budget_V2 | Revised submission snapshot | System (auto-snapshot) | Locked after submission |
| Budget_Approved | Final approved version | System (promoted from V2) | Permanently locked |

### 1.3 Budget Components

```
Total Budget
|
+-- Revenue Budget
|   +-- Revenue by Product Line
|   +-- Revenue by Customer Segment
|   +-- Revenue by Entity (Plant/Sales Office)
|
+-- COGS Budget
|   +-- Material Costs (driven by production volume)
|   +-- Direct Labor (driven by headcount)
|   +-- Manufacturing Overhead (driven by production volume)
|
+-- OPEX Budget
|   +-- Salaries and Benefits (driven by headcount -- from HR Cube)
|   +-- Travel and Entertainment
|   +-- Professional Services
|   +-- Technology and Software
|   +-- Occupancy and Facilities
|   +-- Marketing
|   +-- Other Operating Expenses
|
+-- CAPEX Budget
|   +-- New Equipment
|   +-- Facility Expansion
|   +-- Technology Projects
|   +-- Maintenance Capital
|
+-- Headcount Budget (in HR Cube)
    +-- Current Headcount
    +-- Planned Hires
    +-- Planned Terminations/Transfers
```

---

## 2. Accessing Budget Input Forms

### 2.1 Logging In and Navigation

1. Log in to OneStream at your environment URL
2. Navigate to **Workspace: WS_Planning_Budget**
3. You will see the Budget Planning home dashboard with:
   - Your entity assignment and budget status
   - Links to each budget input form
   - Deadline reminders and notifications

### 2.2 Setting the Point of View (POV)

Every budget input form requires you to set the POV before entering data:

| POV Dimension | How to Set | Notes |
|--------------|-----------|-------|
| **Entity** | Select from dropdown (filtered to your assignments) | You only see entities you are authorized to edit |
| **Scenario** | Pre-set to `Budget_Working` | Cannot be changed during input |
| **Time** | Select the budget year (e.g., FY2027) | Monthly detail auto-populated |
| **Consolidation** | Pre-set to `CON_Input` | No user action needed |

### 2.3 Available Input Forms (CubeViews)

| Form Name | CubeView ID | Purpose | Who Uses |
|-----------|------------|---------|----------|
| Revenue Budget | CV_PLN_DataEntry_RevenueBudget | Revenue by product and customer | Plant Controllers |
| OPEX Budget | CV_PLN_DataEntry_OPEXBudget | Operating expenses by department | Department Managers |
| CAPEX Budget | CV_PLN_DataEntry_CAPEXBudget | Capital expenditure by project | Plant Controllers |
| Headcount Plan | CV_HR_DataEntry_Headcount | Headcount and compensation | HR Business Partners |
| Budget Summary | CV_PLN_Inquiry_BudgetSummary | Read-only summary view | All budget participants |

---

## 3. Revenue Planning Entry

### 3.1 Opening the Revenue Budget Form

1. From the Budget Planning home, click **Revenue Budget**
2. Set POV:
   - **Entity:** Your plant or sales entity
   - **Time:** FY2027 (monthly columns will display Jan-Dec)
3. The form displays:
   - **Rows:** Revenue accounts by product line (UD1)
   - **Columns:** Months (Jan_2027 through Dec_2027) plus annual total
   - **Seed values:** Prior year actuals (grayed out, read-only reference row)

### 3.2 Revenue Entry Methods

**Method 1: Direct Monthly Entry**
- Click on a cell and type the revenue amount for that product/month
- Press Tab to move to the next month
- The annual total updates automatically

**Method 2: Annual Amount with Spread**
1. Enter the annual total in the **FY2027** column
2. Right-click and select **Spread Options**
3. Choose a spread method:
   - **Even Spread:** Divide equally across 12 months
   - **Prior Year Pattern:** Apply prior year monthly percentages
   - **Seasonal Pattern:** Apply a predefined seasonal curve
   - **Custom %:** Enter custom monthly percentages
4. Click **Apply**

**Method 3: Driver-Based (Volume x Price)**
1. Switch to the **Driver Input** tab on the form
2. Enter for each product line:
   - **Unit Volume:** Expected units sold per month
   - **Average Selling Price:** Price per unit
3. Click **Calculate Revenue**
4. The system computes: Revenue = Volume x ASP
5. Switch back to the **Revenue** tab to verify calculated amounts

### 3.3 Revenue Account Guide

| Account | Description | Entry Guidance |
|---------|-------------|---------------|
| REV_Product_Sales | Physical product revenue | Use volume x price drivers where possible |
| REV_Service_Revenue | Installation, maintenance, training | Based on service contracts and project pipeline |
| REV_License_Revenue | Software and IP licensing | Based on license agreements |
| REV_Discounts | Volume discounts, promotional pricing | Enter as negative amounts |
| REV_Returns | Expected product returns | Enter as negative; typically 1-3% of gross revenue |
| REV_Allowances | Customer allowances and rebates | Enter as negative; per customer agreements |

### 3.4 Saving Revenue Data

1. After entering all revenue data, click **Save** (top-right)
2. The system validates your entries:
   - All amounts must be numeric
   - Revenue amounts should be positive (except deductions)
   - Annual total must be non-zero
3. If validation passes, a green "Saved successfully" message appears
4. Your data is saved to the Planning cube immediately

---

## 4. OPEX Planning Entry

### 4.1 Opening the OPEX Budget Form

1. From the Budget Planning home, click **OPEX Budget**
2. Set POV:
   - **Entity:** Your entity
   - **Department (UD3):** Select your department (or "All" if you manage multiple)
   - **Time:** FY2027
3. The form displays:
   - **Rows:** OPEX accounts (salaries, travel, professional services, etc.)
   - **Columns:** Months plus annual total
   - **Reference:** Prior year actuals and current year budget (read-only)

### 4.2 OPEX Entry Guidance

| Account Group | Entry Method | Notes |
|--------------|-------------|-------|
| OPEX_Salaries | **Auto-calculated** from HR Cube | Do NOT enter manually; driven by headcount plan |
| OPEX_Benefits | **Auto-calculated** from HR Cube | Do NOT enter manually; driven by headcount plan |
| OPEX_Travel | Direct monthly entry | Consider seasonal travel patterns |
| OPEX_Professional | Direct monthly or annual + spread | Based on known engagements and planned projects |
| OPEX_Occupancy | Typically even monthly spread | Based on lease agreements |
| OPEX_Technology | Direct monthly entry | Software licenses, hardware refresh, SaaS fees |
| OPEX_Marketing | Direct monthly entry | Aligned with marketing calendar |
| OPEX_Insurance | Annual amount with even spread | Based on policy renewals |
| OPEX_Depreciation | **Auto-calculated** | Driven by CAPEX budget; do not enter manually |
| OPEX_Other | Direct entry | Miscellaneous items |

### 4.3 Important: Salary and Benefits

Salary and benefits lines are **automatically populated** from the HR Cube headcount plan. If these amounts appear incorrect:

1. Do NOT overwrite them in the OPEX form
2. Contact your HR Business Partner to review the headcount plan
3. After headcount corrections, the salary lines will update automatically on the next calculation run (typically overnight)

### 4.4 Spreading Techniques

**Even Spread Example:**
- Annual office supplies budget: $24,000
- Even spread: $2,000 per month

**Seasonal Spread Example:**
- Annual marketing budget: $600,000
- Seasonal pattern: 5%, 5%, 8%, 8%, 10%, 10%, 12%, 12%, 10%, 8%, 7%, 5%
- Jan: $30K, Feb: $30K, Mar: $48K, ... Dec: $30K

**Prior Year Pattern Spread:**
- The system calculates each month's percentage of the prior year annual total
- Applies those percentages to your new annual amount
- Useful for accounts with consistent seasonal patterns

---

## 5. Headcount Planning Entry

### 5.1 Opening the Headcount Plan Form

1. From the Budget Planning home, click **Headcount Plan**
2. Set POV:
   - **Entity:** Your entity
   - **Department (UD3):** Your department
   - **Time:** FY2027
3. The form displays:
   - **Rows:** Headcount categories (active, new hires, terminations)
   - **Columns:** Months plus annual average
   - **Reference:** Current actual headcount

### 5.2 Headcount Entry

| Row | Description | How to Enter |
|-----|-------------|-------------|
| HR_HC_Active | Starting headcount | Pre-populated from current actuals; adjust if known changes |
| HR_HC_NewHires | Planned new hires | Enter the number of new hires per month |
| HR_HC_Terminations | Expected departures | Enter as negative numbers; based on historical turnover rate |
| HR_HC_Transfers | Transfers in/out | Positive for transfers in; negative for transfers out |
| HR_FTE_FullTime | Full-time equivalent | Auto-calculated from active + hires - terms +/- transfers |
| HR_FTE_PartTime | Part-time FTE | Enter separately if applicable |
| HR_FTE_Contractor | Contract workers | Enter separately; not included in benefits calculations |

### 5.3 Compensation Assumptions

After entering headcount, review and adjust compensation assumptions:

1. Click the **Compensation** tab on the headcount form
2. Review pre-populated rates:
   - **Average Base Salary:** Populated from Workday current average; adjust for merit increases
   - **Merit Increase %:** Corporate guideline (default 3%); adjustable per department
   - **Bonus Target %:** By level; populated from compensation policy
   - **Benefit Rate (per employee):** Calculated from current enrollment; adjustable
3. Modify any rates that differ from your department's plan
4. Click **Calculate Compensation** to refresh the cost calculations

### 5.4 Compensation Outputs (Auto-Calculated)

After calculation, review these output rows (read-only):
- HR_COMP_BaseSalary: Headcount x Average Salary
- HR_COMP_Bonus: Base Salary x Bonus Target %
- HR_BEN_Total: Headcount x Benefit Rate
- HR_TAX_Total: (Base + Bonus) x Payroll Tax Rate
- HR_TotalCost: Sum of all compensation components

These totals flow automatically to the OPEX budget as salary and benefits lines.

---

## 6. CAPEX Planning Entry

### 6.1 Opening the CAPEX Budget Form

1. From the Budget Planning home, click **CAPEX Budget**
2. Set POV:
   - **Entity:** Your entity
   - **Project (UD4):** Select a specific project or "All Projects"
   - **Time:** FY2027
3. The form displays:
   - **Rows:** CAPEX accounts (equipment, facility, technology, maintenance)
   - **Columns:** Months plus annual total
   - **Reference:** Current year CAPEX spend

### 6.2 Adding a New CAPEX Project

If you need to add a new project not yet in the system:

1. Complete a **CAPEX Project Request Form** (available on the FP&A SharePoint)
2. Submit to FP&A for project code assignment
3. The FP&A team will add the project to the UD4 dimension
4. Allow 1 business day for the new project to appear in your input form

### 6.3 CAPEX Entry by Project

For each project, enter:

| Field | Description | Example |
|-------|-------------|---------|
| Project Name | Reference (read-only from dimension) | PROJ_CAP_NE_001 (New CNC Machine) |
| Total Project Cost | Total estimated cost over project life | $2,500,000 |
| FY2027 Budget | Amount to be spent in FY2027 | $1,200,000 |
| Monthly Timing | When spending will occur | Enter by month based on project schedule |
| Asset Category | Equipment, Building, Technology, etc. | New Equipment |
| Useful Life (years) | For depreciation calculation | 10 years |
| Depreciation Method | Straight-line, Declining balance | Straight-line |

### 6.4 Depreciation Impact

CAPEX entries automatically trigger depreciation calculations:
- New assets placed in service generate monthly depreciation
- Depreciation flows to OPEX_Depreciation in the P&L
- Net book value flows to BS_PPE_Net on the balance sheet
- The depreciation calculation runs nightly; review the next day

---

## 7. Using Drivers and Templates

### 7.1 Revenue Drivers

| Driver | Formula | Where to Enter |
|--------|---------|---------------|
| Volume | Units to be sold | Revenue Budget > Driver Input tab |
| Price | Average selling price per unit | Revenue Budget > Driver Input tab |
| Growth Rate | % increase over prior year | Revenue Budget > Growth Rate cell |
| Mix % | Product mix percentages (must sum to 100%) | Revenue Budget > Mix tab |

### 7.2 COGS Drivers

COGS is primarily driven by production volume:

| Driver | Formula | Source |
|--------|---------|-------|
| Production Volume | Units to be manufactured | Entered by plant operations |
| Material Cost per Unit | Standard material cost | Maintained by cost accounting |
| Direct Labor Hours per Unit | Standard labor hours | Maintained by industrial engineering |
| Labor Rate | Hourly labor rate (including burden) | From HR Cube compensation plan |
| Overhead Rate | Overhead allocation rate per unit | Calculated from overhead budget / volume |

**Note:** COGS is auto-calculated from these drivers. If you need to override a COGS calculation, enter an adjustment in the COGS override row and add a comment explaining the reason.

### 7.3 Budget Templates

Pre-built templates are available for common budget patterns:

| Template | Description | How to Use |
|----------|-------------|-----------|
| Zero-Based | Start from zero; justify every dollar | Select from Budget menu > Apply Template |
| Prior Year + Growth | Prior year actuals x (1 + growth%) | Set growth % in template parameters |
| Run Rate | Annualize last 3 months of actuals | Select from Budget menu > Apply Template |
| Seasonal | Apply seasonal pattern to annual amount | Select pattern; enter annual total |
| Custom | Your own saved template from prior cycles | Select from "My Templates" in Budget menu |

### 7.4 Applying a Template

1. Open the relevant budget input form
2. Click **Budget Menu** (top toolbar)
3. Select **Apply Template**
4. Choose the template type
5. Configure parameters (growth rate, base period, etc.)
6. Click **Preview** to see the calculated values before applying
7. Click **Apply** to populate the form
8. Adjust individual months as needed
9. **Save**

---

## 8. Submitting for Approval

### 8.1 Pre-Submission Review

Before submitting your budget, review the Budget Summary view:

1. Open **CubeView: CV_PLN_Inquiry_BudgetSummary**
2. Verify:
   - [ ] Revenue totals are complete and reasonable
   - [ ] COGS margins align with targets (typically 30-40% gross margin)
   - [ ] OPEX includes all expected categories
   - [ ] Headcount ties to the HR plan
   - [ ] CAPEX projects are all entered
   - [ ] Monthly phasing looks reasonable (no unusual spikes)
   - [ ] Annual total aligns with corporate targets (within guidance range)

### 8.2 Running Budget Validation

1. Navigate to **Dashboard: DB_PLN_BudgetValidation**
2. Select your entity
3. Click **Run Validation**
4. Review results:

| Validation | Criteria | Action if Failed |
|-----------|---------|-----------------|
| Completeness | All required accounts have data | Enter missing data |
| Revenue Range | Within +/- 15% of corporate target | Justify or adjust |
| OPEX Range | Within +/- 10% of prior year (adjusted) | Justify or adjust |
| Headcount Tie | OPEX salary = HR Cube total | Contact HR BP to reconcile |
| CAPEX within Allocation | Total <= entity CAPEX allocation | Reduce or request additional allocation |
| Margin Check | Gross margin > 25% | Review COGS assumptions |
| Balance | P&L totals flow correctly | Contact FP&A if calculation error |

### 8.3 Submitting the Budget

1. Once all validations pass:
2. Navigate to **Dashboard: DB_PLN_BudgetSubmission**
3. Enter your submission comments:
   - Summary of key assumptions
   - Notable changes from prior year
   - Risks and opportunities
   - Example: "FY2027 budget reflects 8% revenue growth driven by new product launch in Q2. OPEX includes 5 new hires in engineering. CAPEX includes $2.5M for new CNC machine."
4. Click **Submit Budget (V1)**
5. Your budget is snapshot to `Budget_V1` and locked
6. Your entity status changes to **Submitted - V1**
7. You receive a confirmation email

### 8.4 What Happens After Submission

1. Your Regional Finance Manager receives a notification
2. They review your budget against regional targets
3. They may:
   - **Approve** -- no further action from you
   - **Send Back** -- you receive feedback and must revise (see Section 9)
   - **Adjust** -- they make minor adjustments at the regional level

---

## 9. Revision Process

### 9.1 Receiving Feedback

If your budget is sent back for revision:
1. You receive an email notification with feedback comments
2. Your entity status changes to **Revision Required**
3. The `Budget_Working` scenario reopens for editing

### 9.2 Making Revisions

1. Review the feedback comments on **DB_PLN_BudgetSubmission**
2. Open the relevant input forms (Revenue, OPEX, CAPEX, Headcount)
3. Make the requested changes
4. Add revision comments documenting what changed and why

### 9.3 Common Revision Requests

| Request | Typical Action |
|---------|---------------|
| "Reduce OPEX by 5%" | Review discretionary spending; reduce T&E, professional services |
| "Revenue target too aggressive" | Lower growth assumptions; adjust volume or price drivers |
| "Headcount above allocation" | Defer planned hires; prioritize critical roles |
| "CAPEX exceeds budget" | Defer lower-priority projects; phase spending over two years |
| "Need more detail on assumptions" | Add comments to submission; provide supporting analysis |

### 9.4 Resubmitting

1. After making revisions, re-run validation
2. Navigate to **DB_PLN_BudgetSubmission**
3. Enter revision comments explaining changes made
4. Click **Submit Budget (V2)**
5. Your budget is snapshot to `Budget_V2`
6. The review process repeats
7. After final approval at the corporate level, the budget is promoted to `Budget_Approved`

### 9.5 Budget Lock

Once the budget is approved:
- `Budget_Approved` scenario is permanently locked
- No further changes are possible without executive approval
- The approved budget becomes the comparison baseline for monthly actuals
- Any subsequent adjustments are tracked in the Forecast scenarios

---

*End of Document*

# User Guide: Reporting and Dashboard Navigation

## OneStream XF Platform -- Global Manufacturing Enterprise

**Document Version:** 1.0
**Last Updated:** 2026-02-18
**Classification:** Client Confidential
**Audience:** All OneStream Users (Executives, Finance Managers, Plant Controllers, Analysts)
**Prepared By:** OneStream Implementation Team

---

## Table of Contents

1. [Dashboard Overview](#1-dashboard-overview)
2. [Navigating the Executive Summary](#2-navigating-the-executive-summary)
3. [Using Filters and POV Selectors](#3-using-filters-and-pov-selectors)
4. [Drill-Down and Drill-Through](#4-drill-down-and-drill-through)
5. [Exporting to Excel and PDF](#5-exporting-to-excel-and-pdf)
6. [Scheduling Reports](#6-scheduling-reports)
7. [Custom Report Requests](#7-custom-report-requests)
8. [Common Report Descriptions](#8-common-report-descriptions)

---

## 1. Dashboard Overview

### 1.1 Dashboard Home

Upon logging in to OneStream, you are presented with your personalized dashboard home. The content you see depends on your assigned role:

| Role | Default Home Dashboard | Primary Dashboards |
|------|----------------------|-------------------|
| Executive | DB_EXEC_Summary | Executive Summary, Revenue Analysis, Forecast Accuracy |
| Finance Manager | DB_FIN_CloseStatus | Close Status, P&L Analysis, Budget vs. Actual |
| Plant Controller | DB_OPS_PlantPerformance | Plant Operations, Entity P&L, Budget Entry |
| Budget Analyst | DB_PLN_BudgetSummary | Budget Summary, Variance Analysis, Forecast Trend |
| HR Manager | DB_HR_WorkforceAnalytics | Workforce Analytics, Headcount Trend, Cost per FTE |
| Auditor | DB_AUD_AuditDashboard | Audit Trail, Reconciliation Status, Data Quality |

### 1.2 Dashboard Navigation Structure

```
Home Dashboard
|
+-- Financial Dashboards
|   +-- DB_EXEC_Summary         (Executive KPI overview)
|   +-- DB_FIN_PLAnalysis       (Income statement analysis)
|   +-- DB_FIN_BSAnalysis       (Balance sheet analysis)
|   +-- DB_FIN_CashFlowAnalysis (Cash flow statement)
|   +-- DB_FIN_BudgetVsActual   (Variance analysis)
|   +-- DB_FIN_ICReconciliation (Intercompany dashboard)
|
+-- Operational Dashboards
|   +-- DB_OPS_PlantPerformance (Plant-level KPIs)
|   +-- DB_OPS_RevenueByProduct (Product profitability)
|   +-- DB_OPS_RevenueByCustomer(Customer analysis)
|   +-- DB_OPS_OPEXByDepartment (Department spending)
|   +-- DB_OPS_CAPEXTracking    (Capital project tracking)
|
+-- Planning Dashboards
|   +-- DB_PLN_BudgetSummary    (Budget overview)
|   +-- DB_PLN_ForecastTrend    (Forecast accuracy)
|   +-- DB_PLN_RollingForecast  (18-month rolling view)
|
+-- Workflow Dashboards
|   +-- DB_FIN_CloseStatus      (Monthly close tracker)
|   +-- DB_RECON_Status         (Account reconciliation status)
|
+-- HR Dashboards
|   +-- DB_HR_WorkforceAnalytics(Headcount and cost metrics)
|
+-- Administration
    +-- DB_ADMIN_DataOps        (Data load monitoring)
```

### 1.3 Accessing Dashboards

**Method 1: Navigation Menu**
1. Click the **Dashboards** menu in the left navigation panel
2. Expand the category (Financial, Operational, Planning, etc.)
3. Click the desired dashboard name

**Method 2: Quick Links**
- Use the quick links on your home dashboard to jump to frequently used dashboards
- Click any KPI card on the Executive Summary to drill into the related detail dashboard

**Method 3: Search**
- Click the search icon in the top toolbar
- Type the dashboard name or keyword
- Select from the search results

---

## 2. Navigating the Executive Summary

### 2.1 Executive Summary Layout

The Executive Summary dashboard (DB_EXEC_Summary) provides a consolidated view of key financial and operational metrics:

```
+-----------------------------------------------------------------------+
|  POV: Entity [Total_Company v]  Scenario [Actual v]  Period [Feb_2026]|
+-----------------------------------------------------------------------+
|                                                                       |
|  +-------------------+  +-------------------+  +-------------------+  |
|  | REVENUE           |  | EBITDA            |  | NET INCOME        |  |
|  | $125.3M           |  | $28.7M            |  | $18.2M            |  |
|  | vs Budget: +2.1%  |  | vs Budget: -0.5%  |  | vs Budget: +1.3%  |  |
|  | vs PY: +7.8%      |  | vs PY: +5.2%      |  | vs PY: +6.1%      |  |
|  +-------------------+  +-------------------+  +-------------------+  |
|                                                                       |
|  +-------------------+  +-------------------+  +-------------------+  |
|  | GROSS MARGIN %    |  | OPERATING MARGIN %|  | FREE CASH FLOW   |  |
|  | 38.2%             |  | 22.9%             |  | $12.4M            |  |
|  | Target: 37.5%     |  | Target: 22.0%     |  | vs Budget: +8.3%  |  |
|  +-------------------+  +-------------------+  +-------------------+  |
|                                                                       |
|  +----------------------------------+  +---------------------------+  |
|  | REVENUE TREND (12-month line)    |  | P&L WATERFALL             |  |
|  | [Line chart: Actual vs Budget    |  | [Waterfall chart:         |  |
|  |  vs Prior Year by month]         |  |  Revenue -> GM -> EBITDA  |  |
|  |                                  |  |  -> NI]                   |  |
|  +----------------------------------+  +---------------------------+  |
|                                                                       |
|  +----------------------------------+  +---------------------------+  |
|  | REGIONAL PERFORMANCE             |  | CLOSE STATUS              |  |
|  | [Bar chart: Revenue by region    |  | [Status icons per entity  |  |
|  |  with budget comparison]         |  |  Green/Amber/Red]         |  |
|  +----------------------------------+  +---------------------------+  |
|                                                                       |
+-----------------------------------------------------------------------+
```

### 2.2 KPI Cards

Each KPI card at the top of the dashboard displays:
- **Primary Value:** Current period actual amount
- **Budget Variance:** Percentage difference from budget (green = favorable, red = unfavorable)
- **Prior Year Variance:** Percentage difference from same period last year
- **Trend Arrow:** Direction indicator (up/down)

**Clicking a KPI card** opens the related detail dashboard (e.g., clicking Revenue opens DB_FIN_PLAnalysis filtered to revenue accounts).

### 2.3 Charts

**Revenue Trend Chart:**
- Displays 12 months of rolling data
- Three lines: Actual (solid blue), Budget (dashed gray), Prior Year (dotted green)
- Hover over any data point to see the exact value
- Click a data point to drill into that month's detail

**P&L Waterfall:**
- Visual walk from Revenue to Net Income
- Green bars = positive contributions; red bars = negative (expenses)
- Click any bar to drill into the underlying accounts

**Regional Performance:**
- Stacked or grouped bar chart showing revenue by region
- Budget comparison overlay
- Click a region bar to filter the entire dashboard to that region

---

## 3. Using Filters and POV Selectors

### 3.1 POV (Point of View) Bar

The POV bar appears at the top of every dashboard and controls the data context:

| Selector | Options | Default |
|----------|---------|---------|
| **Entity** | Total_Company, Regions, Countries, Individual entities | Total_Company (or your assigned entity) |
| **Scenario** | Actual, Budget_Approved, FC_Working, RF_Current, PriorYear | Actual |
| **Time** | Any month, quarter, or year | Current period |

### 3.2 Changing the POV

1. Click the dropdown arrow next to the dimension name
2. A member selector opens:
   - **Tree View:** Browse the hierarchy by expanding/collapsing nodes
   - **Search:** Type a member name to filter the list
   - **Favorites:** Click the star icon next to frequently used members to save them
3. Select the desired member
4. Click **Apply** (or the dashboard auto-refreshes, depending on configuration)

### 3.3 Common POV Selections

| View | Entity | Scenario | Time | What You See |
|------|--------|----------|------|-------------|
| Global Actual | Total_Company | Actual | Current month | Consolidated actuals for current period |
| Regional Budget Comparison | NA_NorthAmerica | Actual | YTD | North America YTD actuals |
| Plant Detail | Plant_US01_Detroit | Actual | Current month | Detroit plant actuals |
| Budget vs Actual | Total_Company | Budget_Approved | Current month | Approved budget for comparison |
| Prior Year | Total_Company | PriorYear | Same period last year | Prior year actuals for trending |
| Forecast | Total_Company | FC_Working | Full year | Current year forecast |

### 3.4 Dashboard Filters

In addition to the POV, some dashboards have additional filters:

| Filter | Available On | Options |
|--------|-------------|---------|
| Account Group | P&L Analysis, OPEX Dashboard | Revenue, COGS, OPEX, Other Income |
| Product Line | Revenue Analysis | Industrial, Consumer, Components, Services, Spare Parts |
| Customer Segment | Customer Analysis | Industrial, Consumer, Government, OEM |
| Department | OPEX Dashboard | Manufacturing, Engineering, Sales, Finance, HR, IT, etc. |
| Project | CAPEX Tracking | Individual CAPEX projects |
| Status | Close Status, Recon Status | All, Complete, In Progress, Overdue |

---

## 4. Drill-Down and Drill-Through

### 4.1 Drill-Down (Within OneStream)

Drill-down allows you to navigate from summary to detail within the OneStream cubes.

**How to Drill Down:**
1. Right-click on any data value in a dashboard or CubeView
2. Select **Drill Down** from the context menu
3. Choose the dimension to drill into:
   - **Entity Drill:** Total_Company -> NA -> US -> Plant_US01_Detroit
   - **Account Drill:** OPEX_Total -> OPEX_SGA -> OPEX_Salaries
   - **Time Drill:** FY2026 -> Q1_2026 -> Jan_2026
   - **Product Drill:** Total_Products -> PRD_IndustrialEquipment -> PRD_IE_HeavyMachinery
4. The view refreshes to show the next level of detail
5. Click the **Back** button or breadcrumb trail to return to the summary

### 4.2 Drill-Through (To Source System)

Drill-through takes you from a summarized value in OneStream to the underlying transaction detail in the source system.

**How to Drill Through:**
1. Right-click on a data value
2. Select **Drill Through** from the context menu
3. A new window opens showing the source transaction detail:
   - For SAP data: Journal entry lines from ACDOCA
   - For Oracle data: GL journal detail from GL_JE_LINES
   - For NetSuite data: Transaction detail from the saved search
4. The drill-through view shows:
   - Source document number
   - Posting date
   - Line-item amounts
   - Reference text
   - Posting user

**Note:** Drill-through is available for base-level (leaf) entities and input-level accounts only. You cannot drill through from a consolidated or calculated value.

### 4.3 Drill-Down Navigation Breadcrumbs

As you drill down, a breadcrumb trail appears at the top of the view:

```
Total_Company > NA_NorthAmerica > US_UnitedStates > Plant_US01_Detroit
```

Click any level in the breadcrumb to jump back to that level.

---

## 5. Exporting to Excel and PDF

### 5.1 Exporting a Dashboard to PDF

1. Open the dashboard you want to export
2. Set the POV to the desired view
3. Click the **Export** icon in the top toolbar (printer icon)
4. Select **Export to PDF**
5. Configure export options:
   - **Page Size:** Letter or A4
   - **Orientation:** Portrait or Landscape
   - **Include Header/Footer:** Yes/No (adds company logo, date, page numbers)
   - **Include POV:** Yes (recommended -- adds context to the report)
6. Click **Export**
7. The PDF is generated and downloaded to your browser

### 5.2 Exporting a CubeView to Excel

1. Open the CubeView
2. Set the POV to the desired view
3. Click the **Export** icon in the top toolbar
4. Select **Export to Excel**
5. Configure export options:
   - **Format:** `.xlsx` (Excel 2007+)
   - **Include Formatting:** Yes (preserves colors, fonts, number formats)
   - **Include Member Names:** Yes (adds dimension member names as column headers)
   - **Include POV Header:** Yes (adds POV context at top of spreadsheet)
6. Click **Export**
7. The Excel file is generated and downloaded

### 5.3 Exporting Charts

1. Right-click on any chart in a dashboard
2. Select **Export Chart**
3. Choose format:
   - **PNG:** High-resolution image
   - **SVG:** Scalable vector graphic
   - **Excel:** Underlying data table
4. The file downloads automatically

### 5.4 Bulk Report Generation

For generating multiple reports at once (e.g., entity-level P&L for all plants):

1. Navigate to **Dashboard: DB_FIN_ReportLibrary**
2. Select the report type (e.g., "Entity P&L")
3. Select the entities (multi-select supported)
4. Select the period
5. Click **Generate Batch**
6. Reports are generated as a combined PDF or individual files in a ZIP archive
7. Download when complete (you receive a notification)

---

## 6. Scheduling Reports

### 6.1 Setting Up a Scheduled Report

1. Navigate to **Dashboard: DB_FIN_ReportLibrary**
2. Click **Schedule Report** (clock icon)
3. Configure the schedule:

| Setting | Options | Example |
|---------|---------|---------|
| Report | Select from report library | Consolidated P&L |
| Entity | Select entity or entity list | Total_Company |
| Scenario | Actual, Budget, etc. | Actual |
| Period | Current, Prior, Specific | Current Period |
| Frequency | Monthly, Quarterly, Annual, One-time | Monthly |
| Run Day | Day of month or "BD+N" | BD+5 (5th business day after month-end) |
| Run Time | Time of day | 08:00 AM ET |
| Format | PDF, Excel, or Both | PDF |
| Recipients | Email addresses or distribution list | finance-managers@company.com |

4. Click **Save Schedule**

### 6.2 Managing Scheduled Reports

1. Navigate to **Dashboard: DB_ADMIN_ReportSchedules**
2. View all active schedules with:
   - Report name
   - Next run date
   - Last run status (Success/Failed)
   - Recipient list
3. Actions available:
   - **Edit:** Modify schedule parameters
   - **Pause:** Temporarily suspend the schedule
   - **Delete:** Remove the schedule permanently
   - **Run Now:** Execute the report immediately (outside schedule)

### 6.3 Standard Scheduled Reports

The following reports are pre-configured and run automatically after each monthly close:

| Report | Frequency | Run Trigger | Recipients |
|--------|-----------|-------------|-----------|
| Consolidated Financial Statements | Monthly | After close approval | Executive team |
| Regional P&L Summary | Monthly | After close approval | Regional managers |
| Entity P&L (per entity) | Monthly | After close approval | Plant controllers |
| Budget Variance Report | Monthly | After close approval | FP&A team |
| Board Financial Package | Monthly | Manual trigger by CFO | Board members |
| Data Quality Scorecard | Monthly | After data load complete | Data operations, FP&A |
| Close Process Summary | Monthly | After period lock | All finance users |

---

## 7. Custom Report Requests

### 7.1 Self-Service Reporting

Users with Power User or higher access can create custom views:

1. Navigate to **Workspace: WS_Finance_Close** (or relevant workspace)
2. Click **New CubeView**
3. Configure the view:
   - **Cube:** Select Finance, Planning, HR, or Recon
   - **Rows:** Drag dimensions to row axis (e.g., Account, Entity)
   - **Columns:** Drag dimensions to column axis (e.g., Time, Scenario)
   - **POV:** Set remaining dimensions to specific members
   - **Member Selection:** Choose specific members or levels (e.g., "Children of OPEX_Total")
4. Configure formatting:
   - Number format (thousands, millions, decimals)
   - Conditional formatting (red/green for variances)
   - Column width and row height
5. Click **Save** and name your view

### 7.2 Requesting a New Dashboard or Report

If you need a report that cannot be built using self-service CubeViews:

1. Submit a request through the **IT Service Desk** portal
2. Include the following information:

| Field | Description | Example |
|-------|-------------|---------|
| Report Name | Descriptive name | "Product Profitability by Plant" |
| Business Purpose | Why is this report needed? | "Need to compare product margins across manufacturing plants for sourcing decisions" |
| Dimensions Required | Which dimensions and members | Entity (all plants), Account (Revenue through GM), Product (all), Time (monthly) |
| Layout | Sketch or description of desired layout | "Rows: Products; Columns: Plants; Values: Revenue, COGS, Gross Margin, GM%" |
| Calculations | Any custom calculations | "GM% = Gross Margin / Revenue * 100" |
| Frequency | One-time or recurring | "Monthly, available by BD+5" |
| Distribution | Who needs access | "Plant controllers and operations VPs" |
| Priority | Low, Medium, High | "Medium -- needed for Q2 planning cycle" |

3. The report development team will estimate effort and provide a delivery date
4. Typical turnaround: 5-10 business days for standard reports; 2-4 weeks for complex dashboards

---

## 8. Common Report Descriptions

### 8.1 Financial Reports

| Report | Description | Key Content | Primary Audience |
|--------|-------------|-------------|-----------------|
| **Consolidated Income Statement** | Multi-period P&L showing revenue through net income with budget and prior year comparisons | Revenue, COGS, GM, OPEX, EBITDA, EBT, NI with $ and % variances | Executives, Board |
| **Consolidated Balance Sheet** | Period-end balance sheet with roll-forward movements | Assets, Liabilities, Equity with opening/closing and key movements | Executives, Auditors |
| **Consolidated Cash Flow** | Indirect cash flow statement | Operating, Investing, Financing activities; beginning/ending cash | Executives, Treasury |
| **Regional P&L** | Income statement by geographic region | Revenue through operating income by NA, EU, AP, SA | Regional managers |
| **Entity P&L** | Individual entity income statement | Full P&L for a single plant or sales entity | Plant controllers |
| **Budget vs. Actual Variance** | Detailed variance analysis at account level | Actual, Budget, Variance ($), Variance (%), Prior Year, PY Variance | FP&A, Budget owners |
| **Revenue Analysis** | Revenue deep-dive by product and customer | Gross/Net revenue by product line, customer segment, geography | Sales, Marketing |
| **OPEX Analysis** | Operating expense detail by department | OPEX by department and account, with budget comparison and headcount | Department managers |
| **CAPEX Tracking** | Capital project spending vs. budget | Project-level spend, budget, remaining, % complete | Plant managers, CFO |

### 8.2 Operational Reports

| Report | Description | Key Content | Primary Audience |
|--------|-------------|-------------|-----------------|
| **Plant Performance Scorecard** | Plant-level operational KPIs | Production volume, yield, cost per unit, capacity utilization, quality metrics | Plant managers, COO |
| **Inventory Analysis** | Inventory balances and trends | Raw materials, WIP, finished goods by plant; days of inventory | Supply chain, Finance |
| **IC Position Report** | Intercompany balance summary | IC receivables/payables by partner pair; matched/unmatched status | Consolidation team |
| **Customer Profitability** | Revenue and margin by key account | Top 20 customers with revenue, COGS, gross margin, GM% | Sales leadership |

### 8.3 Planning Reports

| Report | Description | Key Content | Primary Audience |
|--------|-------------|-------------|-----------------|
| **Budget Summary** | Annual budget overview by entity | Revenue, OPEX, EBITDA, Headcount, CAPEX by entity | FP&A, Executives |
| **Forecast Accuracy** | Comparison of successive forecasts to actuals | Forecast vintage analysis; accuracy by account group | FP&A |
| **Rolling Forecast** | 18-month forward view | Actual months + forecast months; revenue and EBITDA trending | FP&A, Executives |
| **Headcount Plan vs. Actual** | Staffing plan comparison | Planned vs. actual headcount by department; vacancy analysis | HR, FP&A |

### 8.4 Compliance Reports

| Report | Description | Key Content | Primary Audience |
|--------|-------------|-------------|-----------------|
| **Account Reconciliation Status** | Balance sheet reconciliation tracker | Certified/pending/overdue counts; aging of open items | Accounting, Auditors |
| **Close Process Status** | Monthly close task completion tracker | Task completion by entity; on-time/late indicators | Finance managers |
| **Audit Trail Report** | Data change history for selected period | All data modifications with user, timestamp, old/new values | Internal Audit, External Audit |
| **Data Quality Scorecard** | Monthly data quality metrics | Load success rate, mapping errors, validation pass rates, timeliness | Data operations, FP&A |

---

*End of Document*

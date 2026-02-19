# TC_DataManagement - ETL and Data Management Test Cases

## Overview
Test cases validating the data management (ETL) process for the Global Manufacturing Enterprise.
Covers extraction from SAP S/4HANA, account/entity mapping, data quality rules, and end-to-end
data flow from source systems to the OneStream Finance cube.

---

## Test 1: SAP GL Extraction - Row Counts and Amount Tie-Out

**Objective:** Verify that the SAP GL extraction (CN_SAP_GLActuals) retrieves the correct number
of records and the total amounts tie to the SAP trial balance report.

**Preconditions:**
- SAP HANA connection (CONN_SAP_HANA) is configured and active.
- SAP company code 1000 maps to Plant_US01_Detroit.
- Period: January 2025 (fiscal year 2025, period 001).
- SAP trial balance report for company code 1000 is available as reference.

**Test Data:**
| Metric | SAP Source | OneStream Staging | Match? |
|---|---|---|---|
| Total GL records | 2,847 | 2,847 | Yes |
| Total Debit Amount (USD) | 14,325,680.50 | 14,325,680.50 | Yes |
| Total Credit Amount (USD) | 14,325,680.50 | 14,325,680.50 | Yes |
| Net Balance | 0.00 | 0.00 | Yes |

**Steps:**
1. Run SAP GL extraction for company code 1000, period 2025-01.
2. Count records loaded into OneStream staging table.
3. Sum debit amounts and credit amounts in staging.
4. Compare row count and totals to SAP trial balance report (ZFI_TB_001).
5. Verify net of debits and credits equals zero.

**Expected Results:**
- Row count in staging matches SAP source count exactly.
- Total debits in staging match SAP total debits to the penny.
- Total credits in staging match SAP total credits to the penny.
- No records lost during extraction or transformation.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 2: Account Mapping - SAP GL to OneStream Accounts

**Objective:** Verify that all SAP GL account numbers map to valid OneStream account dimension members.

**Preconditions:**
- Account mapping table is configured in Data Management.
- SAP GL accounts range from 100000 to 999999.
- All leaf accounts in Account_PL.csv, Account_BS.csv, Account_CF.csv, Account_Statistical.csv
  have corresponding SAP source mappings.

**Test Data (sample mappings):**
| SAP GL Account | SAP Description | OneStream Account | Status |
|---|---|---|---|
| 400000 | Revenue - Domestic | REV_Domestic_Industrial | Mapped |
| 400100 | Revenue - Export | REV_Export_Industrial | Mapped |
| 500000 | Raw Materials | RM_Steel | Mapped |
| 610000 | Production Labor | ProdLabor_Machining | Mapped |
| 700000 | SGA Salaries | SGA_Salaries | Mapped |
| 110000 | Cash - Operating | Cash_Unrestricted | Mapped |
| 130000 | Trade Receivables | AR_Trade | Mapped |
| 200000 | Trade Payables | AP_Trade | Mapped |
| 999999 | Unmapped Test | ??? | Unmapped |

**Steps:**
1. Extract the list of all SAP GL accounts posted in the period.
2. Run the account mapping transformation.
3. Identify any SAP GL accounts that do not map to a valid OneStream account.
4. Verify mapped accounts are leaf-level members (not parent/calculated accounts).
5. Verify no SAP GL account maps to multiple OneStream accounts (unless intentional split).

**Expected Results:**
- 100% of SAP GL accounts with balances map to a valid OneStream account.
- No unmapped accounts exist for actual posted data.
- Mapping is one-to-one or many-to-one (never one-to-many without explicit split rule).
- Any unmapped accounts are logged with a clear error message.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 3: Entity Mapping - SAP Company Codes to OneStream Entities

**Objective:** Verify that all SAP company codes map correctly to OneStream entity members.

**Preconditions:**
- SAP company codes are mapped in the connector configuration.

**Test Data:**
| SAP Company Code | SAP Name | OneStream Entity | Currency |
|---|---|---|---|
| 1000 | Detroit Manufacturing | Plant_US01_Detroit | USD |
| 1100 | Houston Manufacturing | Plant_US02_Houston | USD |
| 1200 | Charlotte Manufacturing | Plant_US03_Charlotte | USD |
| 2000 | Toronto Manufacturing | Plant_CA01_Toronto | CAD |
| 2100 | Monterrey Assembly | Plant_MX01_Monterrey | MXN |
| 3000 | Munich Manufacturing | Plant_DE01_Munich | EUR |
| 3100 | Stuttgart Engineering | Plant_DE02_Stuttgart | EUR |
| 3200 | Birmingham Aerospace | Plant_UK01_Birmingham | GBP |
| 3300 | Lyon Automation | Plant_FR01_Lyon | EUR |
| 4000 | Shanghai Electronics | Plant_CN01_Shanghai | CNY |
| 4100 | Shenzhen Electronics | Plant_CN02_Shenzhen | CNY |
| 4200 | Osaka Robotics | Plant_JP01_Osaka | JPY |
| 4300 | Pune Manufacturing | Plant_IN01_Pune | INR |
| 9000 | Shared Services IT | SS_IT | USD |
| 9100 | Shared Services HR | SS_HR | USD |
| 9200 | Shared Services Finance | SS_Finance | USD |

**Steps:**
1. Extract all company codes from SAP with posted transactions.
2. Run entity mapping transformation.
3. Verify each company code maps to exactly one OneStream entity.
4. Verify the mapped entity's currency matches the SAP company code currency.
5. Verify no company code is left unmapped.

**Expected Results:**
- All 16 company codes map to the correct OneStream entity.
- Currency alignment is confirmed for each mapping.
- No orphan company codes exist in the source data.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 4: Sign Convention - Debits and Credits

**Objective:** Verify that the SAP debit/credit indicator (SHKZG) is correctly converted to
OneStream sign convention (positive = debit, negative = credit).

**Preconditions:**
- SAP uses SHKZG field: S = Debit, H = Credit.
- OneStream convention: Assets and Expenses are positive (debit), Revenue and Liabilities are negative (credit).

**Test Data:**
| SAP SHKZG | SAP Amount | Account Type | Expected OneStream Amount |
|---|---|---|---|
| S (Debit) | 50,000.00 | Asset (Cash) | 50,000.00 |
| H (Credit) | 50,000.00 | Revenue | -50,000.00 |
| S (Debit) | 30,000.00 | Expense (COGS) | 30,000.00 |
| H (Credit) | 30,000.00 | Liability (AP) | -30,000.00 |
| S (Debit) | 10,000.00 | Revenue (Return) | 10,000.00 |
| H (Credit) | 10,000.00 | Asset (AR reduction) | -10,000.00 |

**Steps:**
1. Load test transactions with known SHKZG values.
2. Run the sign conversion logic in CN_SAP_GLActuals.
3. Verify each transaction has the correct sign in staging.
4. Verify the trial balance nets to zero after sign conversion.

**Expected Results:**
- Debit entries (SHKZG=S) remain positive.
- Credit entries (SHKZG=H) are negated (multiplied by -1).
- Revenue accounts have negative balances (credit normal).
- Asset accounts have positive balances (debit normal).
- Total of all signed amounts = 0 (trial balance in balance).

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 5: Duplicate Detection - Reject Duplicate Records

**Objective:** Verify that duplicate GL records are detected and rejected during the load process.

**Preconditions:**
- Duplicate detection is enabled in the data management step.
- Duplicate key: Company Code + Document Number + Line Item + Posting Date.

**Test Data:**
| Record | DocNumber | LineItem | PostingDate | Amount | Expected |
|---|---|---|---|---|---|
| 1 | 5000001234 | 001 | 2025-01-15 | 5,000.00 | Loaded |
| 2 | 5000001234 | 002 | 2025-01-15 | -5,000.00 | Loaded |
| 3 | 5000001234 | 001 | 2025-01-15 | 5,000.00 | Rejected (dup of Record 1) |
| 4 | 5000001235 | 001 | 2025-01-15 | 3,000.00 | Loaded |
| 5 | 5000001234 | 001 | 2025-01-16 | 5,000.00 | Loaded (different date) |

**Steps:**
1. Load a data file containing intentional duplicate records.
2. Run the data management import step.
3. Verify duplicates are rejected and logged.
4. Verify non-duplicate records are loaded successfully.
5. Verify the rejection log contains the duplicate record details.

**Expected Results:**
- Record 3 is rejected as a duplicate of Record 1 (same DocNumber + LineItem + Date).
- Records 1, 2, 4, and 5 are loaded successfully.
- Record 5 is NOT flagged as duplicate (different posting date).
- Rejection log entry includes the duplicate key and reason.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 6: Validation Rules - Invalid Data Rejection

**Objective:** Verify that data quality validation rules reject invalid records with appropriate
error messages.

**Preconditions:**
- Validation rules configured: required fields, valid ranges, referential integrity.

**Test Data:**
| Record | Issue | Expected Error |
|---|---|---|
| 1 | Missing GL Account | "Required field GLAccount is null or empty" |
| 2 | Invalid Company Code (9999) | "Company code 9999 not found in entity mapping" |
| 3 | Future posting date (2026-06-01) | "Posting date is in the future; rejected" |
| 4 | Zero amount on both debit and credit | "Both DebitAmount and CreditAmount are zero" |
| 5 | Negative debit amount (-500) | "DebitAmount cannot be negative" |
| 6 | Non-numeric amount ("ABC") | "Amount field contains non-numeric value" |
| 7 | Valid record | Loaded successfully |

**Steps:**
1. Load a data file containing records with known validation issues.
2. Run the data management import with validation enabled.
3. Verify each invalid record is rejected.
4. Verify the error message matches the expected message for each rejection.
5. Verify valid records are loaded despite the presence of invalid records.

**Expected Results:**
- Records 1-6 are rejected with specific, actionable error messages.
- Record 7 loads successfully.
- The validation report shows the count of rejected records by error type.
- No partial loads (each record is atomic: loaded or rejected entirely).

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 7: End-to-End Load - Source Through Staging to Cube

**Objective:** Verify the complete data flow from SAP source through the staging area to the
Finance cube, confirming data integrity at each step.

**Preconditions:**
- SAP connection is active.
- All mappings (account, entity, cost center) are configured.
- The workflow step for data load is enabled.

**Test Data:**
Use Plant_US01_Detroit (Company Code 1000) for January 2025 as the test case.

| Stage | Record Count | Total Revenue | Total Assets |
|---|---|---|---|
| SAP Source | 2,847 | -6,250,000.00 | 45,000,000.00 |
| Staging Table | 2,847 | -6,250,000.00 | 45,000,000.00 |
| Finance Cube (C_Local) | N/A (aggregated) | -6,250,000.00 | 45,000,000.00 |

**Steps:**
1. Run the full data management workflow for Plant_US01_Detroit.
2. Verify record count at each stage (source query, staging, cube load).
3. Verify total revenue and total assets tie across all three stages.
4. Verify account mapping was applied correctly in the cube.
5. Verify the data appears at the correct entity, scenario, and time intersections.

**Expected Results:**
- No data loss between source, staging, and cube.
- Amounts match at every stage.
- Data lands at E#Plant_US01_Detroit:S#Actual:T#2025M1:C#C_Local.
- All dimensions are populated correctly (Account, Entity, CostCenter, Product).

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 8: Incremental vs Full Load

**Objective:** Verify that incremental load adds only new/changed data while full load replaces
all data for the period.

**Preconditions:**
- Initial full load completed for January 2025 with 2,847 records.
- 15 new GL postings added in SAP after the initial load.
- 3 existing postings reversed and reposted with different amounts.

**Test Data:**
| Load Type | Records Processed | Expected Cube State |
|---|---|---|
| Full Load (initial) | 2,847 | 2,847 records in staging |
| Incremental Load | 18 (15 new + 3 changed) | 2,862 records in staging (2,847 - 3 old + 3 new + 15 new) |
| Full Load (refresh) | 2,862 | 2,862 records (complete replacement) |

**Steps:**
1. Run initial full load for January 2025. Verify 2,847 records.
2. Add 15 new postings and modify 3 existing postings in SAP.
3. Run incremental load. Verify only 18 records are processed.
4. Verify cube totals reflect the new and changed records.
5. Run another full load. Verify complete data replacement with 2,862 records.
6. Verify cube totals match after full load vs incremental load.

**Expected Results:**
- Incremental load processes only delta records (new + changed).
- Full load clears and reloads all records for the period.
- Cube totals are identical whether data was loaded incrementally or via full load.
- Incremental load is significantly faster than full load.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

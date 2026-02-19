# TC_Elimination - Intercompany Elimination Test Cases

## Overview
Test cases validating the intercompany elimination engine (FR_IntercompanyElimination) for the
Global Manufacturing Enterprise. All tests use Scenario: Actual, Period: 2025M1 unless otherwise noted.
IC tolerance threshold: $1,000 (per FR_IntercompanyElimination configuration).

---

## Test 1: Simple IC AR/AP Elimination - Matching Balances

**Objective:** Verify that matching IC receivables and payables between two entities are fully eliminated.

**Preconditions:**
- Plant_US01_Detroit has IC AR to Plant_DE01_Munich of $500,000.
- Plant_DE01_Munich has IC AP to Plant_US01_Detroit of $500,000 (translated to USD).
- Both amounts are posted to C_Translated in USD.

**Test Data:**
| Entity | Account | IC Partner | Amount (USD) |
|---|---|---|---|
| Plant_US01_Detroit | AR_Intercompany | I#Plant_DE01_Munich | 500,000.00 |
| Plant_DE01_Munich | AP_Intercompany | I#Plant_US01_Detroit | -500,000.00 |

**Expected Elimination Entries (at Elim_CrossRegion, C_Elimination):**
| Account | Amount | Description |
|---|---|---|
| AP_Intercompany | 500,000.00 | DR AP to reverse liability |
| AR_Intercompany | -500,000.00 | CR AR to reverse asset |

**Steps:**
1. Load IC AR and IC AP data for both entities.
2. Run IC elimination processing for the consolidation group.
3. Verify elimination entries at the appropriate elimination entity.
4. Verify net IC AR and IC AP at the consolidated level is zero.

**Expected Results:**
- Elimination entries DR AP and CR AR for $500,000.
- Net IC balance at consolidated level = $0.
- No unmatched items reported.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 2: IC Revenue/COGS Elimination - Verify Markup Eliminated

**Objective:** Verify that IC revenue and IC COGS between a seller and buyer are eliminated,
including the intercompany markup.

**Preconditions:**
- Plant_DE01_Munich sells components to Plant_US01_Detroit.
- Transfer pricing method: CostPlus (cost + 15% markup).
- Plant_DE01_Munich cost basis: $200,000. IC selling price: $230,000.
- Plant_US01_Detroit records IC COGS of $230,000.

**Test Data:**
| Entity | Account | IC Partner | Amount (USD) |
|---|---|---|---|
| Plant_DE01_Munich | REV_IC_TransferSales | I#Plant_US01_Detroit | -230,000.00 |
| Plant_US01_Detroit | RawMaterials (IC COGS) | I#Plant_DE01_Munich | 230,000.00 |

**Expected Elimination Entries (at Elim_CrossRegion, C_Elimination):**
| Account | Amount | Description |
|---|---|---|
| REV_IC_TransferSales | 230,000.00 | DR IC Revenue (reverse seller's credit) |
| RawMaterials (IC portion) | -230,000.00 | CR IC COGS (reverse buyer's debit) |

**Steps:**
1. Load IC Revenue for seller and IC COGS for buyer.
2. Run IC elimination.
3. Verify elimination entries reverse both IC Revenue and IC COGS.
4. Verify the $30,000 markup (15% of $200K) is eliminated from consolidated gross profit.

**Expected Results:**
- IC Revenue of $230,000 eliminated.
- IC COGS of $230,000 eliminated.
- Consolidated Revenue excludes the IC sale.
- Consolidated COGS excludes the IC purchase.
- Markup does not inflate consolidated gross profit.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 3: IC with FX Difference - Different Currencies, Tolerance Handling

**Objective:** Verify that IC elimination handles FX translation differences within the tolerance
threshold ($1,000).

**Preconditions:**
- Plant_US01_Detroit records IC AR to Plant_CN01_Shanghai of $150,000 (USD).
- Plant_CN01_Shanghai records IC AP to Plant_US01_Detroit of CNY 1,065,000.
- CNY/USD closing rate: 0.14085 (translates to $149,904.25).
- Difference: $95.75 (within $1,000 tolerance).

**Test Data:**
| Entity | Account | IC Partner | Local Amount | Translated USD |
|---|---|---|---|---|
| Plant_US01_Detroit | AR_Intercompany | I#Plant_CN01_Shanghai | 150,000.00 USD | 150,000.00 |
| Plant_CN01_Shanghai | AP_Intercompany | I#Plant_US01_Detroit | -1,065,000.00 CNY | -149,904.25 |

**Steps:**
1. Load IC balances in respective local currencies.
2. Run FX translation for Plant_CN01_Shanghai.
3. Run IC elimination.
4. Verify the $95.75 FX difference is within tolerance.
5. Verify elimination uses the average of the two amounts.

**Expected Results:**
- Elimination amount: ($150,000.00 + $149,904.25) / 2 = $149,952.13.
- Difference of $95.75 absorbed within tolerance.
- No unmatched items flagged.
- Small residual FX difference may remain (less than $0.01 rounding).

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 4: IC Dividend Elimination

**Objective:** Verify that IC dividend income received by a parent entity and dividends paid by
a subsidiary are properly eliminated.

**Preconditions:**
- Plant_DE01_Munich declared a dividend of EUR 500,000 to Germany_Operations.
- Germany_Operations records dividend income from Plant_DE01_Munich.
- Both in EUR (no FX difference within EMEA).

**Test Data:**
| Entity | Account | IC Partner | Amount (EUR) |
|---|---|---|---|
| Germany_Operations | OIE_InvestmentIncome | I#Plant_DE01_Munich | -500,000.00 |
| Plant_DE01_Munich | RetainedEarnings (div paid) | I#Germany_Operations | 500,000.00 |

**Expected Elimination Entries:**
| Account | Amount | Description |
|---|---|---|
| OIE_InvestmentIncome | 500,000.00 | DR to reverse dividend income |
| RetainedEarnings | -500,000.00 | CR to reverse dividend payment |

**Steps:**
1. Post dividend income and dividend payment to respective entities.
2. Run IC elimination.
3. Verify dividend income and payment are both eliminated.
4. Verify consolidated retained earnings is not double-reduced.

**Expected Results:**
- Dividend income eliminated from consolidated P&L.
- Dividend payment reversed from consolidated equity/RE.
- No impact on consolidated cash flow (dividends are internal).

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 5: Partial Match - One Side Posted, Other Not

**Objective:** Verify that when only one side of an IC transaction is posted, the system flags it
as an exception.

**Preconditions:**
- Plant_US02_Houston records IC AR to Plant_IN01_Pune of $75,000.
- Plant_IN01_Pune has NOT posted the corresponding IC AP to Plant_US02_Houston.

**Test Data:**
| Entity | Account | IC Partner | Amount (USD) |
|---|---|---|---|
| Plant_US02_Houston | AR_Intercompany | I#Plant_IN01_Pune | 75,000.00 |
| Plant_IN01_Pune | AP_Intercompany | I#Plant_US02_Houston | 0.00 (not posted) |

**Steps:**
1. Load IC AR for Plant_US02_Houston only.
2. Run IC elimination.
3. Verify the unmatched item is flagged.
4. Review the error log for the unmatched item report.

**Expected Results:**
- No elimination entry generated for this pair.
- Unmatched item reported: "AR/AP: Plant_US02_Houston AR=75,000 vs Plant_IN01_Pune AP=0".
- The IC AR balance remains in the consolidated BS until resolved.
- Difference of $75,000 exceeds the $1,000 tolerance.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 6: Multi-Party IC - Triangular Transactions

**Objective:** Verify correct elimination of triangular IC transactions where three entities
are involved in related transactions.

**Preconditions:**
- Plant_DE01_Munich sells to Plant_CN01_Shanghai ($300,000).
- Plant_CN01_Shanghai performs additional assembly and sells to Plant_US01_Detroit ($450,000).
- Plant_US01_Detroit sells finished goods externally.
- All three pairs have IC relationships defined.

**Test Data:**
| Seller | Buyer | IC Revenue | IC COGS |
|---|---|---|---|
| Plant_DE01_Munich | Plant_CN01_Shanghai | -300,000.00 | 300,000.00 |
| Plant_CN01_Shanghai | Plant_US01_Detroit | -450,000.00 | 450,000.00 |

**Steps:**
1. Load IC Revenue and IC COGS for both transaction pairs.
2. Run IC elimination.
3. Verify each bilateral pair is eliminated independently.
4. Verify total consolidated IC elimination = $750,000 (sum of both legs).
5. Verify no cross-contamination between the two elimination pairs.

**Expected Results:**
- DE01-CN01 pair: $300,000 IC Revenue and COGS eliminated.
- CN01-US01 pair: $450,000 IC Revenue and COGS eliminated.
- Total IC elimination: $750,000.
- Each pair matched and eliminated independently.
- Consolidated revenue only includes the external sale by Plant_US01_Detroit.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 7: IC Loan/Interest Elimination

**Objective:** Verify that IC loans (principal) and IC interest income/expense are properly eliminated.

**Preconditions:**
- SS_Finance has an IC loan receivable of $2,000,000 from Plant_US01_Detroit.
- Plant_US01_Detroit has a corresponding IC loan payable of $2,000,000 to SS_Finance.
- IC interest: SS_Finance records interest income of $12,500/month; Plant_US01_Detroit records
  interest expense of $12,500/month.

**Test Data:**
| Entity | Account | IC Partner | Amount |
|---|---|---|---|
| SS_Finance | LTDebt (IC Loan Rec) | I#Plant_US01_Detroit | 2,000,000.00 |
| Plant_US01_Detroit | LTDebt (IC Loan Pay) | I#SS_Finance | -2,000,000.00 |
| SS_Finance | OIE_InvestmentIncome | I#Plant_US01_Detroit | -12,500.00 |
| Plant_US01_Detroit | INT_LongTermDebt | I#SS_Finance | 12,500.00 |

**Steps:**
1. Load IC loan principal and interest data.
2. Run IC elimination.
3. Verify loan principal elimination (DR Loan Payable, CR Loan Receivable).
4. Verify interest elimination (DR Interest Income, CR Interest Expense).
5. Verify both BS and P&L IC items are eliminated.

**Expected Results:**
- Loan principal: $2,000,000 eliminated (net zero on consolidated BS).
- Interest income/expense: $12,500 eliminated (net zero on consolidated P&L).
- Consolidated debt excludes IC loans.
- Consolidated interest expense excludes IC interest.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

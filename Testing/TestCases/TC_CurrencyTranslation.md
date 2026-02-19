# TC_CurrencyTranslation - Foreign Exchange Translation Test Cases

## Overview
Test cases validating the currency translation engine (FR_CurrencyTranslation) for the
Global Manufacturing Enterprise. Tests cover translation per IAS 21 / ASC 830 standards.
Group reporting currency: USD. Period: 2025M1 unless otherwise noted.

---

## Test 1: P&L Translation at Average Rate

**Objective:** Verify that P&L accounts (Revenue, COGS, OPEX) are translated at the weighted
average exchange rate for the period.

**Preconditions:**
- Plant_DE01_Munich has loaded P&L data for 2025M1 in EUR.
- EUR/USD average rate for January 2025: 1.0850.
- FX rates loaded in the Rates scenario/cube.

**Test Data:**
| Account | Local (EUR) | Avg Rate | Expected (USD) |
|---|---|---|---|
| TotalRevenue | -4,200,000.00 | 1.0850 | -4,557,000.00 |
| TotalCOGS | 2,730,000.00 | 1.0850 | 2,962,050.00 |
| TotalOPEX | 840,000.00 | 1.0850 | 911,400.00 |
| InterestExpense | 35,000.00 | 1.0850 | 37,975.00 |
| IncomeTax | 148,750.00 | 1.0850 | 161,393.75 |
| NetIncome | -446,250.00 | 1.0850 | -484,181.25 |

**Steps:**
1. Load EUR P&L data to Plant_DE01_Munich C_Local.
2. Load EUR/USD average rate = 1.0850.
3. Run FR_CurrencyTranslation.
4. Read C_Translated values for each P&L account.
5. Verify each translated amount = local amount * average rate.

**Expected Results:**
- All P&L accounts translated at 1.0850 average rate.
- Translated amounts match Local * Rate within $0.01.
- No account uses closing or historical rate.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 2: BS Translation at Closing Rate

**Objective:** Verify that Balance Sheet accounts (Assets and Liabilities) are translated at the
period-end closing (spot) exchange rate.

**Preconditions:**
- Plant_DE01_Munich has loaded BS data for 2025M1 in EUR.
- EUR/USD closing rate for January 31, 2025: 1.0780.

**Test Data:**
| Account | Local (EUR) | Close Rate | Expected (USD) |
|---|---|---|---|
| Cash | 3,500,000.00 | 1.0780 | 3,773,000.00 |
| AccountsReceivable | 5,200,000.00 | 1.0780 | 5,605,600.00 |
| Inventory | 8,100,000.00 | 1.0780 | 8,731,800.00 |
| FixedAssets | 22,000,000.00 | 1.0780 | 23,716,000.00 |
| TotalAssets | 38,800,000.00 | 1.0780 | 41,826,400.00 |
| AccountsPayable | -4,100,000.00 | 1.0780 | -4,419,800.00 |
| LTDebt | -12,000,000.00 | 1.0780 | -12,936,000.00 |
| TotalLiabilities | -22,600,000.00 | 1.0780 | -24,362,800.00 |

**Steps:**
1. Load EUR BS data to Plant_DE01_Munich C_Local.
2. Load EUR/USD closing rate = 1.0780.
3. Run FR_CurrencyTranslation.
4. Read C_Translated values for each BS account.
5. Verify each translated amount = local amount * closing rate.

**Expected Results:**
- All asset and liability accounts translated at 1.0780 closing rate.
- Translated amounts match Local * Rate within $0.01.
- No BS account uses average or historical rate.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 3: Equity at Historical Rate

**Objective:** Verify that equity accounts (Common Stock, APIC) are translated at the historical
rate prevailing at the date of the original equity transaction.

**Preconditions:**
- Plant_DE01_Munich was incorporated when EUR/USD historical rate was 1.1200.
- Common Stock: EUR 5,000,000. APIC: EUR 10,000,000.

**Test Data:**
| Account | Local (EUR) | Hist Rate | Expected (USD) |
|---|---|---|---|
| CommonStock | -5,000,000.00 | 1.1200 | -5,600,000.00 |
| APIC | -10,000,000.00 | 1.1200 | -11,200,000.00 |

**Steps:**
1. Load equity data at C_Local.
2. Load EUR/USD historical rate = 1.1200.
3. Run FR_CurrencyTranslation.
4. Verify CommonStock and APIC translated at 1.1200.
5. Verify these accounts are NOT affected by changes in closing or average rates.

**Expected Results:**
- CommonStock: EUR 5M * 1.12 = USD 5.6M.
- APIC: EUR 10M * 1.12 = USD 11.2M.
- Historical rate is fixed; does not change period to period.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 4: CTA Calculation

**Objective:** Verify that the Cumulative Translation Adjustment (CTA) is correctly calculated
as the balancing plug to make the translated Balance Sheet balance.

**Preconditions:**
- All BS accounts translated (assets/liabilities at closing, equity at historical/calculated).
- P&L translated at average rate; Net Income flows to Retained Earnings.

**Test Data (using Test 1-3 data combined):**
| Component | Amount (USD) |
|---|---|
| Translated Total Assets | 41,826,400.00 |
| Translated Total Liabilities | -24,362,800.00 |
| Translated Equity (excl CTA) | -16,800,000.00 + RE_translated |
| **Expected CTA** | **Plug to balance** |

**Steps:**
1. Complete all translation steps (P&L, BS, Equity).
2. Calculate expected CTA = Total Assets - Total Liabilities - Equity (excl CTA).
3. Run FR_CurrencyTranslation CTA step.
4. Compare calculated CTA to the system-generated CTA.
5. Verify CTA is posted to OCI_ForeignCurrency.

**Expected Results:**
- CTA = Assets - Liabilities - Equity (excl CTA).
- Translated BS balances: Assets = Liabilities + Equity + CTA.
- CTA is recorded in OCI_ForeignCurrency within TotalEquity.
- Period movement in CTA = current CTA - prior period CTA.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 5: Retained Earnings Translation

**Objective:** Verify that Retained Earnings is translated using the calculated method (not a
single rate) as required by IAS 21.

**Preconditions:**
- Plant_DE01_Munich opening RE (from prior period closing, already translated): USD 3,200,000.
- Current period Net Income: EUR -446,250 translated at avg rate to USD -484,181.25.
- No dividends declared in January 2025.

**Test Data:**
| Component | Amount (USD) |
|---|---|
| Opening RE (prior period translated) | 3,200,000.00 |
| + Translated Net Income (avg rate) | -484,181.25 |
| - Translated Dividends (avg rate) | 0.00 |
| **= Expected Closing RE** | **2,715,818.75** |

**Steps:**
1. Verify prior period closing RE is available at C_Translated.
2. Run P&L translation to get translated Net Income.
3. Run RE calculation step in FR_CurrencyTranslation.
4. Verify translated RE = Opening RE + Translated NI - Translated Dividends.
5. Verify RE is NOT simply translated at a single FX rate.

**Expected Results:**
- Translated RE = 3,200,000 + (-484,181.25) - 0 = 2,715,818.75.
- RE is calculated, not directly translated at any single rate.
- This preserves the integrity of the translated equity section.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 6: Multi-Currency Entity - EUR Entity with USD Reporting

**Objective:** End-to-end translation test for Plant_FR01_Lyon (EUR) reporting to USD group.

**Preconditions:**
- Plant_FR01_Lyon: functional currency EUR, group currency USD.
- All FX rates loaded: EUR/USD Avg=1.0850, Close=1.0780, Hist=1.1200.
- Complete set of financial data loaded for 2025M1.

**Test Data:**
| Account | Local (EUR) | Rate Type | Rate | Expected (USD) |
|---|---|---|---|---|
| TotalRevenue | -2,800,000 | Average | 1.0850 | -3,038,000 |
| TotalCOGS | 1,680,000 | Average | 1.0850 | 1,822,800 |
| TotalOPEX | 700,000 | Average | 1.0850 | 759,500 |
| TotalAssets | 25,500,000 | Closing | 1.0780 | 27,489,000 |
| TotalLiabilities | -15,300,000 | Closing | 1.0780 | -16,493,400 |
| CommonStock | -3,000,000 | Historical | 1.1200 | -3,360,000 |
| APIC | -5,000,000 | Historical | 1.1200 | -5,600,000 |
| RE | calculated | N/A | N/A | calculated |
| CTA | plug | N/A | N/A | plug |

**Steps:**
1. Load complete financial data set for Plant_FR01_Lyon.
2. Load all three FX rate types.
3. Run FR_CurrencyTranslation.
4. Verify each account category uses the correct rate type.
5. Verify translated BS balances (A = L + E).
6. Verify CTA plugs the BS.

**Expected Results:**
- P&L: average rate 1.0850 applied.
- BS assets/liabilities: closing rate 1.0780 applied.
- Equity (CS, APIC): historical rate 1.1200 applied.
- RE: calculated (opening + NI - dividends).
- CTA: plugs to balance the BS.
- Total translated Assets = Total translated Liabilities + Total translated Equity.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 7: Rate Change Impact - Verify Impact Between Periods

**Objective:** Verify the impact of exchange rate changes between two consecutive periods
on translated balances and CTA.

**Preconditions:**
- Plant_CN01_Shanghai: CNY functional currency.
- December 2024: CNY/USD Close=0.1410, Avg=0.1405.
- January 2025: CNY/USD Close=0.1390, Avg=0.1398.
- CNY depreciated against USD between periods.

**Test Data:**
| Account | Local (CNY) | Dec 2024 Translated | Jan 2025 Translated | Change |
|---|---|---|---|---|
| TotalAssets | 280,000,000 | 39,480,000 | 38,920,000 | -560,000 |
| Cash | 15,000,000 | 2,115,000 | 2,085,000 | -30,000 |

**Steps:**
1. Run translation for December 2024 with December rates.
2. Run translation for January 2025 with January rates.
3. Compare translated BS balances between periods.
4. Verify the change in CTA reflects the rate movement.
5. Verify P&L translated amounts use the respective monthly average rates.

**Expected Results:**
- Translated BS balances decrease due to CNY depreciation (lower closing rate).
- CTA movement reflects the difference between rate-change impact and P&L translation impact.
- Period-over-period change in CTA is recorded in OCI for January 2025.
- The translation gain/loss is unrealized and flows through OCI, not P&L.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

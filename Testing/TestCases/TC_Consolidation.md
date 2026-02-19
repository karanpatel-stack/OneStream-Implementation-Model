# TC_Consolidation - Consolidation Test Scenarios

## Overview
Test cases validating the consolidation engine (FR_Consolidation) for the Global Manufacturing Enterprise.
All tests use Scenario: Actual, Period: 2025M1 (January 2025) unless otherwise noted.

---

## Test 1: Full Consolidation - 100% Owned Subsidiary

**Objective:** Verify that a 100% owned subsidiary's data is fully aggregated to its parent entity.

**Preconditions:**
- Plant_US01_Detroit (100% owned by US_Operations) has loaded GL data for 2025M1.
- Consolidation dimension members C_Local and C_Consolidated are initialized.
- Currency: both entities use USD (no translation required).

**Test Data:**
| Account | Plant_US01_Detroit (C_Local) | Expected US_Operations (C_Consolidated) |
|---|---|---|
| TotalRevenue | -6,250,000.00 | -6,250,000.00 |
| TotalCOGS | 4,125,000.00 | 4,125,000.00 |
| TotalOPEX | 1,250,000.00 | 1,250,000.00 |
| TotalAssets | 45,000,000.00 | 45,000,000.00 |
| TotalLiabilities | -28,000,000.00 | -28,000,000.00 |

**Steps:**
1. Load test data to Plant_US01_Detroit at C_Local for all accounts.
2. Run consolidation for US_Operations.
3. Read US_Operations C_Consolidated values for each account.
4. Compare actual vs expected values.

**Expected Results:**
- Parent entity US_Operations C_Consolidated = sum of all children's C_Consolidated values.
- Plant_US01_Detroit amounts pass through at 100% with no adjustment.
- No entries appear in C_Proportional or C_EquityMethod.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 2: Proportional Consolidation - 60% Owned Entity

**Objective:** Verify proportional consolidation at 60% ownership for Plant_JP01_Osaka (joint venture).

**Preconditions:**
- Plant_JP01_Osaka has ConsolidationMethod=Proportional, OwnershipPercent=60.
- Plant_JP01_Osaka has loaded GL data for 2025M1 in JPY.
- FX translation from JPY to USD has been completed (C_Translated populated).

**Test Data:**
| Account | Plant_JP01_Osaka (C_Translated, USD) | Expected Japan_Operations (C_Proportional) |
|---|---|---|
| TotalRevenue | -2,800,000.00 | -1,680,000.00 (60%) |
| TotalCOGS | 1,820,000.00 | 1,092,000.00 (60%) |
| TotalOPEX | 560,000.00 | 336,000.00 (60%) |
| TotalAssets | 18,500,000.00 | 11,100,000.00 (60%) |
| TotalLiabilities | -11,200,000.00 | -6,720,000.00 (60%) |

**Steps:**
1. Load test data to Plant_JP01_Osaka at C_Translated.
2. Run consolidation for Japan_Operations.
3. Verify C_Proportional = C_Translated * 0.60 for all accounts.
4. Verify C_Consolidated includes the proportional amounts.

**Expected Results:**
- All accounts in Japan_Operations C_Proportional = Plant_JP01_Osaka C_Translated * 60%.
- Amounts are rounded to two decimal places.
- No full consolidation entries exist for this entity.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 3: Equity Method - 30% Owned Entity

**Objective:** Verify equity method pickup for a hypothetical 30% associate (simulated by adjusting
Plant_JP01_Osaka ownership to 30% for this test scenario).

**Preconditions:**
- Test entity configured with OwnershipPercent=30.
- Entity has Net Income of $1,500,000 (translated to USD).
- FR_EquityPickup rule is enabled.

**Test Data:**
| Account | Associate Entity (C_Translated) | Expected Parent (C_EquityMethod) |
|---|---|---|
| NetIncome | -1,500,000.00 | N/A (not consolidated line by line) |
| Equity Method Investment (BS) | N/A | 450,000.00 (30% of NI) |
| Equity Method Income (P&L) | N/A | -450,000.00 (30% of NI) |

**Steps:**
1. Configure test entity with 30% ownership.
2. Load Net Income data to the associate entity.
3. Run consolidation including FR_EquityPickup.
4. Verify only the equity pickup entries appear in the parent.

**Expected Results:**
- No line-by-line consolidation of the associate's individual accounts.
- Parent records equity pickup = 30% of associate's Net Income.
- BS: Investment in Associate account increases by $450,000.
- P&L: Equity Method Income of $450,000.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 4: Multi-Level Consolidation - 3+ Hierarchy Levels

**Objective:** Verify data rolls up correctly through the full entity hierarchy:
Plant_US01_Detroit -> US_Operations -> Americas -> Global.

**Preconditions:**
- All entities in the Americas hierarchy have loaded data.
- Plant_US01_Detroit, Plant_US02_Houston, Plant_US03_Charlotte under US_Operations.
- Plant_CA01_Toronto under Canada_Operations.
- Plant_MX01_Monterrey under Mexico_Operations.
- All Americas entities roll up to Americas, then to Global.

**Test Data:**
| Entity | TotalRevenue (C_Local) |
|---|---|
| Plant_US01_Detroit | -6,250,000.00 |
| Plant_US02_Houston | -4,500,000.00 |
| Plant_US03_Charlotte | -3,800,000.00 |
| Plant_CA01_Toronto | -2,100,000.00 (translated from CAD) |
| Plant_MX01_Monterrey | -1,900,000.00 (translated from MXN) |
| **Expected US_Operations** | **-14,550,000.00** |
| **Expected Americas** | **-18,550,000.00** |

**Steps:**
1. Load test data to all leaf entities.
2. Run FX translation for CAD and MXN entities.
3. Run consolidation bottom-up: US_Operations, Canada_Operations, Mexico_Operations, then Americas.
4. Verify each level aggregates correctly.
5. Verify Global includes Americas, EMEA, APAC, and Corporate_HQ.

**Expected Results:**
- US_Operations = sum of three US plants.
- Americas = US_Operations + Canada_Operations + Mexico_Operations + Elim_Americas.
- Global = Americas + EMEA + APAC + Corporate_HQ + Eliminations.
- No data is double-counted or missed.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 5: Minority Interest / Non-Controlling Interest (NCI)

**Objective:** Verify NCI calculation for entities with ownership less than 100% but above 50%.

**Preconditions:**
- Plant_DE02_Stuttgart: 80% owned (20% NCI).
- Plant_FR01_Lyon: 80% owned (20% NCI).
- Plant_CN02_Shenzhen: 80% owned (20% NCI).
- FR_MinorityInterest rule is enabled.

**Test Data:**
| Entity | NetIncome (C_Translated) | Ownership | Expected NCI |
|---|---|---|---|
| Plant_DE02_Stuttgart | -800,000.00 | 80% | 160,000.00 (20% of NI) |
| Plant_FR01_Lyon | -600,000.00 | 80% | 120,000.00 (20% of NI) |
| Plant_CN02_Shenzhen | -1,200,000.00 | 80% | 240,000.00 (20% of NI) |

**Steps:**
1. Load test data with Net Income for each entity.
2. Run full consolidation.
3. Verify that 100% of each entity's data flows to the parent (full consolidation).
4. Verify MinorityInterest account in equity = (1 - ownership%) * Net Income.
5. Verify consolidated Net Income attributable to parent = ownership% * subsidiary Net Income.

**Expected Results:**
- Full line-by-line consolidation of all accounts (100%).
- MinorityInterest equity account records NCI share of net income.
- BS: MinorityInterest balance reflects cumulative NCI equity.
- P&L: NCI line item shows the minority share of net income.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 6: Consolidation Sequence - Bottom-Up Processing Order

**Objective:** Verify that consolidation processes in the correct bottom-up sequence, and that
all consolidation steps (Local, Translated, Proportional, Elimination, Consolidated) execute
in the proper order.

**Preconditions:**
- All entities loaded with data for 2025M1.
- All FX rates loaded for the period.
- IC transactions exist between entities in different regions.

**Test Data:**
Expected processing order:
1. Leaf entities: C_Local data validated
2. FX translation: C_Translated populated for non-USD entities
3. First-level parents: US_Operations, Canada_Operations, Mexico_Operations, Germany_Operations, UK_Operations, France_Operations, China_Operations, Japan_Operations, India_Operations
4. Second-level parents: Americas, EMEA, APAC, Corporate_HQ
5. IC Eliminations: Elim_Americas, Elim_EMEA, Elim_APAC, Elim_CrossRegion
6. Top-level: Global

**Steps:**
1. Enable detailed consolidation logging.
2. Run full consolidation for Global.
3. Review log to confirm processing sequence.
4. Verify no parent entity is processed before all its children.
5. Verify C_Consolidated = C_Local + C_Translated + C_Proportional + C_Elimination at each level.

**Expected Results:**
- Processing follows strict bottom-up order.
- No parent is consolidated before all children complete.
- C_Consolidated at each level is the sum of all consolidation sub-members.
- Elimination entries are posted at the correct parent level.
- The Global consolidated result includes all entities and all adjustments.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

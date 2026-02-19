# TC_Workflow - Workflow Process Test Cases

## Overview
Test cases validating the OneStream workflow engine for the Global Manufacturing Enterprise.
Covers monthly close, submission gating, approval routing, period locking, role-based access,
budget workflow, and concurrent processing.

---

## Test 1: Monthly Close Sequence - Steps Execute in Order

**Objective:** Verify that the monthly close workflow steps execute in the correct predefined
sequence and that no step can be initiated before its predecessor completes.

**Preconditions:**
- Monthly close workflow is configured with the following steps:
  1. Data Load (SAP GL extraction and staging)
  2. Data Validation (trial balance, completeness checks)
  3. Journal Entries (manual adjustments, accruals)
  4. Intercompany Matching (IC reconciliation)
  5. Currency Translation (FX processing)
  6. Consolidation (entity roll-up)
  7. IC Elimination (intercompany elimination entries)
  8. Management Review (review and approve)
  9. Period Lock (freeze the period)

**Test Data:**
- Entity: Plant_US01_Detroit
- Period: 2025M1 (January 2025)
- Scenario: Actual

**Steps:**
1. Initiate the monthly close workflow for Plant_US01_Detroit, 2025M1.
2. Attempt to skip Step 2 (Data Validation) and jump directly to Step 3.
3. Verify that Step 3 is blocked until Step 2 completes.
4. Complete each step sequentially from Step 1 through Step 9.
5. Verify the workflow status indicator updates at each step.
6. Verify timestamps are recorded for each step completion.

**Expected Results:**
- Steps execute strictly in order: 1 -> 2 -> 3 -> ... -> 9.
- Attempting to skip a step results in an error: "Prerequisite step not completed."
- Each step completion updates the workflow status to "Completed" for that step.
- Timestamps are recorded for start and completion of each step.
- The overall workflow status shows "In Progress" until Step 9 completes, then "Closed."

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 2: Submission Gate - Validation Blocks Submission

**Objective:** Verify that data quality validation rules block submission when critical validation
checks fail.

**Preconditions:**
- Submission gate configured after Step 2 (Data Validation).
- Required validations: Trial Balance in balance, Data Completeness >= 100%.
- Plant_US01_Detroit has intentionally incomplete data (missing TotalRevenue).

**Test Data:**
| Validation Check | Result | Gate Decision |
|---|---|---|
| Trial Balance Check | PASS (debits = credits) | Allow |
| Data Completeness | FAIL (TotalRevenue = 0) | Block |
| **Overall Gate Decision** | **BLOCKED** | **Cannot proceed** |

**Steps:**
1. Load data for Plant_US01_Detroit with TotalRevenue intentionally set to zero.
2. Run Step 2 (Data Validation).
3. Attempt to proceed to Step 3 (Journal Entries).
4. Verify the submission gate blocks progression.
5. Review the gate failure message.
6. Correct the data (load TotalRevenue).
7. Re-run Step 2.
8. Verify the gate now allows progression to Step 3.

**Expected Results:**
- Gate blocks with message: "Data validation failed. Missing data for: TotalRevenue."
- User cannot proceed past the gate until all validations pass.
- After correcting data and re-running validation, the gate opens.
- The workflow log records both the failure and the subsequent pass.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 3: Approval Routing - Correct Approver Receives Notification

**Objective:** Verify that when a workflow step requires approval, the notification is routed
to the correct approver based on the entity and role configuration.

**Preconditions:**
- Approval matrix configured:
  - Plant_US01_Detroit -> Plant Controller (user: john.smith@globalmanuf.com)
  - US_Operations -> Regional Controller (user: sarah.jones@globalmanuf.com)
  - Americas -> VP Finance Americas (user: michael.chen@globalmanuf.com)
- Step 8 (Management Review) requires approval.

**Test Data:**
| Entity | Step | Expected Approver | Role |
|---|---|---|---|
| Plant_US01_Detroit | Management Review | john.smith | Plant Controller |
| US_Operations | Management Review | sarah.jones | Regional Controller |
| Americas | Management Review | michael.chen | VP Finance Americas |

**Steps:**
1. Complete Steps 1-7 for Plant_US01_Detroit.
2. Submit Step 8 (Management Review).
3. Verify john.smith receives an approval notification.
4. Verify sarah.jones does NOT receive a notification (not yet at regional level).
5. Have john.smith approve.
6. Verify the workflow status for Plant_US01_Detroit updates to "Approved."
7. Repeat for US_Operations level to verify sarah.jones receives the notification.

**Expected Results:**
- Only the designated approver for the entity receives the notification.
- Notification includes: entity name, period, scenario, submitter name, timestamp.
- Approval updates the workflow status immediately.
- The approver's action (approve/reject) and timestamp are recorded in the audit trail.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 4: Rejection Flow - Returns to Prior Step with Reason

**Objective:** Verify that when an approver rejects a submission, the workflow returns to the
appropriate prior step and the rejection reason is communicated to the submitter.

**Preconditions:**
- Plant_DE01_Munich has completed Steps 1-7 and submitted Step 8.
- The approver (Germany Operations Controller) identifies an issue with journal entries.

**Test Data:**
| Action | Actor | Step | Result |
|---|---|---|---|
| Submit for Review | Plant Accountant | Step 8 | Pending Approval |
| Reject | Regional Controller | Step 8 | Returned to Step 3 |
| Correct JE | Plant Accountant | Step 3 | Re-submitted |
| Re-approve | Regional Controller | Step 8 | Approved |

**Steps:**
1. Submit Plant_DE01_Munich Step 8 for approval.
2. Approver reviews and selects "Reject."
3. Approver enters rejection reason: "Accrual for warranty reserve missing. Please add JE."
4. Verify workflow status reverts to Step 3 (Journal Entries).
5. Verify the submitter receives a notification with the rejection reason.
6. Submitter adds the missing journal entry and re-submits.
7. Verify the workflow returns to Step 8 for re-approval.
8. Approver approves on re-submission.

**Expected Results:**
- Rejection returns workflow to the specified step (Step 3 in this case).
- The rejection reason is visible to the submitter in the workflow dashboard.
- An email/notification is sent to the submitter with the reason.
- Steps 3 through 7 must be re-completed before re-submitting Step 8.
- The audit trail records both the rejection and the re-approval.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 5: Period Lock - Locked Periods Cannot Be Modified

**Objective:** Verify that once a period is locked (Step 9), no data modifications are allowed
for that period.

**Preconditions:**
- Plant_US01_Detroit has completed the full monthly close for 2025M1.
- Step 9 (Period Lock) has been executed.
- Period 2025M1 status = Locked.

**Test Data:**
| Action | Expected Result |
|---|---|
| Load data to 2025M1 via Data Management | Rejected: "Period 2025M1 is locked" |
| Enter journal entry for 2025M1 | Rejected: "Period 2025M1 is locked" |
| Run consolidation for 2025M1 | Rejected: "Period 2025M1 is locked" |
| View/report on 2025M1 data | Allowed (read-only access) |
| Load data to 2025M2 (next period) | Allowed |

**Steps:**
1. Confirm Period Lock step has been completed for 2025M1.
2. Attempt to load new GL data via CN_SAP_GLActuals for 2025M1.
3. Verify the load is rejected with a clear error message.
4. Attempt to post a manual journal entry to 2025M1.
5. Verify the journal entry is rejected.
6. Run a report on 2025M1 data.
7. Verify the report renders correctly (read-only access is maintained).
8. Load data to 2025M2 to confirm the lock only applies to 2025M1.

**Expected Results:**
- All data modification attempts for 2025M1 are rejected after period lock.
- Error messages clearly state that the period is locked.
- Read-only access (reporting, viewing) remains available.
- Other periods (2025M2, etc.) are not affected by the lock on 2025M1.
- Only a system administrator can unlock a locked period.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 6: Role-Based Access - Users Access Only Their Workflow Steps

**Objective:** Verify that users can only access and execute workflow steps assigned to their role.

**Preconditions:**
- Role configuration:
  - Data Analyst: Can execute Steps 1-2 (Data Load, Validation)
  - Plant Accountant: Can execute Steps 1-5 (through Currency Translation)
  - Plant Controller: Can execute Steps 1-8 (through Management Review)
  - Regional Controller: Can execute Steps 6-9 (Consolidation through Period Lock)
  - System Admin: Can execute all steps

**Test Data:**
| User | Role | Step 1 | Step 3 | Step 6 | Step 9 |
|---|---|---|---|---|---|
| analyst01 | Data Analyst | Allowed | Blocked | Blocked | Blocked |
| accountant01 | Plant Accountant | Allowed | Allowed | Blocked | Blocked |
| controller01 | Plant Controller | Allowed | Allowed | Allowed | Blocked |
| regional01 | Regional Controller | Blocked | Blocked | Allowed | Allowed |
| admin01 | System Admin | Allowed | Allowed | Allowed | Allowed |

**Steps:**
1. Log in as analyst01 and attempt to execute Step 1 (should succeed).
2. As analyst01, attempt to execute Step 3 (should be blocked).
3. Log in as accountant01 and attempt Steps 1, 3, and 6.
4. Verify accountant01 can execute Steps 1-5 but not Step 6.
5. Log in as regional01 and attempt Steps 1, 6, and 9.
6. Verify regional01 can execute Steps 6-9 but not Steps 1-5.
7. Log in as admin01 and verify all steps are accessible.

**Expected Results:**
- Each role can only execute steps within their assigned scope.
- Unauthorized step execution shows: "Access denied. This step requires [RoleName] role."
- Users can VIEW workflow status for all steps but can only EXECUTE assigned steps.
- System Admin has unrestricted access.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 7: Budget Workflow - Complete 8-Step Budget Cycle

**Objective:** Verify that the annual budget workflow completes through all 8 steps with proper
gates and approvals at each stage.

**Preconditions:**
- Budget workflow configured with 8 steps:
  1. Corporate Guidelines Distribution (top-down targets)
  2. Plant-Level Input (bottoms-up budget entry)
  3. Plant Controller Review
  4. Regional Consolidation and Review
  5. Variance Analysis (budget vs prior year, budget vs forecast)
  6. Executive Review
  7. Board Approval
  8. Budget Lock and Publish
- Scenario: Budget, Year: 2026

**Test Data:**
| Step | Actor | Deliverable |
|---|---|---|
| 1 | CFO / FP&A | Revenue targets, OPEX limits by entity |
| 2 | Plant Accountants | Detailed budget by account, cost center, product |
| 3 | Plant Controllers | Reviewed and adjusted plant budgets |
| 4 | Regional Controllers | Consolidated regional budgets |
| 5 | FP&A Team | Variance analysis reports |
| 6 | EVP/CFO | Approved with comments |
| 7 | Board of Directors | Final approval |
| 8 | System Admin | Budget locked, published to reporting |

**Steps:**
1. Initiate the budget workflow for FY2026.
2. Complete each step sequentially, verifying gates between steps.
3. At Step 3, reject one plant's budget and verify it returns to Step 2 for that entity.
4. At Step 6, verify the executive can view all regional consolidated budgets.
5. Complete through Step 8 and verify the budget is locked.
6. Attempt to modify budget data after Step 8 lock.

**Expected Results:**
- All 8 steps complete in sequence.
- Gates between steps enforce quality checks.
- Rejections return to the appropriate prior step for the specific entity only.
- The budget lock prevents modifications after Step 8.
- Published budget is available in reports under S#Budget scenario.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

---

## Test 8: Concurrent Submissions - Multiple Entities Submit Simultaneously

**Objective:** Verify that multiple entities can submit their workflow steps concurrently without
data conflicts or locking issues.

**Preconditions:**
- Three entities are ready to submit Step 2 (Data Validation) simultaneously:
  - Plant_US01_Detroit
  - Plant_DE01_Munich
  - Plant_CN01_Shanghai
- Each entity has a different user submitting.

**Test Data:**
| Entity | User | Submit Time | Expected |
|---|---|---|---|
| Plant_US01_Detroit | accountant_us01 | 10:00:00 AM | Success |
| Plant_DE01_Munich | accountant_de01 | 10:00:02 AM | Success |
| Plant_CN01_Shanghai | accountant_cn01 | 10:00:05 AM | Success |

**Steps:**
1. Have all three users navigate to Step 2 of the workflow.
2. All three users click "Submit" within a 10-second window.
3. Verify all three submissions are processed successfully.
4. Verify no deadlocks, timeouts, or data corruption occur.
5. Verify each entity's workflow status updates independently.
6. Verify the workflow log shows separate entries for each submission.

**Expected Results:**
- All three concurrent submissions succeed without conflict.
- No entity's submission blocks or delays another entity's submission.
- Each entity's workflow status is independent.
- No data corruption or cross-entity data contamination.
- System performance remains acceptable under concurrent load.

**Actual Results:** _To be completed during test execution_

**Status:** [ ] Pass / [ ] Fail

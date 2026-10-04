# Complete UI Test Guide

This guide explains how to test the complete Purchase and Inventory process in the browser. It is written for a first-time user and uses every dedicated business role.

[Back to README](../Readme.md)

## What this walkthrough proves

By the end of the walkthrough, you will have tested:

- Catalog and supplier master data.
- Purchase Request creation and two-level approval.
- Supplier eligibility, quotation entry, comparison, and selection.
- Purchase Order preparation, approval, issue, supplier email, and PDF attachment.
- Partial Goods Receiving.
- Inventory balance and transaction history.
- Role-aware Dashboard summaries, reminders, and recent activity.
- Role-based page and action permissions.

The test uses one Product, two Suppliers, two Quotations, one Purchase Order, and two Goods Receipts.

## Before starting

1. Apply the latest EF Core migrations.
2. Start the backend at `http://localhost:5165`.
3. Start the frontend at `http://localhost:5173`.
4. Start Mailpit, or configure a working SMTP account, if you want to verify email delivery.
5. Use a unique Run Tag in all names, for example `UI1001A`, so repeated tests do not create duplicate values.

Example data used below:

| Record | Example value |
| --- | --- |
| Run Tag | `UI1001A` |
| Product | Ergonomic Keyboard UI1001A |
| Requested quantity | 10 |
| Product estimate | RM 120.00 each |
| Alpha quotation | RM 115.00 each |
| Beta quotation | RM 108.00 each |
| Selected supplier | Beta Equipment UI1001A |
| First delivery | 6 |
| Second delivery | 4 |

## Demo accounts and role responsibilities

All Development demo accounts use password `Project1Demo123!`.

| Account | Role | Work performed in this guide |
| --- | --- | --- |
| `catalog@demo.local` | Catalog Manager | Creates Category, Unit, and Product. |
| `procurement@demo.local` | Procurement Officer | Creates Suppliers, links Products, records Quotations, and prepares/issues the PO. |
| `requester@demo.local` | Requester | Creates and submits the Purchase Request. |
| `department@demo.local` | Department Approver | Performs Department Review. |
| `finance@demo.local` | Finance Approver | Performs Finance Review. |
| `po.approver@demo.local` | Purchase Order Approver | Approves or rejects the submitted PO. |
| `warehouse@demo.local` | Warehouse Officer | Posts deliveries and checks Inventory. |
| `admin@demo.local` | Admin | Can access every page and perform every workflow action. |

Admin is a Super Admin for support and demonstrations. The normal walkthrough uses dedicated roles because this better represents how a company separates responsibilities.

## Role and page matrix

| Page or action | Requester | Dept. | Finance | Procurement | PO Approver | Warehouse | Catalog | Admin |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Dashboard | Own work | Approval queue | Approval queue | Purchasing work | PO approvals | Receiving/stock | Stock health | Company-wide |
| Own Purchase Request Draft | Manage | View | View | View | — | View | View | Manage |
| Department approval | — | Act | — | — | — | — | — | Act |
| Finance approval | — | — | Act | — | — | — | — | Act |
| Product Categories, Units, Products pages | — | — | — | — | — | — | Manage | Manage |
| Suppliers and Supplier Products | — | — | — | Manage | — | — | — | Manage |
| Supplier Quotations | — | — | — | Manage | — | — | — | Manage |
| Purchase Orders | — | — | — | Prepare/issue | Approve | View | — | Manage |
| Goods Receiving | — | — | — | View | — | Manage | — | Manage |
| Inventory | — | — | — | View | — | View | — | View |
| Email Records | — | — | — | Manage delivery | — | — | — | Manage |
| Email and Workflow Templates | — | — | — | — | — | — | — | Manage |

## Dashboard checks for every role

Every authenticated account now opens `/dashboard` after login. Test the Dashboard once with each Demo Account before continuing the full process:

1. Confirm the summary cards match that role's responsibility.
2. Confirm reminders only appear when the displayed count is greater than zero.
3. Click a summary card or reminder and confirm it opens the relevant authorized module.
4. Confirm Recent Activity contains at most 10 records and only includes modules the account can access.
5. Use **Refresh** after completing a workflow action and confirm the live counts change.

Expected role focus:

| Role | Dashboard focus |
| --- | --- |
| Requester | Own Draft, in-review, approved, and rejected Purchase Requests. |
| Department Approver | Purchase Requests waiting at Department Review. |
| Finance Approver | Purchase Requests waiting at Finance Review. |
| Procurement Officer | Quotations to prepare/compare, Purchase Orders to create/issue, and failed supplier emails. |
| Purchase Order Approver | Purchase Orders waiting for approval. |
| Warehouse Officer | Orders ready to receive, Draft receipts, and stock warnings. |
| Catalog Manager | Active/inactive Products and low/out-of-stock Products. |
| Admin | A company-wide combined view of approvals, receiving, stock, and email failures. |

A user with multiple business roles receives a merged view. Repeated summary or reminder types are displayed once. Admin always receives the company-wide Super Admin view.

The reminders are calculated from current business data when the Dashboard loads. They are not inbox messages, so there is no read/unread state or notification bell.

## Step 1: Confirm system configuration

Sign in as `admin@demo.local`.

1. Open **Workflow Templates** under Workspace.
2. Confirm `PURCHASE_REQUEST` has an Active, Published version.
3. Confirm `PURCHASE_ORDER` has an Active, Published version.
4. Open **Email Templates**.
5. Confirm `PURCHASE_ORDER_ISSUED` has an Active version.

Expected result:

- Both business workflows are available for new records.
- The active email template can render Purchase Order emails.
- Admin can access every navigation section.

If a required active template is missing, do not continue. A Purchase Request or Purchase Order cannot start its workflow without an active published workflow template, and a Purchase Order cannot be issued without an active email template.

## Step 2: Create catalog data

Sign out and sign in as `catalog@demo.local`.

### 2.1 Create a Product Category

1. Open **Product Categories**.
2. Click **New category**.
3. Enter:
   - Name: `Interview Equipment UI1001A`
   - Description: `Complete UI test category`
4. Submit the form.

Expected result: a success toast appears and the active Category is listed.

### 2.2 Create a Unit of Measure

1. Open **Units of Measure**.
2. Click **New unit**.
3. Enter:
   - Code: `TST1001A`
   - Name: `Test Unit UI1001A`
4. Submit the form.

Expected result: the Unit is active and available when creating a Product.

### 2.3 Create a Product

1. Open **Products**.
2. Click **New product**.
3. Enter:
   - Name: `Ergonomic Keyboard UI1001A`
   - Category: `Interview Equipment UI1001A`
   - Unit: `Test Unit UI1001A`
   - Default unit price: `120.00`
   - Reorder level: `3`
4. Submit the form.

Expected result:

- The system generates a Product code such as `ITEM-0001`.
- The Product is active.
- Its default price will be used as the Purchase Request estimate.

## Step 3: Create suppliers and eligibility

Sign out and sign in as `procurement@demo.local`.

### 3.1 Create Alpha supplier

1. Open **Suppliers**.
2. Click **New supplier**.
3. Enter:
   - Name: `Alpha Equipment UI1001A`
   - Contact person: `Alpha Sales`
   - Email: a valid test email address
   - Phone and address: any reasonable test values
4. Submit the form.

### 3.2 Create Beta supplier

Repeat the process with:

- Name: `Beta Equipment UI1001A`
- Contact person: `Beta Sales`
- Email: the mailbox that should receive the Purchase Order test email

Expected result: both Suppliers are active and have generated codes such as `SUP-0001` and `SUP-0002`.

### 3.3 Link suppliers to the Product

1. Open **Supplier Products**.
2. Click **New relationship**.
3. Select Alpha Equipment and Ergonomic Keyboard, then save.
4. Create another relationship for Beta Equipment and the same Product.
5. Mark Beta as Preferred if the form provides the option.

Expected result: both Suppliers are eligible to quote for the Product.

Why this is required: a Supplier is not automatically eligible for every Product. The active Supplier Product relationship tells the system exactly what each Supplier can provide.

## Step 4: Create and submit a Purchase Request

Sign out and sign in as `requester@demo.local`.

1. Open **Purchase Requests**.
2. Click **New purchase request**.
3. Enter:
   - Required date: a future date
   - Business justification: `Ten keyboards for new employee onboarding UI1001A`
4. Add `Ergonomic Keyboard UI1001A`.
5. Enter quantity `10`.
6. Confirm the estimated Unit Price is RM 120.00 and is not editable.
7. Click **Create and submit**.

Expected result:

- A Purchase Request number such as `PR-0001` is generated.
- Estimated total is RM 1,200.00.
- The current workflow step is `Department Review`.
- The action history contains `START` and `SUBMIT`.
- A success toast appears and the form closes.

### Optional Draft test

To test the Draft path, create another request and click **Create draft**.

- It appears in **My Tasks** for the Requester.
- Clicking **Edit** opens the form directly.
- Draft Details does not need to show workflow action controls.
- **Save and submit** moves it to Department Review.

## Step 5: Perform Department Review

Sign out and sign in as `department@demo.local`.

1. Open **My Tasks**.
2. Find the request under Purchase Request Tasks.
3. Click **Details**.
4. Click **Approve department review**.
5. Enter comment: `Department requirement confirmed.`
6. Confirm the action.

Expected result:

- The dialog closes and a success toast appears.
- The request disappears from this role's pending tasks.
- The workflow moves to `Finance Review`.
- Workflow History records the actor, action, time, and comment.

Optional rejection test: use a separate request, click Reject, and confirm that a comment is required and the workflow finishes at Rejected.

## Step 6: Perform Finance Review

Sign out and sign in as `finance@demo.local`.

1. Open **My Tasks**.
2. Open the same request.
3. Click **Approve finance review**.
4. Enter comment: `Budget is available.`
5. Confirm the action.

Expected result:

- The current step becomes `Approved`.
- Workflow status becomes `Completed` because Approved is a terminal step.
- No further Purchase Request workflow action is available.

## Step 7: Record two Supplier Quotations

Sign out and sign in as `procurement@demo.local`.

### 7.1 Alpha quotation

1. Open **Supplier Quotations**.
2. Click **New quotation**.
3. Select the Approved Purchase Request.
4. Select `Alpha Equipment UI1001A`.
5. Enter:
   - Supplier reference: `ALPHA-UI1001A` if the supplier provided one; otherwise leave it empty
   - Quotation date: today
   - Valid until: a future date
   - Supplier unit price: `115.00`
   - Notes: `Alpha quotation for complete UI test`
6. Click **Save and submit**.

Expected result: the system generates its own internal number such as `QT-0001`, and the quotation total is RM 1,150.00 with status Submitted.

### 7.2 Beta quotation

Repeat the process with:

- Supplier: `Beta Equipment UI1001A`
- Supplier reference: `BETA-UI1001A`
- Supplier unit price: `108.00`
- Notes: `Beta quotation for complete UI test`

Expected result: the Beta total is RM 1,080.00 and its status is Submitted.

The internal `QT-xxxx` number identifies the record inside Project1. The Supplier reference identifies the supplier's own document and is optional.

## Step 8: Compare and select a quotation

1. On the Supplier Quotations page, click **Compare** for the test Purchase Request.
2. Confirm both Alpha and Beta are shown side by side.
3. Compare unit prices, totals, validity dates, and supplier information.
4. Click **Select winner** for Beta Equipment.
5. Confirm the selection.

Expected result:

- Beta becomes `Selected`.
- Alpha becomes `Not Selected`.
- Only the selected quotation can be used to create the Purchase Order.

The lowest price is not selected automatically. A human user makes the commercial decision because price may not be the only consideration.

## Step 9: Create and submit a Purchase Order

Remain signed in as the Procurement Officer.

1. Open **Purchase Orders**.
2. Click **New purchase order**.
3. Select the winning Beta quotation.
4. Enter:
   - Order date: today
   - Expected delivery date: a future date
   - Delivery address: `Project1 Main Warehouse`
   - Notes: `Complete UI walkthrough order UI1001A`
5. Confirm the copied values:
   - Supplier: Beta Equipment
   - Quantity: 10
   - Unit price: RM 108.00
   - Total: RM 1,080.00
6. Click **Create and submit**.

Expected result:

- A number such as `PO-0001` is generated.
- The status and workflow step are `Pending Approval`.
- The PO appears under Purchase Order Tasks for the Purchase Order Approver.

## Step 10: Approve the Purchase Order

Sign out and sign in as `po.approver@demo.local`.

1. Open **My Tasks**.
2. Find the PO under Purchase Order Tasks.
3. Click **Details**.
4. Check Supplier, items, delivery information, and total.
5. Click **Approve purchase order**.
6. Optionally enter an approval comment and confirm.

Expected result:

- The PO workflow reaches the Approved terminal step.
- The business status becomes `Approved`.
- The task disappears from the PO Approver inbox.
- The PO is now ready for Procurement to issue.

Optional rejection test: create a separate PO, reject it with a required comment, and confirm it returns to Draft for Procurement to edit and resubmit.

## Step 11: Issue the Purchase Order and verify email

Sign out and sign in as `procurement@demo.local`.

1. Open **Purchase Orders**.
2. Find the Approved PO.
3. Click **Issue** and confirm.

Expected result:

- PO status becomes `Issued`.
- It can no longer be edited or deleted.
- One Email Record is created with status Pending.
- One Purchase Order PDF is saved with that Email Record.
- The email background worker later changes Pending to Sent or Failed.

### Verify Email Records

1. Open **Email Records**.
2. Search by the PO number.
3. Open the record.
4. Confirm:
   - From, To, CC, and BCC
   - Subject and rendered HTML body
   - Template code and version
   - Attempt count and timestamps
   - One PDF under Attachments
5. Click **View PDF**.
6. Confirm the PDF contains the correct Supplier, PO number, delivery address, items, prices, and total.
7. Click **Download PDF** and confirm the filename follows `Purchase-Order-PO-xxxx.pdf`.
8. If using Mailpit, open `http://localhost:8025` and verify the same message and attachment.

If sending fails, correct the SMTP configuration and click **Retry email**. Retry reuses the same Email Record and saved attachment. **Resend email** creates a new Email Record so the audit history remains clear.

## Step 12: Post a partial Goods Receipt

Sign out and sign in as `warehouse@demo.local`.

### 12.1 Check starting Inventory

1. Open **Inventory**.
2. Search for `Ergonomic Keyboard UI1001A`.

Expected result: quantity is 0 and status is Out of stock.

### 12.2 Create the first receipt

1. Open **Goods Receiving**.
2. Click **New goods receipt**.
3. Select the Issued PO.
4. Enter:
   - Supplier delivery note: `DN-1-UI1001A`
   - Received date: today
   - Notes: `First partial delivery`
   - Quantity received now: `6`
5. Click **Create draft**.

Expected result: the receipt is Draft, and Inventory is still 0. Saving a Draft never changes stock.

### 12.3 Post the first receipt

1. Find the Draft receipt.
2. Click **Post** and confirm.

Expected result:

- Receipt becomes Posted and cannot be edited or deleted.
- PO becomes Partially Received.
- Inventory becomes 6.
- Stock status becomes Healthy because 6 is above reorder level 3.

## Step 13: Receive the remaining quantity

1. Create another Goods Receipt for the same PO.
2. Confirm the form shows:
   - Ordered: 10
   - Previously received: 6
   - Remaining: 4
3. Enter delivery note `DN-2-UI1001A`.
4. Enter quantity `4`.
5. Create the Draft and then Post it.

Expected result:

- Second receipt becomes Posted.
- PO becomes Received.
- Inventory becomes 10.
- The PO no longer appears as available for another receipt.

### Over-receipt validation

Before posting the correct second receipt, try entering `5` when only `4` remains.

Expected result: the form or backend rejects the value. The system never allows total Posted quantities to exceed the ordered quantity.

## Step 14: Verify the Inventory ledger

1. Open **Inventory**.
2. Search for the test Product.
3. Click **View history**.

Expected rows:

| Movement | Reference | Before | Change | After |
| --- | --- | ---: | ---: | ---: |
| First Posted receipt | First `GR-xxxx` | 0 | +6 | 6 |
| Second Posted receipt | Second `GR-xxxx` | 6 | +4 | 10 |

Each row should show transaction type Goods Receipt, source reference, Warehouse Officer, and timestamp. Filtering by transaction type or date should retain the matching records.

## Step 15: Test role protection

Perform a few direct URL checks:

1. As Requester, open `/users` or `/inventory`.
2. As Procurement Officer, open `/users` or `/email-templates`.
3. As Warehouse Officer, open `/quotations`.
4. As Catalog Manager, open `/purchase-orders`.

Expected result: each unauthorized route opens the `403 Access Denied` page.

Then sign in as Admin and confirm all pages are visible. Remember that the API repeats its own authorization checks; route hiding alone is not the security boundary.

## Optional Email Template version test

Sign in as `admin@demo.local`.

1. Open **Email Templates**.
2. Find active `PURCHASE_ORDER_ISSUED`.
3. Click **New version**.
4. Change the subject, for example: `New PO {{PurchaseOrderNumber}} for {{SupplierName}}`.
5. Focus the Subject or HTML Body field and insert a supported placeholder.
6. Preview the rendered example.
7. Save the Draft.
8. Publish it.

Expected result:

- New version becomes Active.
- Previous version becomes Superseded.
- Existing Email Records keep their old template version and rendered snapshot.
- The next issued PO uses the new active version.

## Optional automated version of this test

The repository includes a PowerShell API smoke test covering the same main business flow:

```powershell
.\scripts\Test-EndToEnd.ps1 `
  -BaseUrl "http://localhost:5165" `
  -DemoPassword "Project1Demo123!"
```

A successful run ends with `Complete system smoke test passed`. The script creates unique records and intentionally keeps the completed audit history.

## Common problems

### A button is missing or disabled

Check the role and the previous record state:

- Quotation requires an Approved Purchase Request and eligible Supplier Product relationship.
- Purchase Order requires a Selected quotation.
- PO approval requires Pending Approval.
- Issue requires Approved and a valid Supplier email.
- Goods Receipt requires Issued or Partially Received with quantity remaining.
- A PO with an existing Draft receipt cannot create another Draft receipt.

### `401 Unauthorized`

Log in again. Development generates a temporary JWT signing key when the API starts, so restarting the backend invalidates existing browser sessions.

### `403 Access Denied`

The user is authenticated but does not have the required role. Use the correct Demo Account.

### Email remains Failed

Check SMTP host, port, SSL setting, username, password, sender address, and Supplier email. Gmail requires a Google App Password when using SMTP authentication.

### Database or API is not available

Confirm SQL Server is running, migrations are applied, the backend is at `http://localhost:5165`, and the frontend API base URL is correct.

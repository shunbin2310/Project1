# Project1 — Purchasing Management System

Vue.js · ASP.NET Core · SQL Server

Project1 follows a purchase from the first request to the final delivery. Users can
review requests, compare supplier prices, issue purchase orders, and track stock.
Each role has its own pages and actions. Workflow history records who took each
action and when.

## Try the project

[Open Live Demo](https://shunbin2310.github.io/Project1/)

The demo is available while the server is running. It is offline after midnight
(Malaysia Time, UTC+8).

To run it on your own computer, follow the [Local Docker Guide](docs/LOCAL_DOCKER.md).
It covers first-time setup, database migrations, daily startup, rebuilding, and
shutdown. Docker runs the website, API, SQL Server, and test email inbox together.

The local database is separate from the live demo. Its data and workflow settings
are not copied from the server.

## Sign in

The login page lists demo accounts. For local Docker, enter the account email and
your `DEMO_PASSWORD` from `.env.docker.local` manually. The **Use account** button
may use a different password.

| Account | What to do |
| --- | --- |
| `admin@demo.local` | Set up templates, or try the whole process with one account. |
| `procurement@demo.local` | Manage products and suppliers, compare quotations, and issue orders. |
| `requester@demo.local` | Create and submit a purchase request. |
| `finance@demo.local` | Approve or reject the request. |
| `warehouse@demo.local` | Receive goods and check stock. |

Admin can perform all the steps. To see how work passes between people, sign out
and switch to the account shown at each step.

## Demo approval flow

This walkthrough follows the **Version 2 templates shown in the demo screenshots**:

| Record | Approval path |
| --- | --- |
| Purchase Request (PR) | Draft → Finance Review → Approved or Rejected |
| Purchase Order (PO) | Draft → Approved when submitted |

Finance reviews the PR. Rejecting it requires a comment and ends that request.
The PO goes straight to Approved when its creator submits it; this template has
no separate PO approval step. The order must still be **issued** before goods
can be received.

### Set up the same flow locally

A new local database starts with the default templates: PR includes Department
Review before Finance Review, and PO includes a separate Pending Approval step.
To use the shorter flow above:

1. Sign in as Admin and open **Workflow Templates**.
2. Open the active `PURCHASE_REQUEST` template and check its steps. If they already
   match the table below, keep it. Otherwise, click **New version**, then **Edit**
   on the new draft.
3. Change the Draft `SUBMIT` action to move to `FINANCE_REVIEW`, then remove the
   `DEPARTMENT_REVIEW` step. Keep the Finance actions as shown below.
4. Click **Save draft**, then **Publish** and confirm.
5. Repeat for `PURCHASE_ORDER`: create a new version, edit Draft `SUBMIT` to move
   to `APPROVED`, and remove `PENDING_APPROVAL`. Save and publish it.

Use these action settings:

| Template | From | Action code | Moves to | Allowed actioner | Comment required |
| --- | --- | --- | --- | --- | --- |
| `PURCHASE_REQUEST` | `DRAFT` | `SUBMIT` | `FINANCE_REVIEW` | Requester | No |
| `PURCHASE_REQUEST` | `FINANCE_REVIEW` | `APPROVE` | `APPROVED` | Role: `FINANCE_APPROVER` | No |
| `PURCHASE_REQUEST` | `FINANCE_REVIEW` | `REJECT` | `REJECTED` | Role: `FINANCE_APPROVER` | Yes |
| `PURCHASE_ORDER` | `DRAFT` | `SUBMIT` | `APPROVED` | Requester | No |

Keep Draft as the Initial step. Approved and Rejected are Terminal steps with no
actions; the PO only needs Draft and Approved. Here, **Requester** means the user
who created that record, including the Procurement Officer who creates the PO.

Confirm both templates are Active and Published. Also open **Email Templates**
and check that `PURCHASE_ORDER_ISSUED` has an Active version.

Publish the templates **before creating your test PR or PO**. Each record keeps
the workflow copied when it was created. Publishing a new template does not
change existing records. The local version number may differ from the screenshots;
check the steps rather than the number.

## Walk through the whole process

Use a new product name each time, such as `Demo Keyboard 01`, so you can find your
records easily. This example buys 10 keyboards and receives them in two deliveries.

### 1. Set up the product and suppliers

Sign in as **Procurement Officer**.

- Open **Products**, click **New product**, and create `Demo Keyboard 01` with a
  default unit price of `120.00` and reorder level of `3`.
- Open **Suppliers** and add `Demo Alpha` and `Demo Beta`, each with a valid test
  email address. Reuse these suppliers if they already exist.
- Open **Supplier Products** and create a relationship between each supplier and
  the keyboard. These links make both suppliers eligible to quote for it.

### 2. Create a purchase request

Sign in as **Requester**. Open **Purchase Requests** → **New purchase request**.

Choose a future required date, enter a business reason, and add the keyboard with
quantity `10`. Its estimated price comes from the product. Click **Create and
submit**.

The request moves to **Finance Review**, with an estimated total of RM 1,200.00.

### 3. Review the request

Sign in as **Finance Approver**. Open **My Tasks**, find the request, and click
**Details** → **Approve finance review**. Confirm the action.

The request becomes **Approved** and can be used for supplier quotations.
To try rejection, use a separate request and enter a reason when rejecting it.

### 4. Record supplier quotations

Sign in as **Procurement Officer**. Open **Supplier Quotations** → **New quotation**.

Select the approved request and Demo Alpha. Enter a price of `115.00` per keyboard,
today's quotation date, and a future validity date. Click **Save and submit**.
Repeat for Demo Beta with a price of `108.00`.

The totals are RM 1,150.00 for Alpha and RM 1,080.00 for Beta. These are example
prices for this walkthrough.

### 5. Compare and select a quotation

On **Supplier Quotations**, click **Compare** for the request. Review both prices
and validity dates, then click **Select winner** for Demo Beta and confirm.

Beta becomes **Selected**. The user chooses the winner; the system does not
automatically choose the lowest price.

### 6. Create and submit a purchase order

Stay as **Procurement Officer**. Open **Purchase Orders** → **New purchase order**.

Select the winning Beta quotation. Enter the order date, a future delivery date,
and a delivery address. Check the copied items: 10 keyboards at RM 108.00 each,
for a total of RM 1,080.00. Click **Create and submit**.

With the template above, the PO becomes **Approved** immediately. No separate
Purchase Order Approver action is needed.

### 7. Issue the order and check the email

Find the approved PO, click **Issue**, and confirm. Its status becomes **Issued**.

Open **Email Records** and find the PO number. Check the supplier email, message,
and attached PO PDF. Email is sent in the background, so check whether the record
changes from Pending to Sent or Failed.

For local Docker, open [Mailpit](http://localhost:8026) to view the captured email
and attachment. This local setup captures test emails instead of sending them to
real recipients.

### 8. Receive the goods

Sign in as **Warehouse Officer**. Open **Goods Receiving** → **New goods receipt**.

Select the issued PO, enter the delivery note and received date, and record
quantity `6`. Click **Create draft**, then find the receipt and click **Post**.

Stock increases by 6 and the PO becomes **Partially Received**. Create and post a
second receipt for the remaining `4`. The PO becomes **Received**.

Saving a draft does not change stock. Posting does. Received quantities must be
whole numbers and cannot exceed the quantity still outstanding.

### 9. Check stock and history

Open **Inventory**, find the keyboard, and click **View history**.

For this new product, stock should now be `10`, with two receipt entries:
`+6` and `+4`. You can also open the PR and PO details to see their workflow
history, including the actor, action, time, and comments.

## More details

- [Local Docker Guide](docs/LOCAL_DOCKER.md) — setup commands and local service addresses.
- [UI Test Guide](docs/UI_TEST_GUIDE.md) — detailed checks using the default approval templates.
- [Architecture](docs/ARCHITECTURE.md) — how the application is organised.
- [Workflow Engine](docs/WORKFLOW_ENGINE.md) — how templates, versions, permissions, and history work.
- [Project Notes](docs/PROJECT_NOTES.md) — development setup and troubleshooting.

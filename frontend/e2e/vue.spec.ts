import { test, expect } from '@playwright/test'

const adminSession = {
  accessToken: 'e2e-test-token',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  user: {
    id: 4,
    email: 'admin@demo.local',
    fullName: 'Demo Admin',
    departmentId: 1,
    departmentCode: 'IT',
    departmentName: 'Information Technology',
    roles: ['ADMIN'],
  },
}

test('signs in with a demo account', async ({ page }) => {
  await page.route('http://localhost:5165/api/auth/login', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      headers: { 'Access-Control-Allow-Origin': 'http://localhost:4173' },
      body: JSON.stringify(adminSession),
    })
  })

  await page.goto('/login')
  await expect(page.locator('.demo-account')).toHaveCount(7)
  await expect(page.getByRole('button', { name: /Procurement Officer/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /Warehouse Officer/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /Catalog Manager/ })).toBeVisible()
  await page.getByRole('button', { name: /Demo Admin/ }).click()

  await expect(page).toHaveURL(/\/departments$/)
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Departments')
})

test.describe('authenticated administration workspace', () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript((session) => {
      window.sessionStorage.setItem('project1.auth.session', JSON.stringify(session))
    }, adminSession)
  })

  test('opens the department management page and create form', async ({ page }) => {
    await page.goto('/departments')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Departments')
    await expect(page.getByRole('link', { name: 'Departments' })).toBeVisible()

    await page.getByRole('button', { name: 'New department' }).click()

    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Create department' })).toBeVisible()
  })

  test('opens the user administration page and create form', async ({ page }) => {
    await page.route('http://localhost:5165/api/users?includeInactive=true', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })
    await page.route('http://localhost:5165/api/users/roles', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          'REQUESTER',
          'DEPARTMENT_APPROVER',
          'FINANCE_APPROVER',
          'PROCUREMENT_OFFICER',
          'WAREHOUSE_OFFICER',
          'CATALOG_MANAGER',
          'ADMIN',
        ]),
      })
    })
    await page.route('http://localhost:5165/api/departments', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })

    await page.goto('/users')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Users')
    await expect(page.getByRole('link', { name: 'Users' })).toBeVisible()
    await page.getByRole('button', { name: 'New user' }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Create user' })).toBeVisible()
    await expect(page.getByLabel('Temporary password')).toHaveCount(0)
    await expect(page.getByText('Procurement Officer', { exact: true })).toBeVisible()
    await expect(page.getByText('Warehouse Officer', { exact: true })).toBeVisible()
    await expect(page.getByText('Catalog Manager', { exact: true })).toBeVisible()
  })

  test('opens workflow template administration and its create form', async ({ page }) => {
    await page.route('http://localhost:5165/api/workflow-templates', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })
    await page.route('http://localhost:5165/api/users?includeInactive=true', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })

    await page.goto('/workflow-templates')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Workflow Templates')
    await expect(page.getByRole('link', { name: 'Workflow Templates' })).toBeVisible()
    await page.getByRole('button', { name: 'New template' }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Create workflow template' })).toBeVisible()
  })

  test('opens the supplier management page and create form', async ({ page }) => {
    await page.goto('/suppliers')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Suppliers')
    await expect(page.getByRole('link', { name: 'Suppliers' })).toBeVisible()

    await page.getByRole('button', { name: 'New supplier' }).click()

    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Create supplier' })).toBeVisible()
  })

  test('shows the common toast after creating a supplier', async ({ page }) => {
    const createdSupplier = {
      id: 7,
      code: 'SUP-0007',
      name: 'Example Supplies',
      contactPerson: null,
      email: null,
      phone: null,
      address: null,
      isActive: true,
      createdAtUtc: '2026-09-13T00:00:00Z',
      updatedAtUtc: null,
    }
    let suppliers: (typeof createdSupplier)[] = []

    await page.route('http://localhost:5165/api/suppliers*', async (route) => {
      if (route.request().method() === 'POST') {
        suppliers = [createdSupplier]
        await route.fulfill({
          status: 201,
          contentType: 'application/json',
          body: JSON.stringify(createdSupplier),
        })
        return
      }

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(suppliers),
      })
    })

    await page.goto('/suppliers')
    await page.getByRole('button', { name: 'New supplier' }).click()

    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Supplier name').fill('Example Supplies')
    await dialog.getByRole('button', { name: 'Create supplier' }).click()

    await expect(dialog).toBeHidden()
    const toast = page.getByRole('status')
    await expect(toast).toContainText('Action completed')
    await expect(toast).toContainText('SUP-0007 was created successfully.')
  })

  test('opens supplier product administration and its create form', async ({ page }) => {
    await page.route(
      'http://localhost:5165/api/supplier-products?includeInactive=true',
      async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
      },
    )
    await page.route('http://localhost:5165/api/suppliers?includeInactive=true', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })
    await page.route('http://localhost:5165/api/products?includeInactive=true', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })

    await page.goto('/supplier-products')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Supplier Products')
    await expect(page.getByRole('link', { name: 'Supplier Products' })).toBeVisible()
    await page.getByRole('button', { name: 'New relationship' }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'New supplier product' })).toBeVisible()
  })

  test('opens the product category management page and create form', async ({ page }) => {
    await page.goto('/product-categories')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Product Categories')
    await expect(page.getByRole('link', { name: 'Product Categories' })).toBeVisible()

    await page.getByRole('button', { name: 'New category' }).click()

    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Create product category' })).toBeVisible()
  })

  test('opens the units of measure management page and create form', async ({ page }) => {
    await page.goto('/units-of-measure')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Units of Measure')
    await expect(page.getByRole('link', { name: 'Units of Measure' })).toBeVisible()

    await page.getByRole('button', { name: 'New unit' }).click()

    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Create unit of measure' })).toBeVisible()
  })

  test('opens the product management page and create form', async ({ page }) => {
    await page.goto('/products')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Products')
    await expect(page.getByRole('link', { name: 'Products', exact: true })).toBeVisible()

    await page.getByRole('button', { name: 'New product' }).click()

    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Create product' })).toBeVisible()
  })

  test('opens the purchase request page and create form', async ({ page }) => {
    await page.goto('/purchase-requests')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Purchase Requests')
    await expect(page.getByRole('link', { name: 'Purchase Requests' })).toBeVisible()

    await page.getByRole('button', { name: 'New purchase request' }).click()

    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Create purchase request' })).toBeVisible()
  })

  test('opens supplier quotations and loads eligible suppliers into the create form', async ({
    page,
  }) => {
    await page.route('http://localhost:5165/api/quotations', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })
    await page.route(
      'http://localhost:5165/api/purchase-requests?stepCode=APPROVED',
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([
            {
              id: 7,
              requestNumber: 'PR-0007',
              requesterName: 'Demo Requester',
              departmentId: 1,
              departmentCode: 'IT',
              departmentName: 'Information Technology',
              requiredDate: '2026-10-01',
              justification: 'New workstation',
              estimatedTotal: 2400,
              createdAtUtc: '2026-09-01T00:00:00Z',
              updatedAtUtc: null,
              items: [
                {
                  id: 11,
                  productId: 1,
                  productCode: 'ITEM-0001',
                  productName: 'Monitor',
                  unitOfMeasureCode: 'UNIT',
                  quantity: 2,
                  estimatedUnitPrice: 1200,
                  lineTotal: 2400,
                },
              ],
              workflow: {
                id: 7,
                templateCode: 'PURCHASE_REQUEST',
                templateName: 'Purchase Request Approval',
                templateVersion: 1,
                entityType: 'PurchaseRequest',
                entityId: 7,
                status: 'Completed',
                currentStepCode: 'APPROVED',
                currentStepName: 'Approved',
                startedAtUtc: '2026-09-01T00:00:00Z',
                completedAtUtc: '2026-09-02T00:00:00Z',
                availableActions: [],
                history: [],
              },
            },
          ]),
        })
      },
    )
    await page.route('http://localhost:5165/api/suppliers', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 1,
            code: 'SUP-0001',
            name: 'Example Supplies',
            isActive: true,
            createdAtUtc: '2026-09-01T00:00:00Z',
          },
        ]),
      })
    })
    await page.route('http://localhost:5165/api/supplier-products', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 1,
            supplierId: 1,
            supplierCode: 'SUP-0001',
            supplierName: 'Example Supplies',
            productId: 1,
            productCode: 'ITEM-0001',
            productName: 'Monitor',
            unitOfMeasureCode: 'UNIT',
            unitOfMeasureName: 'Unit',
            productDefaultUnitPrice: 1200,
            isPreferred: true,
            isActive: true,
            createdAtUtc: '2026-09-01T00:00:00Z',
          },
        ]),
      })
    })

    await page.goto('/quotations')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Supplier Quotations')
    await expect(page.getByRole('link', { name: 'Supplier Quotations' })).toBeVisible()
    await page.getByRole('button', { name: 'New quotation' }).click()
    await expect(page.getByRole('heading', { name: 'Create supplier quotation' })).toBeVisible()
    await expect(page.getByLabel('Eligible supplier')).toContainText('Example Supplies')
    await expect(page.getByText('Monitor', { exact: true })).toBeVisible()
  })

  test('opens purchase orders and previews a selected quotation in the create form', async ({
    page,
  }) => {
    await page.route('http://localhost:5165/api/purchase-orders', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })
    await page.route(
      'http://localhost:5165/api/quotations?status=Selected',
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([
            {
              id: 8,
              quotationNumber: 'QT-0008',
              purchaseRequestId: 4,
              purchaseRequestNumber: 'PR-0004',
              supplierId: 2,
              supplierCode: 'SUP-0002',
              supplierName: 'Office Supply Co',
              supplierQuotationReference: 'REF-08',
              quotationDate: '2026-09-15',
              validUntil: '2030-10-15',
              notes: null,
              status: 'Selected',
              totalAmount: 1500,
              createdByUserId: 4,
              createdByName: 'Demo Admin',
              createdAtUtc: '2026-09-15T00:00:00Z',
              updatedAtUtc: null,
              submittedAtUtc: '2026-09-15T01:00:00Z',
              selectedAtUtc: '2026-09-15T02:00:00Z',
              items: [
                {
                  id: 12,
                  purchaseRequestItemId: 9,
                  productId: 3,
                  productCode: 'ITEM-0003',
                  productName: 'Office Chair',
                  unitOfMeasureCode: 'UNIT',
                  quantity: 3,
                  unitPrice: 500,
                  lineTotal: 1500,
                },
              ],
            },
          ]),
        })
      },
    )

    await page.goto('/purchase-orders')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Purchase Orders')
    await expect(page.getByRole('link', { name: 'Purchase Orders' })).toBeVisible()
    await page.getByRole('button', { name: 'New purchase order' }).click()
    await expect(page.getByRole('heading', { name: 'Create purchase order' })).toBeVisible()
    await expect(page.getByLabel('Selected supplier quotation')).toContainText('Office Supply Co')
    await expect(page.getByText('Office Chair', { exact: true })).toBeVisible()
    await expect(page.getByText('RM 1,500.00', { exact: true }).last()).toBeVisible()
  })

  test('opens goods receiving and loads an issued purchase order into the create form', async ({
    page,
  }) => {
    await page.route('http://localhost:5165/api/goods-receipts', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })
    await page.route('http://localhost:5165/api/purchase-orders', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 5,
            purchaseOrderNumber: 'PO-0005',
            quotationId: 8,
            quotationNumber: 'QT-0008',
            purchaseRequestId: 4,
            purchaseRequestNumber: 'PR-0004',
            supplierId: 2,
            supplierCode: 'SUP-0002',
            supplierName: 'Office Supply Co',
            supplierQuotationReference: 'REF-08',
            orderDate: '2026-09-15',
            expectedDeliveryDate: '2026-09-30',
            deliveryAddress: 'Main warehouse',
            notes: null,
            status: 'Issued',
            totalAmount: 1500,
            createdByUserId: 4,
            createdByName: 'Demo Admin',
            createdAtUtc: '2026-09-15T00:00:00Z',
            updatedAtUtc: null,
            issuedAtUtc: '2026-09-15T01:00:00Z',
            issuedByUserId: 4,
            issuedByName: 'Demo Admin',
            cancelledAtUtc: null,
            cancelledByUserId: null,
            cancelledByName: null,
            cancellationReason: null,
            emailDelivery: null,
            items: [
              {
                id: 2,
                quotationItemId: 12,
                productId: 3,
                productCode: 'ITEM-0003',
                productName: 'Office Chair',
                unitOfMeasureCode: 'UNIT',
                quantity: 10,
                unitPrice: 150,
                lineTotal: 1500,
              },
            ],
          },
        ]),
      })
    })

    await page.goto('/goods-receipts')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Goods Receiving')
    await expect(page.getByRole('link', { name: 'Goods Receiving' })).toBeVisible()
    await page.getByRole('button', { name: 'New goods receipt' }).click()
    await expect(page.getByRole('heading', { name: 'Create goods receipt' })).toBeVisible()
    await expect(page.getByLabel('Open purchase order')).toContainText('PO-0005')
    await expect(page.getByText('Office Chair', { exact: true })).toBeVisible()
    await expect(page.getByText('10', { exact: true }).first()).toBeVisible()
  })

  test('keeps an overflowing desktop navigation inside the sidebar', async ({ page }) => {
    await page.setViewportSize({ width: 1366, height: 560 })
    await page.route('http://localhost:5165/api/quotations', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })
    await page.route(
      'http://localhost:5165/api/purchase-requests?stepCode=APPROVED',
      async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
      },
    )
    await page.route('http://localhost:5165/api/suppliers', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })
    await page.route('http://localhost:5165/api/supplier-products', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })

    await page.goto('/quotations')

    const sidebar = page.locator('.sidebar')
    await expect(sidebar).toBeVisible()
    const dimensions = await sidebar.evaluate((element) => ({
      clientHeight: element.clientHeight,
      scrollHeight: element.scrollHeight,
      overflowY: getComputedStyle(element).overflowY,
    }))

    expect(dimensions.overflowY).toBe('auto')
    expect(dimensions.scrollHeight).toBeGreaterThan(dimensions.clientHeight)

    await sidebar.evaluate((element) => {
      element.scrollTop = element.scrollHeight
    })
    const footer = page.locator('.sidebar-footer')
    await expect(footer).toBeInViewport()

    const [sidebarBox, footerBox] = await Promise.all([sidebar.boundingBox(), footer.boundingBox()])
    expect(sidebarBox).not.toBeNull()
    expect(footerBox).not.toBeNull()
    expect(footerBox!.y + footerBox!.height).toBeLessThanOrEqual(
      sidebarBox!.y + sidebarBox!.height + 1,
    )
  })

  test('opens the My Tasks workflow inbox', async ({ page }) => {
    await page.route('http://localhost:5165/api/purchase-requests', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        headers: { 'Access-Control-Allow-Origin': 'http://localhost:4173' },
        body: '[]',
      })
    })
    await page.route('http://localhost:5165/api/products?includeInactive=true', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        headers: { 'Access-Control-Allow-Origin': 'http://localhost:4173' },
        body: '[]',
      })
    })

    await page.goto('/my-tasks')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('My Tasks')
    await expect(page.getByRole('link', { name: 'My Tasks' })).toBeVisible()
    await expect(page.getByText("You're all caught up")).toBeVisible()
  })

  test('opens inventory and reviews a product stock ledger', async ({ page }) => {
    await page.route('http://localhost:5165/api/inventory?includeInactive=true', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            productId: 3,
            productCode: 'ITEM-0003',
            productName: 'Office Chair',
            productCategoryId: 2,
            productCategoryCode: 'FURNITURE',
            productCategoryName: 'Furniture',
            unitOfMeasureCode: 'UNIT',
            unitOfMeasureName: 'Unit',
            quantityOnHand: 4,
            reorderLevel: 2,
            isLowStock: false,
            isProductActive: true,
            lastUpdatedAtUtc: '2026-09-20T10:30:00Z',
          },
        ]),
      })
    })
    await page.route('http://localhost:5165/api/inventory/3/transactions', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 11,
            productId: 3,
            productCode: 'ITEM-0003',
            productName: 'Office Chair',
            unitOfMeasureCode: 'UNIT',
            type: 'GoodsReceipt',
            quantityChange: 4,
            quantityBefore: 0,
            quantityAfter: 4,
            referenceType: 'GoodsReceipt',
            referenceId: 9,
            referenceNumber: 'GR-0009',
            goodsReceiptItemId: 14,
            performedByUserId: 4,
            performedByName: 'Demo Admin',
            occurredAtUtc: '2026-09-20T10:30:00Z',
          },
        ]),
      })
    })

    await page.goto('/inventory')

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Inventory')
    await expect(page.getByRole('link', { name: 'Inventory' })).toBeVisible()
    await expect(page.getByText('Office Chair', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'View history' }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await expect(page.getByRole('dialog').getByText('GR-0009')).toBeVisible()
    await expect(page.getByRole('dialog').getByText('+4')).toBeVisible()
  })
})

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
        body: JSON.stringify(['REQUESTER', 'DEPARTMENT_APPROVER', 'FINANCE_APPROVER', 'ADMIN']),
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

  test('opens supplier product administration and its create form', async ({ page }) => {
    await page.route('http://localhost:5165/api/supplier-products?includeInactive=true', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: '[]' })
    })
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
})

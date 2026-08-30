import { test, expect } from '@playwright/test'

test('opens the department management page and create form', async ({ page }) => {
  await page.goto('/departments')

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Departments')
  await expect(page.getByRole('link', { name: 'Departments' })).toBeVisible()

  await page.getByRole('button', { name: 'New department' }).click()

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Create department' })).toBeVisible()
})

test('opens the supplier management page and create form', async ({ page }) => {
  await page.goto('/suppliers')

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Suppliers')
  await expect(page.getByRole('link', { name: 'Suppliers' })).toBeVisible()

  await page.getByRole('button', { name: 'New supplier' }).click()

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Create supplier' })).toBeVisible()
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
  await expect(page.getByRole('link', { name: 'Products' })).toBeVisible()

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

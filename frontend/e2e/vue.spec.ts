import { test, expect } from '@playwright/test'

test('opens the department management page and create form', async ({ page }) => {
  await page.goto('/departments')

  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Departments')
  await expect(page.getByRole('link', { name: 'Departments' })).toBeVisible()

  await page.getByRole('button', { name: 'New department' }).click()

  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Create department' })).toBeVisible()
})

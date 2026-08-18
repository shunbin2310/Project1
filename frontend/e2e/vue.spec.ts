import { test, expect } from '@playwright/test'

test('shows the application title', async ({ page }) => {
  await page.goto('/')
  await expect(page.locator('h1')).toHaveText('Purchase & Inventory Management System')
})

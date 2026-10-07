import { describe, expect, it } from 'vitest'

// Temporary exercise: verify that a failed required CI check blocks merging into main.
// Delete this file after observing the blocked PR. Do not merge this failing test.
describe('Temporary branch protection demonstration', () => {
  it('fails intentionally to demonstrate a blocked merge', () => {
    expect(1).toBe(2)
  })
})

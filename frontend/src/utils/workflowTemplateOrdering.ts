import type { WorkflowTemplateStep } from '@/types/workflowTemplate'

function normalizedCode(value: string) {
  return value.trim().toUpperCase()
}

function compareSteps(left: WorkflowTemplateStep, right: WorkflowTemplateStep) {
  if (left.isInitial !== right.isInitial) return left.isInitial ? -1 : 1
  if (left.displayOrder !== right.displayOrder) return left.displayOrder - right.displayOrder
  return left.code.localeCompare(right.code)
}

export function orderWorkflowSteps(steps: readonly WorkflowTemplateStep[]): WorkflowTemplateStep[] {
  const stepByCode = new Map(steps.map((step) => [normalizedCode(step.code), step]))
  const outgoingCodes = new Map<string, Set<string>>()
  const incomingCount = new Map(steps.map((step) => [normalizedCode(step.code), 0]))

  for (const step of steps) {
    const fromCode = normalizedCode(step.code)
    const targets = outgoingCodes.get(fromCode) ?? new Set<string>()

    for (const action of step.actions) {
      const targetCode = normalizedCode(action.toStepCode)
      if (!stepByCode.has(targetCode) || targetCode === fromCode || targets.has(targetCode)) {
        continue
      }

      targets.add(targetCode)
      incomingCount.set(targetCode, (incomingCount.get(targetCode) ?? 0) + 1)
    }

    outgoingCodes.set(fromCode, targets)
  }

  const available = steps
    .filter((step) => incomingCount.get(normalizedCode(step.code)) === 0)
    .sort(compareSteps)
  const ordered: WorkflowTemplateStep[] = []
  const visitedCodes = new Set<string>()

  while (available.length > 0) {
    const step = available.shift()
    if (!step) break

    const stepCode = normalizedCode(step.code)
    if (visitedCodes.has(stepCode)) continue

    visitedCodes.add(stepCode)
    ordered.push(step)

    for (const targetCode of outgoingCodes.get(stepCode) ?? []) {
      const nextIncomingCount = (incomingCount.get(targetCode) ?? 0) - 1
      incomingCount.set(targetCode, nextIncomingCount)

      if (nextIncomingCount === 0) {
        const target = stepByCode.get(targetCode)
        if (target) {
          available.push(target)
          available.sort(compareSteps)
        }
      }
    }
  }

  const remaining = steps
    .filter((step) => !visitedCodes.has(normalizedCode(step.code)))
    .sort(compareSteps)

  return [...ordered, ...remaining]
}

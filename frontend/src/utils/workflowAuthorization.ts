import type { WorkflowActorIdentity, WorkflowAvailableAction } from '@/types/purchaseRequest'

export function isWorkflowActionAuthorized(
  action: WorkflowAvailableAction,
  actor: WorkflowActorIdentity,
) {
  const roles = new Set(actor.roles.map((role) => role.toLowerCase()))
  const actorName = actor.name.toLowerCase()
  const actorId = String(actor.id)

  return action.actioners.some((actioner) => {
    if (actioner.actionerType === 'Role') {
      return roles.has(actioner.actionerKey.toLowerCase())
    }

    return actioner.actionerKey === actorId || actioner.actionerKey.toLowerCase() === actorName
  })
}

export function isWorkflowActionDirectlyAssignedToActor(
  action: WorkflowAvailableAction,
  actor: WorkflowActorIdentity,
) {
  const actorName = actor.name.toLowerCase()
  const actorId = String(actor.id)

  return action.actioners.some(
    (actioner) =>
      actioner.actionerType !== 'Role' &&
      (actioner.actionerKey === actorId || actioner.actionerKey.toLowerCase() === actorName),
  )
}

export function getAuthorizedWorkflowActions(
  actions: WorkflowAvailableAction[],
  actor: WorkflowActorIdentity,
) {
  return actions.filter((action) => isWorkflowActionAuthorized(action, actor))
}

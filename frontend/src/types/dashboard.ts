export type DashboardTone = 'neutral' | 'positive' | 'warning' | 'danger'
export type DashboardSeverity = 'info' | 'warning' | 'critical'

export interface DashboardSummaryCard {
  key: string
  label: string
  value: number
  description: string
  route: string
  tone: DashboardTone
}

export interface DashboardReminder {
  key: string
  title: string
  description: string
  count: number
  route: string
  severity: DashboardSeverity
}

export interface DashboardActivity {
  key: string
  module: string
  reference: string
  title: string
  description: string
  actorName: string
  occurredAtUtc: string
  route: string
}

export interface Dashboard {
  summaryCards: DashboardSummaryCard[]
  reminders: DashboardReminder[]
  recentActivity: DashboardActivity[]
  generatedAtUtc: string
}

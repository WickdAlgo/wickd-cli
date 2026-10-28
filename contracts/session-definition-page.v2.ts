// Generated from canonical Wickd.TradeJournal v2 records. DO NOT EDIT.
import { z } from "zod";

const utcInstant = z.string().refine((s) => Number.isFinite(Date.parse(s)), {
  message: "must be a parseable ISO 8601 instant",
});

export const sessionWeeklyWindow = z.object({
  isoWeekday: z.number().int().min(1),
  startMinute: z.number().int().nonnegative(),
  endMinute: z.number().int().min(1),
});
export type SessionWeeklyWindow = z.infer<typeof sessionWeeklyWindow>;

export const sessionDefinitionRequest = z.object({
  name: z.string().min(1),
  timezone: z.string().min(1),
  weeklySchedule: z.array(sessionWeeklyWindow).min(1),
  holidayCalendarId: z.string().min(1).nullable(),
  holidayCalendarVersion: z.string().min(1).nullable(),
});
export type SessionDefinitionRequest = z.infer<typeof sessionDefinitionRequest>;

export const sessionDefinitionVersion = z.object({
  sessionId: z.string().min(1),
  versionId: z.string().min(1),
  versionNumber: z.number().int(),
  definition: sessionDefinitionRequest,
  contentHash: z.string().min(1),
  publicationSequence: z.number().int(),
  knownAtUtc: utcInstant,
});
export type SessionDefinitionVersion = z.infer<typeof sessionDefinitionVersion>;

export const sessionDefinitionPage = z.object({
  items: z.array(sessionDefinitionVersion),
  nextCursor: z.string().min(1).nullable(),
  publicationWatermark: z.number().int(),
});
export type SessionDefinitionPage = z.infer<typeof sessionDefinitionPage>;

export const sessionDefinitionPagePayload = z.object({
  contract: z.literal("session-definition-page"),
  schemaVersion: z.literal(2),
  requestId: z.string().min(1),
  asOf: utcInstant,
  data: sessionDefinitionPage,
});
export type SessionDefinitionPagePayload = z.infer<typeof sessionDefinitionPagePayload>;

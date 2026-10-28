// Generated from canonical Wickd.TradeJournal v2 records. DO NOT EDIT.
import { z } from "zod";

const utcInstant = z.string().refine((s) => Number.isFinite(Date.parse(s)), {
  message: "must be a parseable ISO 8601 instant",
});

export const strategyScopeAxis = z.object({
  classification: z.enum(["unclassified", "unrestricted", "selected"]),
  ids: z.array(z.string().min(1)),
});
export type StrategyScopeAxis = z.infer<typeof strategyScopeAxis>;

export const strategyVersionScope = z.object({
  versionId: z.string().min(1),
  strategyId: z.string().min(1),
  versionNumber: z.number().int(),
  state: z.string().min(1),
  versionRevision: z.number().int(),
  provenance: z.string().min(1),
  scopeRevision: z.number().int().nullable(),
  recordedPublicationSequence: z.number().int().nullable(),
  recordedAtUtc: utcInstant.nullable(),
  scopeContentHash: z.string().min(1).nullable(),
  instruments: strategyScopeAxis,
  timeframes: strategyScopeAxis,
});
export type StrategyVersionScope = z.infer<typeof strategyVersionScope>;

export const strategyScopeListPage = z.object({
  items: z.array(strategyVersionScope),
  nextCursor: z.string().min(1).nullable(),
  publicationWatermark: z.number().int(),
});
export type StrategyScopeListPage = z.infer<typeof strategyScopeListPage>;

export const strategyScopeListPagePayload = z.object({
  contract: z.literal("strategy-scope-list-page"),
  schemaVersion: z.literal(2),
  requestId: z.string().min(1),
  asOf: utcInstant,
  data: strategyScopeListPage,
});
export type StrategyScopeListPagePayload = z.infer<typeof strategyScopeListPagePayload>;

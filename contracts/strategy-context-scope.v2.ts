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

export const strategyContextScope = z.object({
  versionId: z.string().min(1),
  versionRevision: z.number().int(),
  provenance: z.string().min(1),
  revision: z.number().int().nullable(),
  contentHash: z.string().min(1).nullable(),
  publicationSequence: z.number().int().nullable(),
  knownAtUtc: utcInstant.nullable(),
  sessions: strategyScopeAxis,
  marketConditions: strategyScopeAxis,
  snapshotCursor: z.string().min(1),
});
export type StrategyContextScope = z.infer<typeof strategyContextScope>;

export const strategyContextScopePayload = z.object({
  contract: z.literal("strategy-context-scope"),
  schemaVersion: z.literal(2),
  requestId: z.string().min(1),
  asOf: utcInstant,
  data: strategyContextScope,
});
export type StrategyContextScopePayload = z.infer<typeof strategyContextScopePayload>;

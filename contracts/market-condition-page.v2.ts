// Generated from canonical Wickd.TradeJournal v2 records. DO NOT EDIT.
import { z } from "zod";

const utcInstant = z.string().refine((s) => Number.isFinite(Date.parse(s)), {
  message: "must be a parseable ISO 8601 instant",
});

export const marketConditionRevision = z.object({
  tagId: z.string().min(1),
  revisionId: z.string().min(1),
  revisionNumber: z.number().int(),
  label: z.string().min(1),
  archived: z.boolean(),
  contentHash: z.string().min(1),
  publicationSequence: z.number().int(),
  knownAtUtc: utcInstant,
});
export type MarketConditionRevision = z.infer<typeof marketConditionRevision>;

export const marketConditionPage = z.object({
  items: z.array(marketConditionRevision),
  nextCursor: z.string().min(1).nullable(),
  publicationWatermark: z.number().int(),
});
export type MarketConditionPage = z.infer<typeof marketConditionPage>;

export const marketConditionPagePayload = z.object({
  contract: z.literal("market-condition-page"),
  schemaVersion: z.literal(2),
  requestId: z.string().min(1),
  asOf: utcInstant,
  data: marketConditionPage,
});
export type MarketConditionPagePayload = z.infer<typeof marketConditionPagePayload>;

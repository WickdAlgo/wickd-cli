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

export const marketConditionRevisionPayload = z.object({
  contract: z.literal("market-condition-revision"),
  schemaVersion: z.literal(2),
  requestId: z.string().min(1),
  asOf: utcInstant,
  data: marketConditionRevision,
});
export type MarketConditionRevisionPayload = z.infer<typeof marketConditionRevisionPayload>;

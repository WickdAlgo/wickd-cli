// Generated from canonical Wickd.TradeJournal v2 records. DO NOT EDIT.
import { z } from "zod";

const utcInstant = z.string().refine((s) => Number.isFinite(Date.parse(s)), {
  message: "must be a parseable ISO 8601 instant",
});

export const marketConditionRequest = z.object({
  label: z.string().min(1),
  archived: z.boolean(),
});
export type MarketConditionRequest = z.infer<typeof marketConditionRequest>;

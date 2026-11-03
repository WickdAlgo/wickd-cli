// Generated from canonical Wickd.TradeJournal v2 records. DO NOT EDIT.
import { z } from "zod";

const utcInstant = z.string().refine((s) => Number.isFinite(Date.parse(s)), {
  message: "must be a parseable ISO 8601 instant",
});

export const setupContextRequest = z.object({
  source: z.enum(["manual", "humanReview"]),
  effectiveAtUtc: utcInstant,
  sessionVersionIds: z.array(z.string().min(1)),
  marketConditionRevisionIds: z.array(z.string().min(1)),
});
export type SetupContextRequest = z.infer<typeof setupContextRequest>;

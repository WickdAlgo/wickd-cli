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

export const strategyContextScopeRequest = z.object({
  sessions: strategyScopeAxis,
  marketConditions: strategyScopeAxis,
});
export type StrategyContextScopeRequest = z.infer<typeof strategyContextScopeRequest>;

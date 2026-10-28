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

export const strategyScopeUpdateRequest = z.object({
  instruments: strategyScopeAxis,
  timeframes: strategyScopeAxis,
});
export type StrategyScopeUpdateRequest = z.infer<typeof strategyScopeUpdateRequest>;

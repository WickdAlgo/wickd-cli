// Generated from canonical Wickd.TradeJournal v2 records. DO NOT EDIT.
import { z } from "zod";

const utcInstant = z.string().refine((s) => Number.isFinite(Date.parse(s)), {
  message: "must be a parseable ISO 8601 instant",
});

export const setupContextObservation = z.object({
  observationId: z.string().min(1),
  setupId: z.string().min(1),
  revision: z.number().int(),
  source: z.enum(["manual", "humanReview"]),
  effectiveAtUtc: utcInstant,
  sessionVersionIds: z.array(z.string().min(1)),
  marketConditionRevisionIds: z.array(z.string().min(1)),
  contentHash: z.string().min(1),
  publicationSequence: z.number().int(),
  knownAtUtc: utcInstant,
});
export type SetupContextObservation = z.infer<typeof setupContextObservation>;

export const setupContextObservationPage = z.object({
  items: z.array(setupContextObservation),
  nextCursor: z.string().min(1).nullable(),
  publicationWatermark: z.number().int(),
});
export type SetupContextObservationPage = z.infer<typeof setupContextObservationPage>;

export const setupContextObservationPagePayload = z.object({
  contract: z.literal("setup-context-observation-page"),
  schemaVersion: z.literal(2),
  requestId: z.string().min(1),
  asOf: utcInstant,
  data: setupContextObservationPage,
});
export type SetupContextObservationPagePayload = z.infer<typeof setupContextObservationPagePayload>;

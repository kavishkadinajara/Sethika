import type { NeedCounts, NeedListItem } from "./types";

/*  A need's figures (plan 09, D.2). Pure, so unit-tested in node. */

/** What may still be pledged: everything not yet spoken for (M29). */
export function needLeft(need: NeedCounts): number {
  return Math.max(0, round(need.quantityNeeded - need.quantityPledged));
}

/** What open pledges still promise, beyond what has arrived. */
export function needPromised(need: NeedCounts): number {
  return Math.max(0, round(need.quantityPledged - need.quantityReceived));
}

/** The progress bar's two parts, as percentages of what is needed: arrived, and promised on top of it. */
export function needBar(need: NeedCounts): { received: number; promised: number } {
  if (need.quantityNeeded <= 0) return { received: 0, promised: 0 };
  const received = Math.min(100, (need.quantityReceived / need.quantityNeeded) * 100);
  const promised = Math.min(100 - received, (needPromised(need) / need.quantityNeeded) * 100);
  return { received, promised };
}

/** The steps the need's status allows; the API judges them again with the live numbers. */
export function needSteps(need: Pick<NeedListItem, "status" | "quantityPledged" | "quantityReceived">) {
  const editable = need.status === "Draft" || need.status === "Open";
  const settled = needPromised({ quantityNeeded: 0, ...need }) === 0;
  return {
    edit: editable,
    open: need.status === "Draft",
    close: need.status === "Open" && settled,
    cancel: editable && settled && need.quantityReceived === 0,
  };
}

/** Quantities are in hundredths: this keeps binary fractions out of what is shown. */
function round(value: number): number {
  return Math.round(value * 100) / 100;
}

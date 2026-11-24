/*  Online giving as the API answers it (PaymentEndpoints.cs, plan 09), the twin of
    web/lib/giving/types.ts. The merchant's secret never reaches the app: the API signs the form.  */

/** A gift as the give sheet sends it (M8). Email, address and city are PayHere's; Sethika does not keep them (M33). */
export type GiveInput = { amount: number; need: string | null; email: string; address: string; city: string };

/** PayHere's checkout form, signed by the API (M8): posted to `action` with `fields`. */
export type CheckoutForm = { orderId: string; action: string; fields: Record<string, string> };

export type PaymentStatus = "Pending" | "Paid" | "Failed" | "Cancelled";

/** One of the donor's own orders, as PayHere's signed notification left it (M9). */
export type MyPayment = {
  orderId: string;
  amount: number;
  currencyCode: string;
  status: PaymentStatus;
  institutionName: string;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  institutionSlug: string;
  receiptPublicId: string | null;
  receiptNo: string | null;
};

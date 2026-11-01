/*  The religious-service API's shapes (DonorAppointmentEndpoints.cs, InstitutionAppointmentEndpoints.cs,
    ServiceSetupEndpoints.cs), as JSON sends them. A request sends a Colombo date ("yyyy-MM-dd") and
    start time ("HH:mm"); everything that comes back carries UTC instants (V28), which lib/services/time.ts
    turns into Colombo wall time. Ids are public ones: a type's or leader's publicId, an appointment's
    appointmentNo.                                                                                   */

import type { Named } from "@/lib/bookings/types";

export type AppointmentStatus = "Requested" | "Approved" | "Rejected" | "Cancelled" | "Completed";

/** Where a service happens. `Either` is a type's offer, never an appointment's place. */
export type ServiceLocation = "AtInstitution" | "AtDonorPlace" | "Either";

/** A leader's answer to an assignment. */
export type AssignmentResponse = "Pending" | "Accepted" | "Declined" | "Removed";

/** A service on an institution's public page (V25). */
export type PublicServiceType = Named & {
  publicId: string;
  description: string | null;
  durationMinutes: number;
  locationMode: ServiceLocation;
  minAdvanceDays: number;
  maxAdvanceDays: number;
};

export type ServiceRequest = {
  serviceType: string;
  date: string;
  startTime: string;
  location: Exclude<ServiceLocation, "Either">;
  address: string | null;
  occasion: string | null;
  note: string | null;
};

/** One of the signed-in donor's requests. */
export type DonorAppointment = {
  appointmentNo: string;
  status: AppointmentStatus;
  /** UTC. */
  startsAt: string;
  endsAt: string;
  institution: string;
  institutionName: string;
  institutionNameSi: string | null;
  institutionNameTa: string | null;
  serviceType: string;
  serviceName: string;
  serviceNameSi: string | null;
  serviceNameTa: string | null;
  locationMode: Exclude<ServiceLocation, "Either">;
  address: string | null;
  occasion: string | null;
  note: string | null;
  createdAt: string;
  rejectReason: string | null;
  cancelReason: string | null;
  canCancel: boolean;
};

// ---- the institution ------------------------------------------------------------------------------

/** A service as the institution's services screen shows and edits it. */
export type ServiceTypeView = Named & {
  publicId: string;
  description: string | null;
  durationMinutes: number;
  bufferMinutes: number;
  leadersRequired: number;
  locationMode: ServiceLocation;
  minAdvanceDays: number;
  maxAdvanceDays: number;
  isActive: boolean;
  /** The RowVersion an edit sends back (base64). */
  version: string;
};

export type ServiceTypeInput = {
  name: string;
  nameSi: string | null;
  nameTa: string | null;
  description: string | null;
  durationMinutes: number | null;
  bufferMinutes: number | null;
  leadersRequired: number | null;
  locationMode: ServiceLocation;
  minAdvanceDays: number | null;
  maxAdvanceDays: number | null;
  version?: string;
};

/** One span of a leader's usual week; 0 is Sunday. */
export type WeeklyHours = { dayOfWeek: number; startTime: string; endTime: string };

export type LeaderLanguage = "en" | "si" | "ta";

export type Leader = {
  publicId: string;
  displayName: string;
  nameSi: string | null;
  nameTa: string | null;
  title: string | null;
  phone: string | null;
  language: LeaderLanguage;
  hasAccount: boolean;
  isActive: boolean;
  version: string;
  weeklyHours: WeeklyHours[];
};

export type LeaderInput = {
  displayName: string;
  nameSi: string | null;
  nameTa: string | null;
  title: string | null;
  phone: string | null;
  language: LeaderLanguage;
  linkAccount: boolean;
  version?: string;
};

/** Leave not yet over; instants in UTC. */
export type TimeOff = { id: number; leader: string; leaderName: string; startsAt: string; endsAt: string; reason: string | null };

/** A leader as the assign sheet offers them for one appointment (V20). */
export type LeaderCandidate = {
  publicId: string;
  displayName: string;
  nameSi: string | null;
  nameTa: string | null;
  title: string | null;
  assigned: boolean;
  busy: "TimeOff" | "Appointment" | null;
  normallyAvailable: boolean | null;
};

export type AgendaView = "upcoming" | "waiting" | "needs-leaders" | "history";

export type AppointmentLeader = {
  publicId: string;
  displayName: string;
  nameSi: string | null;
  nameTa: string | null;
  title: string | null;
  response: Exclude<AssignmentResponse, "Removed">;
  respondedOnBehalf: boolean;
  declineReason: string | null;
};

/** One appointment on the institution's agenda (V32). */
export type AgendaAppointment = {
  appointmentNo: string;
  status: AppointmentStatus;
  startsAt: string;
  endsAt: string;
  serviceType: string;
  serviceName: string;
  serviceNameSi: string | null;
  serviceNameTa: string | null;
  leadersRequired: number;
  locationMode: Exclude<ServiceLocation, "Either">;
  address: string | null;
  occasion: string | null;
  donorNote: string | null;
  donorName: string;
  donorPhone: string;
  createdAt: string;
  rejectReason: string | null;
  cancelReason: string | null;
  needsLeaders: boolean;
  leaders: AppointmentLeader[];
};

/** One appointment on a signed-in leader's own schedule (V33). */
export type ScheduleItem = {
  appointmentNo: string;
  startsAt: string;
  endsAt: string;
  serviceType: string;
  serviceName: string;
  serviceNameSi: string | null;
  serviceNameTa: string | null;
  locationMode: Exclude<ServiceLocation, "Either">;
  address: string | null;
  occasion: string | null;
  donorName: string;
  donorPhone: string;
  response: "Pending" | "Accepted";
};

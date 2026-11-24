import { empty, json, jsonBody, send } from "@/lib/onboarding/api";
import type { BookingResult } from "@/lib/bookings/api";
import type {
  AgendaAppointment,
  AgendaView,
  AppointmentStatus,
  DonorAppointment,
  Leader,
  LeaderCandidate,
  LeaderInput,
  ScheduleItem,
  ServiceRequest,
  ServiceTypeInput,
  ServiceTypeView,
  TimeOff,
  WeeklyHours,
} from "./types";

/*  Typed calls to the religious-service API (Phase E). The same `{ code, fields }` refusal and the
    same session plumbing as the booking calls. An institution's services come with its public page
    (getInstitution in lib/bookings/api.ts, V25).                                                  */

export type ServiceResult<T = void> = BookingResult<T>;

// ---- the signed-in donor --------------------------------------------------------------------------

export async function requestService(request: ServiceRequest): Promise<ServiceResult<{ appointmentNo: string; status: AppointmentStatus }>> {
  return json(await send("/api/appointments", jsonBody("POST", request)));
}

export async function listMyAppointments(): Promise<ServiceResult<DonorAppointment[]>> {
  return json(await send("/api/appointments/mine", { method: "GET" }));
}

export async function cancelMyAppointment(appointmentNo: string, reason: string | null): Promise<ServiceResult> {
  return empty(await send(`/api/appointments/${encodeURIComponent(appointmentNo)}/cancel`, jsonBody("POST", { reason })));
}

// ---- the institution, for the institution in the session ----------------------------------------

const INSTITUTION = "/api/institution";

export async function listServiceTypes(): Promise<ServiceResult<ServiceTypeView[]>> {
  return json(await send(`${INSTITUTION}/service-types`, { method: "GET" }));
}

export async function createServiceType(input: ServiceTypeInput): Promise<ServiceResult<{ publicId: string; version: string }>> {
  return json(await send(`${INSTITUTION}/service-types`, jsonBody("POST", input)));
}

export async function updateServiceType(publicId: string, input: ServiceTypeInput): Promise<ServiceResult<{ publicId: string; version: string }>> {
  return json(await send(`${INSTITUTION}/service-types/${encodeURIComponent(publicId)}`, jsonBody("PUT", input)));
}

export async function setServiceTypeActive(publicId: string, active: boolean): Promise<ServiceResult> {
  return empty(await send(`${INSTITUTION}/service-types/${encodeURIComponent(publicId)}/${active ? "activate" : "deactivate"}`, { method: "POST" }));
}

export async function listLeaders(): Promise<ServiceResult<Leader[]>> {
  return json(await send(`${INSTITUTION}/leaders`, { method: "GET" }));
}

export async function createLeader(input: LeaderInput): Promise<ServiceResult<{ publicId: string; version: string }>> {
  return json(await send(`${INSTITUTION}/leaders`, jsonBody("POST", input)));
}

export async function updateLeader(publicId: string, input: LeaderInput): Promise<ServiceResult<{ publicId: string; version: string }>> {
  return json(await send(`${INSTITUTION}/leaders/${encodeURIComponent(publicId)}`, jsonBody("PUT", input)));
}

/** Takes a leader off the roster or back; the answer names the upcoming appointments they still hold (V23). */
export async function setLeaderActive(publicId: string, active: boolean): Promise<ServiceResult<{ appointments: string[] }>> {
  return json(await send(`${INSTITUTION}/leaders/${encodeURIComponent(publicId)}/${active ? "activate" : "deactivate"}`, { method: "POST" }));
}

/** Replaces the leader's whole usual week (V22). */
export async function setWeeklyHours(publicId: string, spans: WeeklyHours[]): Promise<ServiceResult> {
  return empty(await send(`${INSTITUTION}/leaders/${encodeURIComponent(publicId)}/weekly-hours`, jsonBody("PUT", { spans })));
}

export async function listTimeOff(): Promise<ServiceResult<TimeOff[]>> {
  return json(await send(`${INSTITUTION}/leaders/time-off`, { method: "GET" }));
}

/** Leave from `startsAt` to `endsAt`, both "yyyy-MM-ddTHH:mm:00+05:30"; 409 `time_off_clashes` names the appointments in the way. */
export async function addTimeOff(leader: string, input: { startsAt: string; endsAt: string; reason: string | null }): Promise<ServiceResult<{ id: number }>> {
  return json(await send(`${INSTITUTION}/leaders/${encodeURIComponent(leader)}/time-off`, jsonBody("POST", input)));
}

export async function removeTimeOff(id: number): Promise<ServiceResult> {
  return empty(await send(`${INSTITUTION}/leaders/time-off/${id}`, { method: "DELETE" }));
}

export async function getAgenda(view: AgendaView): Promise<ServiceResult<AgendaAppointment[]>> {
  return json(await send(`${INSTITUTION}/appointments?view=${encodeURIComponent(view)}`, { method: "GET" }));
}

export async function getCandidates(appointmentNo: string): Promise<ServiceResult<LeaderCandidate[]>> {
  return json(await send(`${INSTITUTION}/appointments/${encodeURIComponent(appointmentNo)}/candidates`, { method: "GET" }));
}

/** Assigns leaders (public ids); 409 `leader_not_free` names who is not free. */
export async function assignLeaders(appointmentNo: string, leaders: string[]): Promise<ServiceResult<{ status: AppointmentStatus }>> {
  return json(await send(`${INSTITUTION}/appointments/${encodeURIComponent(appointmentNo)}/leaders`, jsonBody("POST", { leaders })));
}

export async function removeLeader(appointmentNo: string, leader: string): Promise<ServiceResult> {
  return empty(await send(`${INSTITUTION}/appointments/${encodeURIComponent(appointmentNo)}/leaders/${encodeURIComponent(leader)}`, { method: "DELETE" }));
}

export async function respondOnBehalf(appointmentNo: string, leader: string, accept: boolean, reason: string | null): Promise<ServiceResult> {
  return empty(
    await send(`${INSTITUTION}/appointments/${encodeURIComponent(appointmentNo)}/leaders/${encodeURIComponent(leader)}/respond`, jsonBody("POST", { accept, reason })),
  );
}

export async function decideAppointment(appointmentNo: string, decision: "reject" | "cancel" | "complete", reason?: string): Promise<ServiceResult> {
  const path = `${INSTITUTION}/appointments/${encodeURIComponent(appointmentNo)}/${decision}`;
  return empty(await send(path, reason === undefined ? { method: "POST" } : jsonBody("POST", { reason })));
}

// ---- a signed-in leader -----------------------------------------------------------------------------

export async function getSchedule(): Promise<ServiceResult<ScheduleItem[]>> {
  return json(await send(`${INSTITUTION}/schedule`, { method: "GET" }));
}

export async function respondAsLeader(appointmentNo: string, accept: boolean, reason: string | null): Promise<ServiceResult> {
  return empty(await send(`${INSTITUTION}/schedule/${encodeURIComponent(appointmentNo)}/respond`, jsonBody("POST", { accept, reason })));
}

import type { SessionState } from "@/auth/session-client";

/*  The HTTP layer as a screen sees it, stubbed at the app's own boundary: http.ts's getJson for
    anonymous reads and the session client's fetch for signed-in calls. A test scripts answers
    per "METHOD /path" and reads back what was sent. The test file wires these into jest.mock
    (factories cannot close over imports, so each test file does the two-line mock itself).  */

export type SentRequest = { method: string; path: string; body: unknown; signedIn: boolean };
type Answer = (request: SentRequest) => Response | Promise<Response>;

export function createApiStub() {
  const sent: SentRequest[] = [];
  let answers: Record<string, Answer> = {};

  async function handle(path: string, init: RequestInit | undefined, signedIn: boolean): Promise<Response> {
    const request: SentRequest = {
      method: init?.method ?? "GET",
      path,
      body: typeof init?.body === "string" ? JSON.parse(init.body) : null,
      signedIn,
    };
    sent.push(request);
    const answer = answers[`${request.method} ${path}`];
    if (!answer) throw new Error(`No answer scripted for ${request.method} ${path}`);
    return answer(request);
  }

  return {
    sent,
    /** Replaces the scripted answers. */
    serve(next: Record<string, Answer>) {
      answers = next;
    },
    /** Adds to the scripted answers. */
    also(more: Record<string, Answer>) {
      answers = { ...answers, ...more };
    },
    getJson: (path: string) => handle(path, undefined, false),
    client: { fetch: (path: string, init?: RequestInit) => handle(path, init, true) },
  };
}

export const ANONYMOUS: SessionState = { status: "anonymous" };

export const DONOR: SessionState = {
  status: "authenticated",
  accessToken: "access",
  expiresAt: Number.MAX_SAFE_INTEGER,
  institutions: [],
  activeInstitution: null,
};

/** Staff acting for an institution: sign-in puts anyone with exactly one straight into it. */
export const STAFF: SessionState = {
  status: "authenticated",
  accessToken: "access",
  expiresAt: Number.MAX_SAFE_INTEGER,
  institutions: [{ slug: "alpha-temple", name: "Alpha Temple" }],
  activeInstitution: "alpha-temple",
};
